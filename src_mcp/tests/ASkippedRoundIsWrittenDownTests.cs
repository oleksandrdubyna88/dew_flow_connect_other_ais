using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The reason a round did not run has a column, the log carries it, and a database from before the
/// column reads as having none.
/// </summary>
/// <remarks>
/// <para>S2.1 of the feature-review plan (§4.12). Schema step 16 — the plan said 15, and the
/// consultation cadence took that number first — appends <c>rounds.note</c>: the skip reason, with a
/// repeat count when consecutive identical skips coalesced into one row.</para>
/// <para><b>The reader asks the schema, never the version.</b> <c>--log</c> opens the file read-only,
/// so the step never runs for it; selecting the column unconditionally is exactly how a new column
/// once emptied the whole rounds list (<c>PLAN_consultant.md</c>). Absent means <c>''</c> — the one
/// spelling of no data.</para>
/// </remarks>
public sealed class ASkippedRoundIsWrittenDownTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"coai-note-{Guid.NewGuid():N}");
    private readonly Serilog.ILogger _log = Serilog.Core.Logger.None;

    private static readonly SessionState Session =
        new("s1", "D:/repo", SessionKey.FeatureBranch, new PanelConfig()) { Stage = Stage.FeatureReview, Feature = "todo/PLAN_x.md" };

    private static readonly DateTime Started = new(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);

    private static RoundRecord Skipped(string reason, int repeats = 1) =>
        new("FeatureReview", 1, RoundRecord.Skipped, 0, "no reviewer ran", Started)
        {
            StartedUtc = Started,
            Subject = "PLAN_x.md",
            Sha = "abc123",
            Note = reason,
            Repeats = repeats,
        };

    [Fact]
    public void TheNoteColumn_IsTheSixteenthStep()
    {
        Schema.Steps.Length.Should().Be(16, "the cadence took step 15 on 2026-09-25, so the note is 16");
        Schema.Steps[^1].Should().Contain("ALTER TABLE rounds ADD COLUMN note");
    }

    [Fact]
    public void ASkippedRound_WritesItsReason_AndTheLogReadsItBack()
    {
        using (var db = RoundsDb.Open(_dir, _log)!)
        {
            db.RecordRound(Session, Skipped("no vendor is ticked for the feature review"), [], new RoundContext("plan", "abc123"));
        }

        var round = RoundsQuery.Read(_dir).Rounds.Should().ContainSingle().Subject;

        round.Stage.Should().Be("FeatureReview");
        round.Note.Should().Be("no vendor is ticked for the feature review");
    }

    [Fact]
    public void ACoalescedSkip_CarriesItsCount_InTheNote()
    {
        using (var db = RoundsDb.Open(_dir, _log)!)
        {
            db.RecordRound(Session, Skipped("no vendor is ticked for the feature review", repeats: 10), []);
        }

        RoundsQuery.Read(_dir).Rounds.Single().Note.Should().Be("no vendor is ticked for the feature review ×10");
    }

    [Fact]
    public void ARoundThatRan_HasAnEmptyNote()
    {
        using (var db = RoundsDb.Open(_dir, _log)!)
        {
            db.RecordRound(Session, Skipped("") with { Verdict = "proceed" }, []);
        }

        RoundsQuery.Read(_dir).Rounds.Single().Note.Should().BeEmpty("empty is the one spelling of no note");
    }

    [Fact]
    public void TheRecordsLoggedNote_IsTheReason_AndTheCountOnlyWhenItRepeated()
    {
        Skipped("why").LoggedNote.Should().Be("why");
        Skipped("why", repeats: 3).LoggedNote.Should().Be("why ×3");
        (Skipped("why") with { Repeats = 0 }).Repeats.Should().Be(1, "a record from before the field counts once");
    }

    /// <summary>
    /// A database last written by a binary without step 16: the column is absent, and the list must
    /// still come back — with an empty note.
    /// </summary>
    [Fact]
    public void ADatabaseFromBeforeTheNote_StillListsItsRounds_WithAnEmptyNote()
    {
        Directory.CreateDirectory(_dir);
        var file = Path.Combine(_dir, RoundsDb.FileName);
        var adds = Array.FindIndex(Schema.Steps, step => step.Contains("ADD COLUMN note", StringComparison.Ordinal));
        adds.Should().BeGreaterThan(0, "the step that adds the column is what this test stops before");

        using (var db = new SqliteConnection($"Data Source={file};Pooling=False"))
        {
            db.Open();
            using var make = db.CreateCommand();
            make.CommandText = string.Join(";\n", Schema.Steps[..adds]) + $"; PRAGMA user_version={adds}";
            make.ExecuteNonQuery();
            using var insert = db.CreateCommand();
            insert.CommandText =
                "INSERT INTO sessions (id, repo_path, branch, opened_utc) VALUES ('s1', 'D:/repo', 'feat/x', '2026-09-01T00:00:00Z');"
                + "INSERT INTO rounds (session_id, stage, number, status, verdict, started_utc, completed_utc) "
                + "VALUES ('s1', 'CodeReview', 1, 'done', 'proceed', '2026-09-01T00:00:00Z', '2026-09-01T00:01:00Z');";
            insert.ExecuteNonQuery();
        }

        var log = RoundsQuery.Read(_dir);

        log.Rounds.Should().ContainSingle("the rounds list is the page, and a missing column must not empty it");
        log.Rounds[0].Note.Should().BeEmpty();
        log.Rounds[0].ConsultMissed.Should().Be(-1, "and the earlier absent column still reads as unmeasured");
    }

    /// <summary>The new binary opens an older file and migrates it forward — the only supported direction.</summary>
    [Fact]
    public void ANewBinary_MigratesAnOlderDatabaseForward()
    {
        Directory.CreateDirectory(_dir);
        var file = Path.Combine(_dir, RoundsDb.FileName);
        using (var older = new SqliteConnection($"Data Source={file};Pooling=False"))
        {
            older.Open();
            using var make = older.CreateCommand();
            make.CommandText = string.Join(";\n", Schema.Steps[..15]) + "; PRAGMA user_version=15";
            make.ExecuteNonQuery();
        }

        using (var db = RoundsDb.Open(_dir, _log)!)
        {
            db.RecordRound(Session, Skipped("why"), []);
        }

        using var check = new SqliteConnection($"Data Source={file};Pooling=False;Mode=ReadOnly");
        check.Open();
        using var version = check.CreateCommand();
        version.CommandText = "PRAGMA user_version";
        version.ExecuteScalar().Should().Be(16L);
        RoundsQuery.Read(_dir).Rounds.Single().Note.Should().Be("why");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
