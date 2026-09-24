namespace CoaiMcp.Core.Context;

/// <summary>
/// What is uncommitted in the checkout a round reviewed — or why that could not be read.
/// </summary>
/// <remarks>
/// "Not read" and "nothing there" are different facts and must be different states (doctrine §4):
/// a git call that failed used to answer an empty list, and a <c>proceed</c> then said nothing about
/// changes the reviewers never saw — the one sentence this exists to add (code round, codex).
/// </remarks>
/// <param name="Paths">The uncommitted paths, when they were read.</param>
/// <param name="Unreadable">Why they could not be read; empty when they were.</param>
public sealed record Uncommitted(IReadOnlyList<string> Paths, string Unreadable)
{
    /// <summary>Read, and nothing there — or a checkout on another commit, which says nothing about this one.</summary>
    public static readonly Uncommitted None = new([], string.Empty);
}

/// <summary>
/// What a code round says when there is nothing in it to review — and what it says about the part
/// of a change it did not see.
/// </summary>
/// <remarks>
/// <para>An empty diff used to launch every reviewer over an empty "## The change", collect nobody's
/// findings and answer <c>proceed</c> — after which the session was <c>Done</c> and the real change
/// could never be reviewed on that branch. A developer who forgot to commit, or who is not allowed to,
/// was told "all clean" about code nobody read (research/PLAN_a_failed_round_can_be_retried.md, S1).</para>
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
    /// What is uncommitted in the checkout, when that checkout stands on the reviewed commit — or why
    /// that could not be read. <see cref="Uncommitted.None"/> for a tree on another commit, which says
    /// nothing about this one.
    /// </param>
    public static string Refusal(
        string branch,
        string baseRef,
        string sha,
        IReadOnlyList<string> changedButExcluded,
        Uncommitted uncommitted)
    {
        const string Opening = "nothing to review, so nothing was reviewed and no round was recorded: ";

        return changedButExcluded.Count > 0
            ? Opening
                + $"every file {branch} changes over {baseRef} is one the gate never shows a reviewer "
                + $"(lock files and build output): {Names(changedButExcluded)}. "
                + "Commit the source change these belong to, then call review_code again."
            : Opening
                + $"{branch} has no committed change over {baseRef} — {Short(sha)} is the same commit or "
                + "already contained in it" + CheckoutSays(uncommitted);
    }

    private const string Committed = "review_code reviews COMMITTED changes only";

    /// <summary>The refusal's second half: what the checkout holds, or that it could not be read.</summary>
    private static string CheckoutSays(Uncommitted uncommitted) => uncommitted switch
    {
        { Paths.Count: > 0 } =>
            $", and the checkout has {Files(uncommitted.Paths.Count)} ({Names(uncommitted.Paths)}). {Committed}: "
            + "commit them and call review_code again, or, if committing is not allowed here, say so to the "
            + "person — a gate that passed nothing is not a gate.",
        { Unreadable.Length: > 0 } =>
            $". Whether the checkout has uncommitted files could not be read ({uncommitted.Unreadable}). "
            + $"{Committed}: if the change is uncommitted, commit it and call review_code again.",
        _ => $". {Committed}. Commit the change first, or pass the baseRef it should be compared against.",
    };

    /// <summary>
    /// What a round that DID run must add when the checkout carries more than it reviewed — or when
    /// nobody could tell whether it did.
    /// </summary>
    /// <remarks>
    /// Committed work plus an uncommitted tail: the diff is not empty, so the round runs — and a
    /// <c>proceed</c> must not cover the part nobody read. Empty only when the checkout was READ and
    /// holds nothing uncommitted; an unreadable one is said, never taken for clean.
    /// </remarks>
    public static string UnreviewedTail(Uncommitted uncommitted) => uncommitted switch
    {
        { Paths.Count: > 0 } =>
            $"; {Files(uncommitted.Paths.Count)} in the checkout "
            + $"{(uncommitted.Paths.Count == 1 ? "was" : "were")} NOT reviewed ({Names(uncommitted.Paths)}) — "
            + "review_code reviews committed changes only",
        { Unreadable.Length: > 0 } =>
            $"; whether the checkout has uncommitted files could not be read ({uncommitted.Unreadable}) — "
            + "anything uncommitted was NOT reviewed",
        _ => string.Empty,
    };

    /// <summary>
    /// <c>again: true</c> over the very commit the last code round reviewed — refused, with the way on.
    /// </summary>
    public static string NoNewCommit(int round, string sha) =>
        $"no new commit since code round {round} ({Short(sha)}), so there is nothing new to review and no "
        + "round was recorded. again: true reopens a finished code review for NEW commits: commit the change, "
        + "then call review_code with again: true.";

    /// <summary>
    /// <c>again: true</c> over commits that changed nothing a reviewer would be shown.
    /// </summary>
    public static string NothingSince(int round, IReadOnlyList<string> changedButExcluded) =>
        $"nothing reviewable since code round {round}, so no round was recorded: "
        + (changedButExcluded.Count > 0
            ? $"the only files changed since are ones the gate never shows a reviewer (lock files and build "
                + $"output): {Names(changedButExcluded)}."
            : "the commits since change no file.")
        + " Commit the source change, then call review_code with again: true.";

    private static string Files(int count) => count == 1 ? "1 uncommitted file" : $"{count} uncommitted files";

    private static string Names(IReadOnlyList<string> paths) =>
        paths.Count <= Named
            ? string.Join(", ", paths)
            : $"{string.Join(", ", paths.Take(Named))} and {paths.Count - Named} more";

    private static string Short(string sha) => sha.Length > 12 ? sha[..12] : sha;
}
