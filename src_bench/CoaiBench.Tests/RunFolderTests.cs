using System.Reflection;
using CoaiMcp.Tests;
using CoaiMcp.Tests.Leftovers;
using FluentAssertions;
using Xunit;

namespace CoaiBench.Tests;

// testsToDelete-baseline-proof: src_bench/CoaiBench.Tests/
/// <summary>
/// This suite's run folder, and the refusal of a run that cannot have one (E5 S3, <c>todo/PLAN_test_leftovers_E5.md</c>). The library
/// (<c>src_mcp/tests_leftovers</c>) is shared; what is proven HERE is this suite's own wiring: its <c>RunTempStart.cs</c> made, claimed and registered the folder
/// before any test, and its <c>TestEntryPoint.cs</c> is the gated <c>Main</c> (exit 78, one stderr line, before discovery).
/// </summary>
/// <remarks>
/// <para><b>This file is the suite's proof file</b> (S6 marks it for the <c>raw-temp</c> baseline): the TEMP assertion and the forced-failure child runs sit together —
/// a folder that cannot be made, a claim that cannot be written, and EACH of TEMP, TMP and TMPDIR changed alone. Each child exits 78 before discovery.</para>
/// <para>A child gets a filter that matches nothing unless it names the sentinel class, so a gate that fails to refuse ends with "zero tests ran" (exit 8): a clean red.</para>
/// </remarks>
public sealed class RunFolderTests : IDisposable
{
    private const string SentinelVariable = "COAI_S3_SENTINEL";

    private static readonly TimeSpan ChildTimeout = TimeSpan.FromSeconds(60);

    private readonly OwnedScope _owned = DarwinLayout.FreshOwnedScope();

    public void Dispose() => _owned.Dispose();

    private static Dictionary<string, string?> AllThreeAt(string path) => RunTempFolder.Variables.ToDictionary(name => name, _ => (string?)path);

    private static string[] LinesOf(string text) => text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// A forced-failure child: the three variables at <paramref name="at"/>, started on <c>DiscoverySentinel</c> with the sentinel in <paramref name="root"/>, so a run that
    /// got as far as discovery or a test body would have written it.
    /// </summary>
    private static SuiteChild.Result RefusedChild(string root, string at, string? selfTest, out string sentinel)
    {
        sentinel = Path.Combine(root, "sentinel");
        var env = AllThreeAt(at);
        env[SentinelVariable] = sentinel;
        if (selfTest is not null)
        {
            env[RunTemp.SelfTestVariable] = selfTest;
        }

        return SuiteChild.Run(["--filter-class", "*DiscoverySentinel"], env, ChildTimeout);
    }

    /// <summary>Exit 78, no result line on stdout, ONE stderr line starting with the prefix and holding <paramref name="mentions"/>, the sentinel untouched, nothing left in the root.</summary>
    private static void TheOneLineRefusal(SuiteChild.Result result, string root, string mentions, string sentinel)
    {
        result.TimedOut.Should().BeFalse("the child must end by itself: " + result.StdErr);
        result.ExitCode.Should().Be(RunTemp.RefusedExitCode, $"stdout: {result.StdOut} / stderr: {result.StdErr}");
        LinesOf(result.StdErr).Should().ContainSingle().Which.Should().StartWith(RunTemp.RefusalPrefix).And.Contain(mentions);
        result.StdOut.Should().NotContain("total:", "no test ran");
        File.Exists(sentinel).Should().BeFalse("discovery never ran: the refusal is before it");
        Directory.GetFileSystemEntries(root).Where(entry => !entry.EndsWith("missing", StringComparison.Ordinal)).Should().BeEmpty("the refused child left nothing in its root");
    }

