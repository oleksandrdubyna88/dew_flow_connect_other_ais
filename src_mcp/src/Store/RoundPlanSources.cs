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
/// <para><b>Not recorded is never empty.</b> An empty column, a value that does not parse and an <c>origin</c> that is neither
/// <c>commit</c> nor <c>text</c> all read as ABSENT — a malformed row never fails the page. The <see cref="JsonException"/> catch
/// wraps each row's deserialise ONLY, never the query: a wider catch would swallow <i>no such column</i> and hide the very failure
/// the <c>HasColumn</c> guard exists to prevent.</para>
/// </remarks>
public static class RoundPlanSources
{
    /// <summary>The page's rounds, each carrying what it reviewed when it has a source — the page's own ids, in the page's own order.</summary>
    public static List<LoggedRound> Attach(SqliteConnection db, List<LoggedRound> rounds, IReadOnlyList<long> ids)
    {
        var sources = Read(db, ids);

        return [.. rounds.Select((round, at) => sources.TryGetValue(ids[at], out var source) ? round with { PlanSource = source } : round)];
    }

    /// <summary>The ids the page asked about, with the source each recorded plan round holds.</summary>
    public static IReadOnlyDictionary<long, PlanSource> Read(SqliteConnection db, IReadOnlyCollection<long> roundIds)
    {
        var sources = new Dictionary<long, PlanSource>();
        if (roundIds.Count == 0 || !RoundsQuery.HasColumn(db, "rounds", "plan_source"))
        {
            return sources;
        }

        using var read = db.CreateCommand();
        read.CommandText = """
            SELECT id, plan_source
            FROM rounds
            WHERE plan_source <> '' AND id IN (SELECT value FROM json_each($ids))
            """;
        PageIds.Bind(read, roundIds);
        using var rows = read.ExecuteReader();
        while (rows.Read())
        {
            foreach (var source in Parsed(Columns.Text(rows, "plan_source")))
            {
                sources[Columns.Id(rows, "id")] = source;
            }
        }

        return sources;
    }

    /// <summary>One source, or none: the row's JSON when it parses and names an origin this build knows.</summary>
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
        source is { Origin: PlanOrigin.Commit or PlanOrigin.Text } ? [source] : [];
}
