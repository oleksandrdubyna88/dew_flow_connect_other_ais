using System.Text.Json;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using Microsoft.Data.Sqlite;

namespace CoaiMcp.Store;

/// <summary>
/// Reading a page of rounds' plan sources for <c>--log</c> (E2.S2 of
/// <c>todo/PLAN_the_plan_gate_reads_the_committed_plan_E2.md</c>, contract C2): one query for the whole page, never one per round.
/// </summary>
/// <remarks>
/// <para>Its own file beside <see cref="RoundChangesLog"/>, whose shape it follows, on the read-only connection
/// <see cref="RoundsQuery.Read"/> already opened. A database written before schema step 23 has no <c>plan_source</c> column: the
/// schema is ASKED (<see cref="RoundsQuery.HasColumn"/>, never a version guess) and the answer is an empty map with no query —
/// those rounds are logged exactly as before, with no source.</para>
/// <para><b>Not recorded is never empty.</b> An empty column, a value that does not parse and a source that is not well-formed
/// (<see cref="PlanSource.IsWellFormed"/>: an unknown origin, a negative size, no text id, a commit source without a relative path
/// or a hex blob, a pasted one with a path or blob) all read as ABSENT — a malformed row never fails the page. The <see cref="JsonException"/> catch
/// wraps each row's deserialise ONLY, never the query: a wider catch would swallow <i>no such column</i> and hide the very failure
/// the <c>HasColumn</c> guard exists to prevent.</para>
/// </remarks>
public static class RoundPlanSources
{
    /// <summary>
    /// The rounds, each carrying what it reviewed when it has a source — correlated by the round's OWN id
    /// (<see cref="PageIds.Of"/>), never by its position, so a reordered or filtered list still attaches each source to its round.
    /// </summary>
    public static List<LoggedRound> Attach(SqliteConnection db, List<LoggedRound> rounds)
    {
        var sources = Read(db, [.. rounds.Select(PageIds.Of)]);

        return [.. rounds.Select(round => sources.TryGetValue(PageIds.Of(round), out var source) ? round with { PlanSource = source } : round)];
    }

    /// <summary>The ids the page asked about, with the source each recorded plan round holds.</summary>
    public static IReadOnlyDictionary<long, PlanSource> Read(SqliteConnection db, IReadOnlyCollection<long> roundIds)
    {
        if (roundIds.Count == 0 || !RoundsQuery.HasColumn(db, "rounds", "plan_source"))
        {
            return new Dictionary<long, PlanSource>();
        }

        return Stored(db, roundIds)
            .SelectMany(row => Parsed(row.Json).Select(source => (row.Id, Source: source)))
            .ToDictionary(row => row.Id, row => row.Source);
    }

    /// <summary>The page's non-empty columns as (id, JSON) — the query only; parsing is each row's own affair.</summary>
    private static List<(long Id, string Json)> Stored(SqliteConnection db, IReadOnlyCollection<long> ids)
    {
        using var read = db.CreateCommand();
        read.CommandText = """
            SELECT id, plan_source
            FROM rounds
            WHERE plan_source <> '' AND id IN (SELECT value FROM json_each($ids))
            """;
        PageIds.Bind(read, ids);
        using var rows = read.ExecuteReader();
        var stored = new List<(long Id, string Json)>();
        while (rows.Read())
        {
            stored.Add((Columns.Id(rows, "id"), Columns.Text(rows, "plan_source")));
        }

        return stored;
    }

    /// <summary>One source, or none: the row's JSON when it parses and is a well-formed source.</summary>
    private static IEnumerable<PlanSource> Parsed(string json)
    {
        try
        {
            return Known(JsonSerializer.Deserialize(json, ServerJsonContext.Default.PlanSource));
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IEnumerable<PlanSource> Known(PlanSource? source) =>
        source is { } read && read.IsWellFormed() ? [read] : [];
}
