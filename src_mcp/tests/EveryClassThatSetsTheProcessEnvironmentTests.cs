using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A test class that writes the process-wide environment, or resets the product's process-wide sweep throttle, runs in a serial collection
/// (<c>todo/PLAN_the_suite_is_deterministic_and_leaves_no_temp.md</c>, C11 and C6, scenario S4).
/// </summary>
/// <remarks>
/// <para><b>Rule 1 — the environment.</b> An environment variable belongs to the PROCESS, and xUnit runs classes without a collection in
/// parallel: a class that sets <c>COAI_DATA_DIR</c> or <c>VIBE_MCP_SERVERS</c> there changes what every other class running beside it
/// reads, and its own "restore" can put back a value another class set in the meantime (a field initializer captured it). A collection
/// defined with <c>DisableParallelization = true</c> runs alone, after the parallel ones, so only the class itself ever sees its value.
/// Every other writer of the environment already sits in one (<c>fakecli-env</c>, <c>console-out</c>, <c>process-privileges</c>); these
/// were the two that did not.</para>
/// <para><b>Rule 2 — the sweep throttle.</b> <c>PanelService</c> walks the temp folder at most once per ten minutes per PROCESS, from a
/// static timestamp; <c>ForgetTheLastSweep</c> resets it, and the next <c>BuildWork</c> ANYWHERE in the process then walks the real temp
/// folder — about a second at today's size — inside whatever round deadline that call runs under (C6: the round-deadline end-to-end test's
/// most likely failure). A caller that resets it in a parallel class lands that walk on a stranger's round.</para>
/// <para>Source scans, judged on the CONCRETE class (a base class's calls count through each derived class), on the parser
/// <see cref="TestSourceIndex"/> the steering-file scan shares. Each has its companion: the scan still finds known instances.</para>
/// </remarks>
public sealed class EveryClassThatSetsTheProcessEnvironmentTests
{
    /// <summary>
    /// A write of the process environment: <c>Environment.SetEnvironmentVariable(</c>, or the helper that clears every <c>FAKECLI_*</c>
    /// variable — <c>FakeCliSteering.Reset(</c> is the same write in a helper's words.
    /// </summary>
    private static readonly Regex SetsTheEnvironment = new(
        @"\bEnvironment\s*\.\s*SetEnvironmentVariable\s*\(|\bFakeCliSteering\s*\.\s*Reset\s*\(", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static readonly Regex ResetsTheSweepThrottle = new(
        @"\bPanelService\s*\.\s*ForgetTheLastSweep\s*\(", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>The concrete classes whose own code or a base's matches <paramref name="what"/> and that are not in a serial collection: <c>file: class</c>.</summary>
    private static List<string> Offenders(IReadOnlyList<TestClass> classes, Regex what)
    {
        var serial = TestSourceIndex.SerialCollections(classes);

        return
        [
            .. TestSourceIndex.Concrete(classes)
                .Where(c => TestSourceIndex.Reaches(c, classes, what) && !TestSourceIndex.InASerialCollection(c, serial))
                .Select(c => $"{c.File}: {c.Name}"),
        ];
    }

    [Fact]
    public void EveryClassThatSetsTheProcessEnvironment_IsInASerialCollection()
    {
        string.Join("; ", Offenders(TestSourceIndex.Classes, SetsTheEnvironment)).Should().BeEmpty(
            "an environment variable is the process's, and a class outside a serial collection runs beside every other class and changes what they read");
    }

    [Fact]
    public void NoStaticHelperSetsTheProcessEnvironment_ButFakeCliSteeringAndTheRunTempInitializer()
    {
        TestSourceIndex.StaticOffenders(TestSourceIndex.Classes, SetsTheEnvironment, allowed: [nameof(FakeCliSteering), nameof(RunTemp)]).Should().BeEmpty(
            "a static class is placed in no collection, so an environment write from a helper runs in whichever class called it; "
            + "RunTemp's module initializer is the one other writer — it runs once, before any test, and never again");
    }

    [Fact]
    public void TheRunTempInitializerIsTheOnlyOtherStaticWriter_AnyOtherStaticHelperStillIsAnOffender()
    {
        var classes = TestSourceIndex.Parse("Synthetic.cs", Definition + """
            public static class RunTemp { public static void Start() => Environment.SetEnvironmentVariable("TEMP", "x"); }
            public static class FakeCliSteering { public static void Reset() => Environment.SetEnvironmentVariable("FAKECLI_X", null); }
            public static class Other { public static void A() => Environment.SetEnvironmentVariable("TEMP", "y"); }
            """);

        TestSourceIndex.StaticOffenders(classes, SetsTheEnvironment, allowed: [nameof(FakeCliSteering), nameof(RunTemp)])
            .Should().BeEquivalentTo(["Synthetic.cs: Other"]);
    }

    [Fact]
    public void AStaticHelperThatSetsTheEnvironmentOrResetsTheFakeCli_IsAnOffender_AndFakeCliSteeringItselfIsNot()
    {
        var classes = TestSourceIndex.Parse("Synthetic.cs", Definition + """
            public static class FakeCliSteering { public static void Reset() => Environment.SetEnvironmentVariable("FAKECLI_X", null); }
            public static class SetsIt { public static void A() => Environment.SetEnvironmentVariable("X", "1"); }
            public static class ResetsIt { public static void A() => FakeCliSteering.Reset(); }
            public static class Quiet { public static string A() => "Environment.SetEnvironmentVariable("; }
            """);

        TestSourceIndex.StaticOffenders(classes, SetsTheEnvironment, allowed: "FakeCliSteering")
            .Should().BeEquivalentTo(["Synthetic.cs: SetsIt", "Synthetic.cs: ResetsIt"]);
    }

    [Fact]
    public void AClassThatResetsTheFakeCliOutsideASerialCollection_IsAnOffender()
    {
        // FakeCliSteering.Reset clears every FAKECLI_* variable of the process: the same rule as writing one.
        var classes = TestSourceIndex.Parse("Synthetic.cs", Definition + """
            [Collection("serial-one")]
            public sealed class Placed { void A() => FakeCliSteering.Reset(); }
            public sealed class Loose { void A() => FakeCliSteering.Reset(); }
            """);

        Offenders(classes, SetsTheEnvironment).Should().BeEquivalentTo(["Synthetic.cs: Loose"]);
    }

    [Fact]
    public void EveryClassThatResetsTheSweepThrottle_IsInASerialCollection()
    {
        string.Join("; ", Offenders(TestSourceIndex.Classes, ResetsTheSweepThrottle)).Should().BeEmpty(
            "the throttle is a static of the process: a reset in a parallel class makes the next BuildWork anywhere walk the real temp folder inside its round's deadline");
    }

    [Fact]
    public void TheScans_StillSeeTheClassesTheyKnowAreThere()
    {
        var classes = TestSourceIndex.Classes;
        var serial = TestSourceIndex.SerialCollections(classes);

        serial.Should().Contain(["fakecli-env", "console-out", "process-privileges"], "the three collections that run alone are found by their definitions");
        var environmentWriters = TestSourceIndex.Concrete(classes).Where(c => TestSourceIndex.Reaches(c, classes, SetsTheEnvironment)).Select(c => c.Name).ToList();
        environmentWriters.Should().Contain(["PanelServiceTests", "ARequestFaultIsNotAnOldBinaryTests", "VibeRuntimeTests"], "the writers the plan names are found, in a collection or not");
        environmentWriters.Should().Contain(["KeyVaultProcessTests", "TheVaultIsFoundWhereCredsForDevsInstallsItTests"], "classes whose only environment write is FakeCliSteering.Reset( are found too");
        var throttleResetters = TestSourceIndex.Concrete(classes).Where(c => TestSourceIndex.Reaches(c, classes, ResetsTheSweepThrottle)).Select(c => c.Name).ToList();
        throttleResetters.Should().Contain(["AScratchSweepRunsOnTheConsultationBeatTests", "VibeHomesTests", "TempDirsAreSwept"], "the seven callers of ForgetTheLastSweep sit in these three classes");
    }

    private const string Definition = """
        [CollectionDefinition("serial-one", DisableParallelization = true)]
        public sealed class SerialOne;
        """;

    [Fact]
    public void AnEnvironmentWriteOutsideASerialCollection_IsAnOffender_AndAWordInAStringOrCommentIsNot()
    {
        var classes = TestSourceIndex.Parse("Synthetic.cs", Definition + """
            [Collection("serial-one")]
            public sealed class Placed { void A() => Environment.SetEnvironmentVariable("X", "1"); }
            public sealed class Loose { void A() => System.Environment.SetEnvironmentVariable("X", "1"); }
            public sealed class Talks { void A() { var s = "Environment.SetEnvironmentVariable("; /* Environment.SetEnvironmentVariable( */ } }
            public abstract class Base { protected void Set() => Environment.SetEnvironmentVariable("X", null); }
            public sealed class Derived : Base { }
            """);

        Offenders(classes, SetsTheEnvironment).Should().BeEquivalentTo(["Synthetic.cs: Loose", "Synthetic.cs: Derived"]);
    }

    [Fact]
    public void ASweepThrottleResetOutsideASerialCollection_IsAnOffender()
    {
        var classes = TestSourceIndex.Parse("Synthetic.cs", Definition + """
            [Collection("serial-one")]
            public sealed class Placed { void A() => PanelService.ForgetTheLastSweep(); }
            public sealed class Loose { void A() => PanelService.ForgetTheLastSweep(); }
            """);

        Offenders(classes, ResetsTheSweepThrottle).Should().BeEquivalentTo(["Synthetic.cs: Loose"]);
    }
}
