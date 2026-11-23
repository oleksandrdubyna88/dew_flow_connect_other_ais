using System.Collections;
using System.Diagnostics;
using System.Text.Json;
using CoaiMcp.Core.Notices;
using CoaiMcp.Runners.Detach;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What <c>start_role</c>, <c>role_status</c> and <c>role_cancel</c> do (epoch 3 E1 s1, scenarios A6 and A15 of <c>v2/epoch-3/e1-role-jobs-ba/SPEC.md</c>, and spec section E.3), through the service the tools call
/// — over the real store, the real launcher and the built <c>coai-mcp</c> as the runner, running the <c>probe</c> role. The tools over a real stdio server are <see cref="ARoleToolsOverStdioTests"/>.
/// </summary>
/// <remarks>In the serial <c>fakecli-env</c> collection: FakeCli verbs, and a runner that reads the environment the tests set.</remarks>
[Collection("fakecli-env")]
public sealed class ARoleToolsTests
{
    private sealed class Rig : IDisposable
    {
        private readonly RoleJobWorld _world;
        internal Rig(Dictionary<string, string?>? settings = null, TimeProvider? clock = null, DetachedLauncher? launcher = null, bool onWsl = false, TimeSpan? grace = null, IDictionary? environment = null)
        {
            _world = new RoleJobWorld(clock);
            Settings = settings ?? new Dictionary<string, string?> { ["COAI_ROLE_LAUNCHER"] = "setsid" };
            Service = new RoleJobService(_world.DataDir, name => Settings.GetValueOrDefault(name), _world.Log, notice => { _world.Notices.Enqueue(notice); return true; })
            {
                Clock = _world.Clock,
                Runner = (ServerBinary.Path, []),
                Launcher = launcher ?? new DetachedLauncher(new ProcessLauncher()),
                OnWsl = onWsl,
                CancelGrace = grace ?? TimeSpan.FromSeconds(10),
                EnvironmentSource = environment ?? Environment.GetEnvironmentVariables(),
            };
        }

        internal Dictionary<string, string?> Settings { get; }

        internal RoleJobService Service { get; }

        internal RoleJobWorld World => _world;

        internal async Task<JsonElement> Start(string brief, string role = "probe", int? timeout = null, string? taskFile = null, string? model = null, string? workItem = null, string? repo = null) =>
            Parse(await Service.StartAsync(role, repo ?? _world.DataDir, taskFile, brief, model, timeout, workItem, TestContext.Current.CancellationToken));

        internal async Task<JsonElement> Status(string id, int wait = 0) => Parse(await Service.StatusAsync(id, wait, TestContext.Current.CancellationToken));

        internal async Task<JsonElement> Cancel(string id) => Parse(await Service.CancelAsync(id, TestContext.Current.CancellationToken));

        public void Dispose() => _world.Dispose();
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static string Id(JsonElement answer) => answer.GetProperty("jobId").GetString()!;

    private static void WaitForState(Rig rig, string id, Func<RoleJobRecord, bool> reached, string what, int seconds = 30) =>
        RoleJobWorld.WaitFor(() => rig.World.Store.Read(id) is { } job && reached(job), what, seconds);

    private static DetachedLauncher LauncherThatStartsNothing(int exit = 0) => new(new NothingStarted(exit));

