using System.Text.Json;
using System.Text.Json.Nodes;
using CoaiMcp.Core.Notices;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server.RoleJobs;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The answers FakeCli gives in the failover scenarios — every one built from a RECORDED shape (<c>tests/fixtures/role-stops</c>), so no test types a vendor's sentence of its own. An answer is one entry of FakeCli's model map:
/// <c>{stdout, stderr, exit, sleepMs, outfileText}</c>.
/// </summary>
internal static class FakeRoleAnswers
{
    /// <summary>The role's own output, as a model returns it: <c>{"status":"ok", ...}</c>.</summary>
    internal const string Ok = """{"status":"ok","note":"done"}""";

    /// <summary>The output arm of a declared refusal.</summary>
    internal const string Refusal = """{"status":"refused","reason":"I will not write this plan: it asks for something I decline to do"}""";

    /// <summary>Claude answers: the success envelope that was RECORDED (an unknown model name that ran on the default), with <c>result</c> set to the role's output.</summary>
    internal static object ClaudeSays(string result)
    {
        var envelope = JsonNode.Parse(RoleStopFixtures.Load("claude-unknown-model-name-runs-on-the-default").StdOut)!.AsObject();
        envelope["result"] = result;

        return new { stdout = envelope.ToJsonString(), exit = 0 };
    }

    /// <summary>Claude's recorded 429 envelope — the monthly spend limit — exit 1 and nothing on stderr.</summary>
    internal static object ClaudeSpendLimit() =>
        new { stdout = RoleStopFixtures.Load("claude-spend-limit-429-envelope").StdOut, exit = 1 };

    /// <summary>Claude's recorded lost sign-in envelope, exit 1.</summary>
    internal static object ClaudeNoSignIn() =>
        new { stdout = RoleStopFixtures.Load("claude-no-sign-in").StdOut, exit = 1 };

    /// <summary>Codex answers through the <c>-o</c> file; the stream on stdout is its own events.</summary>
    internal static object CodexSays(string result) =>
        new { outfileText = result, stdout = """{"type":"thread.started","thread_id":"00000000-0000-0000-0000-000000000000"}""" + "\n", exit = 0 };

    /// <summary>Any CLI falling over: a line on stderr and exit 1.</summary>
    internal static object Crash(string stderr = "the CLI fell over: boom") => new { stderr, exit = 1 };

    /// <summary>A throttle that clears: the shape <c>RateLimit</c> reads (a 503 UNAVAILABLE for high demand), exit 1.</summary>
    internal static object Throttled() => new { stderr = "503 UNAVAILABLE: this model is currently experiencing high demand", exit = 1 };

    /// <summary>A clean exit with nothing said.</summary>
    internal static object Silent() => new { exit = 0 };

    /// <summary>A launch that takes longer than the attempt may.</summary>
    internal static object Slow(int milliseconds) => new { sleepMs = milliseconds, stdout = ClaudeEnvelopeText(Ok), exit = 0 };

    private static string ClaudeEnvelopeText(string result)
    {
        var envelope = JsonNode.Parse(RoleStopFixtures.Load("claude-unknown-model-name-runs-on-the-default").StdOut)!.AsObject();
        envelope["result"] = result;

        return envelope.ToJsonString();
    }
}

/// <summary>The test-only role (<c>failover-probe</c>, routed as <c>ba</c>) that exercises the failover loop: it asks its route's models the brief and accepts <c>{"status":"ok"}</c> — the shipped probe asks no model.</summary>
internal sealed class FailoverProbeWork(TimeSpan attemptTimeout, IReadOnlyList<TimeSpan> ladder) : IRoleWork
{
    internal const string Role = "failover-probe";

    public Task<RoleWorkOutcome> RunAsync(IRoleRun run, CancellationToken ct) =>
        new RoleFailover(run, text => RoleAnswers.Read(text, root => root.TryGetProperty("status", out var status) && status.GetString() == "ok" ? string.Empty : "the status is not ok"))
        {
            AttemptTimeout = attemptTimeout,
            Ladder = ladder,
            Roll = () => 0.5,
        }.RunAsync(run.Request.Brief, ct);
}

/// <summary>
/// One failover scenario's world (epoch 3 E1 s3): a data directory with its job store and projection, a real git repository for the role to read, FakeCli answering every CLI by model (FAKECLI_MODEL_MAP), and the real
/// <see cref="RoleRunner"/> running the test-only role in this process — so the launcher, the classifier, the notices, the ledger and the tree check are the product's and only the vendor is a double.
/// </summary>
/// <remarks>In the serial <c>fakecli-env</c> collection: the fake is steered by environment variables of this process, which the runner's children inherit.</remarks>
[Collection("fakecli-env")]
internal sealed class RoleFailoverRig : IAsyncDisposable
{
    private static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(100);

    private readonly RoleJobWorld _world = new();
    private readonly TempDir _temp = TempDir.For("coai-failover-");
    private readonly Dictionary<string, object> _map = [];
    private readonly Dictionary<string, string?> _settings = [];
    private TempGitRepo? _repo;

    private RoleFailoverRig()
    {
        _settings["COAI_ROLE_CLAUDE_CLI"] = RoleJobWorld.FakeCli;
        _settings["COAI_ROLE_CODEX_CLI"] = RoleJobWorld.FakeCli;
    }

    internal RoleJobWorld World => _world;

    internal string Repo => _repo!.Path;

    internal string RecordFolder => _temp.At("launches");

