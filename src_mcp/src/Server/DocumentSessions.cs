using CoaiMcp.Core.Rounds;

namespace CoaiMcp.Server;

/// <summary>
/// Which review of a document a call is about — the current one, or a fresh one.
/// </summary>
/// <remarks>
/// <para>A document's identity never changes, so without this a finished review would make that
/// document permanently unreviewable: the same policy text a year later meets the same session,
/// already <c>Done</c>, and the only way through would be to EDIT the document. The plan round found
/// that, and the answer is an ordinal on the identity — <c>docs/spec.md</c>, <c>docs/spec.md#2</c> —
/// so a new review is a new session and the finished one stays on the record.</para>
/// <para>Never automatic. Starting a fresh review silently would hide a completed one, which is the
/// opposite failure and the more expensive of the two: a person would believe a document had been
/// reviewed once when it had been reviewed twice, or read findings from a round they thought was
/// closed.</para>
/// </remarks>
public static partial class DocumentSessions
{
    /// <summary>
    /// How many reviews of ONE document this will look for before refusing.
    /// </summary>
    /// <remarks>
    /// A bound rather than a <c>while (true)</c>: the loop's exit depends on the filesystem, and a
    /// data directory that answers "exists" to everything — a permission fault, a stale network
    /// mount — would otherwise spin for ever inside a tool call. A hundredth review of one document
    /// is not a case anybody has; a hung server is.
    /// </remarks>
    public const int MaxReviews = 100;

    /// <summary>The identity of review <paramref name="ordinal"/> of <paramref name="document"/>.</summary>
    public static string Nth(string document, int ordinal) =>
        ordinal <= 1 ? document : $"{document}#{ordinal}";

    /// <summary>
    /// The session key a call should use: the latest review, or the next free one.
    /// </summary>
    /// <param name="exists">Whether a session with this identity is on disk. Injected so the rule is a unit test.</param>
    /// <returns>The identity, or empty when there are already <see cref="MaxReviews"/> of this document.</returns>
    public static string Which(string document, bool newReview, Func<string, bool> exists)
    {
        var taken = 0;
        while (taken < MaxReviews && exists(Nth(document, taken + 1)))
        {
            taken++;
        }

        // Nothing yet: the first review either way, because "start a new one" and "start the first
        // one" are the same act when there is nothing to keep apart from.
        return (taken, newReview) switch
        {
            (0, _) => document,
            (MaxReviews, true) => string.Empty,
            (_, true) => Nth(document, taken + 1),
            _ => Nth(document, taken),
        };
    }

    /// <summary>
    /// Why this identity cannot be reviewed under its own name, or null.
    /// </summary>
    /// <remarks>
    /// An ordinal is written as <c>spec.md#2</c>, and <c>#</c> is an ordinary character in a
    /// filename on every filesystem this runs on — so a real file called <c>notes#2.md</c> would
    /// share an identity with the second review of <c>notes.md</c>, and one person's round would
    /// land in another's session. Refused by NAME rather than escaped: an escape scheme is a second
    /// thing to get right, and a document whose name ends in a hash and a number can be reviewed as
    /// <c>documentText</c> under a name of its own. (codex, the code round.)
    /// </remarks>
    public static string? Reserved(string document) =>
        Ordinal().IsMatch(document)
            ? $"'{document}' ends in '#<number>', which is how this names the second and later "
            + "reviews of one document — so a session keyed by it could be either. Review it as "
            + "documentText with a documentName, or rename the file."
            : null;

    [System.Text.RegularExpressions.GeneratedRegex(@"#[0-9]+\z")]
    private static partial System.Text.RegularExpressions.Regex Ordinal();

    /// <summary>What to tell a caller trying to start a fresh review over an undecided round.</summary>
    /// <remarks>
    /// The orphaning this design was rewritten to prevent, one ordinal further along: the open
    /// session would sit awaiting a resolve that no longer has any way to reach it.
    /// </remarks>
    public static string StillOpen(string document) =>
        $"the current review of '{document}' has a round nobody has decided on yet. Resolve it "
      + "first — pass this document to resolve — and then ask for newReview; starting a fresh "
      + "review now would leave that round open with no way to answer it.";

    /// <summary>What to tell a caller that has reviewed one document a hundred times.</summary>
    public static string TooMany(string document) =>
        $"'{document}' has been reviewed {MaxReviews} times in this branch, which is the limit on one "
      + "document's reviews. Review it under another name, on another branch, or clear the finished "
      + "sessions out of the data directory.";
}
