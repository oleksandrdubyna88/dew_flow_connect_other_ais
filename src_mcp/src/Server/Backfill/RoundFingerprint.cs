using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace CoaiMcp.Server;

/// <summary>
/// A hash of everything the backfill writes about one round — the authors and size columns and every <c>round_authors</c> and
/// <c>round_changes</c> row — taken when a round is SELECTED and again inside the write's transaction (E7.S3). Four unchanged columns do not
/// prove unchanged rows: a live server that rewrote only the round's rows would pass a comparison of columns. Any difference is a write since
/// the selection, and the round is deferred, never overwritten.
/// </summary>
/// <remarks>
/// <para>The command is a FACTORY so the one reader serves the mode's read-only connection (selection) and <see cref="Store.RoundsDb.Command"/>
/// (the transaction) alike — one query, never two that could drift.</para>
/// </remarks>
internal static class RoundFingerprint
{
    private const string Columns = """
        SELECT authors_state, authors_session, authors_id_source, authors_reader, authors_attribution, submodule_writes, uncovered_writes,
               ambiguous_agents, ambiguous_launches, ambiguous_turns, ambiguous_input_tokens, ambiguous_output_tokens,
               ambiguous_cache_creation_tokens, ambiguous_cache_read_tokens, size_state, delta_state, delta_from, size_origin, size_gitlinks
          FROM rounds WHERE id = $id
        """;

    private const string AuthorRows = """
        SELECT ordinal, who, agent, requested, model, effort, agent_type, association, window, launches, turns,
               input_tokens, output_tokens, cache_creation_tokens, cache_read_tokens, write_calls
          FROM round_authors WHERE round_id = $id ORDER BY ordinal
        """;

    private const string ChangeRows = """
        SELECT scope, coverage, kind, files, added, removed
          FROM round_changes WHERE round_id = $id ORDER BY scope, coverage, kind
        """;

    /// <summary>The hash, as lower-case hex, of the round's columns and rows as <paramref name="command"/>'s connection sees them now.</summary>
    public static string Read(Func<SqliteCommand> command, long roundId)
    {
        var text = new StringBuilder();
        foreach (var query in (string[])[Columns, AuthorRows, ChangeRows])
        {
            Append(text, command, query, roundId);
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private static void Append(StringBuilder text, Func<SqliteCommand> command, string query, long roundId)
    {
        using var read = command();
        read.CommandText = query;
        read.Parameters.AddWithValue("$id", roundId);
        using var rows = read.ExecuteReader();
        text.Append('#').Append(query.Length).Append('\n');
        while (rows.Read())
        {
            text.AppendJoin('|', Enumerable.Range(0, rows.FieldCount).Select(at => Cell(rows, at))).Append('\n');
        }
    }

    private static string Cell(SqliteDataReader rows, int at) =>
        rows.IsDBNull(at) ? "NULL" : Convert.ToString(rows.GetValue(at), CultureInfo.InvariantCulture) ?? string.Empty;
}