    [Fact]
    public void TheRunFolder_IsTheOwnerForm_AndAllThreeVariablesNameIt()
    {
        RunTemp.Refusal().Should().BeNull("a run that reached a test passed the gate");
        RunTemp.IsActive.Should().BeTrue("Gate checked and passed this run");
        Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Should().Be(RunTemp.Folder);
        Path.GetDirectoryName(RunTemp.Folder).Should().Be(RunTemp.Real, "the run folder is INSIDE the machine's temp folder, never it");
        Path.GetFileName(RunTemp.Folder).Should().MatchRegex("^" + System.Text.RegularExpressions.Regex.Escape(Owned.NameBase) + "run-[A-Za-z0-9]{6}$");
        RunTempFolder.Variables.Select(Environment.GetEnvironmentVariable).Should().OnlyContain(value => value == RunTemp.Folder, "TEMP, TMP and TMPDIR all name the run folder");
    }

    /// <summary>D8's merge condition (E5 S2-B): this suite makes an owner-form run folder AND registers the start-of-run sweep (the copied fixture) AND the order proof, in ONE assembly. A producer without the sweep, or the sweep without the producer, is red.</summary>
    [Fact]
    public void TheProducerAndTheSweep_LandTogether()
    {
        RunTemp.IsActive.Should().BeTrue("the producer: Gate checked and passed this run");
        System.IO.Path.GetFileName(RunTemp.Folder).Should().MatchRegex(
            "^" + System.Text.RegularExpressions.Regex.Escape(Owned.NameBase + "run-") + "[A-Za-z0-9]{6}\\z", "the producer: the run folder is of the owner form");
        var assembly = Assembly.GetExecutingAssembly();

        var fixtures = assembly.GetCustomAttributes<AssemblyFixtureAttribute>().Select(a => a.AssemblyFixtureType).ToList();
        fixtures.Should().Contain(typeof(LeftoversSweepFixture), "the start-of-run sweep is an assembly fixture of THIS suite");
        fixtures.Should().Contain(typeof(RunTempFixture), "the run folder's fallback refusal");
        assembly.GetCustomAttributes<StartOfRunOrderAttribute>().Should().ContainSingle("the order proof runs on every test of THIS suite");
    }

    /// <summary>The re-review's finding e1: the sweep ENDED before the first test of any collection started. The attribute proves it on every test; this one writes the margin down.</summary>
    [Fact]
    public void TheSweep_EndedBeforeTheFirstTestStarted()
    {
        StartOfRunSweep.EndedAt.Should().NotBe(0, "the sweep returned (a result or an error) and stamped its end");
        StartOfRunSweep.FirstTestStart.Should().NotBe(0, "this test's own Before recorded a start");
        StartOfRunSweep.FirstTestStart.Should().BeGreaterThan(StartOfRunSweep.EndedAt, "no test started before the sweep ended");
        StartOfRunSweep.LastRoot.Should().Be(RunTemp.Real, "the sweep was handed the machine's temp root, never the run folder");
        var margin = System.Diagnostics.Stopwatch.GetElapsedTime(StartOfRunSweep.EndedAt, StartOfRunSweep.FirstTestStart);
        TestContext.Current.AddWarning($"E5 S2-B order: the start-of-run sweep ended {margin.TotalMilliseconds:F1} ms before the first test started");
    }

    [Fact]
    public void TheEntryPoint_IsTheGatedOne() =>
        Assembly.GetEntryAssembly()!.EntryPoint!.DeclaringType!.FullName.Should().Be("CoaiBench.Tests.TestEntryPoint", "xUnit's generated Main is behind RunTemp.Gate");

    [Fact]
    public void ARunThatCannotMakeItsFolder_FailsBeforeAnyTest()
    {
        var root = _owned.Dir("s3-nofolder");
        var missing = Path.Combine(root, "missing");

        var result = RefusedChild(root, missing, null, out var sentinel);

        TheOneLineRefusal(result, root, missing, sentinel);
    }

    [Fact]
    public void AClaimThatCannotBeWritten_FailsTheRun()
    {
        var root = _owned.Dir("s3-claim");

        var result = RefusedChild(root, root, "fail-claim", out var sentinel);

        TheOneLineRefusal(result, root, "could not claim", sentinel);
    }

