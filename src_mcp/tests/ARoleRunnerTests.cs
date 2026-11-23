using System.Diagnostics;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The runner of one job, in process (epoch 3 E1 s1, scenarios A3 and A6 at the runner's end; the exit codes of <c>--role-run</c>). The work is the <c>probe</c> role, driven by its brief — succeed, sleep, fail, a CHILD —
/// and the runner is the real <see cref="RoleRunner"/> over the real store, with a heartbeat and a watch fast enough to test.
/// </summary>
/// <remarks>In the serial <c>fakecli-env</c> collection: the runner applies <c>env.json</c> to THIS process's environment, which other classes read.</remarks>
[Collection("fakecli-env")]
public sealed class ARoleRunnerTests
{
    private static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(100);

    private static RoleRunner RunnerFor(RoleJobWorld world, TimeProvider? clock = null, List<string>? said = null) =>
        new(world.Store, RoleDescriptors.Shipped, clock ?? TimeProvider.System, line => said?.Add(line), world.Log) { WatchEvery = Fast, HeartbeatEvery = Fast };

    private static void WaitForState(RoleJobWorld world, string jobId, Func<RoleJobRecord, bool> reached, string what, int seconds = 20) =>
        RoleJobWorld.WaitFor(() => world.Store.Read(jobId) is { } job && reached(job), what, seconds);

    [Fact]
    public async Task ASuccess_IsPublishedUnderTheLease_WithTheRunnerRecordedAndAHeartbeatWritten()
    {
        using var world = new RoleJobWorld();
        var job = world.NewJob("succeed");

        var exit = await RunnerFor(world).RunAsync(job.JobId, TestContext.Current.CancellationToken);

        exit.Should().Be(RoleRunner.Done);
        var after = world.Store.Read(job.JobId)!;
        after.State.Should().Be(RoleJobStates.Succeeded);
        after.Generation.Should().Be(1);
        after.Runner!.Pid.Should().Be(Environment.ProcessId);
        after.StartedUtc.Should().NotBeNull();
        after.EndedUtc.Should().NotBeNull();
        after.Child.Should().BeNull();
        world.Store.FolderOf(job.JobId)!.ReadResult().Should().Contain("\"probe\":\"ok\"");
        world.Store.FolderOf(job.JobId)!.ReadHeartbeat()!.Generation.Should().Be(1);
    }

    [Fact]
    public async Task AFailure_IsWrittenWithItsStopAndItsReason_AndNoResult()
    {
        using var world = new RoleJobWorld();
        var job = world.NewJob("fail the model said no");

        var exit = await RunnerFor(world).RunAsync(job.JobId, TestContext.Current.CancellationToken);

        exit.Should().Be(RoleRunner.Done, "the runner took the job to a terminal state");
        var after = world.Store.Read(job.JobId)!;
        (after.State, after.Stop, after.Reason).Should().Be((RoleJobStates.Failed, "probe-failed", "the model said no"));
        File.Exists(world.Store.FolderOf(job.JobId)!.ResultFile).Should().BeFalse();
    }

    [Fact]
    public async Task ABriefTheProbeCannotRead_FailsNamingTheLegalForms()
    {
        using var world = new RoleJobWorld();
        var job = world.NewJob("sleep forever");

        await RunnerFor(world).RunAsync(job.JobId, TestContext.Current.CancellationToken);

        var after = world.Store.Read(job.JobId)!;
        after.State.Should().Be(RoleJobStates.Failed);
        after.Reason.Should().Contain(ProbeWork.Forms);
    }

    [Fact]
    public async Task ARoleWithNoWorkInThisBuild_IsClosedAsFailed_NamingTheRole()
    {
        using var world = new RoleJobWorld();
        var job = world.NewJob("anything", role: "ba");

        await RunnerFor(world).RunAsync(job.JobId, TestContext.Current.CancellationToken);

        var after = world.Store.Read(job.JobId)!;
        (after.State, after.Stop).Should().Be((RoleJobStates.Failed, "no-descriptor"));
        after.Reason.Should().Contain("'ba'");
    }

    [Fact]
    public async Task ACancel_ThatTheRunnerSees_EndsTheJobCancelled_AndTheChildIsGone()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the child form starts `sleep`");
        using var world = new RoleJobWorld();
        var job = world.NewJob("child 60");
        var running = Task.Run(() => RunnerFor(world).RunAsync(job.JobId, CancellationToken.None), TestContext.Current.CancellationToken);
        WaitForState(world, job.JobId, current => current.Child is not null, "the runner to start its child and record it");
        var child = world.Store.Read(job.JobId)!.Child!;
        RoleJobWorld.IsAlive(child).Should().BeTrue();

        world.Store.FolderOf(job.JobId)!.RequestCancel();
        var exit = await running;

        exit.Should().Be(RoleRunner.Done);
        var after = world.Store.Read(job.JobId)!;
        (after.State, after.Stop).Should().Be((RoleJobStates.Cancelled, "cancelled"));
        after.Child.Should().BeNull();
        RoleJobWorld.WaitFor(() => !RoleJobWorld.IsAlive(child), "the child of the runner to be gone");
    }

    [Fact]
    public async Task TheJobsOwnTimeout_ClosesItFailed_WithTheTimeoutNamed()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 11, 23, 9, 0, 0, TimeSpan.Zero));
        using var world = new RoleJobWorld(clock);
        var job = world.NewJob("sleep 60", timeoutMinutes: 2);
        var running = Task.Run(() => RunnerFor(world, clock).RunAsync(job.JobId, CancellationToken.None), TestContext.Current.CancellationToken);
        WaitForState(world, job.JobId, current => current.State == RoleJobStates.Running, "the runner to run");
        await clock.WhenTimersAsync(2);

        clock.Advance(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(1));
        var exit = await running;

        exit.Should().Be(RoleRunner.Done);
        var after = world.Store.Read(job.JobId)!;
        (after.State, after.Stop).Should().Be((RoleJobStates.Failed, "timeout"));
        after.Reason.Should().Contain("2 minute");
    }

    [Fact]
    public async Task A3_ARunnerWhoseJobWasTakenOver_PublishesNothing_KillsItsChild_AndExits75()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the child form starts `sleep`");
        using var world = new RoleJobWorld();
        var job = world.NewJob("child 60");
        var running = Task.Run(() => RunnerFor(world).RunAsync(job.JobId, CancellationToken.None), TestContext.Current.CancellationToken);
        WaitForState(world, job.JobId, current => current.Child is not null, "the runner to start its child");
        var child = world.Store.Read(job.JobId)!.Child!;

        world.Store.BumpGeneration(job.JobId).Should().Be(2, "a cancel fences a runner it cannot take the lease from");
        var exit = await running;

        exit.Should().Be(RoleRunner.NotTheWriter, "exit 75: the runner is no longer the writer");
        RoleJobWorld.WaitFor(() => !RoleJobWorld.IsAlive(child), "the runner's child to be killed");
        var after = world.Store.Read(job.JobId)!;
        after.State.Should().Be(RoleJobStates.Running, "the stale runner wrote no terminal state");
        after.Generation.Should().Be(2);
        File.Exists(world.Store.FolderOf(job.JobId)!.ResultFile).Should().BeFalse("and no result");
    }

    [Fact]
    public async Task ARunnerThatCannotTakeTheLease_ExitsWith75_AndStartsNothing()
    {
        using var world = new RoleJobWorld();
        var job = world.NewJob("succeed");
        using var holder = world.HoldLease(job.JobId);
        var said = new List<string>();

        var exit = await RunnerFor(world, said: said).RunAsync(job.JobId, TestContext.Current.CancellationToken);

        exit.Should().Be(RoleRunner.NotTheWriter);
        world.Store.Read(job.JobId)!.State.Should().Be(RoleJobStates.Queued);
        said.Should().ContainSingle().Which.Should().Contain("held by another runner");
    }

    [Fact]
    public async Task ARunnerOnAJobThatIsAlreadyOver_ExitsWith75_AndChangesNothing()
    {
        using var world = new RoleJobWorld();
        var job = world.NewJob("succeed");
        await RunnerFor(world).RunAsync(job.JobId, TestContext.Current.CancellationToken);
        var before = File.ReadAllText(world.Store.FolderOf(job.JobId)!.JobFile);

        var exit = await RunnerFor(world).RunAsync(job.JobId, TestContext.Current.CancellationToken);

        exit.Should().Be(RoleRunner.NotTheWriter);
        File.ReadAllText(world.Store.FolderOf(job.JobId)!.JobFile).Should().Be(before);
    }

    [Fact]
    public async Task AnUnknownJob_IsAnswered65_AndMakesNothing()
    {
        using var world = new RoleJobWorld();
        var said = new List<string>();

        var exit = await RunnerFor(world, said: said).RunAsync("r20260101000000-00000000", TestContext.Current.CancellationToken);

        exit.Should().Be(RoleRunner.NoSuchJob);
        said.Should().ContainSingle().Which.Should().Contain("no job");
        Directory.Exists(world.Store.JobsRoot).Should().BeFalse("a runner told about a job that does not exist makes no folder for it");
    }

    [Fact]
    public async Task TheEnvironmentFile_IsApplied_WithinItsAllowList_AndDeleted()
    {
        const string Allowed = "COAI_ROLE_TEST_RESTORED";
        using var world = new RoleJobWorld();
        var job = world.NewJob("succeed");
        var folder = world.Store.FolderOf(job.JobId)!;
        folder.WriteEnvironment(new RoleJobEnvironment(new Dictionary<string, string> { [Allowed] = "yes", ["ANTHROPIC_API_KEY"] = "sk-hand-edited" }));
        try
        {
            await RunnerFor(world).RunAsync(job.JobId, TestContext.Current.CancellationToken);

            Environment.GetEnvironmentVariable(Allowed).Should().Be("yes", "the runner restores the names it is allowed");
            Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY").Should().NotBe("sk-hand-edited", "a hand-edited file cannot widen the list");
            File.Exists(folder.EnvFile).Should().BeFalse("read once, then deleted");
        }
        finally
        {
            Environment.SetEnvironmentVariable(Allowed, null);
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", null);
        }
    }

    [Fact]
    public void TheEnvironment_HoldsAllowListedNamesOnly()
    {
        var source = new System.Collections.Hashtable
        {
            ["PATH"] = "/usr/bin",
            ["COAI_DATA_DIR"] = "/d",
            ["COAI_ROLE_JOBS_MAX"] = "2",
            ["ANTHROPIC_API_KEY"] = "sk-secret",
            ["COAI_BUGS_KEY"] = "secret",
            ["AWS_SECRET_ACCESS_KEY"] = "secret",
            ["GITHUB_TOKEN"] = "secret",
        };

        var captured = RoleRunEnvironment.Capture(source).Variables;

        captured.Keys.Should().BeEquivalentTo(["PATH", "COAI_DATA_DIR", "COAI_ROLE_JOBS_MAX"], "a vendor key or a token is never written to a job folder");
    }

    // ---- the mode: argv, the exit codes, never 64 ----

    [Theory]
    [InlineData("--role-run")]
    [InlineData("--role-run", "not-a-job-id")]
    [InlineData("--role-run", "r20260101000000-00000000", "--data-dir")]
    [InlineData("--role-run", "r20260101000000-00000000", "--other", "x")]
    [InlineData("--role-run", "r20260101000000-00000000", "extra")]
    [InlineData("--role-run", "../../etc", "--data-dir", "/tmp")]
    public async Task AWrongArgument_IsA65_NeverA64(params string[] args)
    {
        var said = new List<string>();

        var exit = await RoleRunMode.RunAsync(args, _ => null, said.Add, CancellationToken.None);

        exit.Should().Be(65, "this binary HAS the mode: 64 would read as an old binary");
        said.Should().ContainSingle().Which.Should().Be(RoleRunMode.Usage);
    }

    [Fact]
    public async Task AnUnknownJobId_ThroughTheMode_IsA65_AndMakesNoFolder()
    {
        using var world = new RoleJobWorld();
        var said = new List<string>();

        var exit = await RoleRunMode.RunAsync(["--role-run", "r20260101000000-00000000", "--data-dir", world.DataDir], _ => null, said.Add, CancellationToken.None);

        exit.Should().Be(65);
        Directory.Exists(Path.Combine(world.DataDir, "roles")).Should().BeFalse();
        said.Should().ContainSingle().Which.Should().Contain("no job");
    }

    [Fact]
    public async Task AnUnknownJobId_AmongKnownOnes_IsA65_Too()
    {
        using var world = new RoleJobWorld();
        world.NewJob("succeed");

        var exit = await RoleRunMode.RunAsync(["--role-run", "r20260101000000-00000000", "--data-dir", world.DataDir], _ => null, _ => { }, CancellationToken.None);

        exit.Should().Be(65);
    }

    [Fact]
    public void TheModeIsKnown_IsAWriter_AndIsNamedInTheDocument()
    {
        Program.Classify(["--role-run", "r1"]).Should().Be(Program.Startup.RoleRun, "never the usage answer — that exits 64");
        Program.WriterModes.Should().Contain(Program.Startup.RoleRun, "a named data folder that is missing is refused before it runs");
        File.ReadAllText(Path.Combine(RepoPaths.Root(), ".agents", "PROJECT.md")).Should().Contain("`--role-run`");
    }

    [Fact]
    public async Task TheRealBinary_AnUnknownJob_Exits65_NotWhatAnOldBinaryExits()
    {
        using var data = TempDir.For("coai-rolerun-");
        var info = new ProcessStartInfo(ServerBinary.Path, ["--role-run", "r20260101000000-00000000", "--data-dir", data.Path])
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.Environment["COAI_DATA_DIR"] = data.Path;
        using var process = Process.Start(info)!;
        var stdout = await process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        process.ExitCode.Should().Be(65);
        stdout.Should().BeEmpty("a runner never speaks on stdout");
    }
}
