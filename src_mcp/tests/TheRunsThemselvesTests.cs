using CoaiMcp.Core.Collecting;
using CoaiMcp.Store;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A collector run, as something the database remembers rather than something stderr scrolls past.
/// </summary>
/// <remarks>
/// <para>Story 3 stamped a run id on every finding it claimed, so <i>which run decided this</i> was
/// answerable — and nothing anywhere recorded what a run DID, or whether one was happening. That was
/// enough for a CLI one-shot and is not enough for a button, because the durable-status rule wants a
/// state that survives a reload and is read back from storage.</para>
/// <para>Real SQLite over a temp directory, as <see cref="RoundsDbTests"/> does and for the same
/// reason: the thing under test is the SQL and the migration, and a fake would assert what we believe
/// about them.</para>
/// </remarks>
public sealed class TheRunsThemselvesTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "coai-runs-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly Serilog.ILogger _log = Serilog.Core.Logger.None;

    /// <summary>A clock the test moves by hand — the heartbeat is the thing being tested.</summary>
    private readonly MovableClock _clock = new(new DateTimeOffset(2026, 9, 16, 9, 0, 0, TimeSpan.Zero));

    private RoundsDb Db() => RoundsDb.Open(_dir, _log, _clock)!;

    [Fact]
    public void ARunIsRunningBeforeItIsAnythingElse()
    {
        using var db = Db();

        db.StartCollectRun("r1", "local/qwen");

        var run = db.LastCollectRun();
        run.Any.Should().BeTrue();
        run.Running.Should().BeTrue("a run that cannot be seen while it happens is the whole defect");
        run.State.Should().Be(CollectRunState.Running);
        run.Model.Should().Be("local/qwen");
        run.FinishedUtc.Should().BeEmpty();
        run.HeartbeatUtc.Should().NotBeEmpty("the sweep has nothing to judge without one");
    }

    /// <summary>What a run did, written as it goes rather than once at the end.</summary>
    /// <remarks>
    /// Two writes cannot express progress, and the plan promised progress. The beat is on the write
    /// that already happens per candidate, so the cost is a column list rather than a statement.
    /// </remarks>
    [Fact]
    public void ABeatCarriesHowFarItHasGot()
    {
        using var db = Db();
        db.StartCollectRun("r1", "local/qwen");

        db.BeatCollectRun("r1", candidates: 10, picked: 10, new CollectTally(Collected: 3, Skipped: 1));

        var run = db.LastCollectRun();
        run.Candidates.Should().Be(10);
        run.Collected.Should().Be(3);
        run.Skipped.Should().Be(1);
        run.Running.Should().BeTrue("a beat is not an ending");
    }

    [Fact]
    public void AFinishedRunSaysWhatItDid()
    {
        using var db = Db();
        db.StartCollectRun("r1", "local/qwen");

        db.FinishCollectRun(
            "r1", CollectRunState.Done, 10, 10, new CollectTally(7, 2, 1), "language_unsupported:2");

        var run = db.LastCollectRun();
        run.Running.Should().BeFalse();
        run.State.Should().Be(CollectRunState.Done);
        run.FinishedUtc.Should().NotBeEmpty();
        run.Collected.Should().Be(7);
        run.Reasons.Should().Be("language_unsupported:2");
    }

    /// <summary>
    /// A run that is happening RIGHT NOW is not swept, however the sweep was reached.
    /// </summary>
    /// <remarks>
    /// The defect the plan round caught before it shipped. The panel reaches this database only
    /// through one-shot invocations, so a sweep that took every row with no `finished_utc` would end
    /// the run that is alive at that moment — from the very process that was asked to display it.
    /// (Plan round, gemini.)
    /// </remarks>
    [Fact]
    public void ALiveRunIsNotSwept()
    {
        using var db = Db();
        db.StartCollectRun("r1", "local/qwen");

        // Time passes, and the run keeps saying it is alive — which is what a beat is for.
        _clock.Advance(TimeSpan.FromMinutes(20));
        db.BeatCollectRun("r1", 10, 10, new CollectTally(Collected: 2));
        _clock.Advance(TimeSpan.FromMinutes(20));

        db.SweepStaleCollectRuns(TimeSpan.FromMinutes(30)).Should().Be(0, "it spoke 20 minutes ago");
        db.LastCollectRun().Running.Should().BeTrue();
    }

    [Fact]
    public void ARunThatStoppedSpeakingIsEnded()
    {
        using var db = Db();
        db.StartCollectRun("r1", "local/qwen");
        db.BeatCollectRun("r1", 10, 10, new CollectTally(Collected: 2));

        _clock.Advance(TimeSpan.FromMinutes(31));

        db.SweepStaleCollectRuns(TimeSpan.FromMinutes(30)).Should().Be(1);
        var run = db.LastCollectRun();
        run.State.Should().Be(CollectRunState.Interrupted);
        run.Running.Should().BeFalse("the button must not stay in flight because a window closed");
    }

    /// <summary>The sweep marks; it never deletes.</summary>
    /// <remarks>
    /// The id is a foreign key in all but name — every finding the run claimed carries it — so a
    /// deleted row leaves those findings naming a run that cannot be looked up. What it did before it
    /// was interrupted is also kept: those candidates were decided, and their outcomes are true.
    /// </remarks>
    [Fact]
    public void AnInterruptedRunKeepsItsRowAndWhatItHadDone()
    {
        using var db = Db();
        db.StartCollectRun("r1", "local/qwen");
        db.BeatCollectRun("r1", 10, 10, new CollectTally(Collected: 4, Skipped: 1));
        _clock.Advance(TimeSpan.FromMinutes(31));

        db.SweepStaleCollectRuns(TimeSpan.FromMinutes(30));

        var run = db.LastCollectRun();
        run.Id.Should().Be("r1", "the findings that name this run must still resolve to it");
        run.Collected.Should().Be(4, "four candidates really were decided");
        run.FinishedUtc.Should().NotBeEmpty("an ended run has an end, whoever ended it");
    }

    /// <summary>No run is a state the panel renders, and it is not an error.</summary>
    [Fact]
    public void ADatabaseWithNoRunsAnswersEmpty_NotNull()
    {
        using var db = Db();

        var run = db.LastCollectRun();

        run.Any.Should().BeFalse();
        run.Id.Should().BeEmpty();
        run.Running.Should().BeFalse("nothing is happening, which is different from not knowing");
    }

    /// <summary>
    /// A database written before this table gains it, and keeps everything it had.
    /// </summary>
    /// <remarks>
    /// Spelled by hand rather than taken from the current <c>Schema</c> constant: a migration test
    /// built from the code it tests follows that code forward and stops testing anything. This is the
    /// shape <c>ADatabaseFromBeforeTheseColumns_GainsThem_AndItsOwnRoundsStillRead</c> already uses.
    /// </remarks>
    [Fact]
    public void ADatabaseFromBeforeThisTable_GainsIt()
    {
        Directory.CreateDirectory(_dir);
        using (var older = new SqliteConnection(
            $"Data Source={Path.Combine(_dir, RoundsDb.FileName)};Pooling=False"))
        {
            older.Open();
            using var make = older.CreateCommand();
            make.CommandText = """
                CREATE TABLE sessions (
                    id TEXT PRIMARY KEY, repo_path TEXT NOT NULL,
                    branch TEXT NOT NULL, opened_utc TEXT NOT NULL
                );
                INSERT INTO sessions (id, repo_path, branch, opened_utc)
                VALUES ('s0', 'D:/repo', 'feat/old', '2026-09-01T00:00:00Z');
                """;
            make.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();
        using var db = Db();

        db.StartCollectRun("r1", "local/qwen");
        db.LastCollectRun().Id.Should().Be("r1", "the table must arrive as its own appended step");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

/// <summary>A clock a test moves by hand, for the one thing here that is about elapsed time.</summary>
/// <remarks>
/// The alternative is sleeping for thirty-one minutes, which is not a test anybody runs. Injected
/// through <c>RoundsDb.Open</c>'s own <c>TimeProvider</c> parameter, per the family's UTC rule: a
/// clock a test cannot control is a column a test cannot assert.
/// </remarks>
internal sealed class MovableClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now = _now.Add(by);
}
