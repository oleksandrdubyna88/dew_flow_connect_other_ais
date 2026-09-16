using CoaiMcp.Collecting;
using CoaiMcp.Core.Collecting;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Normalizer;
using CoaiMcp.Runners.Collecting;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A collector run reaches the database, against a real repository and a real database.
/// </summary>
/// <remarks>
/// The end-to-end the plan round asked for by name: "the described pure classification logic and git
/// fixture can pass while no command or service loads candidates, creates a run id, and updates the
/// four columns, leaving every candidate unprocessed in the shipped product". So this drives the
/// shipped path — `BugsQuery` out, `Collector` through, `RecordCollect` in — and then reads the rows
/// back. (codex.)
/// </remarks>
[Collection("console-out")]
public sealed class CollectRunTests : IAsyncLifetime
{
    private readonly ProcessLauncher _launcher = new();

    /// <summary>A clock a test controls, per `.agents/conventions/common/utc-timestamps.md`.</summary>
    private static readonly TimeProvider Clock =
        new FakeClock(new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero));
    private readonly string _data = Path.Combine(Path.GetTempPath(), "coai-run-" + Guid.NewGuid().ToString("N")[..8]);
    private string _repo = string.Empty;

    private const string Racy = """
        public sealed class Totals
        {
            public int GetOrAdd(string key, int value)
            {
                if (!_items.ContainsKey(key))
                {
                    _items.Add(key, value);
                }

                return _items[key];
            }
        }
        """;

    private const string Fixed = """
        public sealed class Totals
        {
            public int GetOrAdd(string key, int value)
            {
                lock (_items)
                {
                    if (!_items.ContainsKey(key))
                    {
                        _items.Add(key, value);
                    }

                    return _items[key];
                }
            }
        }
        """;

    public async ValueTask InitializeAsync()
    {
        _repo = Directory.CreateTempSubdirectory("coai-runrepo-").FullName;
        await Git("init", "-b", "main");
        await File.WriteAllTextAsync(Path.Combine(_repo, "Totals.cs"), Racy);
        await Commit("the defect");
    }

    public ValueTask DisposeAsync()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var dir in (string[])[_repo, _data])
        {
            try { Directory.Delete(dir, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task ACollectedCandidateGetsItsFixCommitAndItsRunId()
    {
        var broken = await Head();
        await File.WriteAllTextAsync(Path.Combine(_repo, "Totals.cs"), Fixed);
        await Commit("hold the lock");
        var fix = await Head();

        Seed(broken, "Totals.cs", 5);
        var summary = await Run();

        summary.Candidates.Should().Be(1);
        summary.Collected.Should().Be(1);
        summary.RunId.Should().NotBeEmpty("a row must be able to say which run decided it");

        var row = Row();
        row["collect_state"].Should().Be("collected");
        row["fix_sha"].Should().Be(fix);
        row["collect_run_id"].Should().Be(summary.RunId);
        row["collect_reason"].Should().BeEmpty("a collected candidate has nothing to explain");
    }

    [Fact]
    public async Task ASkippedCandidateRecordsTheReason_AndTheFunnelCountsIt()
    {
        var broken = await Head();
        await File.WriteAllTextAsync(Path.Combine(_repo, "notes.md"), "# hello");
        await Commit("a note");
        Seed(broken, "notes.md", 1);

        var summary = await Run();

        summary.Skipped.Should().Be(1);
        summary.Reasons.Should().ContainKey("language_unsupported").WhoseValue.Should().Be(1);
        Row()["collect_state"].Should().Be("skipped");
        Row()["collect_reason"].Should().Be("language_unsupported");
    }

    /// <summary>A second run does not see what the first one handled.</summary>
    /// <remarks>
    /// What `collect_state` being text rather than a flag buys: the first run consumes nothing it did
    /// not decide, and a later run with a repaired walk can still be pointed at the rest.
    /// </remarks>
    [Fact]
    public async Task ASecondRunSkipsWhatTheFirstOneAlreadyHandled()
    {
        var broken = await Head();
        await File.WriteAllTextAsync(Path.Combine(_repo, "Totals.cs"), Fixed);
        await Commit("hold the lock");
        Seed(broken, "Totals.cs", 5);

        (await Run()).Candidates.Should().Be(1);
        (await Run()).Candidates.Should().Be(0, "the first run left nothing unprocessed");
    }

    /// <summary>
    /// A revisiting run WRITES what it finds — the whole point of `collect_state` being text.
    /// </summary>
    /// <remarks>
    /// <para>The claim guard was `collect_state = ''`, which is "the row is still unprocessed" —
    /// and a revisit is by definition a row that is not. So `--all` re-read every candidate, ran
    /// every git command again, recomputed the right answer, and persisted NOTHING. The summary
    /// even reported it as collected, because the summary counts outcomes and the row counts
    /// writes. Silent, and it disabled the one feature the ternary state exists for. Two reviewers
    /// found it independently. (Code round, gemini and codex.)</para>
    /// <para>The shape is the real one: the first run genuinely cannot find a fix because the fix
    /// has not been committed yet, and the second run — after it has — must be able to say so.</para>
    /// </remarks>
    [Fact]
    public async Task ARevisitingRunPersistsWhatItFinds()
    {
        var broken = await Head();
        Seed(broken, "Totals.cs", 5);

        (await Run()).Skipped.Should().Be(1, "nothing has fixed it yet");
        Row()["collect_state"].Should().Be("skipped");

        await File.WriteAllTextAsync(Path.Combine(_repo, "Totals.cs"), Fixed);
        await Commit("hold the lock");
        var fix = await Head();

        var revisit = await Run(all: true);

        revisit.Collected.Should().Be(1);
        revisit.Lost.Should().Be(0, "nobody else touched the row, so the swap must have landed");
        var row = Row();
        row["collect_state"].Should().Be("collected", "a revisit that writes nothing is not a revisit");
        row["fix_sha"].Should().Be(fix);
        row["collect_run_id"].Should().Be(revisit.RunId);
        row["collect_reason"].Should().BeEmpty("the earlier skip reason is no longer true");
    }

    /// <summary>A row another run decided in the meantime is not overwritten.</summary>
    /// <remarks>
    /// The race guard the swap has to keep: two runs read the same pending finding, and the slower
    /// one must not replace the winner's verdict and its fix commit with its own. Fixtured by
    /// deciding the row out from under a run that has already read it.
    /// </remarks>
    [Fact]
    public async Task ARowDecidedByAnotherRunIsNotOverwritten()
    {
        var broken = await Head();
        await File.WriteAllTextAsync(Path.Combine(_repo, "Totals.cs"), Fixed);
        await Commit("hold the lock");
        Seed(broken, "Totals.cs", 5);

        var winner = await Run();
        winner.Collected.Should().Be(1);

        // The loser read the row while it was still pending, and only now gets to write.
        using var db = RoundsDb.Open(_data, Serilog.Core.Logger.None)!;
        var claimed = db.RecordCollect(
            Id(), was: string.Empty, "skipped", "fix_commit_not_found", string.Empty, "the-loser");

        claimed.Should().BeFalse("the row no longer says what that run read");
        Row()["collect_state"].Should().Be("collected");
        Row()["collect_run_id"].Should().Be(winner.RunId);
    }

    /// <summary>A run exists in the database WHILE it happens, not only once it is over.</summary>
    /// <remarks>
    /// The durable-status rule: a button that starts a process must read its state back from storage,
    /// because a flag in a webview dies on reload. A row written only at the finish cannot express
    /// `running` at all, so there would be nothing for the button to read while the work was on.
    /// </remarks>
    [Fact]
    public async Task ARunIsRecorded_AndSaysWhatItDid()
    {
        var broken = await Head();
        await File.WriteAllTextAsync(Path.Combine(_repo, "Totals.cs"), Fixed);
        await Commit("hold the lock");
        Seed(broken, "Totals.cs", 5);

        var summary = await Run();

        using var db = RoundsDb.Open(_data, Serilog.Core.Logger.None)!;
        var run = db.LastCollectRun();
        run.Id.Should().Be(summary.RunId, "the row and the summary are the same run");
        run.State.Should().Be(CollectRunState.Done);
        run.Running.Should().BeFalse();
        run.Collected.Should().Be(1);
        run.Picked.Should().Be(1);
        run.FinishedUtc.Should().NotBeEmpty();
    }

    /// <summary>A run that throws still ends, and says it failed.</summary>
    /// <remarks>
    /// The completing write used to be reachable only by the happy path. A throw on candidate three
    /// then left the row — and the button reading it — in flight for ever, which is the one thing the
    /// durable-status rule forbids outright. (Plan round, codex.)
    /// </remarks>
    [Fact]
    public async Task ARunThatThrowsStillEnds()
    {
        Seed(await Head(), "Totals.cs", 5);
        using var db = RoundsDb.Open(_data, Serilog.Core.Logger.None)!;
        var run = new CollectRun(new ThrowingCollector(), TimeProvider.System);

        var boom = async () => await run.RunAsync(_data, db, 50);

        await boom.Should().ThrowAsync<InvalidOperationException>();
        var row = db.LastCollectRun();
        row.Any.Should().BeTrue("the run started, so it must be on record");
        row.State.Should().Be(CollectRunState.Failed);
        row.Running.Should().BeFalse("a button that reads this must not wait for ever");
        row.FinishedUtc.Should().NotBeEmpty();
    }

    /// <summary>Somebody stopping a run is not the run failing.</summary>
    /// <remarks>
    /// `interrupted` is what a closed window means too, and the difference matters to a person
    /// reading the row later: nothing is known about what the rest would have decided, while the
    /// candidates already claimed keep their own outcomes.
    /// </remarks>
    [Fact]
    public async Task ACancelledRunIsInterrupted_NotFailed()
    {
        Seed(await Head(), "Totals.cs", 5);
        using var db = RoundsDb.Open(_data, Serilog.Core.Logger.None)!;
        var run = new CollectRun(new CancellingCollector(), TimeProvider.System);

        var stopped = async () => await run.RunAsync(_data, db, 50);

        await stopped.Should().ThrowAsync<OperationCanceledException>();
        db.LastCollectRun().State.Should().Be(CollectRunState.Interrupted);
    }

    /// <summary>A model that is not local is refused before anything is read.</summary>
    /// <remarks>
    /// Two reviewers of the plan round found this independently: narrowing the panel's picker narrows
    /// a PICKER. `--collect-bugs` can be run from a terminal and a stale webview posts what it last
    /// rendered, so the refusal has to live at the boundary every caller passes through. A finding's
    /// title, why and fix are the reviewers' own prose about somebody's code and are NOT anonymised.
    /// </remarks>
    [Fact]
    public async Task AModelThatIsNotLocalIsRefused_BeforeAnythingIsRead()
    {
        Seed(await Head(), "Totals.cs", 5);
        using var db = RoundsDb.Open(_data, Serilog.Core.Logger.None)!;
        var counted = new CountingCollector();
        var run = new CollectRun(counted, TimeProvider.System);

        var summary = await run.RunAsync(_data, db, 50, all: false, model: "gemini/pro");

        // A VALUE, not an exception: this is an expected answer to an ordinary request, and the C#
        // doctrine says nothing throws for control flow. (Code round, codex.)
        summary.Refusal.Should().Contain("not a local model");
        summary.Collected.Should().Be(0);
        counted.Asked.Should().Be(0, "it must refuse before it reads, not after");
        db.LastCollectRun().Any.Should().BeFalse("a refused run never started");
    }

    /// <summary>An empty model is no ranking pass, which is the ordinary CLI case.</summary>
    [Fact]
    public async Task NoModelAtAllIsAllowed()
    {
        Seed(await Head(), "notes.md", 1);

        var summary = await Run();

        summary.Candidates.Should().Be(1, "a run with no ranking pass is a normal run");
    }

    private async Task<CollectSummary> Run(bool all = false)
    {
        using var db = RoundsDb.Open(_data, Serilog.Core.Logger.None)!;
        var run = new CollectRun(
            new Collector(new GitHistory(_launcher), new TreeSitterNormalizer()), TimeProvider.System);

        return await run.RunAsync(_data, db, 50, all);
    }

    /// <summary>One accepted, gating, runtime finding pointing at a real commit.</summary>
    private void Seed(string headSha, string file, int line)
    {
        using var db = RoundsDb.Open(_data, Serilog.Core.Logger.None)!;
        var state = new SessionState("s1", _repo, "main", new PanelConfig()) { Stage = Stage.CodeReview };
        var found = new Finding(
            Severity.Major, Category.Reliability, file, line, "a race", "it races", "hold the lock", ["codex"]);
        db.RecordRound(
            state,
            // A FIXED instant, not the machine's: this row is persisted, and the UTC convention
            // bans an ambient clock in anything a test will later need to pin.
            new RoundRecord("CodeReview", 1, "revise", 1, "all answered", Clock.GetUtcNow().UtcDateTime),
            [found],
            new RoundContext("SCOPE", headSha, "claude-code"));
        db.RecordDecisions("s1", "CodeReview", 1, [Decisions.Accept([found], 0)]);
    }

    /// <summary>The seeded finding's row id, which is what a collector run claims by.</summary>
    private long Id()
    {
        using var db = new Microsoft.Data.Sqlite.SqliteConnection(
            $"Data Source={Path.Combine(_data, RoundsDb.FileName)};Pooling=False");
        db.Open();
        using var read = db.CreateCommand();
        read.CommandText = "SELECT id FROM findings";

        return (long)read.ExecuteScalar()!;
    }

    private Dictionary<string, string> Row()
    {
        using var db = new Microsoft.Data.Sqlite.SqliteConnection(
            $"Data Source={Path.Combine(_data, RoundsDb.FileName)};Pooling=False");
        db.Open();
        using var read = db.CreateCommand();
        read.CommandText = "SELECT collect_state, collect_reason, collect_run_id, fix_sha FROM findings";
        using var rows = read.ExecuteReader();
        rows.Read().Should().BeTrue();
        var row = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < rows.FieldCount; i++)
        {
            row[rows.GetName(i)] = rows.IsDBNull(i) ? string.Empty : rows.GetString(i);
        }

        return row;
    }

    private async Task<string> Head()
    {
        var result = await _launcher.RunAsync(new ProcessRequest("git", ["rev-parse", "HEAD"], _repo));
        result.ExitCode.Should().Be(0, result.StdErr);

        return result.StdOut.Trim();
    }

    private async Task Commit(string message)
    {
        await Git("add", ".");
        await Git("-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", "commit", "-m", message);
    }

    private async Task Git(params string[] args)
    {
        var result = await _launcher.RunAsync(new ProcessRequest("git", args, _repo));
        result.ExitCode.Should().Be(0, $"git {string.Join(' ', args)}: {result.StdErr}");
    }
}

/// <summary>A TimeProvider that does not move — the smallest thing the UTC rule asks for.</summary>
internal sealed class FakeClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

/// <summary>A collector that throws — for the paths that only exist when the work goes wrong.</summary>
internal sealed class ThrowingCollector : ICollector
{
    public Task<CollectOutcome> CollectAsync(Candidate candidate, CancellationToken ct = default) =>
        throw new InvalidOperationException("the walk fell over");
}

/// <summary>A collector that is stopped, as a person pressing cancel would stop it.</summary>
internal sealed class CancellingCollector : ICollector
{
    public Task<CollectOutcome> CollectAsync(Candidate candidate, CancellationToken ct = default) =>
        throw new OperationCanceledException();
}

/// <summary>A collector that only counts how often it was asked.</summary>
/// <remarks>
/// The assertion that matters for the allowlist is not that the call FAILED but that the work never
/// began: "refused after reading every finding" would pass a test that only checked for a throw.
/// </remarks>
internal sealed class CountingCollector : ICollector
{
    public int Asked { get; private set; }

    public Task<CollectOutcome> CollectAsync(Candidate candidate, CancellationToken ct = default)
    {
        Asked++;

        return Task.FromResult(CollectOutcome.Skip("never_reached"));
    }
}
