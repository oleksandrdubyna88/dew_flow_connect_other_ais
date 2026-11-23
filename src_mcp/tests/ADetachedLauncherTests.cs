using System.Diagnostics;
using System.Globalization;
using CoaiMcp.Runners.Detach;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The detached launcher and the runner it starts, in REAL processes (epoch 3 E1 s1, scenarios A1 and A14 of <c>v2/epoch-3/e1-role-jobs-ba/SPEC.md</c>; the measurements behind them are
/// <c>v2/spikes/RESULTS_detach.md</c>). The runner here is the built <c>coai-mcp</c> itself, running the <c>probe</c> role — a double in its place would be a second product.
/// </summary>
/// <remarks>
/// The systemd cases are SKIPPED, with the reason, on a machine without a user manager. The <c>setsid</c> road is forced where the unit is not wanted, which is also how a machine with no user manager is simulated.
/// In the serial <c>fakecli-env</c> collection: FakeCli reads its argv verbs only while <c>FAKECLI_MODE</c> is unset.
/// </remarks>
[Collection("fakecli-env")]
public sealed class ADetachedLauncherTests
{
    private static string Runner => ServerBinary.Path;

    private static DetachSpec SpecFor(RoleJobWorld world, string jobId, int megabytes = 512) =>
        new(Runner, RoleRunMode.ArgumentsFor(jobId, world.DataDir), world.Store.FolderOf(jobId)!.Location, RoleJobIds.UnitOf(jobId), megabytes);

    private static RoleJobRecord Prepared(RoleJobWorld world, string brief, int timeoutMinutes = 5)
    {
        var job = world.NewJob(brief, timeoutMinutes);
        world.Store.FolderOf(job.JobId)!.WriteEnvironment(RoleRunEnvironment.Capture(Environment.GetEnvironmentVariables()));

        return job;
    }

    private static void WaitForState(RoleJobWorld world, string jobId, Func<RoleJobRecord, bool> reached, string what, int seconds = 40) =>
        RoleJobWorld.WaitFor(() => world.Store.Read(jobId) is { } job && reached(job), what, seconds);

    // ---- A1: the starter is killed — the job ends succeeded, and a new server reads it ----

    [Theory]
    [InlineData("systemd-run")]
    [InlineData("setsid")]
    public void A1_TheStarterIsKilled_TheRunnerGoesOn_TheJobEndsSucceeded_AndANewStoreReadsIt(string road)
    {
        if (road == "systemd-run")
        {
            Assert.SkipUnless(SystemdUser.Works(out var why), why);
        }
        else
        {
            Assert.SkipWhen(OperatingSystem.IsWindows(), "setsid is a POSIX program");
        }

        using var world = new RoleJobWorld();
        var job = Prepared(world, "sleep 4");
        var spec = SpecFor(world, job.JobId);
        var (program, arguments) = road == "systemd-run"
            ? ("systemd-run", DetachedLauncher.SystemdRunArguments(spec))
            : ("/bin/sh", DetachedLauncher.SetsidShellArguments(spec));

        // The starter: its own session and process group, running the launcher's REAL command line and then staying alive — the way a server stays up after a start_role.
        var starter = Process.Start(new ProcessStartInfo("setsid", ["bash", "-c", "\"$0\" \"$@\"; sleep 300", program, .. arguments]) { UseShellExecute = false })!;
        try
        {
            WaitForState(world, job.JobId, current => current.State == RoleJobStates.Running, "the runner to take the job and run it");
            var runner = world.Store.Read(job.JobId)!.Runner!;

            RoleJobWorld.Signal("KILL", starter, group: true);
            starter.WaitForExit(10_000);
            starter.HasExited.Should().BeTrue("the starter and its whole process group are dead");
            RoleJobWorld.IsAlive(runner).Should().BeTrue("the runner is not a child of the starter: it was detached");

            WaitForState(world, job.JobId, current => current.Terminal, "the job to end although its starter is dead");
        }
        finally
        {
            if (!starter.HasExited)
            {
                starter.Kill(entireProcessTree: true);
            }

            starter.Dispose();
        }

        var after = new RoleJobStore(world.DataDir, TimeProvider.System, Serilog.Core.Logger.None).Read(job.JobId)!;
        after.State.Should().Be(RoleJobStates.Succeeded, "a fresh store — a new server — reads what the detached runner wrote");
        after.Generation.Should().Be(1);
        after.Runner.Should().NotBeNull();
        after.StartedUtc.Should().NotBeNull();
        after.EndedUtc.Should().NotBeNull();
        world.Store.FolderOf(job.JobId)!.ReadResult().Should().Contain("\"probe\":\"ok\"");
        File.Exists(world.Store.FolderOf(job.JobId)!.EnvFile).Should().BeFalse("the environment file is read once and deleted");
    }

    [Theory]
    [InlineData(LauncherChoice.SystemdRun, "systemd-run")]
    [InlineData(LauncherChoice.Setsid, "setsid")]
    public async Task TheLauncher_RecordsTheRoadThatWorked(LauncherChoice choice, string launcher)
    {
        if (choice == LauncherChoice.SystemdRun)
        {
            Assert.SkipUnless(SystemdUser.Works(out var why), why);
        }
        else
        {
            Assert.SkipWhen(OperatingSystem.IsWindows(), "setsid is a POSIX program");
        }

        using var world = new RoleJobWorld();
        var job = Prepared(world, "succeed");

        var result = await new DetachedLauncher(new ProcessLauncher()).StartAsync(SpecFor(world, job.JobId), choice, TestContext.Current.CancellationToken);

        result.Started.Should().BeTrue(result.Detail);
        result.Launcher.Should().Be(launcher);
        result.Unit.Should().Be(choice == LauncherChoice.SystemdRun ? RoleJobIds.UnitOf(job.JobId) : string.Empty);
        WaitForState(world, job.JobId, current => current.Terminal, "the job to end");
        world.Store.Read(job.JobId)!.State.Should().Be(RoleJobStates.Succeeded);
    }

    [Fact]
    public async Task TheLauncher_ReturnsAtOnce_WithoutWaitingForTheRunner()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "setsid is a POSIX program");
        using var world = new RoleJobWorld();
        var job = Prepared(world, "sleep 20");
        var clock = Stopwatch.StartNew();

        var result = await new DetachedLauncher(new ProcessLauncher()).StartAsync(SpecFor(world, job.JobId), LauncherChoice.Setsid, TestContext.Current.CancellationToken);

        result.Started.Should().BeTrue(result.Detail);
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2), "the runner's streams are the null device, so nothing of the launcher's waits for the runner's life");
        await world.Keeper.CancelAsync(job.JobId, TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    // ---- the order of the roads: by trying, never by probing ----

    [Fact]
    public async Task AUserManagerThatAnswersNonZero_SendsTheStartToSetsid_AndTheResultSaysSo()
    {
        var asked = new List<string>();
        var launcher = new DetachedLauncher(new ScriptedProcesses(asked, request => request.Executable == "systemd-run" ? 1 : 0, stderrOf: _ => "Failed to connect to user scope bus"));

        var result = await launcher.StartAsync(new DetachSpec("/bin/coai-mcp", ["--role-run", "r20261123090000-00000000"], "/tmp", "coai-role-x", 4096), LauncherChoice.Auto, TestContext.Current.CancellationToken);

        asked.Should().Equal(OperatingSystem.IsLinux() ? ["systemd-run", "/bin/sh"] : ["/bin/sh"]);
        result.Started.Should().BeTrue();
        result.Launcher.Should().Be("setsid", "the road recorded is the one that worked");
    }

    [Fact]
    public async Task ANamedRoad_IsNeverLeftForAnother()
    {
        var asked = new List<string>();
        var launcher = new DetachedLauncher(new ScriptedProcesses(asked, _ => 1, stderrOf: _ => "no bus"));

        var result = await launcher.StartAsync(new DetachSpec("/bin/coai-mcp", [], "/tmp", "coai-role-x", 4096), LauncherChoice.SystemdRun, TestContext.Current.CancellationToken);

        result.Started.Should().BeFalse();
        asked.Should().Equal("systemd-run");
        result.Detail.Should().Contain("systemd-run").And.Contain("no bus");
    }

    [Fact]
    public async Task EveryRoadFailing_IsOneAnswerThatNamesEachOfThem()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the POSIX roads");
        var launcher = new DetachedLauncher(new ScriptedProcesses([], _ => 127, stderrOf: _ => "not found"));

        var result = await launcher.StartAsync(new DetachSpec("/bin/coai-mcp", [], "/tmp", "coai-role-x", 4096), LauncherChoice.Auto, TestContext.Current.CancellationToken);

        result.Started.Should().BeFalse();
        result.Launcher.Should().BeEmpty();
        result.Detail.Should().Contain("setsid").And.Contain("sh-nohup").And.Contain("not found");
    }

    [Fact]
    public async Task ARoadThatDoesNotAnswerInTime_IsFailedOver_NotWaitedFor()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the POSIX roads");
        var asked = new List<string>();
        var launcher = new DetachedLauncher(new ScriptedProcesses(asked, request => request.Executable == "systemd-run" ? -1 : 0, stderrOf: _ => string.Empty));

        var result = await launcher.StartAsync(new DetachSpec("/bin/coai-mcp", [], "/tmp", "coai-role-x", 4096), LauncherChoice.Auto, TestContext.Current.CancellationToken);

        result.Started.Should().BeTrue();
        result.Launcher.Should().Be("setsid", "a road that did not answer in time is failed over, never waited for");
    }

    [Fact]
    public void TheSystemdCommand_CarriesBothMemoryProperties_TheUnitAndTheFolder_AndNoValueOfTheEnvironment()
    {
        var spec = new DetachSpec("/opt/coai-mcp", ["--role-run", "r20261123090000-00000000", "--data-dir", "/data"], "/data/roles/jobs/r20261123090000-00000000", "coai-role-r20261123090000-00000000", 2048);

        var arguments = DetachedLauncher.SystemdRunArguments(spec);

        arguments.Should().ContainInOrder("--user", "--collect");
        arguments.Should().Contain("MemoryMax=2048M", "the ceiling");
        arguments.Should().Contain("MemorySwapMax=0", "without it the swap absorbs the excess and nothing is killed (RESULTS_spikes_2026-11-23 spike 2)");
        arguments.Should().Contain("OOMPolicy=continue", "or an oom-killed child stops the unit with its runner, and the job is lost instead of failing over");
        arguments.Should().ContainInOrder("--unit", "coai-role-r20261123090000-00000000");
        arguments.Should().ContainInOrder("--working-directory", "/data/roles/jobs/r20261123090000-00000000");
        arguments.Should().ContainInOrder("--", "/opt/coai-mcp", "--role-run");
        arguments.Should().NotContain(argument => argument.StartsWith("--setenv", StringComparison.Ordinal) || argument.Contains("KEY", StringComparison.Ordinal), "no key in argv or unit properties");
    }

    [Fact]
    public void TheShellRoads_GiveTheRunnerTheNullDeviceForItsStreams_AndItsArgumentsUntouched()
    {
        var spec = new DetachSpec("/opt/my coai/coai-mcp", ["--role-run", "r1", "--data-dir", "/da ta"], "/tmp", "u", 1);

        DetachedLauncher.SetsidShellArguments(spec).Should().Equal("-c", "exec setsid -f \"$0\" \"$@\" </dev/null >/dev/null 2>&1", "/opt/my coai/coai-mcp", "--role-run", "r1", "--data-dir", "/da ta");
        DetachedLauncher.NohupShellArguments(spec)[1].Should().Contain("nohup").And.Contain("</dev/null >/dev/null 2>&1 &");
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("two words", "\"two words\"")]
    [InlineData("", "\"\"")]
    [InlineData("a\"b", "\"a\\\"b\"")]
    [InlineData("C:\\dir with space\\", "\"C:\\dir with space\\\\\"")]
    [InlineData("C:\\plain\\dir", "C:\\plain\\dir")]
    public void TheWindowsCommandLine_QuotesAnArgumentByTheRulesOfCommandLineToArgvW(string argument, string quoted)
    {
        WindowsDetach.Quoted(argument).Should().Be(quoted);
        WindowsDetach.CommandLine(new DetachSpec("C:\\coai mcp\\coai-mcp.exe", ["--role-run", argument], "C:\\", "u", 1)).Should().Be($"\"C:\\coai mcp\\coai-mcp.exe\" --role-run {quoted}");
    }

    // ---- A14: the unit shows both memory properties; an over-limit child is killed by the kernel ----

    [Fact]
    public async Task A14_TheUnitOfARealRunner_ShowsTheMemoryProperties_AndEndsWithItsJob()
    {
        Assert.SkipUnless(SystemdUser.Works(out var why), why);
        using var world = new RoleJobWorld();
        var job = Prepared(world, "sleep 30");
        var launched = await new DetachedLauncher(new ProcessLauncher()).StartAsync(SpecFor(world, job.JobId, megabytes: 777), LauncherChoice.SystemdRun, TestContext.Current.CancellationToken);
        launched.Started.Should().BeTrue(launched.Detail);
        WaitForState(world, job.JobId, current => current.State == RoleJobStates.Running, "the runner to run");

        var shown = SystemdUser.Show(RoleJobIds.UnitOf(job.JobId), "MemoryMax", "MemorySwapMax", "OOMPolicy", "KillMode");

        shown.Should().Contain($"MemoryMax={777L * 1024 * 1024}");
        shown.Should().Contain("MemorySwapMax=0");
        shown.Should().Contain("OOMPolicy=continue");
        shown.Should().Contain("KillMode=control-group", "stopping the unit takes the CLI child with it");

        var cancelled = await world.Keeper.CancelAsync(job.JobId, TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        cancelled!.State.Should().Be(RoleJobStates.Cancelled);
        RoleJobWorld.WaitFor(() => !SystemdUser.IsActive(RoleJobIds.UnitOf(job.JobId)), "the unit to end with its runner");
    }

    [Fact]
    public async Task A14_AnOverLimitChild_IsKilledByTheKernel_AndItsParentLivesToSaySo()
    {
        Assert.SkipUnless(SystemdUser.Works(out var why), why);
        using var world = new RoleJobWorld();
        var folder = Path.Combine(world.DataDir, "hog");
        Directory.CreateDirectory(folder);
        var log = Path.Combine(folder, "alloc.log");
        var result = Path.Combine(folder, "result.txt");
        var spec = new DetachSpec(
            RoleJobWorld.FakeCli,
            ["parent-of-alloc", "800", log, result],
            folder,
            RoleJobIds.UnitOf(RoleJobIds.New(TimeProvider.System)),
            MegabytesOfTheUnit);

        var launched = await new DetachedLauncher(new ProcessLauncher()).StartAsync(spec, LauncherChoice.SystemdRun, TestContext.Current.CancellationToken);

        launched.Started.Should().BeTrue(launched.Detail);
        RoleJobWorld.WaitFor(() => File.Exists(result), "the parent to report how its child ended", seconds: 60);
        var steps = File.ReadAllLines(log).Count(line => line.StartsWith("step", StringComparison.Ordinal));
        File.ReadAllText(result).Should().Be("exit=137", "the child ended by SIGKILL — the kernel's OOM kill inside the unit — and the parent, which is small, survived to write that");
        File.ReadAllLines(log).Should().NotContain("done", "the child never got its eight hundred megabytes");
        steps.Should().BeInRange(1, 79, $"it was killed somewhere under the {MegabytesOfTheUnit} MiB ceiling plus what a process needs of its own, not at the end");
        RoleJobWorld.WaitFor(() => !SystemdUser.IsActive(spec.UnitName), "the unit to end");
    }

    private const int MegabytesOfTheUnit = 300;

    /// <summary>A process launcher that answers from a script and records which program each request started — the seam the road order is tested through.</summary>
    private sealed class ScriptedProcesses(List<string> asked, Func<ProcessRequest, int> exitOf, Func<ProcessRequest, string> stderrOf) : IProcessLauncher
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            asked.Add(request.Executable);
            var exit = exitOf(request);

            return Task.FromResult(exit < 0
                ? new ProcessResult(-1, string.Empty, string.Empty, TimedOut: true)
                : new ProcessResult(exit, string.Empty, stderrOf(request), TimedOut: false));
        }
    }
}
