using System.Text.RegularExpressions;
using CoaiMcp.FakeCli;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Inside one run, the fake CLI's steering file is safe only while EVERY class that writes it is in the serial <c>fakecli-env</c>
/// collection — and nothing else spells its name (<c>todo/PLAN_the_suite_is_deterministic_and_leaves_no_temp.md</c>, C1/D1, gate finding
/// D1, scenario S2b).
/// </summary>
/// <remarks>
/// <para>The file is one file beside the binary, and a vendor-shaped launch with no <c>FAKECLI_MODE</c> reads it. Two classes writing it
/// at once, or a class writing it while a parallel class launches the fake, would steer a launch with another test's answer — the same
/// defect two RUNS had, inside one. A serial collection runs alone, after the parallel ones, so the file can only be seen by the class
/// that wrote it. Nothing used to check that; a seventh class added outside the collection would have broken the other six at random.</para>
/// <para>A source scan, so it gets two companions (<c>testing.md</c>, "a structural test that matches nothing passes forever"): the scan
/// still finds the six known writers, and it is red on a class that writes outside the collection (the parser's own synthetic cases).
/// It was also watched red once on the real tree by taking one class out of the collection.</para>
/// <para><c>DeleteALeftover</c> is not a write: the assembly fixture calls it once, before any test.</para>
/// </remarks>
public sealed class EveryWriterOfTheSteeringFileTests
{
    private const string Serial = "fakecli-env";

    /// <summary>What writes the file or names where it is: the helper's <c>Write</c>, <c>ReadBack</c> and <c>MinimalFile</c>.</summary>
    private static readonly Regex Writes = new(
        @"\bFakeCliSteering\s*\.\s*(?:Write|ReadBack|MinimalFile)\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>The six classes that wrote their own copy of the file before the helper replaced the copies.</summary>
    private static readonly string[] TheSixKnownWriters =
    [
        "AKimiRowRunsThroughTheServiceOnWsl2Tests",
        "AskHumanScenarioTests",
        "AVibeReviewerRunsThroughAFakeVibeTests",
        "QuestionConsultScenarioTests",
        "QuestionRowLookupScenarioTests",
        "QuestionRowOnAgyScenarioTests",
    ];

    private static List<string> Offenders(IReadOnlyList<TestClass> classes)
    {
        var serial = TestSourceIndex.SerialCollections(classes);

        return
        [
            .. TestSourceIndex.Concrete(classes)
                .Where(c => TestSourceIndex.Reaches(c, classes, Writes) && !TestSourceIndex.InASerialCollection(c, serial))
                .Select(c => $"{c.File}: {c.Name}"),
        ];
    }

    [Fact]
    public void EveryClassThatWritesTheSteeringFile_IsInTheSerialFakeCliEnvCollection()
    {
        string.Join("; ", Offenders(TestSourceIndex.Classes)).Should().BeEmpty(
            "the steering file is one file beside the binary; a writer outside the serial collection runs beside classes that launch the fake CLI and steers them");
    }

    [Fact]
    public void TheScan_StillSeesTheSixKnownWriters_AndTheCollectionTheyAreIn()
    {
        var classes = TestSourceIndex.Classes;
        var writers = TestSourceIndex.Concrete(classes).Where(c => TestSourceIndex.Reaches(c, classes, Writes)).Select(c => c.Name).ToList();

        writers.Should().Contain(TheSixKnownWriters, "a scan that finds nothing passes for ever");
        TestSourceIndex.SerialCollections(classes).Should().Contain(Serial, "the collection's definition is where the scan learns that it runs alone");
        classes.Where(c => TheSixKnownWriters.Contains(c.Name) && !c.Collections.Contains(Serial)).Select(c => c.Name).Should().BeEmpty();
    }

    [Fact]
    public void NoTestSourceSpellsTheSteeringFilesName_ButTheHelperAndTheTestsThatPinIt()
    {
        var allowed = new[] { "FakeCliSteering.cs", "AFakeCliSteeringFileIsBesideItsBinaryTests.cs", "EveryWriterOfTheSteeringFileTests.cs" };
        var tests = Path.Combine(RepoPaths.Root(), "src_mcp", "tests");
        var bin = $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}";
        var obj = $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}";
        var sources = Directory.EnumerateFiles(tests, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains(bin, StringComparison.Ordinal) && !file.Contains(obj, StringComparison.Ordinal))
            .ToList();

        var spelling = sources
            .Where(file => !allowed.Contains(Path.GetFileName(file)))
            .Where(SpellsTheFilesName)
            .Select(Path.GetFileName)
            .ToList();

