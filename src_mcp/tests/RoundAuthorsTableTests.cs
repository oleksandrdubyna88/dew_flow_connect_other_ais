using System.Text.Json;
using CoaiMcp.Core.Authors;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;
using static CoaiMcp.Tests.AuthorsSnapshotFixtures;

namespace CoaiMcp.Tests;

/// <summary>
/// The writer of schema step 24 (E5.S2): what <c>RoundAuthorsTable.Record</c> puts in <c>round_authors</c> and in the authors columns of
/// <c>rounds</c>, projected FROM the round's snapshot and never from a <c>RoundContext</c> — so a database deleted and rebuilt from the
/// session files comes back with the same rows — and written inside the caller's transaction without opening one of its own.
/// </summary>
public sealed class RoundAuthorsTableTests : IDisposable
{
    private readonly TempDir _dir = TempDir.For("testsToDelete-coai-authors-table-");
    private readonly Serilog.ILogger _log = Serilog.Core.Logger.None;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _dir.Dispose();
    }

    private string Database => _dir.At(RoundsDb.FileName);

    private const string AuthorsColumns =
        "authors_state, authors_session, authors_id_source, authors_reader, authors_attribution, submodule_writes, uncovered_writes, "
        + "ambiguous_agents, ambiguous_launches, ambiguous_turns, ambiguous_input_tokens, ambiguous_output_tokens, "
        + "ambiguous_cache_creation_tokens, ambiguous_cache_read_tokens";

    private void Record(params RoundRecord[] rounds)
    {
        using var db = RoundsDb.Open(_dir, _log)!;
        foreach (var round in rounds)
        {
            db.RecordRound(State(), round, []);
        }
    }

    [Fact]
    public void AnObservedSnapshot_WritesItsColumnsAndItsRowsInOrder_AndMainsAgentIsEmpty()
    {
        Record(Round("CodeReview", 1, Observed()));

        Rows($"SELECT {AuthorsColumns} FROM rounds").Should().Equal(
            ["observed|" + Caller + "|registry|1|1|1|2|1|2|3|10|20|30|40"],
            "the state as the card renders it, the caller's session, where its id came from, both versions, the two write counts and the ambiguous totals term by term");
        Rows("SELECT ordinal, who, agent, requested, model, effort, agent_type, association, window, launches, turns, input_tokens, output_tokens, cache_creation_tokens, cache_read_tokens, write_calls FROM round_authors ORDER BY ordinal")
            .Should().Equal(
                [
                    "0|main||opus|claude-opus-5-5|high||same-session|round|1|2|5|6|7|8|3",
                    "1|subagent|a1|opus|claude-sonnet-5-5|high|general-purpose|wrote here|round|1|2|5|6|7|8|3",
                    "2|subagent|a2|opus|claude-sonnet-5-5|high|general-purpose|child of|before-open|1|2|5|6|7|8|3",
                ],
                "one row per row of the snapshot, in its order; main's agent is the EMPTY string — the one mapping, in one place");
    }

    [Fact]
    public void TheSameRoundRecordedTwice_HasTheSameRowsAndColumns()
    {
        Record(Round("CodeReview", 1, Observed()));
        var first = Everything();

        Record(Round("CodeReview", 1, Observed()));

        Everything().Should().Equal(first, "a re-record replaces the round's rows; it never doubles them");
        Rows("SELECT COUNT(*) FROM round_authors").Should().Equal(["3"]);
    }

    [Fact]
    public void AReRecordWithFewerRows_LeavesNoneBehind()
    {
        Record(Round("CodeReview", 1, Observed()));
        var fewer = Observed() with { Rows = [Observed().Rows[0]] };

        Record(Round("CodeReview", 1, fewer));

        Rows("SELECT ordinal || '=' || who FROM round_authors ORDER BY ordinal").Should().Equal(["0=main"], "delete then insert: the two rows the new snapshot lacks are gone");
    }

    [Fact]
    public void AReRecordWithoutAnySnapshot_LeavesTheEarlierColumnsAndRowsUntouched()
    {
        Record(Round("CodeReview", 1, Observed()));
        var before = Everything();

        Record(Round("CodeReview", 1, authors: null));

        Everything().Should().Equal(before, "a backfilled snapshot survives a live re-record that measured nothing — exactly as a null size writes nothing");
    }

    [Fact]
    public void AnUnknownTotal_IsMinusOneNeverZero()
    {
        var zeroAgents = Observed() with { Ambiguous = ActivityTotals.Zero };
        Record(Round("CodeReview", 1, Observed(number: 1) with { Ambiguous = ActivityTotals.Unknown }), Round("CodeReview", 2, zeroAgents with { Number = 2 }), Round("CodeReview", 3, Unobserved(number: 3)));

        Rows("SELECT number || '=' || ambiguous_agents || ',' || ambiguous_launches || ',' || ambiguous_turns || ',' || ambiguous_input_tokens || ',' || ambiguous_output_tokens || ',' || ambiguous_cache_creation_tokens || ',' || ambiguous_cache_read_tokens FROM rounds ORDER BY number")
            .Should().Equal(
                ["1=-1,-1,-1,-1,-1,-1,-1", "2=0,0,0,0,0,0,0", "3=-1,-1,-1,-1,-1,-1,-1"],
                "an unknown total is -1 in every term, a measured zero is 0, and the two never coincide");
        Rows("SELECT number || '=' || submodule_writes || ',' || uncovered_writes FROM rounds ORDER BY number")
            .Should().Equal(["1=1,2", "2=1,2", "3=-1,-1"], "an unobserved snapshot built without E4 has -1 for the two write counts too: unknown is never 'covered'");
    }

    [Fact]
    public void AnUnobservedSnapshotE4Built_AlsoStoresMinusOneForTheWriteCounts()
    {
        var fromE4 = Observed() with
        {
            State = ObservationKinds.Unobserved,
            Reasons = [ObservationReasons.TranscriptEmpty],
            Ambiguous = ActivityTotals.Unknown,
            UnobservedLaunches = -1,
            SubmoduleWrites = -1,
            UncoveredWrites = -1,
        };

        Record(Round("CodeReview", 1, fromE4));

        Rows("SELECT authors_state || '|' || submodule_writes || ',' || uncovered_writes || '|' || ambiguous_agents FROM rounds").Should().Equal(["unobserved: transcript empty|-1,-1|-1"]);
    }

    [Fact]
    public void TheTwoVersionsAreStored()
    {
        Record(Round("CodeReview", 1, Observed() with { Reader = 7, Attribution = 9 }));

        Rows("SELECT authors_reader || ',' || authors_attribution FROM rounds").Should().Equal(["7,9"]);
    }

    [Fact]
    public void TheIdSourceIsStoredAsGiven_HealedIncluded()
    {
        Record(Round("CodeReview", 1, Observed() with { IdSource = "healed" }));

        Rows("SELECT authors_id_source FROM rounds").Should().Equal(["healed"]);
    }

    [Fact]
    public void AgentKeyOf_IsTheInverseOfWhatTheWriterWrote()
    {
        var keys = new[] { AgentKey.Main, new AgentKey("a1"), new AgentKey("agent-0123456789abcdef") };
        var snapshot = Observed() with { Rows = [.. keys.Select(key => Row(key.Id, "claude-opus-5-5", Association.WroteHere))] };
        Record(Round("CodeReview", 1, snapshot));

        var columns = Rows("SELECT agent FROM round_authors ORDER BY ordinal");

        columns.Should().Equal("", "a1", "agent-0123456789abcdef");
        columns.Select(RoundAuthorsTable.AgentKeyOf).Should().Equal(keys, "the ONE inverse, beside the one mapping: E6's read and E7's heal call it");
        RoundAuthorsTable.AgentKeyOf(string.Empty).Should().Be(AgentKey.Main);
    }

    [Fact]
    public void TheRowsAreProjectedFromTheRound_SoARebuiltDatabaseComesBackIdentical()
    {
        var data = _dir.At("data");
        var store = new SessionStore(data);
        var session = new PersistedSession(State(repo: _dir.Path), [Round("CodeReview", 1, Observed())]) { OpenedUtc = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc) };
        store.Save(session);
        using (var db = RoundsDb.Open(data, _log)!)
        {
            db.RecordRound(session.State, session.Rounds[0], []);
        }

        var first = Everything(data);
        SqliteConnection.ClearAllPools();
        foreach (var file in Directory.GetFiles(data, RoundsDb.FileName + "*"))
        {
            File.Delete(file);
        }

        var loaded = store.Load(_dir.Path, "feat/x")!;
        using (var db = RoundsDb.Open(data, _log)!)
        {
            db.RecordRound(loaded.State, loaded.Rounds[0], []);
        }

        first.Should().Contain(row => row.StartsWith("round_authors|", StringComparison.Ordinal));
        Everything(data).Should().Equal(first, "the table reads round.Authors from the session file, never a RoundContext — a deleted database is rebuilt from the files");
    }

    [Fact]
    public void TheWriterJoinsTheCallersTransaction_ARollbackLeavesNothing_ACommitKeepsTheRows()
    {
        Record(Round("CodeReview", 1, authors: null));
        using var db = RoundsDb.Open(_dir, _log)!;
        var roundId = RoundId();

        Exec(db, "BEGIN");
        RoundAuthorsTable.Record(db, roundId, Observed());
        Exec(db, "ROLLBACK");
        Rows("SELECT COUNT(*) FROM round_authors").Should().Equal(["0"], "a rollback leaves no round_authors row");
        Rows($"SELECT {AuthorsColumns} FROM rounds").Should().Equal(["|||0|0|0|0|0|0|0|0|0|0|0"], "and every authors column back at its default");

        Exec(db, "BEGIN");
        RoundAuthorsTable.Record(db, roundId, Observed());
        Exec(db, "COMMIT");
        Rows("SELECT COUNT(*) FROM round_authors").Should().Equal(["3"], "the same writer commits with its caller's COMMIT");
    }

    [Fact]
    public void AFaultAfterTheWriter_LeavesNoAuthors()
    {
        // The real caller's path: RecordRound's own SqliteTransaction, with a fault planted AFTER the writer — RecordFindings runs after it.
        using (var plant = new SqliteConnection($"Data Source={Database};Pooling=False"))
        {
            using (RoundsDb.Open(_dir, _log)) { }
            plant.Open();
            using var trigger = plant.CreateCommand();
            trigger.CommandText = "CREATE TRIGGER planted BEFORE INSERT ON findings BEGIN SELECT RAISE(ABORT, 'planted'); END;";
            trigger.ExecuteNonQuery();
        }

        using (var db = RoundsDb.Open(_dir, _log)!)
        {
            var act = () => db.RecordRound(State(), Round("CodeReview", 1, Observed()), [Found("one finding, so RecordFindings runs after the writer")]);

            act.Should().Throw<SqliteException>().WithMessage("*planted*");
        }

        Rows("SELECT COUNT(*) FROM rounds").Should().Equal(["0"], "no rounds row");
        Rows("SELECT COUNT(*) FROM round_authors").Should().Equal(["0"], "no round_authors row — the writer left nothing behind a fault in its caller's transaction");
    }

    private static Finding Found(string title) =>
        new(Severity.Major, Category.Reliability, "src/Parser.cs", 40, title, title + " — why", "the fix", ["codex"]);

    private long RoundId() => long.Parse(Rows("SELECT id FROM rounds").Single(), System.Globalization.CultureInfo.InvariantCulture);

    private static void Exec(RoundsDb db, string sql)
    {
        using var command = db.Command();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private List<string> Everything(string? dataDir = null) =>
    [
        .. Rows($"SELECT 'rounds', {AuthorsColumns} FROM rounds ORDER BY id", dataDir),
        .. Rows("SELECT 'round_authors', * FROM round_authors ORDER BY round_id, ordinal", dataDir),
    ];

    private List<string> Rows(string sql, string? dataDir = null)
    {
        using var db = new SqliteConnection($"Data Source={Path.Combine(dataDir ?? _dir.Path, RoundsDb.FileName)};Pooling=False;Mode=ReadOnly");
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
