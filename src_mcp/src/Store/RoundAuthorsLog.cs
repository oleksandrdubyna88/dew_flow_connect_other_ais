using CoaiMcp.Core.Authors;
using Microsoft.Data.Sqlite;

namespace CoaiMcp.Store;

/// <summary>
/// Reading a page of rounds' authors states for <c>--log</c> (E5.S3 of <c>research/PLAN_a_round_records_who_wrote_the_code_and_how_much_E5.md</c>):
/// one query for the whole page, never one per round.
/// </summary>
/// <remarks>
/// <para>Its own file beside <see cref="RoundPlanSources"/>, whose shape it follows, on the read-only connection
/// <see cref="RoundsQuery.Read"/> already opened. A database written before schema step 24 has no <c>authors_state</c> column: the schema is
/// ASKED (<see cref="RoundsQuery.HasColumn"/>, never a version guess) and the answer is an empty map with no query — those rounds are logged
/// exactly as before, with no state.</para>
/// <para><b>Not recorded is never a word.</b> An empty column, and a stored value the shared rule refuses
/// (<see cref="AuthorsSnapshot.IsWellFormed"/>), read as ABSENT — a malformed row never fails the page. That check wraps each ROW only,
/// never the query: there is no JSON to fail here, and a catch around the query would swallow <i>no such column</i> and hide the very
/// failure the <c>HasColumn</c> guard exists to prevent.</para>
/// </remarks>
public static class RoundAuthorsLog
{
    /// <summary>
    /// The rounds, each carrying the state of its authors when one was recorded — correlated by the round's OWN id
    /// (<see cref="PageIds.Of"/>), never by its position, so a reordered or filtered list still attaches each state to its round.
    /// </summary>
    public static List<LoggedRound> Attach(SqliteConnection db, List<LoggedRound> rounds)
    {
        var states = Read(db, [.. rounds.Select(PageIds.Of)]);

        return [.. rounds.Select(round => states.TryGetValue(PageIds.Of(round), out var state) ? round with { AuthorsState = state } : round)];
    }

    /// <summary>The ids the page asked about, with the well-formed state each recorded round holds.</summary>
    public static IReadOnlyDictionary<long, string> Read(SqliteConnection db, IReadOnlyCollection<long> roundIds)
    {
        if (roundIds.Count == 0 || !RoundsQuery.HasColumn(db, "rounds", "authors_state"))
        {
            return new Dictionary<long, string>();
        }

        return Stored(db, roundIds).Where(row => AuthorsSnapshot.IsWellFormed(row.State)).ToDictionary(row => row.Id, row => row.State);
    }

    /// <summary>The page's non-empty columns as (id, state) — the query only; judging a state is each row's own affair.</summary>
    private static List<(long Id, string State)> Stored(SqliteConnection db, IReadOnlyCollection<long> ids)
    {
        using var read = db.CreateCommand();
        read.CommandText = """
            SELECT id, authors_state
            FROM rounds
            WHERE authors_state <> '' AND id IN (SELECT value FROM json_each($ids))
            """;
        PageIds.Bind(read, ids);
        using var rows = read.ExecuteReader();
        var stored = new List<(long Id, string State)>();
        while (rows.Read())
        {
            stored.Add((Columns.Id(rows, "id"), Columns.Text(rows, "authors_state")));
        }

        return stored;
    }
}