        sources.Select(Path.GetFileName).Should().Contain(allowed, "the scan sees the files it allows; a scan that finds none of them reads nothing");
        spelling.Should().BeEmpty("one writer, one path: a test that spells the file's name writes it behind the helper's back and outside the collection check");
    }

    [Fact]
    public void NoStaticHelperWritesTheSteeringFile_ButFakeCliSteeringItself()
    {
        // A static class is no test class: no collection places it and the scan above never judges it, so a helper calling the
        // writer would run in whichever class called the helper (code gate, 2026-10-19). FakeCliSteering is the ONE static that may.
        TestSourceIndex.StaticOffenders(TestSourceIndex.Classes, Writes, allowed: nameof(FakeCliSteering)).Should().BeEmpty(
            "one writer, one place: a static helper that writes the steering file hides the writer from the collection check");
    }

    [Fact]
    public void AStaticHelperThatWritesTheSteeringFile_IsAnOffender_AndFakeCliSteeringItselfIsNot()
    {
        var classes = TestSourceIndex.Parse("Synthetic.cs", Definitions + """
            public static class FakeCliSteering { public static void Write(object o) { var f = FakeCliSteering.MinimalFile; } }
            public static class Helper { public static void Steer() => FakeCliSteering.Write(null!); }
            public static class Quiet { public static string A() => "FakeCliSteering.Write("; }
            [Collection("fakecli-env")]
            public sealed class Placed { void A() => FakeCliSteering.Write(null!); }
            """);

        TestSourceIndex.StaticOffenders(classes, Writes, allowed: "FakeCliSteering").Should().BeEquivalentTo(["Synthetic.cs: Helper"]);
    }

    /// <summary>Whether the source file spells the steering file's name or the type that knows it — each file READ once, then lexed once per view.</summary>
    private static bool SpellsTheFilesName(string file)
    {
        var source = File.ReadAllText(file);

        return ProductionSources.JoinedWithText(source).Contains(MinimalSteeringFile.Name, StringComparison.Ordinal)
            || ProductionSources.Joined(source).Contains(nameof(MinimalSteeringFile), StringComparison.Ordinal);
    }

    // --- the parser, on synthetic source: the scan is red where it must be --------------------------------------------------------

    private const string Definitions = """
        [CollectionDefinition("fakecli-env", DisableParallelization = true)]
        public sealed class FakeCliEnvCollection;
        [CollectionDefinition("parallel-one")]
        public sealed class ParallelOne;
        """;

    [Fact]
    public void AClassThatWritesOutsideTheSerialCollection_IsAnOffender()
    {
        var classes = TestSourceIndex.Parse("Synthetic.cs", Definitions + """
            [Collection("fakecli-env")]
            public sealed class Inside { void A() => FakeCliSteering.Write(null!); }
            public sealed class Outside { void A() => FakeCliSteering.Write(null!); }
            [Collection("parallel-one")]
            public sealed class InAParallelOne { void A() { var f = FakeCliSteering.MinimalFile; } }
            public sealed class Innocent { void A() { var s = "FakeCliSteering.Write("; /* FakeCliSteering.Write( */ } }
            """);

        Offenders(classes).Should().BeEquivalentTo(["Synthetic.cs: Outside", "Synthetic.cs: InAParallelOne"]);
    }

    [Fact]
    public void ABaseClassesWriteCounts_ThroughEachConcreteDerivedClass()
    {
        var classes = TestSourceIndex.Parse("Synthetic.cs", Definitions + """
            public abstract class Base : IAsyncLifetime { protected void Steer() => FakeCliSteering.Write(null!); }
            [Collection("fakecli-env")]
            public sealed class Placed : Base { }
            public sealed class Unplaced : Base { }
            """);

        Offenders(classes).Should().BeEquivalentTo(["Synthetic.cs: Unplaced"], "the abstract base is not judged, each concrete class derived from it is");
    }

    [Fact]
    public void ANestedClassesWriteCountsForItsOuterClass_AndANestedClassIsNotJudgedAlone()
    {
        var classes = TestSourceIndex.Parse("Synthetic.cs", Definitions + """
            public sealed class Outer
            {
                void Fine() { }
                private sealed class Inner { void A() => FakeCliSteering.Write(null!); }
            }
            [Collection("fakecli-env")]
            public sealed class PlacedOuter
            {
                private sealed class Helper { void A() => FakeCliSteering.Write(null!); }
            }
            public sealed class WithAPrimaryConstructor(string name) : IDisposable { public void Dispose() => FakeCliSteering.Write(null!); }
            """);

        Offenders(classes).Should().BeEquivalentTo(["Synthetic.cs: Outer", "Synthetic.cs: WithAPrimaryConstructor"],
            "a helper nested in a placed class runs inside its collection, and one nested in an unplaced class makes that class the offender");
    }
}
