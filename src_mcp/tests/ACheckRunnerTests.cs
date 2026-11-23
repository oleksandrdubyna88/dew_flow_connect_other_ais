using System.Diagnostics;
using System.Text;
using CoaiMcp.Core.Toolchain;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Toolchain;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The check runner (epoch 3 E2 s1 t2), against REAL processes — the fake CLI started by verb: build then test of each project, one record per step with argv, exit code, duration and log, a build that fails
/// without its test, no toolchain never green, a step over its timeout stopped with its whole process tree, no shell, <c>{jobs}</c> replaced, a minimal environment, a log cut at 20 MiB.
/// </summary>
/// <remarks>In the <c>fakecli-env</c> collection because it starts the fake CLI by verb, which is only read while <c>FAKECLI_MODE</c> is unset.</remarks>
[Collection("fakecli-env")]
public sealed class ACheckRunnerTests : IDisposable
{
    private readonly TempDir _dir = TempDir.For("coai-checks-");

    private string Tree => _dir.At("tree");

    private CheckRequest Request(int jobs = 4) => new(Tree, _dir.At("logs"), jobs);

    public ACheckRunnerTests() => Directory.CreateDirectory(Tree);

    public void Dispose() => _dir.Dispose();

    private static CheckRunner Runner() => new(new ProcessLauncher());

    private Task<CheckResult> RunAsync(string toolchainFile, int jobs = 4) => Runner().RunAsync(ToolchainFixture.Read(toolchainFile), Request(jobs));

    private static string LogOf(StepRecord step) => File.ReadAllText(step.LogPath);

    [Fact]
    public async Task ABuildAndATestThatExitZero_AreGreen_WithOneRecordPerStep()
    {
        var result = await RunAsync(ToolchainFixture.File(["emit", "build said hi"], ["emit", "tests said hi"]));

        result.Green.Should().BeTrue(result.Note);
        result.State.Should().Be(CheckState.Green);
        result.Steps.Should().HaveCount(2);
        result.Steps.Select(step => step.Step).Should().Equal(CheckStep.Build, CheckStep.Test);
        foreach (var step in result.Steps)
        {
            step.Project.Should().Be("app");
            step.Started.Should().BeTrue();
            step.ExitCode.Should().Be(0);
            step.Duration.Should().BePositive();
            step.LogPath.Should().StartWith(_dir.At("logs"));
            step.LogCut.Should().BeFalse();
        }

        result.Steps[0].Argv.Should().Equal(FakeCliInvocations.Exe, "emit", "build said hi");
        LogOf(result.Steps[0]).Should().Be("build said hi");
        LogOf(result.Steps[1]).Should().Be("tests said hi");
        result.Steps[0].LogPath.Should().NotBe(result.Steps[1].LogPath);
    }

    [Fact]
    public async Task ABuildThatFails_IsNotGreen_AndTheTestStepIsNotRun()
    {
        var counter = _dir.At("test-was-started");

        var result = await RunAsync(ToolchainFixture.File(["stderr-exit", "boom from the build", "3"], ["count", counter, "emit", "never"]));

        result.Green.Should().BeFalse();
        result.State.Should().Be(CheckState.Failed);
        result.Steps.Should().ContainSingle("the test step of a tree that does not build is not run");
        result.Steps[0].ExitCode.Should().Be(3);
        result.Steps[0].Passed.Should().BeFalse();
        LogOf(result.Steps[0]).Should().Contain("boom from the build");
        File.Exists(counter).Should().BeFalse("the fake CLI writes this file on every start, so the test step was never started");
        result.FailedStep.Should().BeSameAs(result.Steps[0]);
        result.Note.Should().Contain("build").And.Contain("exited 3");
    }

    [Fact]
    public async Task ATestThatFails_AfterAPassingBuild_IsNotGreen_AndBothStepsAreRecorded()
    {
        var result = await RunAsync(ToolchainFixture.File(["emit", "built"], ["emit-exit", "2 failed", "1"]));

        result.Green.Should().BeFalse();
        result.Steps.Select(step => step.Passed).Should().Equal(true, false);
        result.Steps[1].ExitCode.Should().Be(1);
        LogOf(result.Steps[1]).Should().Be("2 failed");
    }

    [Fact]
    public async Task NoToolchainFile_IsNotConfigured_AndNeverGreen_AndNothingRuns()
    {
        var result = await Runner().RunAsync(new ToolchainRead.NotConfigured(), Request());

        result.State.Should().Be(CheckState.NotConfigured);
        result.State.Wire().Should().Be("not_configured");
        result.Green.Should().BeFalse();
        result.Steps.Should().BeEmpty();
        Directory.Exists(_dir.At("logs")).Should().BeFalse("nothing ran, so no log folder was made");
    }

    [Fact]
    public async Task AToolchainWhoseProjectsNameNoCommand_IsNotConfigured_NotAVacuousGreen()
    {
        var result = await RunAsync(ToolchainFixture.File([], []));

        result.State.Should().Be(CheckState.NotConfigured);
        result.Green.Should().BeFalse("nothing checked is never green, whatever the file says");
        result.Steps.Should().BeEmpty();
    }

