using System.Text.Json;
using System.Text.Json.Nodes;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E1 s5 t2 — the acceptance of the epic on the REAL <c>coai-mcp</c>, over real stdio, with the real detached runner and the real BA role: FakeCli is the only double, and it answers every vendor CLI by model from
/// ONE map, so nothing is paid. Two claims of the epic's "done when": (d1) a refusal, a limit and a crash each send the job to the next model — three switches, three log lines, three notices — and the BA's plan is still
/// written; (d2) the MCP server that started a job is killed in the middle of an attempt, a NEW server reads the same job, and it ends <c>succeeded</c> with its files written.
/// </summary>
/// <remarks>
/// <para>The runner is started through <c>setsid</c> (the launcher a machine without a user manager uses), so it inherits the FakeCli steering variables from the server's own environment: those names are not on the
/// allow-list of <c>env.json</c>, on purpose. The in-process failover scenarios are <see cref="ARoleFailsOverTests"/>; this suite adds the wire, the second process and the real publish. Skipped on Windows (the setsid road).</para>
/// <para>The limit of the three is a 503 high-demand throttle, not the recorded spend-limit envelope: a spent limit also skips the vendor's other models, and this route is one vendor on purpose so that every switch is to the very next model.</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class TheRoleJobsEpicAcceptanceTests
{
    private static readonly string[] OneVendorRoute = ["fable", "opus", "sonnet", "haiku"];

    private static string RouteSetting(params string[] models) =>
        new JsonObject { ["ba"] = new JsonArray([.. models.Select(model => (JsonNode)new JsonObject { ["model"] = model, ["vendor"] = "claude" })]) }.ToJsonString();

    private static string ClaudeStdout(string result) => ((JsonObject)JsonNode.Parse(JsonSerializer.Serialize(FakeRoleAnswers.ClaudeSays(result)))!)["stdout"]!.GetValue<string>();

    private sealed class World : IAsyncDisposable
    {
        private readonly TempDir _temp = TempDir.For("coai-epic-acceptance-");

        private World(TempGitRepo repo, TempDir data)
        {
            Repo = repo;
            Data = data;
        }

        internal TempGitRepo Repo { get; }

        internal TempDir Data { get; }

        internal string Map => _temp.At("model-map.json");

        internal string Launches => _temp.At("launches");

        internal static async Task<World> CreateAsync()
        {
            var world = new World(await TempGitRepo.InitAsync(new ProcessLauncher(), "coai-epic-acceptance-repo-"), TempDir.For("coai-epic-acceptance-data-"));
            await world.Repo.WriteAsync("keep.txt", "base\n");
            BaScenario.Lay(world.Repo.Path);
            await world.Repo.CommitAsync("the plan folder");
            Directory.CreateDirectory(world.Launches);

            return world;
        }

        internal World Says(Dictionary<string, object> map)
        {
            File.WriteAllText(Map, JsonSerializer.Serialize(map));

            return this;
        }

        /// <summary>A real server on this world's data folder; the fake CLI answers both vendors from the map.</summary>
        internal System.Diagnostics.Process Server(params string[] route) => StdioServer.Start(
            Data.Path,
            logLevel: "information",
            alsoInTheEnvironment:
            [
                ("COAI_ROLE_LAUNCHER", "setsid"),
                ("COAI_ROLE_CLAUDE_PERSON_SIGN_IN", "1"),
                ("COAI_ROLES_MODELS", RouteSetting(route)),
                ("COAI_ROLE_CLAUDE_CLI", RoleJobWorld.FakeCli),
                ("FAKECLI_MODE", "vendor"),
                ("FAKECLI_MODEL_MAP", Map),
                ("FAKECLI_RECORD_DIR", Launches),
            ]);

        internal async Task<JsonElement> StartAsync(System.Diagnostics.Process server) =>
            await StdioServer.Call(server, 2, "start_role", $$"""{"role":"ba","repoPath":{{JsonSerializer.Serialize(Repo.Path)}},"taskFile":"{{BaScenario.EpicFile}}"}""");

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

        internal IReadOnlyList<JsonElement> Notices(string code)
        {
            var file = Path.Combine(Data.Path, "server-notices.jsonl");

            return File.Exists(file)
                ? [.. File.ReadAllLines(file).Where(line => line.Length > 0).Select(line => JsonDocument.Parse(line).RootElement.Clone()).Where(line => line.GetProperty("code").GetString() == code)]
                : [];
        }

        /// <summary>The Information lines of the runners' own logs that say a model was switched.</summary>
        internal IReadOnlyList<string> SwitchLines() =>
        [
            .. Directory.Exists(Path.Combine(Data.Path, "logs"))
                ? Directory.EnumerateFiles(Path.Combine(Data.Path, "logs"), "*role*.log", SearchOption.AllDirectories)
                    .SelectMany(file => ReadShared(file)).Where(line => line.Contains(" switched ", StringComparison.Ordinal) && line.Contains(" -> ", StringComparison.Ordinal))
                : [],
        ];

        private static IEnumerable<string> ReadShared(string file)
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);

            return reader.ReadToEnd().Split('\n');
        }

        internal string Stories => Path.Combine(Repo.Path, BaScenario.EpicFolder, "stories");

        public async ValueTask DisposeAsync()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            await Repo.DisposeAsync();
            Data.Dispose();
            _temp.Dispose();
        }
    }

    [Fact]
    public async Task D1_ARefusal_ALimit_AndACrash_EachGoToTheNextModel_ThreeSwitches_ThreeLines_ThreeNotices()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the setsid road");
        await using var world = await World.CreateAsync();
        world.Says(new()
        {
            ["fable"] = FakeRoleAnswers.ClaudeSays(FakeRoleAnswers.Refusal),
            ["opus"] = FakeRoleAnswers.Throttled(),
            ["sonnet"] = FakeRoleAnswers.Crash("sonnet fell over"),
            ["haiku"] = FakeRoleAnswers.ClaudeSays(BaScenario.Valid().ToJsonString()),
        });
        var server = world.Server(OneVendorRoute);
        try
        {
            await StdioServer.InitializeAsync(server);
            var started = await world.StartAsync(server);
            started.GetProperty("route").EnumerateArray().Select(entry => entry.GetString()).Should().Equal("claude/fable", "claude/opus", "claude/sonnet", "claude/haiku");
            var jobId = started.GetProperty("jobId").GetString()!;

            var status = await World.WaitEndAsync(server, jobId);

            status.GetProperty("state").GetString().Should().Be("succeeded", status.ToString());
            status.GetProperty("attempts").EnumerateArray().Select(attempt => (attempt.GetProperty("model").GetString(), attempt.GetProperty("stop").GetString()))
                .Should().Equal(("fable", "refusal"), ("opus", "limit"), ("sonnet", "crash"), ("haiku", string.Empty));
            status.GetProperty("result").GetProperty("status").GetString().Should().Be("ok");

            var notices = world.Notices("role-model-switched");
            notices.Should().HaveCount(3, "one notice per switch");
            notices.Select(notice => notice.GetProperty("subject").GetString()).Should().Equal($"{jobId}/1", $"{jobId}/2", $"{jobId}/3");
            var lines = world.SwitchLines();
            lines.Should().HaveCount(3, "one Information line per switch");
            lines[0].Should().Contain($"role job {jobId} (ba) switched claude/fable -> claude/opus after refusal");
            lines[1].Should().Contain("switched claude/opus -> claude/sonnet after limit");
            lines[2].Should().Contain("switched claude/sonnet -> claude/haiku after crash").And.Contain("sonnet fell over");
            world.Notices("role-job-failed").Should().BeEmpty();

            Directory.GetFiles(world.Stories, "task.yaml", SearchOption.AllDirectories).Should().HaveCount(3, "the plan of the fourth model was written");
            File.Exists(Path.Combine(world.Stories, "ba-manifest.json")).Should().BeTrue();
        }
        finally
        {
            await StdioServer.StopAsync(server);
        }
    }

    [Fact]
    public async Task D2_TheMcpServerIsKilledInTheMiddleOfAnAttempt_ANewServerReadsTheJob_AndItEndsSucceeded()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the setsid road");
        await using var world = await World.CreateAsync();
        var slow = (JsonObject)JsonNode.Parse(JsonSerializer.Serialize(FakeRoleAnswers.ClaudeSays(BaScenario.Valid().ToJsonString())))!;
        slow["sleepMs"] = 6000;
        world.Says(new()
        {
            ["fable"] = FakeRoleAnswers.Crash("fable fell over"),
            ["opus"] = JsonSerializer.Deserialize<JsonElement>(slow.ToJsonString()),
        });
        string jobId;
        var first = world.Server("fable", "opus");
        try
        {
            await StdioServer.InitializeAsync(first);
            var started = await world.StartAsync(first);
            jobId = started.GetProperty("jobId").GetString()!;
            var store = new RoleJobStore(world.Data.Path, TimeProvider.System, Serilog.Core.Logger.None);
            RoleJobWorld.WaitFor(() => Directory.GetFiles(world.Launches, "*.argv").Length >= 2 && store.Read(jobId) is { State: RoleJobStates.Running }, "the second model to be launched", seconds: 60);
            Directory.Exists(world.Stories).Should().BeFalse("the job is in the middle of an attempt: nothing is written yet");
        }
        finally
        {
            await StdioServer.StopAsync(first);
        }

        var second = world.Server("fable", "opus");
        try
        {
            await StdioServer.InitializeAsync(second);

            var status = await World.WaitEndAsync(second, jobId);

            status.GetProperty("state").GetString().Should().Be("succeeded", status.ToString());
            status.GetProperty("generation").GetInt32().Should().Be(1, "the runner was never taken over: it outlived the server");
            status.GetProperty("attempts").EnumerateArray().Select(attempt => (attempt.GetProperty("model").GetString(), attempt.GetProperty("stop").GetString()))
                .Should().Equal(("fable", "crash"), ("opus", string.Empty));
            Directory.GetFiles(world.Stories, "task.yaml", SearchOption.AllDirectories).Should().HaveCount(3);
            world.Notices("role-job-lost").Should().BeEmpty();
        }
        finally
        {
            await StdioServer.StopAsync(second);
        }
    }
}
