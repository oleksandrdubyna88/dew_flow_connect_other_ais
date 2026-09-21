namespace CoaiMcp.Core.Collecting;

/// <summary>One review tree this machine holds, as a refusal names it.</summary>
/// <remarks>
/// Named rather than counted, because a refusal that says "you have ten" without saying WHICH ten
/// leaves a person no move. Every field is something they can act on: the checkout it belongs to,
/// the commit it holds, where it is on disk, and when it was made.
/// </remarks>
/// <param name="Repository">The repository the tree was made from — its git common dir.</param>
/// <param name="Sha">The commit checked out in it.</param>
/// <param name="Path">Where it is on disk.</param>
/// <param name="Created">When it was made, ISO-8601 UTC.</param>
public sealed record ReviewTreeRow(
    string Repository = "",
    string Sha = "",
    string Path = "",
    string Created = "");

/// <summary>
/// The whole repository as it was at the commit the reviewers read, checked out where a person can
/// open it — what <c>--tree-at</c> answers — or why it cannot be had.
/// </summary>
/// <remarks>
/// <para><b>Why a checkout at all, when <c>--file-at</c> already shows the file.</b> A
/// <c>coai-revision:</c> document is one file with no project behind it: no imports resolved, no
/// go-to-definition, no find-references. For the 55.7 % of pairs whose <c>head_sha</c> is orphaned
/// there is no other way to see the code AROUND the finding as it was. And a call hierarchy asked
/// from the current checkout counts today's callers — convincing and wrong — so a window whose
/// folder IS the reviewed commit is the only honest place to ask it.</para>
/// <para><b>A VIEW and never a payload</b>, on <see cref="FileAtRevision"/>'s precedent and for its
/// reasons: nothing here is stored in the rounds database, nothing here is sent, and nothing here is
/// reachable from <c>UploadRun.Wire</c>, which is a function of <c>StoredPair</c> alone.</para>
/// <para><b>Durable, unlike a round's tree.</b> <c>WorktreeManager</c>'s worktree is a lease with a
/// <c>finally</c>: the round ends, the tree goes. This one belongs to a person and lives until they
/// remove it. That is a different lifetime with different invariants, which is why it is a different
/// class rather than a widening of that one.</para>
/// <para><see cref="Reason"/> is empty exactly when <see cref="Path"/> is a tree a person can open;
/// otherwise it is one of <see cref="ReviewTreeReason"/> and <see cref="Path"/> is empty — except
/// <see cref="ReviewTreeReason.IncompleteAndDirty"/>, which names the directory precisely so it can
/// be looked at by hand.</para>
/// <para>The two lists are init-only properties rather than positional parameters because a
/// positional default must be a compile-time constant and <c>[]</c> is not one; everything here is
/// still immutable.</para>
/// </remarks>
public sealed record ReviewTree
{
    /// <summary>The pair asked about.</summary>
    public long FindingId { get; init; }

    /// <summary>The commit checked out, as the pair stores it.</summary>
    public string Sha { get; init; } = "";

    /// <summary>
    /// The checkout the ROW named, echoed back.
    /// </summary>
    /// <remarks>
    /// The reader compares it, which is the point: a pair recollected while the request was in flight
    /// can answer the same finding id at the same commit for a DIFFERENT repository, and an answer
    /// taken on trust would open the wrong one in a new window. The id and the commit alone cannot
    /// tell those apart. (Code round, codex.)
    /// </remarks>
    public string RepoPath { get; init; } = "";

    /// <summary>The tree on disk, or empty when there is none to open.</summary>
    public string Path { get; init; } = "";

    /// <summary>The repository the tree belongs to — its git common dir, which is the identity.</summary>
    public string Repository { get; init; } = "";

    /// <summary>True when a tree that already existed was handed back rather than made.</summary>
    public bool Reused { get; init; }

    /// <summary>Empty when the tree is there; otherwise one of <see cref="ReviewTreeReason"/>.</summary>
    public string Reason { get; init; } = "";

    /// <summary>
    /// Submodule mounts that stayed empty after population — named rather than hidden, because the
    /// person about to navigate into one is better served by knowing than by finding out.
    /// </summary>
    public IReadOnlyList<string> EmptyMounts { get; init; } = [];

    /// <summary>
    /// Every tree this machine already holds. Filled only for <see cref="ReviewTreeReason.Budget"/>,
    /// where it IS the answer — see <see cref="ReviewTreeRow"/>.
    /// </summary>
    public IReadOnlyList<ReviewTreeRow> Trees { get; init; } = [];
}

/// <summary>
/// Why a review tree could not be had — spelled once, and where a sibling mode already has a word
/// for the same fact, THAT word.
/// </summary>
/// <remarks>
/// The three new words all describe states only a DURABLE tree can be in: a cap that refuses rather
/// than evicts, a tree another press is still building, and a half-built tree somebody has since
/// typed into. None of them can happen to a round's lease, which is why none of them is in
/// <see cref="RealMethodReason"/>.
/// </remarks>
public static class ReviewTreeReason
{
    /// <summary>The database has no pair for that finding — recollected under the page, or never there.</summary>
    public const string PairNotFound = RealMethodReason.PairNotFound;

    /// <summary>The recorded checkout is gone, or is not a git repository any more.</summary>
    public const string RepoPathMissing = RealMethodReason.RepoPathMissing;

    /// <summary>git itself failed — a timeout, a permission, a disk. We learned nothing; try again.</summary>
    public const string GitFailed = RealMethodReason.GitFailed;

    /// <summary>The commit object is not in the repository at all — or the stored sha is not one.</summary>
    public const string CommitUnreachable = RealMethodReason.CommitUnreachable;

    /// <summary>
    /// This machine already holds as many review trees as it is allowed. Refused rather than evicted:
    /// every eviction rule that could have made room can remove a tree somebody is reading, and a
    /// product that deletes a person's work to save them a click has made the wrong trade.
    /// </summary>
    public const string Budget = "budget";

    /// <summary>
    /// Another press is building this very tree. Not an error — the second presser waits a moment and
    /// presses again. Told apart from a crash by the directory's age; see the reader.
    /// </summary>
    public const string InProgress = "in_progress";

    /// <summary>
    /// A tree that never finished being built, which somebody has since changed. It would be
    /// rebuilt if it were clean; it is not, so it is named and left exactly where it is.
    /// </summary>
    public const string IncompleteAndDirty = "incomplete_and_dirty";
}
