using System.Text;
using CoaiMcp.Core.Notices;
using CoaiMcp.Core.Toolchain;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The security verify of E4 s5 (RESULTS_e4_s5_security_verify.md), must-change M1 to M4 and the shoulds S1, S3, S4 and S5: a file name taken from a test's OUTPUT never becomes an option of an argv; a green round says which step a re-run excused, and a narrowed re-run excuses only a step
/// whose first exit code is the tool's "tests failed" code; "nothing selected" is not a failed step; a unit that cannot be stopped ends the job in the re-run and in the formatter turn.
/// </summary>
[Collection("fakecli-env")]
public sealed class AS5VerifyFixesTests
{
    private const string Failing = "echo 'failed Flaky.Tests.Sometimes (3ms)'; echo '  boom'; echo '    at Flaky.Tests.Sometimes() in {FILE}:3'; echo; echo 'Test run summary: Failed!'; echo '  total: 5'; echo '  failed: 1'; echo '  succeeded: 4'; echo '  skipped: 0'; exit {EXIT}";

    private const string Passing = "echo 'Test run summary: Passed!'; echo '  total: 5'; echo '  failed: 0'; echo '  succeeded: 5'; echo '  skipped: 0'; exit 0";

    private static string FailingAt(string file, int exit) => Failing.Replace("{FILE}", file, StringComparison.Ordinal).Replace("{EXIT}", exit.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);

    private static string Toolchain(string secondRun, string firstRun, string? narrowed = null, string extra = "")
    {
        var full = $"mkdir -p .flaky; if [ -f .flaky/seen ]; then {secondRun}; else touch .flaky/seen; {firstRun}; fi";
        var text = new StringBuilder("version: 1\n").Append(extra).Append("projects:\n  - path: \".\"\n    adapter: dotnet\n");
        text.Append("    build:\n      - \"/bin/sh\"\n      - \"-c\"\n      - \"echo built\"\n");
        text.Append($"    test:\n      - \"/bin/sh\"\n      - \"-c\"\n      - {ToolchainFixture.Quote(full)}\n");
        if (narrowed is not null)
        {
            text.Append($"    testFor:\n      - \"/bin/sh\"\n      - \"-c\"\n      - {ToolchainFixture.Quote(narrowed)}\n      - \"sel\"\n      - \"{{file}}\"\n    testFiles:\n      - \"tests\"\n");
        }

        return text.ToString();
    }

    private static Dictionary<string, string> Base(params (string Path, string Text)[] more) =>
        new(more.Select(file => KeyValuePair.Create(file.Path, file.Text))) { [".gitignore"] = ".flaky/\n", ["tests/FlakyTests.cs"] = "class FlakyTests {}\n" };

    private static CoderRig.Launch Green() => new(new Dictionary<string, string> { [CoderRig.StateFile] = CoderRig.Green + "\n" }, CoderRig.Report());

    private static IEnumerable<System.Text.Json.JsonElement> TestSteps(System.Text.Json.JsonElement envelope) =>
        envelope.GetProperty("checks").EnumerateArray().Where(step => step.GetProperty("step").GetString() == "test");

    private static string[] ArgvOf(System.Text.Json.JsonElement step) => [.. step.GetProperty("argv").EnumerateArray().Select(item => item.GetString()!)];

    // ---- M1: the names come from the output ----

    [Fact]
    public void M1_TheNarrowedArgv_NeverHoldsAnUnsafeName_WhoeverTheCallerIs()
    {
        TestSelection.Argv(["t", "{stem}", "{file}"], ["tests/--ignore-exit-code.cs", "tests/Ok.cs", "tests/../../x.cs", "tests/2 x.cs"], ".", 1)
            .Should().Equal("t", "Ok", "./tests/Ok.cs");
        TestSelection.IsSafe("tests/--ignore-exit-code.cs").Should().BeFalse();
        TestSelection.IsSafe("tests/./a.cs").Should().BeFalse();
        TestSelection.IsSafe("tests/Fine.cs").Should().BeTrue();
    }

