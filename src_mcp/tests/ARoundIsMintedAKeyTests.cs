using System.Text.Json;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog.Core;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// At a round's start the engine mints its identity, persists it on the round — the session file and the
/// <c>rounds.round_key</c> column — and a round that starts over (<c>again</c>) is a new round with a new identity
/// (story 4.1 of <c>todo/PLAN_every_round_is_counted.md</c>).
/// </summary>
/// <remarks>
/// <para>Driven end to end through the real <see cref="PanelService"/> with FakeCli, like
/// <see cref="ARoundsNumberComesFromTheJournalTests"/> and for the same reason: the key is minted in the engine, written by
/// the live record and projected by the database, and no unit of the three can prove the three agree. What a launch of
/// the round carries to a Team server is <c>ARoundHasAnIdOnTheWireTests</c>'s.</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class ARoundIsMintedAKeyTests : IAsyncLifetime
{
    private const string Scope = """
        # SCOPE

        The reviewer's own words must survive a failure. A reviewer that falls over is recorded with
        an outcome and nothing else, so the round summary says "unparseable" without the text that
        would not parse, and the same answer replayed by hand goes through cleanly.

        When it is done: the raw answer is kept beside the session, the refusal names the file, and
        a failed reviewer still reports what it consumed. Constraint: no new dependency, and the
        launcher stays the one in v2.Shared.
        """;

    private const string Clean = """{"findings": []}""";

    private readonly ProcessLauncher _launcher = new();
    private string _repo = string.Empty;
    private string _data = string.Empty;

    private static string FakeCliExe => Path.Combine(
        AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli");

    public async ValueTask InitializeAsync()
    {
        _repo = Directory.CreateTempSubdirectory("coai-key-repo-").FullName;
        _data = Directory.CreateTempSubdirectory("coai-key-data-").FullName;
        await Git("init", "-b", "main");
        await File.WriteAllTextAsync(Path.Combine(_repo, "app.cs"), "v1\n");
        await Git("add", ".");
        await Git("commit", "-m", "base");
        await Git("checkout", "-b", "feature");
        await File.WriteAllTextAsync(Path.Combine(_repo, "app.cs"), string.Concat(Enumerable.Repeat("line\n", 100)));
        await Git("add", ".");
        await Git("commit", "-m", "the feature");
        Environment.SetEnvironmentVariable("FAKECLI_MODE", "vendor");
    }

    public ValueTask DisposeAsync()
    {
        foreach (var name in (string[])["FAKECLI_MODE", "FAKECLI_STDOUT", "FAKECLI_OUTFILE_TEXT", "FAKECLI_EXIT", "FAKECLI_STDERR"])
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var dir in (string[])[_repo, _data])
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        return ValueTask.CompletedTask;
    }

    private static void Script(string answer)
    {
        Environment.SetEnvironmentVariable("FAKECLI_STDOUT", answer);
        Environment.SetEnvironmentVariable("FAKECLI_OUTFILE_TEXT", answer);
        Environment.SetEnvironmentVariable("FAKECLI_EXIT", "0");
    }

    private async Task Git(params string[] args)
    {
        var result = await _launcher.RunAsync(new ProcessRequest(
            "git", ["-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", .. args], _repo));
        result.ExitCode.Should().Be(0, $"git {string.Join(' ', args)}: {result.StdErr}");
    }

    private PanelService Service() =>
        new(
            new PanelSettings
            {
                Providers = [new("codex") { ExecutablePath = FakeCliExe }],
                Rounds = PanelConfig.Uniform(3, 2, StagePolicy.Human),
                DataDir = _data,
                ReviewerTimeout = TimeSpan.FromSeconds(30),
                RateLimitBackoff = TimeSpan.FromMilliseconds(5),
            },
            VaultKeys.None("no vault in tests"),
            default,
            _launcher,
            Logger.None, Noticing.None);

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    /// <summary>The keys of one stage's rounds, straight from the table — in round order.</summary>
    private IReadOnlyList<string> KeysInTheDatabase(string stage)
    {
        using var db = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(_data, Store.RoundsDb.FileName)}");
        db.Open();
        using var read = db.CreateCommand();
        read.CommandText = "SELECT round_key FROM rounds WHERE stage = $stage ORDER BY number";
        read.Parameters.AddWithValue("$stage", stage);
        using var rows = read.ExecuteReader();
        var found = new List<string>();
        while (rows.Read())
        {
            found.Add(rows.GetString(0));
        }

        return found;
    }

    /// <summary>The same stage's keys as the session file remembers them — through the status the extension reads.</summary>
    private async Task<IReadOnlyList<string>> KeysInTheSessionFile(PanelService service, string stage) =>
        [.. Parse(await service.StatusAsync(_repo, "feature")).GetProperty("rounds").EnumerateArray()
            .Where(r => r.GetProperty("stage").GetString() == stage)
            .Select(r => r.TryGetProperty("key", out var key) ? key.GetString() ?? string.Empty : "<absent>")];

    [Fact]
    public async Task ARound_IsWrittenWithItsKey_InTheSessionFileAndTheDatabase()
    {
        var service = Service();
        await service.OpenAsync(_repo, "feature");
        Script(Clean);
        Parse(await service.ReviewPlanAsync(_repo, "feature", "a plan nobody objects to")).GetProperty("verdict").GetString().Should().Be("proceed");

        var inFile = (await KeysInTheSessionFile(service, "PlanReview")).Should().ContainSingle().Which;
        RoundKeys.IsWellFormed(inFile).Should().BeTrue($"the round was minted a 32-hex-character key when it started, and the session file keeps it — got '{inFile}'");
        KeysInTheDatabase("PlanReview").Should().Equal([inFile], "the projection keeps the same key under round_key");
    }

    /// <summary>
    /// The ENGINE stamps the key on every launch of the round — including a conversation's second turn, which the stage
    /// builds fresh and the turn loop hands its first turn's key — and it is the key the record carries.
    /// </summary>
    [Fact]
    public async Task EveryLaunchOfTheRound_IsStampedWithTheRecordsKey_ASecondTurnIncluded()
    {
        var spy = new SpyAdapter();
        var launcher = new VendorScripted(_launcher);
        var service = new PanelService(
            new PanelSettings { Providers = [new("spy") { ExecutablePath = "vendor" }], DataDir = _data, ReviewerTimeout = TimeSpan.FromSeconds(30) },
            VaultKeys.None("no vault in tests"), default, launcher, Logger.None, Noticing.None);
        ReviewerWork Turn() => new(new ReviewerInvocation("spy", "Architecture", new ProcessRequest("vendor", [], _data) { Timeout = TimeSpan.FromSeconds(30) }, Adapter: spy));
        var stage = new StageRun(RoundMachine.BeginPlanRound, false, Stage.PlanReview, false,
            (_, _, _, _) => Task.FromResult(new RoundWork([Turn() with { Continue = new OneMoreTurn(Turn()) }], [])))
        {
            Session = new SessionRule.CreateIfAbsent(() => new PersistedSession(
                new SessionState("stamped", _repo, "feature", service.Settings.Rounds) { Stage = Stage.PlanReview }, [])),
        };

        var answer = Parse(await service.Engine.RunStageAsync(_repo, "feature", "a plan nobody objects to", stage, CancellationToken.None));

        answer.TryGetProperty("error", out var error).Should().BeFalse($"{error}");
        var key = (await KeysInTheSessionFile(service, "PlanReview")).Should().ContainSingle().Which;
        RoundKeys.IsWellFormed(key).Should().BeTrue(key);
        spy.Seen.Should().HaveCount(2, "the first turn and the one the continuation asked for");
        spy.Seen.Select(launch => launch.Round).Should().Equal([key, key],
            "the engine stamps the work it was handed, and the turn loop hands a later turn — built without one — its first turn's key");
    }

    /// <summary>§9.6's sequence — round 1, resolve, a new commit, <c>again</c> — and each round has its own identity.</summary>
    [Fact]
    public async Task Again_GetsANewId()
    {
        var service = Service();
        await service.OpenAsync(_repo, "feature");
        Script(Clean);
        await service.ReviewPlanAsync(_repo, "feature", "a plan nobody objects to");
        await service.ResolveAsync(_repo, "feature", "[]");
        Script(Clean);
        Parse(await service.ReviewCodeAsync(_repo, "feature", "main", Scope)).GetProperty("verdict").GetString().Should().Be("proceed");
        await service.ResolveAsync(_repo, "feature", "[]");
        await File.AppendAllTextAsync(Path.Combine(_repo, "app.cs"), "the checkpoint's follow-up\n");
        await Git("add", ".");
        await Git("commit", "-m", "after the checkpoint");

        Script(Clean);
        var again = Parse(await service.ReviewCodeAsync(_repo, "feature", "main", Scope, again: true));
        again.GetProperty("verdict").GetString().Should().Be("proceed", $"the new commits are reviewed: {again}");

        var keys = KeysInTheDatabase("CodeReview");
        keys.Should().HaveCount(2, "two code rounds ran");
        keys.Should().OnlyContain(key => RoundKeys.IsWellFormed(key), "each round was minted a key of its own");
        keys.Distinct().Should().HaveCount(2, "again starts a new round, and a new round is a new identity — never the first round's");
        (await KeysInTheSessionFile(service, "CodeReview")).Should().Equal(keys, "and the session file names them the same way the database does");
    }

    /// <summary>An adapter that keeps every launch it was asked to shape — the seam every launch passes through.</summary>
    private sealed class SpyAdapter : IReviewerRuntime
    {
        public List<ReviewerInvocation> Seen { get; } = [];

        public string Provider => "spy";

        public ReviewerInvocation Build(string role, string prompt, string worktreePath, string schemaFilePath, string outputDir, ReviewerSettings settings) =>
            throw new NotSupportedException("the test builds its own work");

        public ReviewerInvocation ForLaunch(ReviewerInvocation invocation)
        {
            lock (Seen)
            {
                Seen.Add(invocation);
            }

            return invocation;
        }
    }

    /// <summary>A continuation that asks for exactly one more turn — <paramref name="next"/>, built with no round of its own.</summary>
    private sealed class OneMoreTurn(ReviewerWork next) : IReviewerContinuation
    {
        public int FollowUps => 1;

        public Task<TurnDecision> AfterAsync(int turn, ReviewerOutcome.Ok answered, CancellationToken ct) =>
            Task.FromResult<TurnDecision>(turn == 1 ? new TurnDecision.Next(next, "one more") : new TurnDecision.Stop("that was the one"));
    }

    /// <summary>git through the real launcher; every vendor launch answered with a clean review.</summary>
    private sealed class VendorScripted(IProcessLauncher git) : IProcessLauncher
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default) =>
            request.Executable == "git"
                ? git.RunAsync(request, ct)
                : Task.FromResult(new ProcessResult(0, Clean, string.Empty, TimedOut: false));
    }
}
