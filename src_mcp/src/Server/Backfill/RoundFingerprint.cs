using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace CoaiMcp.Server;

/// <summary>
/// A hash of EVERYTHING the database holds about one round — every column of its <c>rounds</c> row and every <c>round_authors</c> and
/// <c>round_changes</c> row, whole — taken when a round is SELECTED and again inside the write's transaction (E7.S3). Four unchanged columns do not
/// prove unchanged rows: a live server that rewrote only the round's rows would pass a comparison of columns. Any difference is a write since
/// the selection, and the round is deferred, never overwritten.
/// </summary>
/// <remarks>
/// <para><b>Whole rows, not a list</b> (code gate findings 0, 6 and 18): a hand-kept column list would miss a column it does not name — a re-recorded
/// <c>head_sha</c> under a size measured from the old commits, or a column a later schema step adds — and the guard would pass a write it should
/// refuse. <c>SELECT *</c> covers every present and future column by construction; the price is hashing text the backfill never reads, which
/// leaves the process as a digest and nothing else.</para>
/// <para>The command is a FACTORY so the one reader serves the mode's read-only connection (selection) and <see cref="Store.RoundsDb.Command"/>
/// (the transaction) alike — one query, never two that could drift.</para>
/// </remarks>
internal static class RoundFingerprint
{
    private const string Columns = "SELECT * FROM rounds WHERE id = $id";

    private const string AuthorRows = "SELECT * FROM round_authors WHERE round_id = $id ORDER BY ordinal";

    private const string ChangeRows = "SELECT * FROM round_changes WHERE round_id = $id ORDER BY scope, coverage, kind";

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
