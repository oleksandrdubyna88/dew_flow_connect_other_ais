using System.Collections.Immutable;
using System.Globalization;
using CoaiMcp.Core.Authors;
using CoaiMcp.Store;
using Microsoft.Data.Sqlite;

namespace CoaiMcp.Server;

/// <summary>
/// What a round's authors look like once projected — the <c>authors_*</c> columns and the <c>round_authors</c> rows — as one canonical list of
/// lines, built from a SNAPSHOT (<see cref="Of"/>) and from the DATABASE (<see cref="Read"/>) by the same formatter, so the two compare equal
/// exactly when <see cref="RoundAuthorsTable.Record"/> would write nothing new (E7.S1 rule 5, S3's equal-result no-op).
/// </summary>
/// <remarks>
/// <para><b>What is compared and what is not.</b> The state, the caller's session, the two versions, the two write counts, the seven ambiguous
/// totals and every row, in ordinal order. NOT <c>authors_id_source</c>: a heal writes the FILE only and its snapshot says <c>healed</c> while the
/// column keeps the live source, so the two legitimately differ and that difference is not a disagreement. Nothing the database never held
/// (<c>Elsewhere</c>, <c>ClaimedByPeer</c>, the windows, the peer gates) is in it either — it cannot be compared with something absent.</para>
/// <para>The agent column is <see cref="RoundAuthorsTable.AgentColumn"/>'s — <c>main</c> is the EMPTY string there — never a second spelling.</para>
/// </remarks>
internal static class AuthorsProjection
{
    /// <summary>The lines the writer would store for <paramref name="authors"/>.</summary>
    public static ImmutableArray<string> Of(AuthorsSnapshot authors) =>
    [
        Columns(
            authors.Render(), authors.Session, authors.Reader, authors.Attribution, authors.SubmoduleWrites, authors.UncoveredWrites,
            authors.Ambiguous.Agents, authors.Ambiguous.Launches, authors.Ambiguous.Turns, authors.Ambiguous.Tokens.Input,
            authors.Ambiguous.Tokens.Output, authors.Ambiguous.Tokens.CacheCreation, authors.Ambiguous.Tokens.CacheRead),
        .. authors.Rows.Select(RowLine),
    ];

    /// <summary>The lines the database holds for round <paramref name="roundId"/>; the columns line only when no row exists.</summary>
    public static ImmutableArray<string> Read(SqliteConnection db, long roundId) => [ColumnsLine(db, roundId), .. RowLines(db, roundId)];

    /// <summary>Whether storing <paramref name="authors"/> would change nothing the database already holds for the round.</summary>
    public static bool Agree(AuthorsSnapshot authors, SqliteConnection db, long roundId) => Of(authors).SequenceEqual(Read(db, roundId), StringComparer.Ordinal);

    private static string RowLine(AuthorRow row) => Columns(
        row.Who, RoundAuthorsTable.AgentColumn(row.Agent), row.Requested, row.Model, row.Effort, row.AgentType, row.Association, row.Window,
        row.Launches, row.Turns, row.Tokens.Input, row.Tokens.Output, row.Tokens.CacheCreation, row.Tokens.CacheRead, row.WriteCalls);

    private static string Columns(params object[] values) =>
        string.Join('|', values.Select(value => Convert.ToString(value, CultureInfo.InvariantCulture)));

    private static string ColumnsLine(SqliteConnection db, long roundId)
    {
        using var read = db.CreateCommand();
        read.CommandText = """
            SELECT authors_state, authors_session, authors_reader, authors_attribution, submodule_writes, uncovered_writes,
                   ambiguous_agents, ambiguous_launches, ambiguous_turns, ambiguous_input_tokens, ambiguous_output_tokens,
                   ambiguous_cache_creation_tokens, ambiguous_cache_read_tokens
              FROM rounds WHERE id = $id
            """;
        read.Parameters.AddWithValue("$id", roundId);
        using var rows = read.ExecuteReader();

        return rows.Read() ? Columns(Values(rows, 13)) : string.Empty;
    }

    private static IEnumerable<string> RowLines(SqliteConnection db, long roundId)
    {
        using var read = db.CreateCommand();
        read.CommandText = """
            SELECT who, agent, requested, model, effort, agent_type, association, window,
                   launches, turns, input_tokens, output_tokens, cache_creation_tokens, cache_read_tokens, write_calls
              FROM round_authors WHERE round_id = $id ORDER BY ordinal
            """;
        read.Parameters.AddWithValue("$id", roundId);
        using var rows = read.ExecuteReader();
        var lines = new List<string>();
        while (rows.Read())
        {
            lines.Add(Columns(Values(rows, 15)));
        }

        return lines;
    }

    /// <summary>The first <paramref name="count"/> columns, each as its own type: a count stays an integer, a state stays text.</summary>
    private static object[] Values(SqliteDataReader rows, int count) =>
        [.. Enumerable.Range(0, count).Select(at => rows.IsDBNull(at) ? string.Empty : rows.GetValue(at))];
}