    internal string MapFile => _temp.At("model-map.json");

    internal static async Task<RoleFailoverRig> CreateAsync()
    {
        var rig = new RoleFailoverRig();
        rig._repo = await TempGitRepo.InitAsync(new ProcessLauncher(), "coai-failover-repo-");
        await rig._repo.WriteAsync("plan.md", "the plan\n");
        await rig._repo.CommitAsync("base");
        Directory.CreateDirectory(rig.RecordFolder);
        Environment.SetEnvironmentVariable("FAKECLI_MODE", "vendor");
        Environment.SetEnvironmentVariable("FAKECLI_MODEL_MAP", rig.MapFile);
        Environment.SetEnvironmentVariable("FAKECLI_RECORD_DIR", rig.RecordFolder);

        return rig;
    }

    /// <summary>A setting the runner's work reads (where a vendor's CLI is — <c>COAI_ROLE_CLAUDE_CLI</c>…).</summary>
    internal RoleFailoverRig Setting(string name, string? value)
    {
        _settings[name] = value;

        return this;
    }

    /// <summary>What FakeCli answers for <paramref name="model"/>: one answer, or one per launch of it (the last repeats).</summary>
    internal RoleFailoverRig Says(string model, params object[] answers)
    {
        _map[model] = answers.Length == 1 ? answers[0] : answers;
        File.WriteAllText(MapFile, JsonSerializer.Serialize(_map));

        return this;
    }

    /// <summary>A queued job of the test-only role over <paramref name="route"/> (<c>vendor/model</c>, in order), made the way a start makes it.</summary>
    internal RoleJobRecord NewJob(IReadOnlyList<string> route, string brief = "write the plan", int timeoutMinutes = 5) =>
        _world.Store.Create(
            new RoleJobRequest { JobId = RoleJobIds.New(_world.Clock), Role = FailoverProbeWork.Role, RepoPath = Repo, Brief = brief, TimeoutMinutes = timeoutMinutes },
            new RoleJobPlan(route, 0, false));

    internal RoleRunner RunnerFor(TimeSpan attemptTimeout, IReadOnlyList<TimeSpan>? ladder = null, TimeProvider? clock = null)
    {
        var descriptors = new List<RoleDescriptor> { new(FailoverProbeWork.Role, "a test-only role that asks its route's models", () => new FailoverProbeWork(attemptTimeout, ladder ?? [TimeSpan.FromMinutes(5)]), RouteAs: "ba") };

        return new RoleRunner(_world.ProjectedStore(), descriptors, clock ?? TimeProvider.System, _ => { }, _world.Log)
        {
            WatchEvery = Fast,
            HeartbeatEvery = Fast,
            SettingsOf = () => name => _settings.GetValueOrDefault(name),
            Notice = notice =>
            {
                _world.Notices.Enqueue(notice);

                return true;
            },
        };
    }

    /// <summary>Runs the job to its end with the real runner: its exit code and the record it left.</summary>
    internal async Task<(int Exit, RoleJobRecord Job)> RunAsync(RoleJobRecord job, TimeSpan? attemptTimeout = null, IReadOnlyList<TimeSpan>? ladder = null)
    {
        var exit = await RunnerFor(attemptTimeout ?? TimeSpan.FromSeconds(30), ladder).RunAsync(job.JobId, TestContext.Current.CancellationToken);

        return (exit, _world.Store.Read(job.JobId)!);
    }

    /// <summary>The argv of every launch FakeCli recorded, in the order they were made (the claude capability probe's <c>--help</c> is not a launch and is not recorded).</summary>
    internal IReadOnlyList<string[]> Launches() =>
    [
        .. new DirectoryInfo(RecordFolder).EnumerateFiles("*.argv").OrderBy(file => file.CreationTimeUtc).ThenBy(file => file.LastWriteTimeUtc)
            .Select(file => LaunchRecords.Read(file.FullName).Split('\0')),
    ];

    /// <summary>The model each recorded launch asked for (<c>--model</c> or <c>-m</c>), in order.</summary>
    internal IReadOnlyList<string> Models() =>
    [
        .. Launches().Select(argv =>
        {
            var at = Array.FindIndex(argv, arg => arg is "--model" or "-m");

            return at >= 0 ? argv[at + 1] : string.Empty;
        }),
    ];

    /// <summary>The usage ledger's lines of this data directory.</summary>
    internal IReadOnlyList<JsonElement> LedgerLines()
    {
        var file = Path.Combine(_world.DataDir, "usage.jsonl");

        return File.Exists(file)
            ? [.. File.ReadAllLines(file).Where(line => line.Length > 0).Select(line => JsonDocument.Parse(line).RootElement.Clone())]
            : [];
    }

    /// <summary>The Information lines of the model switches, rendered.</summary>
    internal IReadOnlyList<string> SwitchLines() =>
    [
        .. _world.Said.Where(line => line.Level == Serilog.Events.LogEventLevel.Information && line.RenderMessage().Contains(" switched ", StringComparison.Ordinal)).Select(line => line.RenderMessage()),
    ];

    internal IReadOnlyList<ServerNotice> NoticesOf(string code) => [.. _world.Notices.Where(notice => notice.Code == code)];

    public async ValueTask DisposeAsync()
    {
        FakeCliSteering.Reset();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        _world.Dispose();
        if (_repo is not null)
        {
            await _repo.DisposeAsync();
        }

        _temp.Dispose();
    }
}