    [Theory]
    [InlineData("tests/--ignore-exit-code.cs")]
    [InlineData("tests/Ghost.cs")]
    public async Task M1_AFileNameOfTheOutput_ThatIsNotASafeFileOfTheHead_GivesAWholeReRun_AndNoArgvItemTheFileDidNotWrite(string named)
    {
        const string narrowed = "echo \"passed Flaky.Tests.Sometimes (1ms)\"; echo 'Test run summary: Passed!'; echo '  total: 1'; echo '  failed: 0'; echo '  succeeded: 1'; echo '  skipped: 0'";
        await using var rig = await CoderRig.CreateAsync(toolchain: Toolchain(Passing, FailingAt(named, 2), narrowed), extra: Base());
        rig.Says("sonnet", Green());
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        var envelope = rig.Envelope(job);
        var steps = TestSteps(envelope).ToList();
        steps.Select(step => step.GetProperty("scope").GetString()).Should().Equal("full", "rerun");
        ArgvOf(steps[1]).Should().Equal(ArgvOf(steps[0]), "the re-run is the whole step again: not one item of its argv was written by the output");
        envelope.GetProperty("flaky").EnumerateArray().Single().GetProperty("rerun").GetString().Should().Be("whole");
    }

    // ---- M2 ----

    [Fact]
    public async Task M2_AGreenRoundWithAnExcusedStep_SaysSo_AndTheFlakyRowHasTheFirstExitCode()
    {
        await using var rig = await CoderRig.CreateAsync(toolchain: Toolchain(Passing, FailingAt("tests/FlakyTests.cs", 2)), extra: Base());
        rig.Says("sonnet", Green());
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        var envelope = rig.Envelope(job);
        var note = envelope.GetProperty("checksNote").GetString()!;
        note.Should().Contain("excused by a re-run").And.NotContain("every one exited 0");
        envelope.GetProperty("flaky").EnumerateArray().Single().GetProperty("firstExit").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task M2_ANarrowedReRun_DoesNotTurnAStepGreen_WhoseFirstRunEndedWithACodeThatIsNotTestsFailed()
    {
        const string narrowed = "echo \"passed Flaky.Tests.Sometimes (1ms)\"; echo 'Test run summary: Passed!'; echo '  total: 1'; echo '  failed: 0'; echo '  succeeded: 1'; echo '  skipped: 0'";
        const string secondFails = "echo 'Test run summary: Passed!'; echo '  total: 5'; echo '  failed: 0'; echo '  succeeded: 5'; echo '  skipped: 0'; echo 'ERROR: coverage 41 below 80'; exit 3";
        await using var rig = await CoderRig.CreateAsync(toolchain: Toolchain(secondFails, FailingAt("tests/FlakyTests.cs", 3), narrowed), extra: Base());
        rig.Says("sonnet", Green());
        rig.Rig.Setting("COAI_FIX_ROUNDS", "0");
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, "not-green"), ended.Reason);
        var steps = TestSteps(rig.Envelope(job)).ToList();
        steps.Select(step => step.GetProperty("scope").GetString()).Should().Equal("full", "rerun");
        steps[1].GetProperty("files").GetInt32().Should().Be(0, "the exit code 3 is no 'tests failed' code of the platform runner, so the whole step was run again");
        rig.Envelope(job).GetProperty("flaky").GetArrayLength().Should().Be(0);
    }

    // ---- M3 ----

    [Fact]
    public async Task M3_ASelectedRunThatSelectedNothing_IsNotTheFailedStep_TheDigestIsOfTheFullRun()
    {
        const string selected = "echo 'Zero tests ran'; exit 8";
        var text = new StringBuilder("version: 1\nprojects:\n  - path: \".\"\n    adapter: dotnet\n    build:\n      - \"/bin/sh\"\n      - \"-c\"\n      - \"echo built\"\n");
        text.Append($"    test:\n      - \"/bin/sh\"\n      - \"-c\"\n      - {ToolchainFixture.Quote("echo 'FAILED the full run: tests/Other.cs'; exit 1")}\n");
        text.Append($"    testFor:\n      - \"/bin/sh\"\n      - \"-c\"\n      - {ToolchainFixture.Quote(selected)}\n      - \"sel\"\n      - \"{{file}}\"\n    testFiles:\n      - \"tests\"\n");
        await using var rig = await CoderRig.CreateAsync(toolchain: text.ToString(), extra: Base());
        rig.Says("sonnet", new CoderRig.Launch(new Dictionary<string, string> { ["tests/New.cs"] = "class New {}\n", [CoderRig.StateFile] = CoderRig.Green + "\n" }, CoderRig.Report()));
        rig.Rig.Setting("COAI_FIX_ROUNDS", "0");
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, "not-green"), ended.Reason);
        var envelope = rig.Envelope(job);
        TestSteps(envelope).Select(step => (step.GetProperty("scope").GetString(), step.GetProperty("exitCode").GetInt32())).Should().Equal(("selected", 8), ("full", 1));
        var digest = envelope.GetProperty("lastDigest").GetRawText();
        digest.Should().Contain("FAILED the full run").And.NotContain("Zero tests ran");
    }

    [Fact]
    public void M3_FailedStep_SkipsANothingSelectedRecord()
    {
        var selected = new StepRecord("p", CheckStep.Test, ["x"], true, 8, false, TimeSpan.Zero, "a.log", false) { Scope = "selected", NothingSelected = true };
        var full = new StepRecord("p", CheckStep.Test, ["x"], true, 1, false, TimeSpan.Zero, "b.log", false);

        new CheckResult(CheckState.Failed, [selected, full], "n").FailedStep.Should().Be(full);
    }

    // ---- S3, S4: the rule ----

    [Fact]
    public void S3_ThePassedNameOfAnExcusedTest_IsRedactedAndBounded()
    {
        var long_ = new string('x', 500);
        FlakyRule.SafeName(long_).Length.Should().BeLessThanOrEqualTo(FlakyRule.NameLimit + 20);
        FlakyRule.SafeName("Ns.Tests.Sometimes").Should().Be("Ns.Tests.Sometimes");
    }

    [Fact]
    public void S4_TheNamedProof_AlsoAsksForACompleteSummary()
    {
        var first = new RunSummary("mtp", true, 10, 1, 9, ["T.One"], [], true);
        var input = new RerunInput(CheckStep.Test, "full", true, false, false, TimeSpan.FromSeconds(30), first, [new FailedTest("T.One", "tests/OneTests.cs")], true, new HashSet<string>(), 0, new HashSet<string>(), true) { FirstExit = 2, TestsFailedCodes = [2] };
        var decision = FlakyRule.Decide(input);
        var cut = new RunSummary("mtp", true, 3, 0, 3, [], ["T.One", "T.Two", "T.Three"], false);

        decision.Mode.Should().Be(RerunMode.Narrowed);
        FlakyRule.Excused(decision, first, new RerunOutcome(true, false, false, 0, cut)).Should().BeEmpty();
    }

    // ---- M4: a unit that cannot be stopped ----

    [Fact]
    public async Task M4_AReRunWhoseUnitCannotBeStopped_EndsTheJobCoderBoundary_NoFixTurnStarts()
    {
        const string hang = "trap '' TERM; sleep 300";
        await using var rig = await CoderRig.CreateAsync(toolchain: Toolchain(hang, FailingAt("tests/FlakyTests.cs", 2), extra: "timeouts:\n  test: 3s\n"), extra: Base());
        rig.Degrade = arguments => arguments.Any(argument => argument.StartsWith("coai-check-", StringComparison.Ordinal)) ? WithoutStopTimeout(arguments) : arguments;
        rig.Says("sonnet", Green());
        var job = rig.NewJob();
        try
        {
            var (_, ended) = await rig.RunAsync(job);

            (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, RoleStopWords.CoderBoundary), ended.Reason);
            rig.Launches(job).Should().ContainSingle("no fix turn starts while a process of a check unit may live");
        }
        finally
        {
            await KillUnitsAsync(rig, job);
        }
    }

    [Fact]
    public async Task M4_AFormatterUnitThatCannotBeStopped_EndsTheJobCoderBoundary_AndItsUnitsAreRecorded()
    {
        var text = "version: 1\nprojects:\n  - path: \".\"\n    adapter: node\n    format:\n      - \"/bin/sh\"\n      - \"-c\"\n      - " + ToolchainFixture.Quote("trap '' TERM; sleep 300") + "\n      - \"fmt\"\n      - \"{file}\"\n    build:\n      - \"/bin/sh\"\n      - \"-c\"\n      - \"echo built\"\n    test:\n      - \"/bin/sh\"\n      - \"-c\"\n      - \"echo tested\"\n";
        await using var rig = await CoderRig.CreateAsync(toolchain: text, extra: new Dictionary<string, string> { [".gitignore"] = ".flaky/\n" });
        rig.Degrade = arguments => arguments.Any(argument => argument.StartsWith("coai-check-", StringComparison.Ordinal)) ? WithoutStopTimeout(arguments) : arguments;
        rig.Says("sonnet", new CoderRig.Launch(new Dictionary<string, string> { ["a.js"] = "const x = 1;\n", [CoderRig.StateFile] = CoderRig.Green + "\n" }, CoderRig.Report()));
        var job = rig.NewJob();
        var before = CoderFormat.FormatTimeout;
        CoderFormat.FormatTimeout = TimeSpan.FromSeconds(3);
        try
        {
            var (_, ended) = await rig.RunAsync(job);

            (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, RoleStopWords.CoderBoundary), ended.Reason);
            Directory.EnumerateFiles(Path.Combine(rig.World.Store.FolderOf(job.JobId)!.Location, "checks", "format-0"), "unit-s*.json").Should().NotBeEmpty("the formatter's unit leaves a record (S5)");
        }
        finally
        {
            CoderFormat.FormatTimeout = before;
            await KillUnitsAsync(rig, job);
        }
    }

    // ---- S1: what the head ADDS to the toolchain file ----

    [Fact]
    public async Task S1_ACommandAndTestFilesTheHeadAdds_AreNamedInTheReport()
    {
        const string head0 = "version: 1\nprojects:\n  - path: \".\"\n    adapter: dotnet\n    build:\n      - \"/bin/sh\"\n      - \"-c\"\n      - \"echo built\"\n    test:\n      - \"/bin/sh\"\n      - \"-c\"\n      - \"echo tested\"\n";
        var withTestFor = head0 + "    testFor:\n      - \"/bin/sh\"\n      - \"-c\"\n      - \"echo x\"\n      - \"sel\"\n      - \"{file}\"\n    testFiles:\n      - \"tests\"\n";
        await using var rig = await CoderRig.CreateAsync(toolchain: head0, extra: Base());
        rig.Says("sonnet", new CoderRig.Launch(new Dictionary<string, string> { [".coai/toolchain.yaml"] = withTestFor, [CoderRig.StateFile] = CoderRig.Green + "\n" }, CoderRig.Report()));
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        var toolchain = rig.Envelope(job).GetProperty("toolchain");
        toolchain.GetProperty("changedTestCommands").EnumerateArray().Select(line => line.GetString()).Should().Contain(line => line!.Contains("added the testFor command", StringComparison.Ordinal));
        toolchain.GetProperty("changedDefinitions").EnumerateArray().Select(line => line.GetString()).Should().Contain(line => line!.Contains("added the testFor command", StringComparison.Ordinal));
    }

    // ---- S5 ----

    [Fact]
    public async Task S5_TheFormatterTurnsUnits_AreInTheEnvelopesChecks()
    {
        await using var rig = await CoderRig.CreateAsync(toolchain: "version: 1\nprojects:\n  - path: \".\"\n    adapter: node\n    format:\n      - \"/bin/sh\"\n      - \"-c\"\n      - \"echo formatted\"\n      - \"fmt\"\n      - \"{file}\"\n    build:\n      - \"/bin/sh\"\n      - \"-c\"\n      - \"echo built\"\n    test:\n      - \"/bin/sh\"\n      - \"-c\"\n      - \"echo tested\"\n", extra: new Dictionary<string, string> { [".gitignore"] = ".flaky/\n" });
        rig.Says("sonnet", new CoderRig.Launch(new Dictionary<string, string> { ["a.js"] = "const x = 1;\n", [CoderRig.StateFile] = CoderRig.Green + "\n" }, CoderRig.Report()));
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        var envelope = rig.Envelope(job);
        envelope.GetProperty("checks").EnumerateArray().Select(step => step.GetProperty("step").GetString()).Should().Contain("format");
        Directory.EnumerateFiles(Path.Combine(rig.World.Store.FolderOf(job.JobId)!.Location, "checks", "format-0"), "unit-s*.json").Should().NotBeEmpty();
    }

    private static async Task KillUnitsAsync(CoderRig rig, RoleJobRecord job)
    {
        foreach (var unit in await rig.UnitsOfAsync(job))
        {
            await new CoaiMcp.Runners.Processes.ProcessLauncher().RunAsync(new CoaiMcp.Runners.Processes.ProcessRequest("systemctl", ["--user", "kill", "--signal=SIGKILL", "--kill-whom=all", unit.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0]], rig.Repo) { Timeout = TimeSpan.FromSeconds(20) });
        }
    }

    private static IReadOnlyList<string> WithoutStopTimeout(IReadOnlyList<string> arguments)
    {
        var list = arguments.ToList();
        var at = list.FindIndex(argument => argument.StartsWith("TimeoutStopSec=", StringComparison.Ordinal));
        if (at > 0)
        {
            list.RemoveRange(at - 1, 2);
        }

        return list;
    }
}