    [Fact]
    public async Task AToolchainFileThatIsRefused_IsNotGreen_AndNothingRuns()
    {
        var result = await Runner().RunAsync(new ToolchainRead.Refused(["'version' must be the whole number 1"]), Request());

        result.State.Should().Be(CheckState.ToolchainRefused);
        result.Green.Should().BeFalse();
        result.Steps.Should().BeEmpty();
        result.Note.Should().Contain("'version'");
    }

    [Fact]
    public async Task AStepOverItsTimeout_IsStopped_WithItsWholeProcessTree_AndRecordedFailed()
    {
        Assert.SkipUnless(ProcessTree.Supported, "the tree discipline lists descendants on Linux and Windows only");
        var pids = _dir.At("pids");
        var watch = Stopwatch.StartNew();

        // `serve` writes its pid and its grandchild's, answers nothing and lives for ever: a build that hangs with a child of its own.
        var result = await RunAsync(ToolchainFixture.File(["serve", pids, "-1", "grandchild", "{}"], ["emit", "never"], "timeouts:\n  build: 3s\n"));

        result.Green.Should().BeFalse();
        result.Steps.Should().ContainSingle();
        result.Steps[0].TimedOut.Should().BeTrue();
        result.Steps[0].Passed.Should().BeFalse();
        result.Note.Should().Contain("timeout");
        LogOf(result.Steps[0]).Should().Contain("process tree was killed");
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30), "a build that lives for ever must not set the pace");
        // The pids as the server wrote them: a pid that is already gone has no identity left to read, which is the point.
        var written = File.ReadAllLines(pids).Select(int.Parse).ToList();
        written.Should().HaveCount(2, "the server wrote its own pid and its grandchild's before the deadline");
        try
        {
            foreach (var pid in written)
            {
                await AssertGoneAsync(pid);
            }
        }
        finally
        {
            AReaderIsEndedWithItsTreeTests.IdentitiesIn(pids).ToList().ForEach(AReaderIsEndedWithItsTreeTests.KillIfAlive);
        }
    }

    private static async Task AssertGoneAsync(int pid)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (AReaderIsEndedWithItsTreeTests.Alive(pid) && DateTime.UtcNow < until)
        {
            await Task.Delay(50);
        }

        AReaderIsEndedWithItsTreeTests.Alive(pid).Should().BeFalse($"process {pid} of the stopped step's tree must be gone, the grandchild as well as the root");
    }

    [Fact]
    public async Task TheArgvIsStartedWithNoShell_AnArgumentIsNeverParsed()
    {
        const string Hostile = "a; echo INJECTED && $(id) `id` | cat > hostile-side-effect";

        var result = await RunAsync(ToolchainFixture.File(["emit", Hostile], ["emit", "ok"]));

        result.Green.Should().BeTrue(result.Note);
        LogOf(result.Steps[0]).Should().Be(Hostile, "a shell would have split it, expanded it or run it");
        File.Exists(Path.Combine(Tree, "hostile-side-effect")).Should().BeFalse();
    }

    [Theory]
    [InlineData(4, "-m:4")]
    [InlineData(2, "-m:2")]
    public async Task TheJobsPlaceholder_IsReplacedInsideAnArgumentItem_AndTheRecordShowsWhatWasStarted(int jobs, string expected)
    {
        var result = await RunAsync(ToolchainFixture.File(["emit", "-m:{jobs}"], ["emit", "{jobs} and {jobs}"]), jobs);

        result.Steps[0].Argv.Should().Contain(expected).And.NotContain(item => item.Contains("{jobs}", StringComparison.Ordinal));
        LogOf(result.Steps[0]).Should().Be(expected);
        LogOf(result.Steps[1]).Should().Be($"{jobs} and {jobs}");
    }

    [Fact]
    public async Task TheStepStartsWithAMinimalEnvironment_NoneOfThisProcessesOwnVariablesCrossOver()
    {
        const string Name = "COAI_CHECK_RUNNER_LEAK_PROBE";
        Environment.SetEnvironmentVariable(Name, "must-not-cross");
        try
        {
            var result = await RunAsync(ToolchainFixture.File(["env-names"], ["emit", "ok"]));

            var names = LogOf(result.Steps[0]).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            names.Should().Contain("PATH", "the program must still be found and run");
            names.Should().NotContain(Name, "a variable of this process, whatever it holds, is not the build's");
            names.Should().NotContain(name => name.StartsWith("COAI_", StringComparison.Ordinal));
            names.Should().Contain("NO_COLOR", "the output is plain text for the digest");
        }
        finally
        {
            Environment.SetEnvironmentVariable(Name, null);
        }
    }

    [Fact]
    public async Task ALogIsCutAt20MiB_AndSaysSo_AndTheStepStillPasses()
    {
        var result = await RunAsync(ToolchainFixture.File(["emit-bytes", "25000000"], ["emit", "ok"]));

        var step = result.Steps[0];
        step.LogCut.Should().BeTrue();
        step.Passed.Should().BeTrue("a chatty step is not a failed one");
        var length = new FileInfo(step.LogPath).Length;
        length.Should().BeGreaterThan(CheckLimits.LogBytes, "the output was kept up to the limit, then the cut marker");
        length.Should().BeLessThan(CheckLimits.LogBytes + 200);
        File.ReadAllText(step.LogPath).Should().EndWith("[coai: the log is cut here at 20 MiB]\n");
        result.Steps[1].LogCut.Should().BeFalse();
    }

    [Fact]
    public async Task ALogOfManyBytesAndFewCharacters_IsCutAtTheByteLimit_AndStaysValidUtf8()
    {
        // 12 million two-byte characters: 24 MB, well under the launcher's own ceiling in characters, so only the log's byte limit can cut it.
        var result = await RunAsync(ToolchainFixture.File(["emit-repeat", "é", "12000000"], []));

        var step = result.Steps[0];
        step.LogCut.Should().BeTrue();
        new FileInfo(step.LogPath).Length.Should().BeGreaterThan(CheckLimits.LogBytes - 4).And.BeLessThan(CheckLimits.LogBytes + 200);
        var text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(File.ReadAllBytes(step.LogPath));
        text.Should().EndWith("[coai: the log is cut here at 20 MiB]\n").And.StartWith("éééé");
    }

    [Fact]
    public void ACutNeverFallsInsideACharacter_SoTheLogStaysValidUtf8()
    {
        var text = new string('é', 10);
        var bytes = Encoding.UTF8.GetBytes(text);

        var length = CheckLog.CharacterBoundary(bytes, 5);

        length.Should().Be(4, "é is two bytes; a limit of 5 falls inside the third character, so the cut backs off to 4");
        new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes, 0, length).Should().Be(new string('é', 2));
        CheckLog.CharacterBoundary(bytes, 4).Should().Be(4, "a limit that is already a boundary is kept");
    }

    [Fact]
    public async Task AProgramThatIsNotThere_IsAFailedStepWithItsReasonInTheLog_NotAFaultOfTheRun()
    {
        var text = $"version: 1\nprojects:\n  - path: app\n    adapter: dotnet\n    build:\n      - {ToolchainFixture.Quote(_dir.At("no-such-program"))}\n";

        var result = await Runner().RunAsync(ToolchainFixture.Read(text), Request());

        result.State.Should().Be(CheckState.Failed);
        result.Steps.Should().ContainSingle();
        result.Steps[0].Started.Should().BeFalse();
        result.Steps[0].ExitCode.Should().Be(StepRecord.NotStartedExitCode);
        result.Steps[0].Passed.Should().BeFalse();
        LogOf(result.Steps[0]).Should().Contain("could not be started");
        result.Note.Should().Contain("could not be started");
    }

    [Fact]
    public async Task AProgramNamedWithAFolder_IsRelativeToTheRepositoryRoot_TheWayTheFileWritesIt()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the script below is a POSIX one");
        var script = Path.Combine(Tree, "tools", "run.sh");
        Directory.CreateDirectory(Path.GetDirectoryName(script)!);
        await File.WriteAllTextAsync(script, "#!/bin/sh\necho relative-ok in $(basename \"$PWD\")\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var text = "version: 1\nprojects:\n  - path: app\n    adapter: dotnet\n    test:\n      - ./tools/run.sh\n";

        var result = await Runner().RunAsync(ToolchainFixture.Read(text), Request());

        result.Green.Should().BeTrue(result.Note);
        LogOf(result.Steps[0]).Should().Be("relative-ok in tree\n", "the step ran in the tree, started by the path the file wrote");
    }

    [Fact]
    public async Task TwoProjects_RunInTheFilesOrder_BuildThenTest_AndAFailureEndsTheRun()
    {
        var text = ToolchainFixture.File(["emit", "a-build"], ["emit", "a-test"]) + "  - path: second\n    adapter: node\n    build:\n      - " + ToolchainFixture.Quote(FakeCliInvocations.Exe) + "\n      - stderr-exit\n      - second broke\n      - \"9\"\n    test:\n      - " + ToolchainFixture.Quote(FakeCliInvocations.Exe) + "\n      - emit\n      - never\n";

        var result = await Runner().RunAsync(ToolchainFixture.Read(text), Request());

        result.Steps.Select(step => (step.Project, step.Step)).Should().Equal(("app", CheckStep.Build), ("app", CheckStep.Test), ("second", CheckStep.Build));
        result.State.Should().Be(CheckState.Failed);
        result.Steps[2].ExitCode.Should().Be(9);
    }

    [Fact]
    public async Task ARunThatIsCancelled_Throws_AndIsNeverReportedAsAFailedStep()
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(700));

        var run = () => Runner().RunAsync(ToolchainFixture.Read(ToolchainFixture.File(["sleep", "20000"], [])), Request(), cancel.Token);

        await run.Should().ThrowAsync<OperationCanceledException>("a job withdrawn is not a build that failed");
    }
}
