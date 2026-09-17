using Microsoft.Data.Sqlite;

namespace CoaiMcp.Store;

/// <summary>
/// WHICH pairs a send would offer — the predicate, in one place.
/// </summary>
/// <remarks>
/// <para>It was written twice: once in <see cref="RoundsDb.Sendable"/>, which SELECTS them, and once
/// in <c>BugsQuery</c>, which COUNTS them for the button. Two reviewers said the same thing about it
/// and they were right: a rule added to one and not the other makes the button show a number the run
/// does not use — it offers to send N and sends a different N, or refuses with "nothing to send"
/// after promising four. The same argument <see cref="CollectRuns"/> exists for, and the same
/// answer.</para>
/// <para>A `keep = 1` pair with no `sent_utc` and no `send_refusal`: kept by a person, never
/// acknowledged, and not carrying a refusal our own normaliser earned. `--requeue-refused` is what
/// clears the third condition once the normaliser is repaired.</para>
/// </remarks>
internal static class SendablePairs
{
    /// <summary>The three conditions, without the `WHERE`, so a caller can join more.</summary>
    internal const string Conditions = "p.keep = 1 AND p.sent_utc = '' AND p.send_refusal = ''";

    /// <summary>The same three, for a query with no alias.</summary>
    internal const string ConditionsUnaliased = "keep = 1 AND sent_utc = '' AND send_refusal = ''";

    /// <summary>How many pairs a send would offer right now.</summary>
    internal static int Count(SqliteConnection db)
    {
        using var read = db.CreateCommand();
        read.CommandText = $"SELECT COUNT(*) FROM collect_pairs WHERE {ConditionsUnaliased}";

        return Convert.ToInt32(read.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }
}