    /// <summary>Each of the three variables, changed ALONE after the folder was made, is refused on its own: one combined case would pass for a check of only one.</summary>
    [Theory]
    [InlineData("mismatch-temp", "TEMP")]
    [InlineData("mismatch-tmp", "TMP")]
    [InlineData("mismatch-tmpdir", "TMPDIR")]
    public void ARunWhoseVariableNoLongerNamesItsFolder_IsRefused_ForEachVariableAlone(string selfTest, string variable)
    {
        var root = _owned.Dir("s3-lost");

        var result = RefusedChild(root, root, selfTest, out var sentinel);

        TheOneLineRefusal(result, root, "TEMP, TMP and TMPDIR must all name this run's folder", sentinel);
        LinesOf(result.StdErr).Single().Should().Contain($"{variable}='").And.Contain("-mismatch'", $"only {variable} was changed");
    }

    /// <summary>
    /// D9, merge condition (o) (E5 S6): the end-of-run audit reads the run folder BEFORE the run ends. A child plants an orphan claim (a claim whose folder is missing) inside
    /// its own run folder, absent from the registry and with no inherited <c>TESTSTODELETE_RUN</c>; only <c>Discover</c> sees it, and <c>Discover</c> answers nothing once the run folder is
    /// gone. So the audit must run first, and the run must fail with its message.
    /// </summary>
    [Fact]
    public void AnOrphanClaim_IsReportedByTheAudit_BeforeTheRunFolderEnds()
    {
        var root = _owned.Dir("s6-orphan");
        var env = AllThreeAt(root);
        env["COAI_S6_ORPHAN_CLAIM"] = "1";
        env["TESTSTODELETE_RUN"] = null; // SuiteChild removes a variable given as null: the child's own record is what the audit reads

        var result = SuiteChild.Run(["--filter-class", "*OrphanClaimChild"], env, ChildTimeout);

        result.TimedOut.Should().BeFalse(result.StdErr);
        result.ExitCode.Should().NotBe(0, "the audit fails the run");
        (result.StdOut + result.StdErr).Should().Contain("coai tests: this run left 1 folder(s) it claimed").And.Contain("1 claimed in the temp root");
        Directory.GetFileSystemEntries(root).Should().BeEmpty("the audit removes what it names, and the run's end removes the rest");
    }
}

/// <summary>A theory whose <c>MemberData</c> runs at DISCOVERY: with the sentinel variable set it appends a line, so a refused child proves discovery never ran. Skips in a normal run.</summary>
public sealed class DiscoverySentinel
{
    public static TheoryData<int> Rows()
    {
        Append("discovery");

        return new TheoryData<int> { 1 };
    }

    private static void Append(string what)
    {
        if (Environment.GetEnvironmentVariable("COAI_S3_SENTINEL") is { Length: > 0 } file)
        {
            File.AppendAllText(file, what + "\n");
        }
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void ReadsTheSentinel_OnlyWhenAChildIsAskedTo(int row)
    {
        if (Environment.GetEnvironmentVariable("COAI_S3_SENTINEL") is not { Length: > 0 })
        {
            Assert.Skip("child-only: RunFolderTests starts the suite on this class with COAI_S3_SENTINEL set");
        }

        Append("test body");
        row.Should().Be(1);
    }
}

/// <summary>A fact that plants an ORPHAN CLAIM inside its own run folder (a claim whose folder is missing), absent from the registry: the end-of-run audit must name it before the run folder ends (E5 S6, D8 (o)).</summary>
public sealed class OrphanClaimChild
{
    [Fact]
    public void PlantsAClaimWhoseFolderIsMissing()
    {
        if (Environment.GetEnvironmentVariable("COAI_S6_ORPHAN_CLAIM") != "1")
        {
            Assert.Skip("child-only: AnOrphanClaim_IsReportedByTheAudit_BeforeTheRunFolderEnds starts the suite on this class with COAI_S6_ORPHAN_CLAIM=1");
        }

        File.WriteAllText(Path.Combine(RunTemp.Folder, Owned.NameFor("orphan") + ".owner"), Owner.This.Format() + "\n");
    }
}
