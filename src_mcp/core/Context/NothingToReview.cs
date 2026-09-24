namespace CoaiMcp.Core.Context;

/// <summary>
/// What a code round says when there is nothing in it to review — and what it says about the part
/// of a change it did not see.
/// </summary>
/// <remarks>
/// <para>An empty diff used to launch every reviewer over an empty "## The change", collect nobody's
/// findings and answer <c>proceed</c> — after which the session was <c>Done</c> and the real change
/// could never be reviewed on that branch. A developer who forgot to commit, or who is not allowed to,
/// was told "all clean" about code nobody read (todo/PLAN_a_failed_round_can_be_retried.md, S1).</para>
/// <para>So an empty diff is a REFUSAL, the rule the product already applies to a stage nobody serves,
/// and the sentence says WHICH empty it is — three causes with three different cures. Pure, so every
/// wording is a unit test rather than a round.</para>
/// </remarks>
public static class NothingToReview
{
    /// <summary>How many paths a sentence names before it counts the rest.</summary>
    private const int Named = 5;

    /// <summary>
    /// The refusal for a diff with nothing a reviewer would be shown.
    /// </summary>
    /// <param name="changedButExcluded">
    /// Every path the branch DID change, all of them ones the gate never shows a reviewer — lock
    /// files, build output. Empty when the branch changed nothing at all.
    /// </param>
    /// <param name="uncommitted">
    /// What is uncommitted in the checkout, when that checkout stands on the reviewed commit; empty
    /// otherwise, because a tree on another commit says nothing about this one.
    /// </param>
    public static string Refusal(
        string branch,
        string baseRef,
        string sha,
        IReadOnlyList<string> changedButExcluded,
        IReadOnlyList<string> uncommitted)
    {
        const string Opening = "nothing to review, so nothing was reviewed and no round was recorded: ";
        const string Committed = "review_code reviews COMMITTED changes only";

        if (changedButExcluded.Count > 0)
        {
            return Opening
                + $"every file {branch} changes over {baseRef} is one the gate never shows a reviewer "
                + $"(lock files and build output): {Names(changedButExcluded)}. "
                + "Commit the source change these belong to, then call review_code again.";
        }

        var head = $"{branch} has no committed change over {baseRef} — {Short(sha)} is the same commit or "
            + "already contained in it";

        return uncommitted.Count > 0
            ? Opening + head + $", and the checkout has {Files(uncommitted.Count)} "
                + $"({Names(uncommitted)}). {Committed}: commit them and call review_code again, or, if "
                + "committing is not allowed here, say so to the person — a gate that passed nothing is not a gate."
            : Opening + head + $". {Committed}. Commit the change first, or pass the baseRef it should be "
                + "compared against.";
    }

    /// <summary>
    /// What a round that DID run must add when the checkout carries more than it reviewed.
    /// </summary>
    /// <remarks>
    /// Committed work plus an uncommitted tail: the diff is not empty, so the round runs — and a
    /// <c>proceed</c> must not cover the part nobody read. Empty when there is no tail.
    /// </remarks>
    public static string UnreviewedTail(IReadOnlyList<string> uncommitted) =>
        uncommitted.Count == 0
            ? string.Empty
            : $"; {Files(uncommitted.Count)} in the checkout "
                + $"{(uncommitted.Count == 1 ? "was" : "were")} NOT reviewed ({Names(uncommitted)}) — "
                + "review_code reviews committed changes only";

    private static string Files(int count) => count == 1 ? "1 uncommitted file" : $"{count} uncommitted files";

    private static string Names(IReadOnlyList<string> paths) =>
        paths.Count <= Named
            ? string.Join(", ", paths)
            : $"{string.Join(", ", paths.Take(Named))} and {paths.Count - Named} more";

    private static string Short(string sha) => sha.Length > 12 ? sha[..12] : sha;
}
