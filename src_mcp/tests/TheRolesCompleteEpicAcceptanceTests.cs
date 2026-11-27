using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CoaiMcp.Core.Work;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Toolchain;
using CoaiMcp.Runners.Worktrees;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E3 s8 t2 — the acceptance of the epic on the REAL <c>coai-mcp</c>, over real stdio, with the real detached runner, the real unit and the real gate: FakeCli is the only double, and it answers as every CLI, so nothing is paid and no vendor is called.
/// <list type="bullet">
/// <item><b>d1</b>: a scout job and a judge job end <c>succeeded</c> with their strict JSON envelopes (<see cref="D1_AScoutJobAndAJudgeJob_EndSucceeded_WithTheirStrictJsonEnvelopes"/>).</item>
/// <item><b>d2</b>: for each vendor the TABLE of this repository ships (<see cref="CoderBoundaryTable.Shipped"/>, read here, never copied) and for claude, which is always launchable, a coder job goes red, then green after one fix turn; the recorded argv resumes the session of turn 0; the prompt the fake recorded on turn 0 is that
/// vendor's own and ends with the block of the instruction step of the fixture (V17, V20); a fixture whose declared step exits non-zero ends <c>instructions-failed</c> with no launch (V21) (<see cref="D2_ACoderOfEachShippedVendor_GoesRedThenGreenAfterOneFixTurn_AndResumesTheSessionOfTurnZero_WithItsOwnPromptAndTheInstructionBlock"/>). For each vendor the table
/// refuses — and for one that no row names — <c>start_role</c> with a model of that vendor is refused and the fake records no launch (<see cref="D2_ARefusedVendor_IsRefusedAtTheStart_AndTheFakeRecordsNoLaunchOfIt"/>).</item>
/// <item><b>d3</b>: <c>review_plan</c> with <c>featureFolder</c> on an invalid fixture is refused with zero reviewer launches; on a valid one the prompt the fake recorded holds the sections in topological order; the pack of the <c>v2</c> folder of this repository for this epic is under 256 KiB.</item>
/// <item><b>d4</b>: no test calls a vendor or reads a real sign-in: every server of this class is started by ONE door, with a temp <c>HOME</c>, a temp data folder and the fake as each CLI (<see cref="D4_EveryServerOfThisClass_RunsWithATempHomeATempDataFolderAndTheFakeAsEveryCli"/>).</item>
/// </list>
/// </summary>
/// <remarks>
/// <para><b>One server for each vendor.</b> The fake answers <c>--version</c> and <c>--help</c> from one steering, so a server that is told the CLI of claude cannot also be told the CLI of agy. Each vendor therefore has its own server, its own data folder and its own stand-in home; the table decides which vendors there are.
/// A vendor the table ships and this class has no kit for is a red test that says so, never a skipped one.</para>
/// <para><b>How the fake is steered.</b> The model map is a file of the fixture repository (<c>.fake/model-map.json</c>, committed at the base, so it is in the coder's worktree), named by a relative path in the steering file beside the fake's binary; the fake finds it in the worktree through <c>--add-dir</c> too, because an agy
/// coder starts in its read-only guard folder. The reviewers' answers come from the plain <c>FAKECLI_*</c> names of the server's environment. Skipped on Windows and where there is no systemd user manager.</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class TheRolesCompleteEpicAcceptanceTests
{
    private const string Clean = """{"findings": []}""";

    private const string StepScript = "printf 'instruction-%s-line\\n' canary";

    private const string StepOutput = "instruction-canary-line\n";

    private const string FailingStepScript = "echo the-step-says-this >&2; exit 3";

    private const string Gemini = "gemini";

    // ---------- the vendors: what the table ships, and one kit for each ----------

    /// <summary>The vendors a coder can be launched on by the table of THIS repository, plus claude, which is not in the table and is always launchable. The theory reads the table at discovery: a row that turns on or off changes the cases, and no copy of it is kept here.</summary>
    public static TheoryData<string> ShippedVendors => [.. ShippedIds];

    /// <summary>The vendors the table refuses (a row with the verdict refuse or the mark off) and one vendor that no row names: a coder is never launched on any of them.</summary>
    public static TheoryData<string> RefusedVendors => [.. RefusedIds];

    private static string[] ShippedIds => [CoderBoundaryTable.Claude, .. CoderBoundaryTable.Shipped.Vendors.Where(row => row.Ships).Select(row => row.Id)];

    private static string[] RefusedIds => [.. CoderBoundaryTable.Shipped.Vendors.Where(row => !row.Ships).Select(row => row.Id), Gemini];

    /// <summary>What a shipped vendor needs of a test: a model, its stand-in sign-in, the environment of the server for its CLI, one scripted turn, and how its recorded argv names the session.</summary>
    private sealed record Kit(string Id, string Model, string Session, IReadOnlyList<(string Name, string Value)> Environment, Action<string> LaySignIn, Func<string, object> Turn, Func<string[], string> ResumedFrom);

    private static Kit KitOf(string vendor) => vendor switch
    {
        "claude" => new Kit(
            "claude",
            "sonnet",
            "22222222-2222-2222-2222-222222222222",
            [
                ("COAI_ROLE_CLAUDE_PERSON_SIGN_IN", "1"),
                ("FAKECLI_VERSION_STDOUT", "2.1.330 (Claude Code)\n"),
                ("FAKECLI_HELP_STDOUT", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "claude", "help-2.1.330-wsl.txt"))),
            ],
            home =>
            {
                Directory.CreateDirectory(Path.Combine(home, ".claude"));
                File.WriteAllText(Path.Combine(home, ".claude", ".credentials.json"), CoderBed.SignInLine + "\n");
            },
            state => CoderRig.Entry(new CoderRig.Launch(new Dictionary<string, string> { [CoderRig.StateFile] = state + "\n" }, CoderRig.Report(), Session: "22222222-2222-2222-2222-222222222222")),
            argv => argv.SkipWhile(arg => arg != "--resume").Skip(1).FirstOrDefault() ?? string.Empty),
        "codex" => new Kit(
            "codex",
            "terra",
            CodexScript.ThreadId,
            [
                ("FAKECLI_VERSION_STDOUT", "codex-cli 0.171.0\n"),
                ("FAKECLI_EXEC_HELP_FILE", Path.Combine(AppContext.BaseDirectory, "fixtures", "codex", "exec-help-0.164.0-wsl.txt")),
                ("FAKECLI_EXEC_RESUME_HELP_FILE", Path.Combine(AppContext.BaseDirectory, "fixtures", "codex", "exec-resume-help-0.164.0-wsl.txt")),
            ],
            home =>
            {
                Directory.CreateDirectory(Path.Combine(home, ".codex"));
                File.WriteAllText(Path.Combine(home, ".codex", "auth.json"), JsonSerializer.Serialize(new
                {
                    auth_mode = "chatgpt",
                    tokens = new { id_token = "made-up-id-token", access_token = Jwt(DateTime.UtcNow.AddDays(2)), refresh_token = "made-up-refresh-token-not-a-credential", account_id = "made-up", oauth_provenance = "made-up", login_owner_id = "made-up" },
                    last_refresh = "2026-11-26T10:00:00Z",
                }));
            },
            state => CodexScript.Turn(CodexScript.Writes(state), CodexScript.Report()),
            argv => argv is ["exec", "resume", ..] && argv.Contains(CodexScript.ThreadId) ? CodexScript.ThreadId : string.Empty),
        "agy" => new Kit(
            "agy",
            AgyCoderBed.Model,
            AgyCoderBed.Conversation,
            [
                ("FAKECLI_VERSION_STDOUT", "1.5.18\n"),
                ("FAKECLI_HELP_STDOUT", AgyCoderBed.Help()),
            ],
            home =>
            {
                var file = Path.Combine(home, AgyCoderUnit.SignInRelative);
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, AgyCoderBed.SignInFile(DateTime.UtcNow.AddHours(2)));
            },
            state => AgyCoderBed.Launch([AgyCoderBed.Writes(state)], conversation: AgyCoderBed.Conversation),
            argv => argv.SkipWhile(arg => arg != "--conversation").Skip(1).FirstOrDefault() ?? string.Empty),
        _ => throw new InvalidOperationException($"the table ships the vendor '{vendor}' and this acceptance has no kit for it: add one (a model, its stand-in sign-in, one scripted turn and the way its argv names the session)"),
    };

    private static string Jwt(DateTime expires) =>
        $"{B64("{\"alg\":\"none\"}")}.{B64($"{{\"exp\":{new DateTimeOffset(expires).ToUnixTimeSeconds()}}}")}.{B64("signature-made-up")}";

    private static string B64(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Step(string script) => CoderRig.DefaultToolchain() + $"instructions:\n  argv:\n    - \"/bin/sh\"\n    - \"-c\"\n    - {ToolchainFixture.Quote(script)}\n";

    private static string Sha256Of(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    // ---------- the world: the temp folders, the fixture repositories, the one door of a server ----------

    private sealed class World : IAsyncDisposable
    {
        private readonly TempDir _temp;
        private readonly List<string> _jobs = [];
        private readonly List<TempGitRepo> _repos = [];
        private IDisposable _steering = FakeCliSteering.Unwritten;

        internal World()
        {
            Assert.SkipWhen(OperatingSystem.IsWindows(), "the setsid road");
            Assert.SkipUnless(SystemdUser.Works(out var why), why);
            _temp = TempDir.For("coai-roles-epic-");
            Data = TempDir.For("coai-roles-epic-data-");
            Root = TempDir.For("coai-roles-epic-root-");
            Home = TempDir.For("coai-roles-epic-home-");
            Jobs = TempDir.For("coai-roles-epic-jobs-");
            Reviews = TempDir.For("coai-roles-epic-reviews-");
            Maps = TempDir.For("coai-roles-epic-maps-");
        }

        internal TempDir Data { get; }

        internal TempDir Root { get; }

        internal TempDir Home { get; }

        internal TempDir Jobs { get; }

        /// <summary>Where every REVIEWER launch of the server records its argv and prompt (the coder's fake records inside its worktree).</summary>
        internal TempDir Reviews { get; }

        /// <summary>The model map of the read-only roles: a plain file, read by a launch that is not in a unit.</summary>
        internal TempDir Maps { get; }

        /// <summary>The environment every server of this class is started with: the one place that names HOME, the data folder and the CLIs.</summary>
        internal IReadOnlyList<(string Name, string Value)> Environment(Kit? kit, string coderList, string scoutAndJudgeMap = "") =>
        [
            ("COAI_ROLE_LAUNCHER", "setsid"),
            ("COAI_ROLES_MODELS", coderList),
            ("COAI_ROLE_CLAUDE_CLI", RoleJobWorld.FakeCli),
            ("COAI_ROLE_CODEX_CLI", RoleJobWorld.FakeCli),
            ("COAI_ROLE_AGY_CLI", RoleJobWorld.FakeCli),
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
            ("FAKECLI_EXIT", "0"),
            ("FAKECLI_RECORD_DIR", Reviews.Path),
            .. scoutAndJudgeMap.Length > 0 ? new[] { ("FAKECLI_MODEL_MAP", scoutAndJudgeMap), ("COAI_ROLE_CLAUDE_PERSON_SIGN_IN", "1") } : [],
            .. kit?.Environment ?? [],
        ];

        internal static string CoderList(params (string Model, string Vendor)[] models) =>
            new JsonObject { ["coder"] = new JsonArray([.. models.Select(entry => (JsonNode)new JsonObject { ["model"] = entry.Model, ["vendor"] = entry.Vendor })]) }.ToJsonString();

        /// <summary>A real server on this world's data folder; every CLI is the fake.</summary>
        internal System.Diagnostics.Process Server(Kit? kit, string coderList, string scoutAndJudgeMap = "") =>
            StdioServer.Start(Data.Path, logLevel: "information", alsoInTheEnvironment: [.. Environment(kit, coderList, scoutAndJudgeMap)]);

        /// <summary>Lays the stand-in sign-in of the vendor and writes the steering file the coder's fake reads inside its unit (a relative path: the worktree).</summary>
        internal void Arrange(Kit kit)
        {
            kit.LaySignIn(Home.Path);
            _steering = FakeCliSteering.Write(new Dictionary<string, string> { ["FAKECLI_MODEL_MAP"] = ".fake/model-map.json", ["FAKECLI_RECORD_DIR"] = ".fake/launches" });
        }

        /// <summary>A repository on branch <c>feature</c> with the plan and ONE task of the coder fixture, the toolchain file with the step given, and the model map of the fake: turn 0 writes the red state, the fix turn the green one.</summary>
        internal async Task<(TempGitRepo Repo, string Base)> CoderRepoAsync(Kit kit, string toolchain)
        {
            var repo = await TempGitRepo.InitAsync(new ProcessLauncher(), "coai-roles-epic-repo-");
            _repos.Add(repo);
            var map = JsonSerializer.Serialize(new Dictionary<string, object> { [kit.Model] = new[] { kit.Turn(CoderRig.Red), kit.Turn(CoderRig.Green) } });
            var baseSha = await CoderRig.LayFixtureAsync(repo, toolchain, new Dictionary<string, string>
            {
                [".fake/model-map.json"] = map,
                [".fake/launches/.gitkeep"] = string.Empty,
                [".gitignore"] = ".fake/*.count\n.fake/launches/*\n!.fake/launches/.gitkeep\n",
            });
            await repo.GitAsync("checkout", "-b", "feature");

            return (repo, baseSha);
        }

        /// <summary>A repository on branch <c>feature</c> that holds the plan folder <c>plan</c>, committed.</summary>
        internal async Task<TempGitRepo> PlanRepoAsync(Dictionary<string, string> plan)
        {
            var repo = await TempGitRepo.InitAsync(new ProcessLauncher(), "coai-roles-epic-plan-");
            _repos.Add(repo);
            await repo.GitAsync("config", "core.autocrlf", "false");
            Lay(repo, new() { ["app.cs"] = "v1\n" });
            await repo.CommitAsync("base");
            await repo.GitAsync("checkout", "-b", "feature");
            plan["feature.yaml"] += "plan: PLAN.md\n";
            plan["PLAN.md"] = "# The plan\n\nWhat the feature is, in a few words.\n";
            Lay(repo, plan.ToDictionary(pair => $"plan/{pair.Key}", pair => pair.Value));
            await repo.CommitAsync("the plan folder");

            return repo;
        }

        private static void Lay(TempGitRepo repo, Dictionary<string, string> files)
        {
            foreach (var (path, text) in files)
            {
                var file = Path.Combine(repo.Path, path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, text, new UTF8Encoding(false));
            }
        }

        internal static async Task<JsonElement> StartAsync(System.Diagnostics.Process server, int id, string role, TempGitRepo repo, params (string Name, object Value)[] rest)
        {
            var arguments = new JsonObject { ["role"] = role, ["repoPath"] = repo.Path };
            foreach (var (name, value) in rest)
            {
                arguments[name] = JsonSerializer.SerializeToNode(value);
            }

            return await StdioServer.Call(server, id, "start_role", arguments.ToJsonString());
        }

        internal string Track(JsonElement started)
        {
            started.TryGetProperty("jobId", out var jobId).Should().BeTrue(started.ToString());
            _jobs.Add(jobId.GetString()!);

            return jobId.GetString()!;
        }

        internal static async Task<JsonElement> WaitEndAsync(System.Diagnostics.Process server, string jobId, int firstId)
        {
            for (var id = firstId; id < firstId + 30; id++)
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

        internal IReadOnlyList<string[]> CoderLaunches(string jobId)
        {
            var folder = Path.Combine(WorktreeOf(jobId), ".fake", "launches");

            return Directory.Exists(folder) ? RoleFailoverRig.LaunchesIn(folder) : [];
        }

        /// <summary>The prompts every reviewer of the server was sent, in order of launch.</summary>
        internal IReadOnlyList<string> ReviewerPrompts() =>
            [.. RoleFailoverRig.LaunchesIn(Reviews.Path).Select(argv => argv[^1])];

        internal static async Task<string> GitAsync(string folder, params string[] args)
        {
            var result = await new ProcessLauncher().RunAsync(new ProcessRequest("git", args, folder));

            return result.ExitCode == 0 ? result.StdOut.Trim() : $"git exited {result.ExitCode}: {result.StdErr.Trim()}";
        }

        public async ValueTask DisposeAsync()
        {
            var store = new RoleJobStore(Data.Path, TimeProvider.System, Serilog.Core.Logger.None);
            var ended = new[] { RoleJobStates.Succeeded, RoleJobStates.Failed, RoleJobStates.Cancelled, RoleJobStates.Lost };
            foreach (var job in _jobs)
            {
                if (store.Read(job) is { } record && !ended.Contains(record.State))
                {
                    store.FolderOf(job)?.RequestCancel();
                    var until = DateTime.UtcNow.AddSeconds(30);
                    while (store.Read(job) is { } waiting && !ended.Contains(waiting.State) && DateTime.UtcNow < until)
                    {
                        await Task.Delay(100);
                    }
                }
            }

            var left = new List<string>();
            foreach (var job in _jobs)
            {
                left.AddRange(await CoderRig.UnitsOfJobAsync(job, Root.Path));
            }

            foreach (var unit in left)
            {
                await new ProcessLauncher().RunAsync(new ProcessRequest("systemctl", ["--user", "stop", unit], Root.Path) { Timeout = TimeSpan.FromSeconds(20) });
            }

            _steering.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var repo in _repos)
            {
                await repo.DisposeAsync();
            }

            foreach (var folder in new[] { Data, Root, Home, Jobs, Reviews, Maps })
            {
                folder.Dispose();
            }

            _temp.Dispose();
            left.Should().BeEmpty("a coder test must stop every unit it starts");
        }
    }

    // ---------- d1: a scout job and a judge job ----------

    [Fact]
    public async Task D1_AScoutJobAndAJudgeJob_EndSucceeded_WithTheirStrictJsonEnvelopes()
    {
        await using var world = new World();
        await using var repo = await TempGitRepo.InitAsync(new ProcessLauncher(), "coai-roles-epic-readonly-");
        await repo.WriteAsync("keep.txt", "base\n");
        ReadOnlyScenario.Lay(repo.Path);
        await repo.CommitAsync("the files");
        var head = await repo.HeadAsync();
        var map = Path.Combine(world.Maps.Path, "model-map.json");
        File.WriteAllText(map, JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["haiku"] = FakeRoleAnswers.ClaudeSays(ReadOnlyScenario.Scout().ToJsonString()),
            ["fable"] = FakeRoleAnswers.ClaudeSays(JudgeScenario.Judge().ToJsonString()),
        }));
        var lists = new JsonObject
        {
            ["scout"] = new JsonArray(new JsonObject { ["model"] = "haiku", ["vendor"] = "claude" }),
            ["important"] = new JsonArray(new JsonObject { ["model"] = "fable", ["vendor"] = "claude" }),
        }.ToJsonString();
        var server = world.Server(null, lists, map);
        try
        {
            await StdioServer.InitializeAsync(server);

            var scoutStart = await World.StartAsync(server, 2, "scout", repo, ("brief", ReadOnlyScenario.Brief));
            var scout = await World.WaitEndAsync(server, world.Track(scoutStart), 10);
            var judgeStart = await World.StartAsync(server, 3, "judge", repo, ("brief", "Is the router plan sound?"));
            var judge = await World.WaitEndAsync(server, world.Track(judgeStart), 50);

            scoutStart.GetProperty("route").EnumerateArray().Select(entry => entry.GetString()).Should().Equal("claude/haiku");
            judgeStart.GetProperty("route").EnumerateArray().Select(entry => entry.GetString()).Should().Equal("claude/fable");
            scout.GetProperty("state").GetString().Should().Be("succeeded", scout.ToString());
            var facts = scout.GetProperty("result");
            facts.GetProperty("status").GetString().Should().Be("ok");
            facts.GetProperty("commit").GetString().Should().Be(head);
            facts.GetProperty("facts").GetArrayLength().Should().Be(2);
            facts.GetProperty("facts")[0].GetProperty("evidence")[0].GetProperty("verified").GetBoolean().Should().BeTrue("the evidence is checked by coai, not by the model");
            facts.GetProperty("files").EnumerateArray().Select(file => (file.GetProperty("path").GetString(), file.GetProperty("exists").GetBoolean())).Should().Equal(("src/Router.cs", true), ("README.md", true), ("src/Missing.cs", false));
            facts.GetProperty("unknown")[0].GetString().Should().Contain("bands");
            var scoutCounts = facts.GetProperty("counts");
            (scoutCounts.GetProperty("facts").GetInt32(), scoutCounts.GetProperty("evidence").GetInt32(), scoutCounts.GetProperty("verified").GetInt32(), scoutCounts.GetProperty("filesMissing").GetInt32()).Should().Be((2, 3, 3, 1));

            judge.GetProperty("state").GetString().Should().Be("succeeded", judge.ToString());
            var verdict = judge.GetProperty("result");
            verdict.GetProperty("status").GetString().Should().Be("ok");
            verdict.GetProperty("verdict").GetString().Should().Be("revise");
            verdict.GetProperty("commit").GetString().Should().Be(head);
            verdict.GetProperty("findings").EnumerateArray().Select(finding => (finding.GetProperty("id").GetString(), finding.GetProperty("severity").GetString())).Should().Equal(("f1", "major"), ("f2", "nit"));
            verdict.GetProperty("findings")[0].GetProperty("evidence")[0].GetProperty("verified").GetBoolean().Should().BeTrue();
            verdict.GetProperty("decision").GetProperty("chosen").GetString().Should().Be("keep one router");
            var judgeCounts = verdict.GetProperty("counts");
            (judgeCounts.GetProperty("findings").GetInt32(), judgeCounts.GetProperty("evidence").GetInt32(), judgeCounts.GetProperty("verified").GetInt32()).Should().Be((2, 1, 1));

            (await World.GitAsync(repo.Path, "status", "--porcelain=v1")).Should().BeEmpty("neither role wrote a file of the repository");
        }
        finally
        {
            await StdioServer.StopAsync(server);
        }
    }

    // ---------- d2: the coder of each shipped vendor ----------

    [Theory]
    [MemberData(nameof(ShippedVendors))]
    public async Task D2_ACoderOfEachShippedVendor_GoesRedThenGreenAfterOneFixTurn_AndResumesTheSessionOfTurnZero_WithItsOwnPromptAndTheInstructionBlock(string vendor)
    {
        var kit = KitOf(vendor);
        await using var world = new World();
        world.Arrange(kit);
        var (repo, baseSha) = await world.CoderRepoAsync(kit, Step(StepScript));
        var (brokenRepo, _) = await world.CoderRepoAsync(kit, Step(FailingStepScript));
        var server = world.Server(kit, World.CoderList((kit.Model, vendor)));
        try
        {
            await StdioServer.InitializeAsync(server);

            var started = await World.StartAsync(server, 2, "coder", repo, ("taskFile", CoderRig.TaskFile), ("model", kit.Model), ("timeoutMinutes", 5));
            var jobId = world.Track(started);
            started.GetProperty("route").EnumerateArray().Select(entry => entry.GetString()).First().Should().Be($"{vendor}/{kit.Model}", "the model the caller names is first on the route, and it is the one that runs");
            var status = await World.WaitEndAsync(server, jobId, 10);

            status.GetProperty("state").GetString().Should().Be("succeeded", status.ToString());
            status.GetProperty("attempts").EnumerateArray().Select(attempt => attempt.GetProperty("vendor").GetString()).Should().OnlyContain(name => name == vendor);
            var envelope = status.GetProperty("result");
            envelope.GetProperty("green").GetBoolean().Should().BeTrue("red on turn 0, green after the fix turn");
            envelope.GetProperty("fixTurns").GetInt32().Should().Be(1);
            envelope.GetProperty("baseSha").GetString().Should().Be(baseSha);

            // The recorded argv: turn 0 starts the session, the fix turn resumes the session of turn 0 (claude --resume, codex exec resume, agy --conversation).
            var launches = world.CoderLaunches(jobId);
            launches.Should().HaveCount(2, "turn 0 and one fix turn");
            launches.Select(kit.ResumedFrom).Should().Equal(string.Empty, kit.Session);

            // V17 and V20: the prompt of turn 0 is the vendor's own, and ends with the block of the step the fixture declares; a fix turn does not repeat it.
            var prompt = PromptOf(launches[0]);
            prompt.Should().EndWith($"\n\n## Run by coai before your turn\n\nCommand (argv, run with no shell): [\"/bin/sh\",\"-c\",\"{StepScript.Replace("\\", "\\\\", StringComparison.Ordinal)}\"]\nBase commit: {baseSha}\n\nOutput:\n\n```\n{StepOutput}```\n");
            var own = OwnLines(vendor);
            own.Should().NotBeEmpty("a vendor's prompt differs from every other vendor's in at least one line");
            prompt.Should().ContainAll(own);
            foreach (var other in ShippedIds.Where(id => id != vendor))
            {
                foreach (var line in OwnLines(other))
                {
                    prompt.Should().NotContain(line, $"the prompt of {vendor} never holds a line of the prompt of {other}");
                }
            }

            PromptOf(launches[1]).Should().NotContain("Run by coai before your turn", "a fix turn resumes the session and does not repeat the block");
            envelope.GetProperty("prompt").GetProperty("id").GetString().Should().Be(PromptIdOf(vendor), "the envelope names the prompt of the vendor");
            envelope.GetProperty("prompt").GetProperty("sha256").GetString().Should().Be(Sha256Of(prompt));
            var step = envelope.GetProperty("instructions");
            step.GetProperty("declared").GetBoolean().Should().BeTrue();
            step.GetProperty("exitCode").GetInt32().Should().Be(0);
            step.GetProperty("bytes").GetInt32().Should().Be(StepOutput.Length);
            step.GetProperty("sha256").GetString().Should().Be(Sha256Of(StepOutput), "the output of the step is in the envelope by its sha256");

            // V21: in the same run, a fixture whose declared step exits non-zero ends instructions-failed, and the fake records no launch.
            var refused = await World.StartAsync(server, 60, "coder", brokenRepo, ("taskFile", CoderRig.TaskFile), ("model", kit.Model), ("timeoutMinutes", 5));
            var refusedJob = world.Track(refused);
            var ended = await World.WaitEndAsync(server, refusedJob, 70);

            ended.GetProperty("state").GetString().Should().Be("failed", ended.ToString());
            ended.GetProperty("stop").GetString().Should().Be(RoleStopWords.InstructionsFailed);
            ended.GetProperty("reason").GetString().Should().Contain("exited 3");
            world.CoderLaunches(refusedJob).Should().BeEmpty("a step that cannot run refuses the job before any model is launched");
            ended.GetProperty("attempts").GetArrayLength().Should().Be(0, "no model was launched, so no attempt was made and nothing failed over");
            world.ReviewerPrompts().Should().BeEmpty("no reviewer was launched by a coder job either");
        }
        finally
        {
            await StdioServer.StopAsync(server);
        }

    }

    /// <summary>The prompt a recorded launch was sent: the last field of the record, and for agy the content of its one stream-json message.</summary>
    private static string PromptOf(string[] argv)
    {
        var sent = argv[^1];

        return sent.StartsWith("{\"event\":\"user\"", StringComparison.Ordinal) ? JsonDocument.Parse(sent).RootElement.GetProperty("message").GetProperty("content").GetString()! : sent;
    }

    /// <summary>The lines of the prompt of <paramref name="vendor"/> that no other vendor's prompt has: what makes it the vendor's own. The prompts are the ones the server embeds.</summary>
    private static string[] OwnLines(string vendor)
    {
        string[] ids = ["claude", .. CoderBoundaryTable.Shipped.Vendors.Select(row => row.Id)];
        var texts = ids.ToDictionary(id => id, id => ResolveVendor(id) is { } found ? PromptTemplate.Embedded(found.PromptResource).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n') : []);
        var others = ids.Where(id => id != vendor).SelectMany(id => texts[id]).ToHashSet(StringComparer.Ordinal);

        return [.. texts[vendor].Where(line => line.Trim().Length > 0 && !others.Contains(line) && !line.Contains('{', StringComparison.Ordinal))];
    }

    private static ICoderVendor? ResolveVendor(string vendor) =>
        vendor == "claude" ? CoderVendors.Claude : CoderVendors.Shipped.Resolve(vendor) is CoderVendorResolution.Launchable launchable ? launchable.Vendor : null;

    private static string PromptIdOf(string vendor) => ResolveVendor(vendor)!.PromptId;

    // ---------- d2: a refused vendor ----------

    [Theory]
    [MemberData(nameof(RefusedVendors))]
    public async Task D2_ARefusedVendor_IsRefusedAtTheStart_AndTheFakeRecordsNoLaunchOfIt(string vendor)
    {
        var kit = KitOf("claude");
        await using var world = new World();
        world.Arrange(kit);
        var (repo, _) = await world.CoderRepoAsync(kit, Step(StepScript));
        var model = $"{vendor}-model-for-the-refusal";
        var server = world.Server(kit, World.CoderList((model, vendor), (kit.Model, "claude")));
        try
        {
            await StdioServer.InitializeAsync(server);

            var answer = await StdioServer.Call(server, 2, "start_role", $$"""{"role":"coder","repoPath":{{JsonSerializer.Serialize(repo.Path)}},"taskFile":"{{CoderRig.TaskFile}}","model":"{{model}}"}""");

            answer.TryGetProperty("jobId", out _).Should().BeFalse("a model of a vendor the table refuses starts no job");
            answer.GetProperty("error").GetString().Should().Contain(model).And.Contain(vendor);
        }
        finally
        {
            await StdioServer.StopAsync(server);
        }

        world.ReviewerPrompts().Should().BeEmpty("the fake recorded no launch of any CLI");
        Directory.EnumerateFileSystemEntries(world.Root.Path).Should().BeEmpty("the refusal came before any worktree");
        Directory.EnumerateFileSystemEntries(world.Jobs.Path).Should().BeEmpty("and before any job folder");
    }

    [Fact]
    public void D2_TheTheoriesReadTheTableOfThisRepository_AndEachShippedVendorHasAKit()
    {
        var shipped = CoderBoundaryTable.Shipped.Vendors.Where(row => row.Ships).Select(row => row.Id).ToList();
        var refused = CoderBoundaryTable.Shipped.Vendors.Where(row => !row.Ships).Select(row => row.Id).ToList();

        shipped.Concat(refused).Should().BeEquivalentTo(CoderBoundaryTable.Shipped.Vendors.Select(row => row.Id), "every row of the table is in one of the two lists the theories use");
        foreach (var vendor in shipped)
        {
            KitOf(vendor).Id.Should().Be(vendor);
        }

        ShippedIds.Should().Equal([CoderBoundaryTable.Claude, .. shipped]);
        RefusedIds.Should().Equal([.. refused, Gemini]);
        CoderBoundaryTable.Shipped.Row(Gemini).Should().BeNull("gemini is the vendor that no row names");
    }

    // ---------- d3: review_plan over a feature folder ----------

    private static async Task<(System.Diagnostics.Process Server, World World)> PlanServerAsync(World world)
    {
        var kit = KitOf("claude");
        world.Arrange(kit);
        var server = world.Server(kit, World.CoderList((kit.Model, "claude")));
        await StdioServer.InitializeAsync(server);

        return (server, world);
    }

    /// <summary>A plan in which the story that comes FIRST by name depends on the one that comes second, so that the topological order is not the order of the names.</summary>
    private static Dictionary<string, string> OutOfOrderPlan()
    {
        var plan = WorkPlan.Base();
        const string Second = "epoch-1/e1-core/stories/s2-second";
        plan[WorkPlan.Story + "/story.yaml"] = "id: s1-first\ntitle: First\ndependsOn:\n  - s2-second\n";
        plan[Second + "/story.yaml"] = "id: s2-second\ntitle: Second\ndependsOn: []\n";
        plan[WorkPlan.TaskPath(Second, "t1-two")] = WorkPlan.Task("t1-two");

        return plan;
    }

    [Fact]
    public async Task D3_AnInvalidFolder_IsRefusedWithZeroReviewerLaunches_AValidOneIsPackedInTopologicalOrder_ForTheReviewerThatFakeCliRecorded()
    {
        await using var world = new World();
        var broken = OutOfOrderPlan();
        broken[WorkPlan.TaskPath("epoch-1/e1-core/stories/s2-second", "t1-two")] = WorkPlan.Task("t1-two", dependsOn: "\n  - t9-nobody");
        await using var invalid = await world.PlanRepoAsync(broken);
        await using var valid = await world.PlanRepoAsync(OutOfOrderPlan());
        var (server, _) = await PlanServerAsync(world);
        try
        {
            await StdioServer.Call(server, 2, "open", $$"""{"repoPath":{{JsonSerializer.Serialize(invalid.Path)}},"branch":"feature"}""");
            var refused = await StdioServer.Call(server, 3, "review_plan", $$"""{"repoPath":{{JsonSerializer.Serialize(invalid.Path)}},"branch":"feature","featureFolder":"plan"}""", timeoutSeconds: 120);

            refused.GetProperty("error").GetString().Should().ContainAll("unknown-dependency", "t9-nobody", "No reviewer was launched");
            world.ReviewerPrompts().Should().BeEmpty("zero reviewer launches");

            await StdioServer.Call(server, 4, "open", $$"""{"repoPath":{{JsonSerializer.Serialize(valid.Path)}},"branch":"feature"}""");
            var round = await StdioServer.Call(server, 5, "review_plan", $$"""{"repoPath":{{JsonSerializer.Serialize(valid.Path)}},"branch":"feature","featureFolder":"plan"}""", timeoutSeconds: 120);

            round.TryGetProperty("error", out var error).Should().BeFalse($"the round ran, got: {error}");
            round.GetProperty("verdict").GetString().Should().Be("proceed");
            var prompts = world.ReviewerPrompts();
            prompts.Should().NotBeEmpty("a valid folder launches the reviewers");
            foreach (var prompt in prompts)
            {
                var order = new[] { "plan/feature.yaml", "plan/PLAN.md", "plan/epoch-1/epoch.yaml", "plan/epoch-1/e1-core/epic.yaml", "plan/epoch-1/e1-core/stories/s2-second/story.yaml", "plan/epoch-1/e1-core/stories/s2-second/tasks/t1-two/task.yaml", "plan/epoch-1/e1-core/stories/s1-first/story.yaml", "plan/epoch-1/e1-core/stories/s1-first/tasks/t1-one/task.yaml" }
                    .Select(path => prompt.IndexOf($"## {path}\n", StringComparison.Ordinal)).ToList();
                order.Should().OnlyContain(index => index >= 0, "every file of the pack is a section of the prompt").And.BeInAscendingOrder("the sections stand in topological order: the story that is depended on comes before the story that depends on it, though its name comes later");
            }
        }
        finally
        {
            await StdioServer.StopAsync(server);
        }
    }

    [Fact]
    public void D3_ThePackOfTheV2FolderOfThisRepository_ForThisEpic_IsUnder256KiB()
    {
        var folder = ListedFolder.OfDisk(Path.Combine(RepoPaths.Root(), "v2"));

        var outcome = WorkPack.Build(folder, new StrictYamlReader(), "v2", "v2/epoch-3/e3-roles-complete");

        var pack = outcome.Should().BeOfType<PackOutcome.Packed>(outcome is PackOutcome.Refused refused ? refused.Sentence : string.Empty).Subject.Pack;
        pack.Bytes.Should().BeLessThan(256 * 1024).And.BeLessThan(WorkPack.PackBytes);
        pack.Sections.Select(section => section.Path).Should().Contain("v2/epoch-3/e3-roles-complete/SPEC.md", "the pack is of this epic and holds its spec");
        pack.Sections.Should().Contain(section => section.Path.StartsWith("v2/epoch-3/e3-roles-complete/stories/s8-proof/", StringComparison.Ordinal));
    }

    // ---------- d4: no vendor, no real sign-in ----------

    [Fact]
    public async Task D4_EveryServerOfThisClass_RunsWithATempHomeATempDataFolderAndTheFakeAsEveryCli()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the environment of a child is read from /proc");
        await using var world = new World();
        var kit = KitOf("claude");
        var server = world.Server(kit, World.CoderList((kit.Model, "claude")));
        try
        {
            await StdioServer.InitializeAsync(server);

            var environment = File.ReadAllText($"/proc/{server.Id}/environ").Split('\0', StringSplitOptions.RemoveEmptyEntries).Select(item => item.Split('=', 2)).Where(pair => pair.Length == 2).ToDictionary(pair => pair[0], pair => pair[1]);

            environment["HOME"].Should().Be(world.Home.Path);
            environment["COAI_DATA_DIR"].Should().Be(world.Data.Path);
            foreach (var name in new[] { "HOME", "COAI_DATA_DIR" })
            {
                environment[name].Should().StartWith(TempDir.Root, $"{name} is below the temp root of the tests, never the person's own folder");
            }

            foreach (var name in new[] { "COAI_ROLE_CLAUDE_CLI", "COAI_ROLE_CODEX_CLI", "COAI_ROLE_AGY_CLI", "COAI_EXE_CODEX", "COAI_EXE_GEMINI" })
            {
                environment[name].Should().Be(RoleJobWorld.FakeCli, $"{name} names the fake: no vendor CLI is ever started");
            }

            environment[CoderWorktree.RootVariable].Should().StartWith(TempDir.Root);
            environment[CoderOsLayer.JobsVariable].Should().StartWith(TempDir.Root);
        }
        finally
        {
            await StdioServer.StopAsync(server);
        }
    }

    [Fact]
    public void D4_NoServerOfThisClass_IsStartedByAnythingButTheOneDoor_AndTheClassNamesNoRealFolderAndNoVendorProgram()
    {
        var source = File.ReadAllText(Path.Combine(RepoPaths.Root(), "src_mcp", "tests", "TheRolesCompleteEpicAcceptanceTests.cs"));
        var code = string.Join('\n', source.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal) && !line.TrimStart().StartsWith("///", StringComparison.Ordinal)));

        Regex.Matches(code, @"StdioServer\.Start\(").Should().HaveCount(1, "ONE door starts every server, and it sets HOME, the data folder and every CLI");
        code.Should().Contain("(\"HOME\", Home.Path)");
        // The words are put together here, so that this test does not find itself.
        code.Should().NotContain(string.Concat("Special", "Folder"), "the class asks the person's home for nothing")
            .And.NotContain(string.Concat("\"/hom", "e/"), "and names no folder of the person")
            .And.NotMatch(@"ProcessRequest\(""(claude|codex|agy|gemini)""", "and starts no vendor program of its own");
        Regex.IsMatch("new ProcessRequest(\"codex\", [], \"x\")", @"ProcessRequest\(""(claude|codex|agy|gemini)""").Should().BeTrue("the control: the scan above matches a line that starts a vendor program");
    }
}
