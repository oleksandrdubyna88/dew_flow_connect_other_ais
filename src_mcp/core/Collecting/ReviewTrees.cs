namespace CoaiMcp.Core.Collecting;

/// <summary>One review tree this machine holds, as the list shows it.</summary>
/// <param name="Name">Our own name for it — what a removal takes, and never a path.</param>
/// <param name="Repository">The git common dir it was made from; empty when the record cannot be read.</param>
/// <param name="RepoPath">A working tree of that repository, which is where git must be run from.</param>
/// <param name="Sha">The commit checked out.</param>
/// <param name="Path">Where it is on disk.</param>
/// <param name="Created">When it was made, ISO-8601 UTC.</param>
/// <param name="State">One of <see cref="ReviewTreeState"/>.</param>
public sealed record HeldReviewTree(
    string Name = "",
    string Repository = "",
    string RepoPath = "",
    string Sha = "",
    string Path = "",
    string Created = "",
    string State = "");

/// <summary>
/// What a review tree is, as far as this machine can tell — and every one of these has a different
/// safe action, which is why they are not collapsed into "ok" and "broken".
/// </summary>
public static class ReviewTreeState
{
    /// <summary>A record, a directory, and git still lists it. The ordinary case.</summary>
    public const string Ready = "ready";

    /// <summary>A directory with no readable record: it never finished being made.</summary>
    public const string Incomplete = "incomplete";

    /// <summary>A record whose directory somebody removed by hand. Nothing to open; the record can go.</summary>
    public const string Vanished = "vanished";

    /// <summary>
    /// Record and directory both there, and git does not list it as a worktree.
    /// </summary>
    /// <remarks>
    /// Somebody ran <c>worktree prune</c> while the directory existed, or the <c>.git</c> file that
    /// links it back was damaged. It is not <see cref="Ready"/> — git commands in it will fail — and
    /// it is not <see cref="Vanished"/>, because the files are right there and may be somebody's. It
    /// gets its own word so the person is told the truth and the removal can refuse to guess.
    /// </remarks>
    public const string Unregistered = "unregistered";

    /// <summary>
    /// The checkout it was made from is gone, so nothing can be asked about it.
    /// </summary>
    /// <remarks>
    /// <c>worktree unlock</c> and <c>worktree remove</c> must run from a working tree of the parent
    /// repository; without one there is no way to deregister this tree, only to delete its files.
    /// </remarks>
    public const string Unreachable = "unreachable";
}

/// <summary>Every review tree this machine holds — what <c>--trees</c> answers.</summary>
/// <remarks>
/// <para>Built from OUR records and OUR directories, joined — never from the filesystem at large. A
/// round worktree (<c>coai-wt-</c>) and a person's own worktree therefore cannot appear here, and
/// what cannot be listed cannot be offered for removal.</para>
/// <para>The join is in both directions on purpose: a record with no directory is
/// <see cref="ReviewTreeState.Vanished"/> and a directory with no record is
/// <see cref="ReviewTreeState.Incomplete"/> — and the second only exists because a process can die
/// between <c>worktree add</c> and the record, which is exactly the state nobody would think to look
/// for. (Plan round, codex.)</para>
/// </remarks>
public sealed record ReviewTrees
{
    /// <summary>Where they live, so a person can find them without this product.</summary>
    public string Root { get; init; } = "";

    /// <summary>Oldest first.</summary>
    public IReadOnlyList<HeldReviewTree> Trees { get; init; } = [];

    /// <summary>Empty when the list is the list; otherwise why it could not be made.</summary>
    public string Reason { get; init; } = "";
}

/// <summary>What happened to a tree somebody asked to give back — what <c>--tree-remove</c> answers.</summary>
public sealed record ReviewTreeRemoval
{
    /// <summary>The name asked about, echoed.</summary>
    public string Name { get; init; } = "";

