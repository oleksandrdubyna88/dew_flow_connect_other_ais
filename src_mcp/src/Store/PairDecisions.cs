using CoaiMcp.Core.Collecting;
using Microsoft.Data.Sqlite;

namespace CoaiMcp.Store;

/// <summary>
/// What `--pairs-decide` writes: a person's keep and their comment, together, in one transaction.
/// </summary>
/// <remarks>
/// <para><b>A unit of its own, beside <see cref="UploadRuns"/>, rather than more of
/// <see cref="RoundsDb"/></b>, which was 1 116 lines against the 800 the conventions allow before
/// this story touched it. The connection is <see cref="RoundsDb"/>'s and so is the one call into
/// this; what lives here is the one question the comment added to a decision.</para>
/// <para><b>Keep and comment travel as one write</b>, so a comment never changes a decision and a
/// decision never erases a comment: the panel sends both for every pair it writes, and this writes
/// both or neither.</para>
/// <para><b>A SENT pair's comment cannot change here either.</b> The page makes the box read-only
/// once the server has acknowledged the pair; without this, `--pairs-decide` would be the way round
/// it — the local row would diverge from what crossed, and the page would render the divergence as
/// sent. So a decision whose comment differs from a sent pair's is REFUSED, the whole batch with it,
/// and nothing is written. An unchanged one is accepted, which is what lets a person still change a
/// sent pair's keep. (Plan round of 4.2.)</para>
/// <para><b>The check is INSIDE the transaction</b>, so the row it reads is the row it writes: read
/// first and written later, a send acknowledged in between would slip a change past it.</para>
/// </remarks>
internal static class PairDecisions
{
    /// <summary>Writes the batch, or refuses it whole and says which pair and why.</summary>
    /// <returns>How many rows were decided, and a refusal that is empty when there was none.</returns>
    internal static (int Decided, string Refusal) Record(
        SqliteConnection db, IReadOnlyList<CommentedDecision> decisions)
    {
        using var transaction = db.BeginTransaction();
        var refusal = FirstChangeToASentComment(db, decisions);
        if (refusal.Length > 0)
        {
            transaction.Rollback();

            return (0, refusal);
        }

        var decided = 0;
        foreach (var decision in decisions)
        {
            decided += Write(db, decision);
        }

        transaction.Commit();

        return (decided, string.Empty);
    }

    /// <summary>The refusal for the first decision that would rewrite a sent pair's words.</summary>
    /// <remarks>
    /// It names the finding and never the text: a refusal is written to stderr and into a toast, and
    /// the comment is logged nowhere.
    /// </remarks>
    private static string FirstChangeToASentComment(
        SqliteConnection db, IReadOnlyList<CommentedDecision> decisions)
        => decisions
            .Where(decision => ChangesASentComment(db, decision))
            .Select(decision => $"pair {decision.FindingId} was already sent, so its comment cannot change: "
                                + "the new text would never cross. Nothing was written.")
            .FirstOrDefault(string.Empty);

    private static bool ChangesASentComment(SqliteConnection db, CommentedDecision decision)
    {
        using var read = db.CreateCommand();
        read.CommandText = """
            SELECT 1 FROM collect_pairs
             WHERE finding_id = $id AND sent_utc != '' AND comment != $comment
            """;
        read.Parameters.AddWithValue("$id", decision.FindingId);
        read.Parameters.AddWithValue("$comment", decision.Comment);

        return read.ExecuteScalar() is not null;
    }

    private static int Write(SqliteConnection db, CommentedDecision decision)
    {
        using var write = db.CreateCommand();
        write.CommandText =
            "UPDATE collect_pairs SET keep = $keep, comment = $comment WHERE finding_id = $id";
        write.Parameters.AddWithValue("$keep", decision.Keep);
        write.Parameters.AddWithValue("$comment", decision.Comment);
        write.Parameters.AddWithValue("$id", decision.FindingId);

        return write.ExecuteNonQuery();
    }
}