    private sealed class NothingStarted(int exit) : IProcessLauncher
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default) =>
            Task.FromResult(new ProcessResult(exit, string.Empty, exit == 0 ? string.Empty : "no runner could be started in this test", TimedOut: false));
    }

    // ---- start_role ----

    [Fact]
    public async Task AStart_AnswersAtOnce_WithTheJobTheStateTheRouteAndTheLauncher_AndTheRunnerIsLaunchedDetached()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the setsid road");
        using var rig = new Rig();
        var clock = Stopwatch.StartNew();

        var answer = await rig.Start("sleep 20");

        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2), "start_role answers in two seconds or less: it never waits for the runner");
        RoleJobIds.IsValid(Id(answer)).Should().BeTrue();
        answer.GetProperty("state").GetString().Should().BeOneOf(RoleJobStates.Queued, RoleJobStates.Starting, RoleJobStates.Running);
        answer.GetProperty("route").ValueKind.Should().Be(JsonValueKind.Array);
        answer.GetProperty("launcher").GetString().Should().Be("setsid");
        WaitForState(rig, Id(answer), job => job.State == RoleJobStates.Running, "the runner to be running");
        rig.World.Store.Read(Id(answer))!.Launcher.Should().Be("setsid", "the road is recorded in the job");
        await rig.Cancel(Id(answer));
    }

    [Fact]
    public async Task AStart_ThroughTheUnit_AnswersAtOnce_TooAndSaysSystemdRun()
    {
        Assert.SkipUnless(SystemdUser.Works(out var why), why);
        using var rig = new Rig(new Dictionary<string, string?> { ["COAI_ROLE_LAUNCHER"] = "auto" });
        var clock = Stopwatch.StartNew();

        var answer = await rig.Start("sleep 20");

        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
        answer.GetProperty("launcher").GetString().Should().Be("systemd-run");
        await rig.Cancel(Id(answer));
        RoleJobWorld.WaitFor(() => !SystemdUser.IsActive(RoleJobIds.UnitOf(Id(answer))), "the unit to end");
    }

    [Fact]
    public async Task ARoleWithoutADescriptor_IsRefused_NamingTheOnesThatCanBeStarted_AndMakesNoFolder()
    {
        using var rig = new Rig(launcher: LauncherThatStartsNothing());

        foreach (var role in new[] { "ba", "nonesuch", "coder", string.Empty })
        {
            var answer = await rig.Start("succeed", role);

            answer.GetProperty("error").GetString().Should().Contain("no descriptor").And.Contain("probe", $"role '{role}' is refused naming what can be started");
        }

        Directory.Exists(rig.World.Store.JobsRoot).Should().BeFalse("a refused start makes no job folder");
    }

    [Fact]
    public async Task ARoleWithADescriptorOfItsOwn_IsStartedThroughTheSameRoad()
    {
        var descriptors = new List<RoleDescriptor> { new("scribe", "a test role", () => new ProbeWork()) };
        using var rig = new Rig(launcher: LauncherThatStartsNothing());
        var service = new RoleJobService(rig.World.DataDir, _ => null, rig.World.Log, _ => true)
        {
            Descriptors = descriptors,
            Runner = (ServerBinary.Path, []),
            Launcher = LauncherThatStartsNothing(),
        };

        var answer = Parse(await service.StartAsync("scribe", rig.World.DataDir, null, "succeed", null, null, null, TestContext.Current.CancellationToken));

        rig.World.Store.Read(Id(answer))!.Role.Should().Be("scribe");
    }

    public static TheoryData<string, string> Refusals => new()
    {
        { "neither", "exactly one of taskFile" },
        { "both", "exactly one of taskFile" },
        { "absolute", "taskFile must be a path inside the repository" },
        { "dotdot", "no `..` segment" },
        { "drive", "taskFile must be a path inside the repository" },
        { "timeout0", "timeoutMinutes must be from 1 to 1440" },
        { "timeout1441", "timeoutMinutes must be from 1 to 1440" },
        { "modelspace", "model must be one model name" },
        { "workitem", "workItem" },
        { "norepo", "is not a folder" },
        { "hugebrief", "exactly one of taskFile" },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task ABadArgument_IsRefusedWithTheCure_BeforeAnyFolderIsMade(string which, string sentence)
    {
        using var rig = new Rig(launcher: LauncherThatStartsNothing());

        var answer = which switch
        {
            "neither" => await rig.Start(string.Empty),
            "both" => await rig.Start("succeed", taskFile: "v2/e1/task.yaml"),
            "absolute" => await rig.Start(string.Empty, taskFile: "/etc/passwd"),
            "dotdot" => await rig.Start(string.Empty, taskFile: "v2/../../outside/task.yaml"),
            "drive" => await rig.Start(string.Empty, taskFile: "C:\\x\\task.yaml"),
            "timeout0" => await rig.Start("succeed", timeout: 0),
            "timeout1441" => await rig.Start("succeed", timeout: 1441),
            "modelspace" => await rig.Start("succeed", model: "two words"),
            "workitem" => await rig.Start("succeed", workItem: "../x"),
            "norepo" => await rig.Start("succeed", repo: Path.Combine(rig.World.DataDir, "no-such-folder")),
            _ => await rig.Start(new string('x', 65_537)),
        };

        answer.GetProperty("error").GetString().Should().Contain(sentence);
        Directory.Exists(rig.World.Store.JobsRoot).Should().BeFalse();
    }

    [Fact]
    public async Task ATaskFile_IsStoredAsOneSpelling_WithTheWorkItemAndTheRequest()
    {
        using var rig = new Rig(launcher: LauncherThatStartsNothing());

        var answer = await rig.Start(string.Empty, taskFile: "v2\\epoch-3\\./e1\\epic.yaml", model: "fable", workItem: "v2/epoch-3/e1", timeout: 45);

        var request = rig.World.Store.ReadRequest(Id(answer))!;
        (request.TaskFile, request.Model, request.WorkItem, request.TimeoutMinutes, request.Role).Should().Be(("v2/epoch-3/e1/epic.yaml", "fable", "v2/epoch-3/e1", 45, "probe"));
        rig.World.Store.Read(Id(answer))!.TaskFile.Should().Be("v2/epoch-3/e1/epic.yaml");
    }

    [Fact]
    public async Task TheEnvironmentFile_HoldsAllowListedNamesOnly_AndIsOwnerOnly()
    {
        var environment = new Hashtable { ["PATH"] = "/usr/bin", ["HOME"] = "/home/x", ["COAI_DATA_DIR"] = "/d", ["ANTHROPIC_API_KEY"] = "sk-secret", ["COAI_BUGS_KEY"] = "secret" };
        using var rig = new Rig(launcher: LauncherThatStartsNothing(), environment: environment);

        var answer = await rig.Start("succeed");

        var folder = rig.World.Store.FolderOf(Id(answer))!;
        var text = File.ReadAllText(folder.EnvFile);
        text.Should().Contain("/usr/bin").And.NotContain("sk-secret").And.NotContain("COAI_BUGS_KEY");
        if (!OperatingSystem.IsWindows())
        {
            File.GetUnixFileMode(folder.EnvFile).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Fact]
    public async Task AStartWhoseRunnerCannotBeLaunched_IsRefusedWithTheReason_AndTheJobIsClosedFailed()
    {
        using var rig = new Rig(launcher: LauncherThatStartsNothing(exit: 1));

        var answer = await rig.Start("succeed");

        answer.GetProperty("error").GetString().Should().Contain("could not be started").And.Contain("no runner could be started");
        var job = rig.World.Store.Read(Id(answer))!;
        (job.State, job.Stop).Should().Be((RoleJobStates.Failed, "launch-failed"));
        File.Exists(rig.World.Store.FolderOf(job.JobId)!.EnvFile).Should().BeFalse("no environment is left in a folder nothing will read");
    }

    // ---- A15: the cap — refuse, never queue ----

    [Fact]
    public async Task A15_AtTheCap_AStartIsRefused_NamingTheLiveJobs_AndNothingIsQueued()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the setsid road");
        using var rig = new Rig(new Dictionary<string, string?> { ["COAI_ROLE_LAUNCHER"] = "setsid", ["COAI_ROLE_JOBS_MAX"] = "2" });
        var first = Id(await rig.Start("sleep 60"));
        var second = Id(await rig.Start("sleep 60"));

        var third = await rig.Start("sleep 60");

        var refusal = third.GetProperty("error").GetString()!;
        refusal.Should().Contain("cap is 2").And.Contain(first).And.Contain(second).And.Contain("never queued");
        rig.World.Store.JobIds().Should().Equal(new[] { first, second }.Order(StringComparer.Ordinal), "the refused start made no job");

        await rig.Cancel(first);
        var after = await rig.Start("succeed");
        after.TryGetProperty("jobId", out _).Should().BeTrue("a slot was freed");
        await rig.Cancel(second);
    }

    [Theory]
    [InlineData(false, 3)]
    [InlineData(true, 2)]
    public async Task A15_TheDefaultCap_IsThree_AndTwoOnWsl(bool onWsl, int cap)
    {
        using var rig = new Rig(new Dictionary<string, string?>(), launcher: LauncherThatStartsNothing(), onWsl: onWsl);
        for (var started = 0; started < cap; started++)
        {
            (await rig.Start("succeed")).TryGetProperty("jobId", out _).Should().BeTrue();
        }

        var refused = await rig.Start("succeed");

        refused.GetProperty("error").GetString().Should().Contain($"cap is {cap}");
    }

    [Theory]
    [InlineData("COAI_ROLE_JOBS_MAX", "0")]
    [InlineData("COAI_ROLE_JOBS_MAX", "many")]
    [InlineData("COAI_ROLE_JOBS_MAX", "21")]
    [InlineData("COAI_ROLE_MEMORY_MB", "12")]
    [InlineData("COAI_ROLE_LAUNCHER", "docker")]
    public async Task ASettingThatIsNotLegal_RefusesTheStart_NamingTheKeyAndWhatIsLegal(string key, string value)
    {
        using var rig = new Rig(new Dictionary<string, string?> { [key] = value }, launcher: LauncherThatStartsNothing());

        var answer = await rig.Start("succeed");

        answer.GetProperty("error").GetString().Should().Contain(key).And.Contain(value).And.Contain("must");
        Directory.Exists(rig.World.Store.JobsRoot).Should().BeFalse();
    }

    [Fact]
    public async Task A15_AJobWhoseRunnerDied_DoesNotHoldASlotForEver()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 11, 23, 9, 0, 0, TimeSpan.Zero));
        using var rig = new Rig(new Dictionary<string, string?> { ["COAI_ROLE_JOBS_MAX"] = "1" }, clock, LauncherThatStartsNothing());
        var dead = Id(await rig.Start("succeed"));
        (await rig.Start("succeed")).GetProperty("error").GetString().Should().Contain("cap is 1");

        clock.Advance(TimeSpan.FromMinutes(2));
        var next = await rig.Start("succeed");

        next.TryGetProperty("jobId", out _).Should().BeTrue("the sweep closed the dead job as lost and freed its slot");
        rig.World.Store.Read(dead)!.State.Should().Be(RoleJobStates.Lost);
    }

    [Fact]
    public async Task TwoStartsAtTheCap_CannotBothPass()
    {
        using var rig = new Rig(new Dictionary<string, string?> { ["COAI_ROLE_JOBS_MAX"] = "1" }, launcher: LauncherThatStartsNothing());

        var answers = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => rig.Start("succeed")));

        answers.Count(answer => answer.TryGetProperty("jobId", out _)).Should().Be(1, "the count and the creation are one step under a lock file");
        rig.World.Store.JobIds().Should().HaveCount(1);
    }

    // ---- role_status ----

    [Fact]
    public async Task AStatus_ForAnUnknownOrMalformedJob_IsARefusal_AndTheWaitIsBounded()
    {
        using var rig = new Rig(launcher: LauncherThatStartsNothing());

        (await rig.Status("r20260101000000-00000000")).GetProperty("error").GetString().Should().Contain("no role job");
        (await rig.Status("../../x")).GetProperty("error").GetString().Should().Contain("not a role job id");
        (await rig.Status("r20260101000000-00000000", 51)).GetProperty("error").GetString().Should().Contain("0 to 50");
        (await rig.Status("r20260101000000-00000000", -1)).GetProperty("error").GetString().Should().Contain("0 to 50");
    }

    [Fact]
    public async Task AStatus_HasTheShapeOfTheSpec_AndWaitsForTheEndWhenAsked()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the setsid road");
        using var rig = new Rig();
        var id = Id(await rig.Start("sleep 2"));

        var immediate = await rig.Status(id);
        new[] { RoleJobStates.Succeeded, RoleJobStates.Failed }.Should().NotContain(immediate.GetProperty("state").GetString());
        foreach (var key in new[] { "state", "generation", "attempts", "stalled", "result", "role", "launcher", "route", "stop", "reason" })
        {
            immediate.TryGetProperty(key, out _).Should().BeTrue($"role_status answers {key}");
        }

        immediate.GetProperty("result").ValueKind.Should().Be(JsonValueKind.Null, "no result until the job has one");

        var waited = await rig.Status(id, wait: 30);

        waited.GetProperty("state").GetString().Should().Be(RoleJobStates.Succeeded);
        waited.GetProperty("generation").GetInt32().Should().Be(1);
        waited.GetProperty("stalled").GetBoolean().Should().BeFalse();
        waited.GetProperty("result").GetProperty("probe").GetString().Should().Be("ok", "the result is embedded as JSON, not as a string");
        waited.GetProperty("attempts").GetArrayLength().Should().Be(0, "attempts are the failover loop's (story s3)");
    }

    [Fact]
    public async Task AStatus_ClosesADeadRunnersJobAsLost_WithOneNotice()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 11, 23, 9, 0, 0, TimeSpan.Zero));
        using var rig = new Rig(clock: clock, launcher: LauncherThatStartsNothing());
        var id = Id(await rig.Start("succeed"));
        clock.Advance(TimeSpan.FromMinutes(2));

        (await rig.Status(id)).GetProperty("state").GetString().Should().Be(RoleJobStates.Lost);
        (await rig.Status(id)).GetProperty("state").GetString().Should().Be(RoleJobStates.Lost);

        rig.World.NoticeCodes.Should().Equal(ServerNoticeCodes.RoleJobLost);
    }

    // ---- A6 and E.3: role_cancel ----

    [Fact]
    public async Task A6_ACancelOfALiveJob_StopsTheRunnerAndItsChild_AndTheJobIsCancelled()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the child form starts `sleep`");
        using var rig = new Rig();
        var id = Id(await rig.Start("child 120"));
        WaitForState(rig, id, job => job.Child is not null, "the runner to start its child");
        var runner = rig.World.Store.Read(id)!.Runner!;
        var child = rig.World.Store.Read(id)!.Child!;
        var clock = Stopwatch.StartNew();

        var answer = await rig.Cancel(id);

        answer.GetProperty("state").GetString().Should().Be(RoleJobStates.Cancelled);
        answer.GetProperty("alreadyOver").GetBoolean().Should().BeFalse();
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10), "a runner that is listening stops by itself, well inside the grace");
        RoleJobWorld.WaitFor(() => !RoleJobWorld.IsAlive(child), "the CLI child to be gone");
        RoleJobWorld.WaitFor(() => !RoleJobWorld.IsAlive(runner), "the runner to be gone");
        rig.World.Store.Read(id)!.Child.Should().BeNull();
    }

    [Fact]
    public async Task A6_ACancelOfAJobWhoseRunnerIsDead_ClosesItAtOnce_WithoutWaitingTheGrace()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "POSIX signals");
        using var rig = new Rig();
        var id = Id(await rig.Start("child 120"));
        WaitForState(rig, id, job => job.Child is not null, "the runner to start its child");
        var runner = rig.World.Store.Read(id)!.Runner!;
        var child = rig.World.Store.Read(id)!.Child!;
        using (var process = Process.GetProcessById(runner.Pid))
        {
            process.Kill();
            process.WaitForExit(10_000);
        }

        var clock = Stopwatch.StartNew();
        var answer = await rig.Cancel(id);

        answer.GetProperty("state").GetString().Should().Be(RoleJobStates.Cancelled);
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(8), "nothing holds the lease, so there is no runner to wait for");
        RoleJobWorld.WaitFor(() => !RoleJobWorld.IsAlive(child), "the dead runner's child to be stopped by identity");
    }

    [Fact]
    public async Task A6_ACancelOfAJobThatIsOver_IsAnsweredAsItIs_AndChangesNothing()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the setsid road");
        using var rig = new Rig();
        var id = Id(await rig.Start("succeed"));
        await rig.Status(id, wait: 30);
        var before = File.ReadAllText(rig.World.Store.FolderOf(id)!.JobFile);

        var answer = await rig.Cancel(id);

        answer.GetProperty("state").GetString().Should().Be(RoleJobStates.Succeeded);
        answer.GetProperty("alreadyOver").GetBoolean().Should().BeTrue();
        File.ReadAllText(rig.World.Store.FolderOf(id)!.JobFile).Should().Be(before);
        (await rig.Cancel("r20260101000000-00000000")).GetProperty("error").GetString().Should().Contain("no role job");
    }

    [Fact]
    public async Task E3_ACancelOfAStalledRunner_KillsItsTreeInTheGrace_RaisesTheGeneration_AndTheJobIsCancelled()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "SIGSTOP is a POSIX signal");
        using var rig = new Rig(grace: TimeSpan.FromSeconds(2));
        var id = Id(await rig.Start("child 120"));
        WaitForState(rig, id, job => job.Child is not null, "the runner to start its child");
        var before = rig.World.Store.Read(id)!;
        using var runnerProcess = Process.GetProcessById(before.Runner!.Pid);
        RoleJobWorld.Signal("STOP", runnerProcess);
        var clock = Stopwatch.StartNew();

        var answer = await rig.Cancel(id);

        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10), "grace, then the kill: a stopped runner cannot hold a cancel for ever");
        answer.GetProperty("state").GetString().Should().Be(RoleJobStates.Cancelled);
        RoleJobWorld.WaitFor(() => !RoleJobWorld.IsAlive(before.Runner), "the stopped runner to be killed by identity");
        RoleJobWorld.WaitFor(() => !RoleJobWorld.IsAlive(before.Child!), "and its CLI child");
        rig.World.Store.Read(id)!.Generation.Should().BeGreaterThan(before.Generation + 1, "the generation was raised to fence the runner, and the cancel took the lease after it");
        File.Exists(rig.World.Store.FolderOf(id)!.ResultFile).Should().BeFalse("nothing was published");
    }

    [Fact]
    public void TheCancelGrace_IsTenSeconds()
    {
        RoleJobLimits.CancelGrace.Should().Be(TimeSpan.FromSeconds(10));
        new RoleJobService("x", _ => null, Serilog.Core.Logger.None, _ => true).CancelGrace.Should().Be(TimeSpan.FromSeconds(10));
    }
}