    /// <summary>One of <see cref="ReviewTreeRemovalReason"/>. Never empty: something always happened.</summary>
    public string Reason { get; init; } = "";

    /// <summary>
    /// What is in the way, when something is — paths, named so the refusal can be acted on.
    /// </summary>
    /// <remarks>
    /// A submodule's own files are named individually. One <c>git status</c> in the parent SEES a
    /// dirty submodule but reports only the mount (<c>mods/sub</c>), so naming what is inside takes a
    /// second status in that submodule — measured 2026-09-21 against real git, and the reason this
    /// list exists rather than a count.
    /// </remarks>
    public IReadOnlyList<string> InTheWay { get; init; } = [];

    /// <summary>How many IGNORED files would go with the tree — build output, and sometimes not.</summary>
    public int Ignored { get; init; }

    /// <summary>A few of them by name, so the number can be judged rather than trusted.</summary>
    public IReadOnlyList<string> IgnoredSample { get; init; } = [];
}

/// <summary>
/// What a removal did, or refused to do. Every word is a different next move for the person.
/// </summary>
public static class ReviewTreeRemovalReason
{
    /// <summary>Gone: files, registration and record.</summary>
    public const string Removed = "removed";

    /// <summary>
    /// The directory was already gone, so only the record was dropped.
    /// </summary>
    /// <remarks>
    /// Git's own registration of it is deliberately LEFT. <c>git worktree prune</c> is the only tool
    /// git offers and it has no path filter: it clears every registration in that repository whose
    /// directory is unreachable, which on a machine with a worktree on an unmounted drive is more
    /// than anybody asked for. (Plan round, codex, Blocking.) The next checkout at this identity
    /// prunes under its own narrow guard; until then the registration is harmless and the answer
    /// says it is there.
    /// </remarks>
    public const string Forgotten = "forgotten";

    /// <summary>Somebody's work is in it. Nothing was touched, and <see cref="ReviewTreeRemoval.InTheWay"/> says what.</summary>
    public const string Dirty = "dirty";

    /// <summary>
    /// Only IGNORED files are in it, and they were not asked for.
    /// </summary>
    /// <remarks>
    /// The first plan called ignored files reproducible and removed them with the tree. A reviewer
    /// was right that this is false for the class: a <c>.env</c>, a local config, a globally ignored
    /// <c>notes.md</c> are all somebody's work and all invisible to a plain status. So they are
    /// counted, sampled, and REFUSED by default; the person asks for them explicitly, per tree, and
    /// that second ask is the only confirmation this product has.
    /// </remarks>
    public const string HasIgnored = "has_ignored";

    /// <summary>Not a name this product holds a record for — so not something it may delete.</summary>
    public const string NotOurs = "not_ours";

    /// <summary>git ran and did not answer, or could not be asked. The tree is untouched.</summary>
    public const string GitFailed = "git_failed";

    /// <summary>
    /// The tree is there and git does not know it, so it cannot be deregistered — only deleted.
    /// </summary>
    /// <remarks>
    /// Refused rather than deleted, because "git cannot tell me about it" is precisely when this
    /// product has no way to know whether the files are somebody's.
    /// </remarks>
    public const string Unregistered = ReviewTreeState.Unregistered;

    /// <summary>The parent checkout is gone, so no git command can deregister this tree.</summary>
    public const string Unreachable = ReviewTreeState.Unreachable;

    /// <summary>
    /// No record proves this directory is the tree its name says, so nothing may be deregistered on
    /// its word.
    /// </summary>
    /// <remarks>
    /// A tree whose maker died before writing a record, or beside a record that describes a different
    /// (repository, commit). Refusing is not a dead end: <c>--tree-at</c> at that commit inspects such
    /// a directory itself and rebuilds it when it holds nothing, which is the action the sentence
    /// names. (Code round 2, codex and gemini.)
    /// </remarks>
    public const string Incomplete = ReviewTreeState.Incomplete;
}
