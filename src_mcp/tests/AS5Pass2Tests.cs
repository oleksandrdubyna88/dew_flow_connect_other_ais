using System.Text;
using CoaiMcp.Core.Toolchain;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Pass 2 of the security verify of E4 s5 (RESULTS_e4_s5_security_verify.md): M5 (a code is in <c>testsFailed</c> only when a RESULTS line measured it as exclusive to failed tests), S9 (the head-listing read takes only a regular file), S10 (the format note says "could not be stopped" only when that
/// happened), S11 (the envelope and the job's file get the safe name of an excused test) and S12 (a changed adapter or memoryMb is named).
/// </summary>
[Collection("fakecli-env")]
public sealed class AS5Pass2Tests
{
    // ---- M5 ----

    [Theory]
    [InlineData("dotnet", "vstest")]
    [InlineData("node", "jest")]
    [InlineData("node", "vitest")]
    [InlineData("node", "node-test")]
    public void M5_ATool_WhoseExitCodeOneIsAlsoACoverageGateOrACrash_HasNoTestsFailedCode(string adapter, string tool)
    {
        ToolchainDefaults.Current.Stacks[adapter].NoTestRan[tool].TestsFailed.Should().BeEmpty();
    }

    [Fact]
    public void M5_TheMtpRunner_KeepsItsOwnCodeForFailedTests()
    {
        ToolchainDefaults.Current.Stacks["dotnet"].NoTestRan["mtp"].TestsFailed.Should().Equal(2);
    }

    [Fact]
    public void M5_TheRuleBesideTheTable_IsInTheFile()
    {
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "shared", "toolchains.json")).Should().Contain("measured it as exclusive to failed tests");
    }

    private const string JestFirst = "echo 'FAIL ./tests/flaky.js'; echo '  ● sometimes'; echo; echo '    boom'; echo; echo '      at Object.<anonymous> (tests/flaky.js:3:1)'; echo; echo 'Tests:       1 failed, 4 passed, 5 total'; echo 'Jest: \"global\" coverage threshold for lines (80%) not met: 41%'; exit 1";

    private const string JestNarrowed = "echo 'PASS ./tests/flaky.js'; echo '  ✓ sometimes (1 ms)'; echo; echo 'Tests:       1 passed, 1 total'; exit 0";

    private const string JestWhole = "echo 'Tests:       5 passed, 5 total'; exit 0";

    private static string NodeToolchain(string firstRun, string secondRun, string narrowed)
    {
        var full = $"mkdir -p .flaky; if [ -f .flaky/seen ]; then {secondRun}; else touch .flaky/seen; {firstRun}; fi";
        var text = new StringBuilder("version: 1\nprojects:\n  - path: \".\"\n    adapter: node\n");
        text.Append("    build:\n      - \"/bin/sh\"\n      - \"-c\"\n      - \"echo built\"\n");
        text.Append($"    test:\n      - \"/bin/sh\"\n      - \"-c\"\n      - {ToolchainFixture.Quote(full)}\n");
        text.Append($"    testFor:\n      - \"/bin/sh\"\n      - \"-c\"\n      - {ToolchainFixture.Quote(narrowed)}\n      - \"sel\"\n      - \"{{file}}\"\n    testFiles:\n      - \"tests\"\n");

        return text.ToString();
    }

    private static CoderRig.Launch Green() => new(new Dictionary<string, string> { [CoderRig.StateFile] = CoderRig.Green + "\n" }, CoderRig.Report());

    [Fact]
    public async Task M5_JestShapedOutput_ExitOne_WithACoverageLine_AndAPassingNarrowedReRun_IsNotExcused_TheReRunIsTheWholeStep()
    {
        await using var rig = await CoderRig.CreateAsync(toolchain: NodeToolchain(JestFirst, JestWhole, JestNarrowed), extra: new Dictionary<string, string> { [".gitignore"] = ".flaky/\n", ["tests/flaky.js"] = "test('sometimes', () => {});\n" });
        rig.Says("sonnet", Green());
        rig.Rig.Setting("COAI_FIX_ROUNDS", "0");
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        var envelope = rig.Envelope(job);
        var tests = envelope.GetProperty("checks").EnumerateArray().Where(step => step.GetProperty("step").GetString() == "test").ToList();
        var rerun = tests.Single(step => step.GetProperty("scope").GetString() == "rerun");
        rerun.GetProperty("files").GetInt32().Should().Be(0, "exit 1 of jest is not 'tests failed' only (a coverage gate ends with 1 too), so only the whole step may excuse");
        envelope.GetProperty("flaky").EnumerateArray().Select(row => row.GetProperty("rerun").GetString()).Should().NotContain("narrowed");
        ended.Reason.Should().NotBeNull();
    }

    // ---- S9 ----

    [Fact]
    public void S9_OnlyARegularFile_IsAFileOfTheHead()
    {
        const string oid = "0123456789abcdef0123456789abcdef01234567";
        HeadEntry.IsRegularFile($"100644 blob {oid}      12\ttests/File.cs\0", "tests/File.cs").Should().BeTrue();
        HeadEntry.IsRegularFile($"100755 blob {oid}      12\ttests/run.sh\0", "tests/run.sh").Should().BeTrue();
        HeadEntry.IsRegularFile($"040000 tree {oid}       -\ttests/Sub\0", "tests/Sub").Should().BeFalse("a folder");
        HeadEntry.IsRegularFile($"120000 blob {oid}      12\ttests/Link\0", "tests/Link").Should().BeFalse("a link");
        HeadEntry.IsRegularFile($"160000 commit {oid}       -\ttests/Mod\0", "tests/Mod").Should().BeFalse("a submodule");
        HeadEntry.IsRegularFile(string.Empty, "tests/Gone.cs").Should().BeFalse("not there");
        HeadEntry.IsRegularFile($"100644 blob {oid}      12\ttests/Other.cs\0", "tests/File.cs").Should().BeFalse("another path");
    }

    [Fact]
    public async Task S9_AFolderNamedByTheOutput_GivesAWholeReRun()
    {
        const string first = "echo 'failed Flaky.Tests.Sometimes (3ms)'; echo '  boom'; echo '    at Flaky.Tests.Sometimes() in tests/Sub:3'; echo; echo 'Test run summary: Failed!'; echo '  total: 5'; echo '  failed: 1'; echo '  succeeded: 4'; echo '  skipped: 0'; exit 2";
        const string second = "echo 'Test run summary: Passed!'; echo '  total: 5'; echo '  failed: 0'; echo '  succeeded: 5'; echo '  skipped: 0'; exit 0";
        const string narrowed = "echo \"passed Flaky.Tests.Sometimes (1ms)\"; echo 'Test run summary: Passed!'; echo '  total: 1'; echo '  failed: 0'; echo '  succeeded: 1'; echo '  skipped: 0'";
        var text = NodeToolchain(first, second, narrowed).Replace("adapter: node", "adapter: dotnet", StringComparison.Ordinal);
        await using var rig = await CoderRig.CreateAsync(toolchain: text, extra: new Dictionary<string, string> { [".gitignore"] = ".flaky/\n", ["tests/Sub/Inner.cs"] = "class Inner {}\n" });
        rig.Says("sonnet", Green());
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        rig.Envelope(job).GetProperty("flaky").EnumerateArray().Single().GetProperty("rerun").GetString().Should().Be("whole", "tests/Sub is a folder of the head, not a file");
    }

    // ---- S10 ----

    [Fact]
    public void S10_TheFormatNote_SaysCouldNotBeStopped_OnlyWhenAUnitSurvived()
    {
        var survived = new StepRecord("p", CheckStep.Format, ["x"], true, StepRecord.UnitSurvivedExitCode, false, TimeSpan.Zero, "a.log", false);
        var refused = new CheckResult(CheckState.BoundaryRefused, [], "the preflight of the checks refused");
        var left = new CheckResult(CheckState.BoundaryRefused, [survived], "the format step left a unit");

        CoderFormat.RefusalNote(left).Should().Contain("could not be stopped");
        CoderFormat.RefusalNote(refused).Should().NotContain("could not be stopped").And.Contain("refused");
    }

    // ---- S11 ----

    [Fact]
    public async Task S11_ALongNameOfAnExcusedTest_IsTheSafeNameInTheEnvelopeAndInTheJobsFile()
    {
        var name = "Ns.Tests." + new string('A', 240);
        var first = $"echo 'failed {name} (3ms)'; echo '  boom'; echo '    at {name}() in tests/FlakyTests.cs:3'; echo; echo 'Test run summary: Failed!'; echo '  total: 5'; echo '  failed: 1'; echo '  succeeded: 4'; echo '  skipped: 0'; exit 2";
        const string second = "echo 'Test run summary: Passed!'; echo '  total: 5'; echo '  failed: 0'; echo '  succeeded: 5'; echo '  skipped: 0'; exit 0";
        var text = NodeToolchain(first, second, "echo x").Replace("adapter: node", "adapter: dotnet", StringComparison.Ordinal);
        text = text[..text.IndexOf("    testFor:", StringComparison.Ordinal)];
        await using var rig = await CoderRig.CreateAsync(toolchain: text, extra: new Dictionary<string, string> { [".gitignore"] = ".flaky/\n", ["tests/FlakyTests.cs"] = "class FlakyTests {}\n" });
        rig.Says("sonnet", Green());
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        var shown = rig.Envelope(job).GetProperty("flaky").EnumerateArray().Single().GetProperty("test").GetString()!;
        shown.Should().Be(FlakyRule.SafeName(name)).And.HaveLength(FlakyRule.SafeName(name).Length);
        shown.Length.Should().BeLessThan(name.Length);
        File.ReadAllLines(Path.Combine(rig.World.Store.FolderOf(job.JobId)!.Location, "checks", "flaky-excused.txt")).Should().Equal(shown);
    }

    // ---- S12 ----

    [Fact]
    public async Task S12_AChangedAdapterAndAChangedMemoryMb_AreNamedInTheReport()
    {
        const string baseFile = "version: 1\nprojects:\n  - path: \".\"\n    adapter: dotnet\n    memoryMb: 1024\n    build:\n      - \"/bin/sh\"\n      - \"-c\"\n      - \"echo built\"\n    test:\n      - \"/bin/sh\"\n      - \"-c\"\n      - \"echo tested\"\n";
        var head = baseFile.Replace("adapter: dotnet", "adapter: node", StringComparison.Ordinal).Replace("memoryMb: 1024", "memoryMb: 2048", StringComparison.Ordinal);
        await using var rig = await CoderRig.CreateAsync(toolchain: baseFile, extra: new Dictionary<string, string> { [".gitignore"] = ".flaky/\n" });
        rig.Says("sonnet", new CoderRig.Launch(new Dictionary<string, string> { [".coai/toolchain.yaml"] = head, [CoderRig.StateFile] = CoderRig.Green + "\n" }, CoderRig.Report()));
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        var lines = rig.Envelope(job).GetProperty("toolchain").GetProperty("changedDefinitions").EnumerateArray().Select(line => line.GetString()!).ToList();
        lines.Should().Contain(line => line.Contains("changed the adapter", StringComparison.Ordinal));
        lines.Should().Contain(line => line.Contains("changed memoryMb", StringComparison.Ordinal));
    }
}
