using System.Text.Json;
using System.Text.Json.Nodes;
using CoaiMcp.Core.Notices;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Worktrees;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E2 s4 t4 — the fix loop on the REAL <c>coai-mcp</c>, over real stdio, with the real detached runner, the real OS layer (a <c>systemd-run --user</c> unit for the coder and one for every check step) and a fixture repository with its own
/// <c>.coai/toolchain.yaml</c> (a real small build and test, <c>/bin/sh</c> scripts): FakeCli is the only double and stands in for claude, so no model is called. The four scenarios of the story: d1 green at turn 0, green after two fix turns, never green (the cap,
/// then <c>not-green</c> with the last digest); d2 the MCP server killed while a check runs, a NEW server reads the same job and it ends <c>succeeded</c> and green, with the generation unchanged and no <c>role-job-lost</c> notice.
/// </summary>
/// <remarks>
/// <para><b>How the fake is steered inside the coder's unit.</b> The unit replaces <c>/tmp</c> and sees the worktree as the one writable folder, so the model map is a file of the FIXTURE repository (<c>.fake/model-map.json</c>, committed at the base, so
/// it is in the worktree) and the steering file beside the fake's binary names it by a path relative to the working folder; the launch counter and the record of every launch are ignored files below <c>.fake/</c>, so they never reach a commit. The real
/// home is never read: <c>HOME</c> of the server is a temp folder holding the one made-up sign-in line the layer binds. The runner is started through <c>setsid</c> (the road a machine without a user manager uses), so it outlives the server that started it.</para>
/// <para>The in-process scenarios of the same loop are <see cref="ACoderFixTurnsTests"/> and <see cref="ACoderFailoverTests"/>; this suite adds the wire, the second process and the real publish. Skipped on Windows (the setsid road) and where there is no user manager.</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class TheCoderFixLoopOverStdioTests
{
    private const string SessionOfTurnZero = "11111111-1111-1111-1111-111111111111";

    private static string Resumed(string[] argv) => argv.SkipWhile(arg => arg != "--resume").Skip(1).FirstOrDefault() ?? string.Empty;

    private static CoderRig.Launch Writes(string text, string session = "") =>
        new(new Dictionary<string, string> { [CoderRig.StateFile] = text + "\n" }, CoderRig.Report(), Session: session);

    /// <summary>The unit of the second step of the checks — the test, which the fixture makes sleep; the build step before it is over in a moment and a wait that stopped at it would race.</summary>
    private static bool IsTheTestStep(string unit) => unit.StartsWith("coai-check-", StringComparison.Ordinal) && unit.EndsWith("-s2.service", StringComparison.Ordinal);

    private static string[] States(JsonElement envelope) =>
        [.. envelope.GetProperty("rounds").EnumerateArray().Select(round => $"{round.GetProperty("turn").GetInt32()}:{round.GetProperty("state").GetString()}")];

    private sealed class World : IAsyncDisposable
    {
        private readonly TempDir _temp = TempDir.For("coai-coder-stdio-");
        private readonly List<string> _jobs = [];
        private IDisposable _steering = FakeCliSteering.Unwritten;

        private World(TempGitRepo repo, string baseSha)
        {
            Repo = repo;
            Base = baseSha;
            Data = TempDir.For("coai-coder-stdio-data-");
            Root = TempDir.For("coai-coder-stdio-root-");
            Home = TempDir.For("coai-coder-stdio-home-");
            Jobs = TempDir.For("coai-coder-stdio-jobs-");
            Directory.CreateDirectory(Path.Combine(Home.Path, ".claude"));
            File.WriteAllText(Path.Combine(Home.Path, ".claude", ".credentials.json"), CoderBed.SignInLine + "\n");
            _steering = FakeCliSteering.Write(new Dictionary<string, string> { ["FAKECLI_MODEL_MAP"] = ".fake/model-map.json", ["FAKECLI_STATE_DIR"] = CoaiMcp.Runners.Toolchain.BoundaryUnit.UnitTemp, ["FAKECLI_RECORD_DIR"] = "launches" });
        }

        internal TempGitRepo Repo { get; }

        internal string Base { get; }

        internal TempDir Data { get; }

        internal TempDir Root { get; }

        internal TempDir Home { get; }

        internal TempDir Jobs { get; }

        /// <summary>
        /// The world: a repository that holds the plan with its one task, the toolchain file the BASE commit reads, and the model map of the fake — claude's answer for each launch of <c>sonnet</c>, in order (the last repeats).
        /// </summary>
        internal static async Task<World> CreateAsync(IReadOnlyList<CoderRig.Launch> launches, string? toolchain = null)
        {
            Assert.SkipWhen(OperatingSystem.IsWindows(), "the setsid road");
            Assert.SkipUnless(SystemdUser.Works(out var why), why);
            var repo = await TempGitRepo.InitAsync(new ProcessLauncher(), "coai-coder-stdio-repo-");
            var map = JsonSerializer.Serialize(new Dictionary<string, object> { ["sonnet"] = launches.Select(CoderRig.Entry).ToArray() });
            var baseSha = await CoderRig.LayFixtureAsync(repo, toolchain, new Dictionary<string, string>
            {
                [".fake/model-map.json"] = map,
                [".fake/launches/.gitkeep"] = string.Empty,
                [".gitignore"] = ".fake/*.count\n.fake/launches/*\n!.fake/launches/.gitkeep\n",
            });

            return new World(repo, baseSha);
        }

        /// <summary>A real server on this world's data folder: the coder's route is claude/sonnet, every folder it makes is a temp folder of this world, the CLI is the fake.</summary>
        internal System.Diagnostics.Process Server() => StdioServer.Start(
            Data.Path,
            logLevel: "information",
            alsoInTheEnvironment:
            [
                ("COAI_ROLE_LAUNCHER", "setsid"),
                ("COAI_ROLE_CLAUDE_PERSON_SIGN_IN", "1"),
                ("COAI_ROLES_MODELS", new JsonObject { ["coder"] = new JsonArray(new JsonObject { ["model"] = "sonnet", ["vendor"] = "claude" }) }.ToJsonString()),
                ("COAI_ROLE_CLAUDE_CLI", RoleJobWorld.FakeCli),
                (CoderWorktree.RootVariable, Root.Path),
                (CoderOsLayer.JobsVariable, Jobs.Path),
                ("HOME", Home.Path),
                ("FAKECLI_MODE", "vendor"),
                ("FAKECLI_VERSION_STDOUT", "2.1.330 (Claude Code)\n"),
                ("FAKECLI_HELP_STDOUT", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "claude", "help-2.1.330-wsl.txt"))),
            ]);

        internal async Task<string> StartAsync(System.Diagnostics.Process server)
        {
            var started = await StdioServer.Call(server, 2, "start_role", $$"""{"role":"coder","repoPath":{{JsonSerializer.Serialize(Repo.Path)}},"taskFile":"{{CoderRig.TaskFile}}","timeoutMinutes":5}""");
            started.TryGetProperty("jobId", out var id).Should().BeTrue(started.ToString());
            _jobs.Add(id.GetString()!);

            return id.GetString()!;
        }

        internal static async Task<JsonElement> WaitEndAsync(System.Diagnostics.Process server, string jobId)
        {
            for (var id = 10; id < 40; id++)
            {
                var status = await StdioServer.Call(server, id, "role_status", $$"""{"jobId":"{{jobId}}","maxSeconds":20}""", timeoutSeconds: 60);
                if (status.GetProperty("state").GetString() is "succeeded" or "failed" or "cancelled" or "lost")
                {
                    return status;
                }
            }

            throw new TimeoutException($"job {jobId} did not end");
        }

        internal string WorktreeOf(string jobId) => Path.Combine(Root.Path, $"coai-coder-{jobId}");

        /// <summary>The argv of every launch of the fake, in order — recorded below <c>launches</c> of the claude coder unit's own /tmp (<c>coder-temp/claude/tmp</c> of the job): a file of the worktree is cleaned away before every round of checks.</summary>
        internal IReadOnlyList<string[]> Launches(string jobId) => RoleFailoverRig.LaunchesIn(Path.Combine(Jobs.Path, jobId, "coder-temp", "claude", "tmp", "launches"));

        internal async Task<string> GitAsync(string folder, params string[] args)
        {
            var result = await new ProcessLauncher().RunAsync(new ProcessRequest("git", args, folder));

            return result.ExitCode == 0 ? result.StdOut.Trim() : $"git exited {result.ExitCode}: {result.StdErr.Trim()}";
        }

        internal IReadOnlyList<JsonElement> Notices(string code)
        {
            var file = Path.Combine(Data.Path, "server-notices.jsonl");

            return File.Exists(file)
                ? [.. File.ReadAllLines(file).Where(line => line.Length > 0).Select(line => JsonDocument.Parse(line).RootElement.Clone()).Where(line => line.GetProperty("code").GetString() == code)]
                : [];
        }

        public async ValueTask DisposeAsync()
        {
            // A scenario that failed halfway must not leave a runner or a unit: cancel what is not over, then stop whatever unit is still loaded.
            var store = new RoleJobStore(Data.Path, TimeProvider.System, Serilog.Core.Logger.None);
            foreach (var id in _jobs)
            {
                if (store.Read(id) is { State: not (RoleJobStates.Succeeded or RoleJobStates.Failed or RoleJobStates.Cancelled or RoleJobStates.Lost) })
                {
                    store.FolderOf(id)?.RequestCancel();
                    var until = DateTime.UtcNow.AddSeconds(30);
                    while (store.Read(id) is { State: not (RoleJobStates.Succeeded or RoleJobStates.Failed or RoleJobStates.Cancelled or RoleJobStates.Lost) } && DateTime.UtcNow < until)
                    {
                        await Task.Delay(100);
                    }
                }
            }

            var left = new List<string>();
            foreach (var id in _jobs)
            {
                left.AddRange(await CoderRig.UnitsOfJobAsync(id, Root.Path));
            }

            foreach (var unit in left)
            {
                await new ProcessLauncher().RunAsync(new ProcessRequest("systemctl", ["--user", "stop", unit], Root.Path) { Timeout = TimeSpan.FromSeconds(20) });
            }

            _steering.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            await Repo.DisposeAsync();
            foreach (var folder in new[] { Data, Root, Home, Jobs })
            {
                folder.Dispose();
            }

            _temp.Dispose();
            left.Should().BeEmpty("a coder test must stop every unit it starts");
        }
    }

    // ---------- d1: the three scenarios of the loop ----------

    [Fact]
    public async Task D1_GreenAtTurnZero_NoFixTurn_TheEnvelopeIsGreen_TheBranchHoldsTheOneCommit()
    {
        await using var world = await World.CreateAsync([Writes(CoderRig.Green)]);
        var server = world.Server();
        try
        {
            await StdioServer.InitializeAsync(server);
            var jobId = await world.StartAsync(server);

            var status = await World.WaitEndAsync(server, jobId);

            status.GetProperty("state").GetString().Should().Be("succeeded", status.ToString());
            var envelope = status.GetProperty("result");
            envelope.GetProperty("green").GetBoolean().Should().BeTrue();
            envelope.GetProperty("fixTurns").GetInt32().Should().Be(0);
            States(envelope).Should().Equal("0:green");
            world.Launches(jobId).Should().ContainSingle("turn 0 only: nothing was red");
            (await world.GitAsync(world.Repo.Path, "rev-list", "--count", $"{world.Base}..refs/heads/coder/{jobId}")).Should().Be("1");
            envelope.GetProperty("headSha").GetString().Should().Be(await world.GitAsync(world.Repo.Path, "rev-parse", $"refs/heads/coder/{jobId}"));
        }
        finally
        {
            await StdioServer.StopAsync(server);
        }
    }

    [Fact]
    public async Task D1_GreenAfterTwoFixTurns_BothResumeTheSessionOfTurnZero_OneCommitEach_TheEnvelopeIsGreen()
    {
        await using var world = await World.CreateAsync([Writes(CoderRig.Red + " a", SessionOfTurnZero), Writes(CoderRig.Red + " b"), Writes(CoderRig.Green)]);
        var server = world.Server();
        try
        {
            await StdioServer.InitializeAsync(server);
            var jobId = await world.StartAsync(server);

            var status = await World.WaitEndAsync(server, jobId);

            status.GetProperty("state").GetString().Should().Be("succeeded", status.ToString());
            var envelope = status.GetProperty("result");
            envelope.GetProperty("green").GetBoolean().Should().BeTrue();
            envelope.GetProperty("fixTurns").GetInt32().Should().Be(2);
            States(envelope).Should().Equal("0:failed", "1:failed", "2:green");
            var launches = world.Launches(jobId);
            launches.Should().HaveCount(3);
            launches.Select(Resumed).Should().Equal(string.Empty, SessionOfTurnZero, SessionOfTurnZero);
            launches[1][^1].Should().Contain("the state is red", "the failure digest is the prompt of the fix turn");
            (await world.GitAsync(world.Repo.Path, "rev-list", "--count", $"{world.Base}..refs/heads/coder/{jobId}")).Should().Be("3");
            status.GetProperty("attempts").EnumerateArray().Should().ContainSingle("one model, one attempt, however many turns it took");
        }
        finally
        {
            await StdioServer.StopAsync(server);
        }
    }

    [Fact]
    public async Task D1_NeverGreen_StopsAtTheCap_FailedNotGreen_WithTheLastDigestInTheResult()
    {
        await using var world = await World.CreateAsync([Writes(CoderRig.Red + " 0", SessionOfTurnZero), Writes(CoderRig.Red + " 1"), Writes(CoderRig.Red + " 2"), Writes(CoderRig.Red + " 3"), Writes(CoderRig.Red + " 4")]);
        var server = world.Server();
        try
        {
            await StdioServer.InitializeAsync(server);
            var jobId = await world.StartAsync(server);

            var status = await World.WaitEndAsync(server, jobId);

            (status.GetProperty("state").GetString(), status.GetProperty("stop").GetString()).Should().Be(("failed", "not-green"), status.ToString());
            world.Launches(jobId).Should().HaveCount(4, "turn 0 and exactly three fix turns, never a fourth");
            var envelope = status.GetProperty("result");
            envelope.GetProperty("green").GetBoolean().Should().BeFalse();
            envelope.GetProperty("fixTurns").GetInt32().Should().Be(3);
            States(envelope).Should().Equal("0:failed", "1:failed", "2:failed", "3:failed");
            envelope.GetProperty("lastDigest").EnumerateArray().Select(item => item.GetProperty("message").GetString()).Should().Contain(message => message!.Contains("the state is red"));
            (await world.GitAsync(world.Repo.Path, "rev-list", "--count", $"{world.Base}..refs/heads/coder/{jobId}")).Should().Be("4", "every commit of every turn is kept");
        }
        finally
        {
            await StdioServer.StopAsync(server);
        }
    }

    // ---------- d2: the server killed while a check runs ----------

    [Fact]
    public async Task D2_TheMcpServerIsKilledWhileACheckRuns_ANewServerReadsTheSameJob_ItEndsSucceededAndGreen_TheGenerationIsUnchanged_NoJobLostNotice()
    {
        await using var world = await World.CreateAsync([Writes(CoderRig.Green)], CoderRig.DefaultToolchain(testTail: "; sleep 6"));
        string jobId;
        var first = world.Server();
        try
        {
            await StdioServer.InitializeAsync(first);
            jobId = await world.StartAsync(first);
            var until = DateTime.UtcNow.AddSeconds(60);
            while (!(await CoderRig.UnitsOfJobAsync(jobId, world.Root.Path)).Any(unit => IsTheTestStep(unit)))
            {
                DateTime.UtcNow.Should().BeBefore(until, "a check step was to be running in its unit");
                await Task.Delay(50, TestContext.Current.CancellationToken);
            }
        }
        finally
        {
            await StdioServer.StopAsync(first);
        }

        var store = new RoleJobStore(world.Data.Path, TimeProvider.System, Serilog.Core.Logger.None);
        store.Read(jobId)!.State.Should().Be(RoleJobStates.Running, "the server died while the job was in the middle of its checks: the job is not over, and the runner outlived the server");
        (await CoderRig.UnitsOfJobAsync(jobId, world.Root.Path)).Should().Contain(unit => IsTheTestStep(unit), "CONTROL: the test step (the one that sleeps) was still running in its unit after the server was gone");

        var second = world.Server();
        try
        {
            await StdioServer.InitializeAsync(second);

            var status = await World.WaitEndAsync(second, jobId);

            status.GetProperty("state").GetString().Should().Be("succeeded", status.ToString());
            status.GetProperty("generation").GetInt32().Should().Be(1, "the runner was never taken over: it outlived the server");
            var envelope = status.GetProperty("result");
            envelope.GetProperty("green").GetBoolean().Should().BeTrue();
            States(envelope).Should().Equal("0:green");
            world.Notices(ServerNoticeCodes.RoleJobLost).Should().BeEmpty("the lock was held the whole time: the job was never lost");
            world.Launches(jobId).Should().ContainSingle("the fix loop did not start the model again");
        }
        finally
        {
            await StdioServer.StopAsync(second);
        }
    }
}
