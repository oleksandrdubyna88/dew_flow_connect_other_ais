using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using CoaiMcp.Storage;
using CoaiMcp.Store;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;
using static CoaiMcp.Tests.AuthorsSnapshotFixtures;

namespace CoaiMcp.Tests;

/// <summary>
/// Schema step 24: <c>round_authors</c> and the authors columns of <c>rounds</c> (E5.S2 of
/// <c>research/PLAN_a_round_records_who_wrote_the_code_and_how_much_E5.md</c>) — appended after step 23, reached by every database shape in the
/// field, and left alone by what an OLDER server runs on a file that already has it. Shaped as step 23's test is
/// (<see cref="ThePlanSourceIsStepTwentyThreeTests"/>).
/// </summary>
/// <remarks>
/// Real SQLite over a temp directory named <c>testsToDelete-…</c>; the two preview-fork shapes are built as step 23's test builds them.
/// </remarks>
public sealed class TheRoundsAuthorsAreStepTwentyFourTests : IDisposable
{
    private readonly TempDir _dir = TempDir.For("testsToDelete-coai-step24-");
    private readonly Serilog.ILogger _log = Serilog.Core.Logger.None;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _dir.Dispose();
    }

    private string Database => _dir.At(RoundsDb.FileName);

    private static int AuthorsStep => Array.IndexOf(Schema.Steps, Schema.TheRoundsAuthors);

    private static int QuestionsStep => Array.IndexOf(Schema.Steps, Schema.TheQuestionsAsked);

    /// <summary>Every column step 24 adds to <c>rounds</c> — the fourteen of the plan, named once here.</summary>
    private static readonly string[] AuthorsColumns =
    [
        "authors_state", "authors_session", "authors_id_source", "authors_reader", "authors_attribution", "submodule_writes", "uncovered_writes",
        "ambiguous_agents", "ambiguous_launches", "ambiguous_turns", "ambiguous_input_tokens", "ambiguous_output_tokens",
        "ambiguous_cache_creation_tokens", "ambiguous_cache_read_tokens",
    ];

    private void MakeAt(IEnumerable<string> steps, string alsoRun = "")
    {
        var list = steps.ToArray();
        using var db = new SqliteConnection($"Data Source={Database};Pooling=False");
        db.Open();
        using var make = db.CreateCommand();
        make.CommandText = string.Join(";\n", list) + $"; PRAGMA user_version={list.Length};" + alsoRun;
        make.ExecuteNonQuery();
    }

    private (long Version, bool State, bool Table) Shape()
    {
        using var db = new SqliteConnection($"Data Source={Database};Pooling=False;Mode=ReadOnly");
        db.Open();
        using var ask = db.CreateCommand();
        ask.CommandText = "PRAGMA user_version";

        return ((long)ask.ExecuteScalar()!, RoundsQuery.HasColumn(db, "rounds", "authors_state"), HasTable(db, "round_authors"));
    }

    private static bool HasTable(SqliteConnection db, string table)
    {
        using var ask = db.CreateCommand();
        ask.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name";
        ask.Parameters.AddWithValue("$name", table);

        return (long)ask.ExecuteScalar()! == 1;
    }

    private void OpensAndEndsWithEveryStep(string because)
    {
        using (var opened = RoundsDb.Open(_dir, _log))
        {
            opened.Should().NotBeNull(because);
        }

        Shape().Should().Be(((long)Schema.Steps.Length, true, true));
    }

    private const string OneOldRound =
        "INSERT INTO sessions (id, repo_path, branch, opened_utc) VALUES ('s', 'D:/repo', 'main', '2026-10-01T00:00:00.0000000Z');"
        + "INSERT INTO rounds (id, session_id, stage, number, status, verdict, started_utc, completed_utc) VALUES (1, 's', 'CodeReview', 1, 'done', 'proceed', '2026-10-01T00:00:00.0000000Z', '2026-10-01T00:05:00.0000000Z');";

    private void Record(params RoundRecord[] rounds)
    {
        using var db = RoundsDb.Open(_dir, _log)!;
        foreach (var round in rounds)
        {
            db.RecordRound(State(), round, []);
        }
    }

    [Fact]
    public void TheRoundsAuthors_IsTheTwentyFourthStep_AppendedNeverInserted()
    {
        AuthorsStep.Should().Be(23, "step 24 by position: step 23 is the plan source, and a step is never inserted ahead of one that shipped");
        Schema.Steps.Length.Should().BeGreaterThanOrEqualTo(24);
        Schema.Steps[23].Should().Be(Schema.TheRoundsAuthors);
    }

    [Theory]
    [InlineData(23)]
    [InlineData(22)]
    [InlineData(21)]
    public void ADatabaseAtAnEarlierStep_OpensAtTheHead_WithEveryOldRoundNotRecorded_ThenRecordsARoundWithAuthors(int stoppedAfter)
    {
        MakeAt(Schema.Steps[..stoppedAfter], OneOldRound);
        Shape().Should().Be(((long)stoppedAfter, false, false), "the fixture is an earlier release's database, not today's");

        OpensAndEndsWithEveryStep($"the ordinary path: a database at step {stoppedAfter} migrates forward");

        Query("SELECT authors_state || '|' || authors_session || '|' || authors_id_source || '|' || authors_reader || '|' || authors_attribution FROM rounds")
            .Should().Equal(["|||0|0"], "a round written before the step has no authors: empty state, empty strings, versions 0 — not recorded");
        Query("SELECT COUNT(*) FROM round_authors").Should().Equal(["0"]);

        Record(Round("CodeReview", 2, Observed(number: 2)));

        Query("SELECT number || '=' || authors_state || '|' || (SELECT COUNT(*) FROM round_authors WHERE round_id = rounds.id) FROM rounds ORDER BY number")
            .Should().Equal(["1=|0", "2=observed|3"], "the migrated file takes the new round's authors and leaves the old round as it was");
    }

    [Fact]
    public void AMigratedDatabase_OpensAgainUnchanged()
    {
        MakeAt(Schema.Steps[..AuthorsStep], OneOldRound);
        OpensAndEndsWithEveryStep("first open migrates");
        var first = Query("SELECT sql FROM sqlite_master ORDER BY name");

        OpensAndEndsWithEveryStep("second open runs nothing");

        Query("SELECT sql FROM sqlite_master ORDER BY name").Should().Equal(first, "a file already at the head runs no step");
    }

    [Fact]
    public void TheStepAddsEveryColumnThePlanNames_AndTheLookupIndex()
    {
        OpensAndEndsWithEveryStep("a fresh file");

        foreach (var column in AuthorsColumns)
        {
            Query($"SELECT COUNT(*) FROM pragma_table_info('rounds') WHERE name = '{column}'").Should().Equal(["1"], column);
        }

        Query("SELECT COUNT(*) FROM pragma_table_info('round_authors')").Should().Equal(["17"], "round_id, ordinal and the fifteen columns of one row");
        Query("SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'rounds_by_authors_session'").Should().Equal(["1"], "the peer lookup runs on EVERY round");
    }

    [Fact]
    public void TheConsultantPreviewDatabase_StillReachesTheHead_StepTwentyFourIncluded()
    {
        MakeAt([.. Schema.Steps[..QuestionsStep], Schema.WhyAConsultationFailed]);
        Shape().Version.Should().Be(17, "the consultant preview's own stamp");

        OpensAndEndsWithEveryStep("a database the consultant preview wrote is repaired, then migrated to step 24");
    }

    [Fact]
    public void TheSecurityPreviewDatabase_StillReachesTheHead_StepTwentyFourIncluded()
    {
        MakeAt([.. Schema.Steps[..QuestionsStep], Schema.SecurityEvidence]);
        Shape().Version.Should().Be(17, "the security preview's own stamp");

        OpensAndEndsWithEveryStep("a database the security preview wrote is repaired, then migrated to step 24");
    }

    [Fact]
    public void AReadOnlyReader_OnAStepTwentyThreeFile_LeavesItAtTwentyThree()
    {
        MakeAt(Schema.Steps[..AuthorsStep], OneOldRound);

        var counts = RoundCounts.Read(_dir, new DateTime(2026, 10, 1, 1, 0, 0, DateTimeKind.Utc));

        counts.Windows.Should().NotBeEmpty("the counter reads a file without the columns — it never asks for them");
        Shape().Should().Be(((long)AuthorsStep, false, false), "a reader opened read-only runs no step and leaves the file byte for byte");
    }

    [Fact]
    public void AnOlderServersReSave_LeavesTheProjectedAuthorsInPlace()
    {
        Record(Round("CodeReview", 1, Observed()));
        var before = Everything();
        before.Should().Contain(row => row.StartsWith("round_authors|", StringComparison.Ordinal), "the fixture holds projected authors before the older server touches it");

        // Exactly what a server released BEFORE step 24 runs on the file, which is data-driven and therefore the OLD code path: its own 23
        // steps (it runs none: the file is past them), both preview forks, and a re-record whose upsert names none of the authors columns.
        using (var old = new SqliteConnection($"Data Source={Database};Pooling=False"))
        {
            old.Open();
            ConsultantPreviewFork.Repair(old);
            SecurityPreviewFork.Repair(old);
            SqliteMigrator.Migrate(old, Schema.Steps[..AuthorsStep]);
        }

        Record(Round("CodeReview", 1, authors: null));

        Everything().Should().Equal(before, "version 24, both tables' shapes and the projected authors are byte-equal after an older server's re-save");
        Query("PRAGMA user_version").Should().Equal([Schema.Steps.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
    }

    /// <summary>The shape and the rows an older server could change: <c>user_version</c>, both tables' columns, every authors column and every row.</summary>
    private List<string> Everything() =>
    [
        .. Query("PRAGMA user_version").Select(version => "user_version|" + version),
        .. Rows("SELECT 'rounds_info', name, type, \"notnull\", dflt_value FROM pragma_table_info('rounds') ORDER BY cid"),
        .. Rows("SELECT 'round_authors_info', name, type, \"notnull\", dflt_value FROM pragma_table_info('round_authors') ORDER BY cid"),
        .. Rows("SELECT 'rounds_authors', " + string.Join(", ", AuthorsColumns) + " FROM rounds ORDER BY id"),
        .. Rows("SELECT 'round_authors', * FROM round_authors ORDER BY round_id, ordinal"),
    ];

    private List<string> Query(string sql) => Rows(sql);

    private List<string> Rows(string sql)
    {
        using var db = new SqliteConnection($"Data Source={Database};Pooling=False;Mode=ReadOnly");
        db.Open();
        using var read = db.CreateCommand();
        read.CommandText = sql;
        using var rows = read.ExecuteReader();
        var values = new List<string>();
        while (rows.Read())
        {
            values.Add(string.Join('|', Enumerable.Range(0, rows.FieldCount).Select(at => rows.IsDBNull(at) ? "NULL" : rows.GetValue(at).ToString())));
        }

        return values;
    }
}
