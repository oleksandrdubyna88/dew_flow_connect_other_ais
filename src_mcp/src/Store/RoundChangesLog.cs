using CoaiMcp.Core.Context;
using Microsoft.Data.Sqlite;

namespace CoaiMcp.Store;

/// <summary>
/// Reading a page of rounds' sizes for <c>--log</c> (E2.S5 of <c>todo/PLAN_a_round_records_who_wrote_the_code_and_how_much_E2.md</c>,
/// contract C3): two grouped queries for the whole page, never one per round.
/// </summary>
/// <remarks>
/// <para>Its own file beside <see cref="RoundChangesTable"/>, which writes what this reads, and on the read-only connection
/// <see cref="RoundsQuery.Read"/> already opened. A database written before schema step 22 has neither the columns nor the
/// table: the schema is ASKED (<see cref="RoundsQuery.HasColumn"/>, never a version guess) and the answer is an empty map with no
/// query — those rounds are logged exactly as before, with no size.</para>
/// <para>A round whose <c>size_state</c> is empty measured nothing and is absent from the map. The whole scope starts at
/// <c>rounds.base_ref</c> — the base the diff was resolved against, which a backfill must not recompute (plan §10 point 9) — and
/// the delta at <c>delta_from</c>. A binary's <c>added</c>/<c>removed</c> are NULL and read null: <see cref="Columns.Number"/>
/// turns NULL into 0, which would say "a file changed and nothing in it did", so these two are read with <see cref="Lines"/>.</para>
/// </remarks>
public static class RoundChangesLog
{
    /// <summary>The ids the page asked about, with the size each recorded one holds.</summary>
    public static IReadOnlyDictionary<long, RoundSize> Read(SqliteConnection db, IReadOnlyCollection<long> roundIds)
    {
        if (roundIds.Count == 0 || !RoundsQuery.HasColumn(db, "rounds", "size_state"))
        {
            return new Dictionary<long, RoundSize>();
        }

        var states = States(db, roundIds);
        var cells = Cells(db, states.Keys);

        return states.ToDictionary(state => state.Key, state => SizeOf(state.Key, state.Value, cells));
    }

    /// <summary>The page's rounds, each carrying its own size when it has one — the page's own ids, in the page's own order.</summary>
    public static List<LoggedRound> Attach(SqliteConnection db, List<LoggedRound> rounds, IReadOnlyList<long> ids)
    {
        var sizes = Read(db, ids);

        return [.. rounds.Select((round, at) => sizes.TryGetValue(ids[at], out var size) ? round with { Size = size } : round)];
    }

    private sealed record StateRow(string Whole, string Delta, string DeltaFrom, string Origin, string BaseRef);

    private static Dictionary<long, StateRow> States(SqliteConnection db, IReadOnlyCollection<long> ids)
    {
        using var read = db.CreateCommand();
        read.CommandText = """
            SELECT id, size_state, delta_state, delta_from, size_origin, base_ref
            FROM rounds
            WHERE size_state <> '' AND id IN (SELECT value FROM json_each($ids))
            """;
        BindIds(read, ids);
        using var rows = read.ExecuteReader();
        var states = new Dictionary<long, StateRow>();
        while (rows.Read())
        {
            states[Columns.Id(rows, "id")] = new StateRow(
                Columns.Text(rows, "size_state"), Columns.Text(rows, "delta_state"), Columns.Text(rows, "delta_from"),
                Columns.Text(rows, "size_origin"), Columns.Text(rows, "base_ref"));
        }

        return states;
    }

    private static Dictionary<(long Round, string Scope), List<SizeCell>> Cells(SqliteConnection db, IReadOnlyCollection<long> ids)
    {
        var byScope = new Dictionary<(long Round, string Scope), List<SizeCell>>();
        if (ids.Count == 0)
        {
            return byScope;
        }

        using var read = db.CreateCommand();
        read.CommandText = """
            SELECT round_id, scope, coverage, kind, files, added, removed
            FROM round_changes
            WHERE round_id IN (SELECT value FROM json_each($ids))
            ORDER BY round_id, rowid
            """;
        BindIds(read, ids);
        using var rows = read.ExecuteReader();
        while (rows.Read())
        {
            var key = (Columns.Id(rows, "round_id"), Columns.Text(rows, "scope"));
            if (!byScope.TryGetValue(key, out var list))
            {
                byScope[key] = list = [];
            }

            list.Add(CellOf(rows));
        }

        return byScope;
    }

    private static SizeCell CellOf(SqliteDataReader rows) => new(
        Columns.Text(rows, "coverage"), Columns.Text(rows, "kind"), Columns.Number(rows, "files"),
        Lines(rows, "added"), Lines(rows, "removed"));

    /// <summary>A line count, or null when the column is NULL — a binary has none, and null is not 0.</summary>
    private static int? Lines(SqliteDataReader rows, string column)
    {
        var at = rows.GetOrdinal(column);

        return rows.IsDBNull(at) ? null : rows.GetInt32(at);
    }

    private static RoundSize SizeOf(long id, StateRow state, Dictionary<(long Round, string Scope), List<SizeCell>> cells) => new(
        new ScopeSize(state.Whole, state.BaseRef, CellsOf(cells, id, RoundChangesTable.WholeScope)),
        new ScopeSize(state.Delta, state.DeltaFrom, CellsOf(cells, id, RoundChangesTable.DeltaScope)),
        state.Origin);

    private static IReadOnlyList<SizeCell> CellsOf(Dictionary<(long Round, string Scope), List<SizeCell>> cells, long id, string scope) =>
        cells.TryGetValue((id, scope), out var list) ? list : [];

    /// <summary>
    /// The page's ids as ONE bound parameter, a JSON array of integers that the constant SQL unpacks with
    /// <c>json_each</c> — no SQL text is ever built from data, and no page can reach SQLite's variable limit.
    /// </summary>
    private static void BindIds(SqliteCommand command, IReadOnlyCollection<long> ids) =>
        command.Parameters.AddWithValue("$ids", "[" + string.Join(',', ids) + "]");
}
