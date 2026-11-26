using System.Text.Json;
using System.Text.Json.Nodes;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Worktrees;
using CoaiMcp.Server;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E2 s6 t2 — the acceptance of the epic on the REAL <c>coai-mcp</c>, over real stdio, with the real detached runner, the real OS layer and the real gate: FakeCli is the only double (claude for the coder, codex and gemini for
/// the reviewers), so nothing is paid. One run tells the whole story of the epic: the feature's plan goes through the plan gate; <c>start_role</c> role <c>coder</c> runs the fixture task of that plan to green after ONE fix turn; then
/// <c>review_code</c> on <c>coder/&lt;jobId&gt;</c> answers a verdict with no plan round on that branch (it inherits the plan). d1 is that story; d2 is what the same run proves about the boundary and the record: the route held claude models
/// only, the fake saw <c>--resume</c> exactly once, nothing outside the worktree changed, and the envelope's <c>filesChanged</c> equals the git diff of base to head.
/// </summary>
/// <remarks>
/// How the fake is steered, and why the reviewers see it too: the model map is a file of the fixture repository (<c>.fake/model-map.json</c>, committed at the base, so it is in the coder's worktree), named by a path relative to the working
/// folder in the steering file beside the fake's binary; the reviewers' answers come from the plain <c>FAKECLI_STDOUT</c> / <c>FAKECLI_OUTFILE_TEXT</c> of the server's environment (a model with no map entry is steered by them). The runner is
/// started through <c>setsid</c>. Skipped on Windows and where there is no systemd user manager. The fix loop alone is <see cref="TheCoderFixLoopOverStdioTests"/>; the plan binding alone is <see cref="PlanBindingTests"/>.
/// </remarks>
[Collection("fakecli-env")]
public sealed class TheCoderEpicAcceptanceTests
{
    private const string SessionOfTurnZero = "22222222-2222-2222-2222-222222222222";

    private const string Clean = """{"findings": []}""";

    private const string PlanFile = "plan/PLAN.md";

    private const string PlanText = "# The plan\n\nGoal: src/thing.cs holds the state the fixture's checks accept, and a test names it. What must be true when it is done: the build and the test of .coai/toolchain.yaml are green.\n\nConstraints: no new dependency.\n";

    private static string Resumed(string[] argv) => argv.SkipWhile(arg => arg != "--resume").Skip(1).FirstOrDefault() ?? string.Empty;

    private static string Verdict(JsonElement answer) => answer.TryGetProperty("verdict", out var verdict) ? verdict.GetString()! : "(" + answer + ")";

    private sealed class World : IAsyncDisposable
    {
        private readonly TempDir _temp = TempDir.For("coai-coder-epic-");
        private IDisposable _steering = FakeCliSteering.Unwritten;
        private string _job = string.Empty;

        private World(TempGitRepo repo, string baseSha)
        {
            Repo = repo;
            Base = baseSha;
            Data = TempDir.For("coai-coder-epic-data-");
            Root = TempDir.For("coai-coder-epic-root-");
            Home = TempDir.For("coai-coder-epic-home-");
            Jobs = TempDir.For("coai-coder-epic-jobs-");
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

        internal static async Task<World> CreateAsync(IReadOnlyList<CoderRig.Launch> launches)
        {
            Assert.SkipWhen(OperatingSystem.IsWindows(), "the setsid road");
            Assert.SkipUnless(SystemdUser.Works(out var why), why);
            var repo = await TempGitRepo.InitAsync(new ProcessLauncher(), "coai-coder-epic-repo-");
            var map = JsonSerializer.Serialize(new Dictionary<string, object> { ["sonnet"] = launches.Select(CoderRig.Entry).ToArray() });
            var baseSha = await CoderRig.LayFixtureAsync(repo, null, new Dictionary<string, string>
            {
                [PlanFile] = PlanText,
                [".fake/model-map.json"] = map,
                [".fake/launches/.gitkeep"] = string.Empty,
                [".gitignore"] = ".fake/*.count\n.fake/launches/*\n!.fake/launches/.gitkeep\n",
            });
            await repo.GitAsync("checkout", "-b", "feature");

            return new World(repo, baseSha);
        }

        /// <summary>A real server on this world's data folder: the coder's list names a claude model AND a codex one (the coder keeps the claude one), the reviewers are codex and gemini, every CLI is the fake.</summary>
        internal System.Diagnostics.Process Server() => StdioServer.Start(
            Data.Path,
            logLevel: "information",
            alsoInTheEnvironment:
            [
                ("COAI_ROLE_LAUNCHER", "setsid"),
                ("COAI_ROLE_CLAUDE_PERSON_SIGN_IN", "1"),
                ("COAI_ROLES_MODELS", new JsonObject { ["coder"] = new JsonArray(new JsonObject { ["model"] = "gpt-5.5", ["vendor"] = "codex" }, new JsonObject { ["model"] = "sonnet", ["vendor"] = "claude" }) }.ToJsonString()),
                ("COAI_ROLE_CLAUDE_CLI", RoleJobWorld.FakeCli),
                ("COAI_PROVIDERS", "codex,gemini"),
                ("COAI_ROUND_WORKTREES", _temp.At("round-trees")),
                ("COAI_EXE_CODEX", RoleJobWorld.FakeCli),
                ("COAI_EXE_GEMINI", RoleJobWorld.FakeCli),
                (CoderWorktree.RootVariable, Root.Path),
                (CoderOsLayer.JobsVariable, Jobs.Path),
                ("HOME", Home.Path),
                ("FAKECLI_MODE", "vendor"),
                ("FAKECLI_STDOUT", Clean),
                ("FAKECLI_OUTFILE_TEXT", Clean),
                ("FAKECLI_VERSION_STDOUT", "2.1.330 (Claude Code)\n"),
                ("FAKECLI_HELP_STDOUT", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "claude", "help-2.1.330-wsl.txt"))),
            ]);

        internal async Task<JsonElement> CallAsync(System.Diagnostics.Process server, int id, string tool, string arguments) => await StdioServer.Call(server, id, tool, arguments, timeoutSeconds: 120);

        internal string Quoted => JsonSerializer.Serialize(Repo.Path);

        internal async Task<JsonElement> StartAsync(System.Diagnostics.Process server, int id)
        {
            var started = await StdioServer.Call(server, id, "start_role", $$"""{"role":"coder","repoPath":{{Quoted}},"taskFile":"{{CoderRig.TaskFile}}","timeoutMinutes":5}""");
            started.TryGetProperty("jobId", out var jobId).Should().BeTrue(started.ToString());
            _job = jobId.GetString()!;

            return started;
        }

        internal async Task<JsonElement> WaitEndAsync(System.Diagnostics.Process server, string jobId)
        {
            for (var id = 100; id < 130; id++)
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

        internal async Task<string> GitAsync(string folder, params string[] args)
        {
            var result = await new ProcessLauncher().RunAsync(new ProcessRequest("git", args, folder));

            return result.ExitCode == 0 ? result.StdOut.Trim() : $"git exited {result.ExitCode}: {result.StdErr.Trim()}";
        }

        public async ValueTask DisposeAsync()
        {
            var store = new RoleJobStore(Data.Path, TimeProvider.System, Serilog.Core.Logger.None);
            if (_job.Length > 0 && store.Read(_job) is { State: not (RoleJobStates.Succeeded or RoleJobStates.Failed or RoleJobStates.Cancelled or RoleJobStates.Lost) })
            {
                store.FolderOf(_job)?.RequestCancel();
                var until = DateTime.UtcNow.AddSeconds(30);
                while (store.Read(_job) is { State: not (RoleJobStates.Succeeded or RoleJobStates.Failed or RoleJobStates.Cancelled or RoleJobStates.Lost) } && DateTime.UtcNow < until)
                {
                    await Task.Delay(100);
                }
            }

            var left = _job.Length > 0 ? await CoderRig.UnitsOfJobAsync(_job, Root.Path) : [];
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

    [Fact]
    public async Task D1_TheFeaturesPlanProceeds_TheCoderRunsToGreenAfterOneFixTurn_TheCodeRoundOnItsBranchIsReviewedWithNoPlanRoundOfItsOwn()
    {
        await using var world = await World.CreateAsync(
        [
            new(new Dictionary<string, string> { [CoderRig.StateFile] = CoderRig.Red + "\n" }, CoderRig.Report(), Session: SessionOfTurnZero),
            new(new Dictionary<string, string> { [CoderRig.StateFile] = CoderRig.Green + "\n" }, CoderRig.Report()),
        ]);
        var server = world.Server();
        string jobId;
        try
        {
            await StdioServer.InitializeAsync(server);
            await world.CallAsync(server, 2, "open", $$"""{"repoPath":{{world.Quoted}},"branch":"feature"}""");
            var planned = await world.CallAsync(server, 3, "review_plan", $$"""{"repoPath":{{world.Quoted}},"branch":"feature","committedPlan":"{{PlanFile}}"}""");
            Verdict(planned).Should().Be("proceed", planned.ToString());
            (await world.CallAsync(server, 4, "resolve", $$"""{"repoPath":{{world.Quoted}},"branch":"feature","decisions":"[]"}""")).GetProperty("stage").GetString().Should().Be("CodeReview");

            var started = await world.StartAsync(server, 5);
            jobId = started.GetProperty("jobId").GetString()!;
            var status = await world.WaitEndAsync(server, jobId);

            status.GetProperty("state").GetString().Should().Be("succeeded", status.ToString());
            var envelope = status.GetProperty("result");
            envelope.GetProperty("green").GetBoolean().Should().BeTrue();
            envelope.GetProperty("fixTurns").GetInt32().Should().Be(1);

            var branch = $"coder/{jobId}";
            await world.CallAsync(server, 6, "open", $$"""{"repoPath":{{world.Quoted}},"branch":"{{branch}}"}""");
            var round = await world.CallAsync(server, 7, "review_code", $$"""{"repoPath":{{world.Quoted}},"branch":"{{branch}}","baseRef":"feature","planText":""}""");
            round.TryGetProperty("error", out var error).Should().BeFalse(error.ToString());
            Verdict(round).Should().Be("proceed", round.ToString());
            await world.CallAsync(server, 8, "resolve", $$"""{"repoPath":{{world.Quoted}},"branch":"{{branch}}","decisions":"[]"}""");
            var coderStatus = await world.CallAsync(server, 9, "status", $$"""{"repoPath":{{world.Quoted}},"branch":"{{branch}}"}""");
            coderStatus.GetProperty("planProceeded").GetBoolean().Should().BeTrue("the coder's branch inherits the proceed of the feature's plan");
            coderStatus.GetProperty("inherited").GetProperty("jobId").GetString().Should().Be(jobId);
        }
        finally
        {
            await StdioServer.StopAsync(server);
        }

        var session = new SessionStore(world.Data.Path).Load(world.Repo.Path, $"coder/{jobId}")!;
        session.Rounds.Select(round => round.Stage).Should().Equal([Stage.CodeReview.ToString()], "one code round, and no plan round on the coder's branch");
        new SessionStore(world.Data.Path).Load(world.Repo.Path, "feature")!.Rounds.Select(round => round.Stage).Should().Equal([Stage.PlanReview.ToString()], "the plan round ran once, on the feature branch");
    }

    [Fact]
    public async Task D2_TheRouteHoldsClaudeModelsOnly_TheFakeSawResumeOnce_NothingOutsideTheWorktreeChanged_FilesChangedIsTheDiffOfBaseToHead()
    {
        await using var world = await World.CreateAsync(
        [
            new(new Dictionary<string, string> { [CoderRig.StateFile] = CoderRig.Red + "\n" }, CoderRig.Report(), Session: SessionOfTurnZero),
            new(new Dictionary<string, string> { [CoderRig.StateFile] = CoderRig.Green + "\n" }, CoderRig.Report()),
        ]);
        var mainBefore = (Head: await world.GitAsync(world.Repo.Path, "rev-parse", "HEAD"), Branches: await world.GitAsync(world.Repo.Path, "branch", "--format=%(refname)"), Status: await world.GitAsync(world.Repo.Path, "status", "--porcelain=v1", "--ignored"));
        mainBefore.Status.Should().NotContain("src/", "CONTROL: the main checkout starts with no source change");
        var server = world.Server();
        try
        {
            await StdioServer.InitializeAsync(server);
            var started = await world.StartAsync(server, 2);
            var jobId = started.GetProperty("jobId").GetString()!;
            // The codex model of the list is not a coder's model in this epoch: the route keeps the claude one.
            started.GetProperty("route").EnumerateArray().Select(model => model.GetString()).Should().Equal(["claude/sonnet"]);

            var status = await world.WaitEndAsync(server, jobId);

            status.GetProperty("state").GetString().Should().Be("succeeded", status.ToString());
            status.GetProperty("attempts").EnumerateArray().Select(attempt => attempt.GetProperty("vendor").GetString()).Should().OnlyContain(vendor => vendor == "claude");
            var envelope = status.GetProperty("result");
            envelope.GetProperty("green").GetBoolean().Should().BeTrue();
            var launches = RoleFailoverRig.LaunchesIn(Path.Combine(world.Jobs.Path, jobId, "coder", "tmp", "launches"));
            launches.Should().HaveCount(2, "turn 0 and one fix turn");
            launches.Count(argv => argv.Contains("--resume")).Should().Be(1, "the fix turn resumes the session; turn 0 does not");
            launches.Select(Resumed).Should().Equal(string.Empty, SessionOfTurnZero);

            var head = envelope.GetProperty("headSha").GetString()!;
            envelope.GetProperty("baseSha").GetString().Should().Be(world.Base);
            var diff = (await world.GitAsync(world.Repo.Path, "diff", "--name-status", world.Base, head)).Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Split('\t')).Select(parts => (parts[0], parts[1])).Order().ToArray();
            diff.Should().NotBeEmpty("CONTROL: the coder changed a file");
            envelope.GetProperty("filesChanged").EnumerateArray().Select(file => (file.GetProperty("status").GetString()!, file.GetProperty("path").GetString()!)).Order().Should().Equal(diff);

            (await world.GitAsync(world.Repo.Path, "rev-parse", "HEAD")).Should().Be(mainBefore.Head, "the main checkout is where it was");
            (await world.GitAsync(world.Repo.Path, "status", "--porcelain=v1", "--ignored")).Should().Be(mainBefore.Status, "no file of the main checkout was written, not even an ignored one");
            (await world.GitAsync(world.Repo.Path, "branch", "--format=%(refname)")).Split('\n').Except(mainBefore.Branches.Split('\n')).Should().Equal([$"refs/heads/coder/{jobId}"]);
            var checkout = envelope.GetProperty("mainCheckout");
            checkout.GetProperty("changed").GetBoolean().Should().BeFalse(checkout.ToString());
        }
        finally
        {
            await StdioServer.StopAsync(server);
        }
    }
}
