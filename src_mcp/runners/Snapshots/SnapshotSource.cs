namespace CoaiMcp.Runners.Snapshots;

/// <summary>
/// Where a snapshot's bytes come from — the two source modes D6 of <c>todo/PLAN_vibe_and_kimi_runtimes.md</c> names.
/// </summary>
/// <remarks>
/// A closed union rather than a flag, because the two differ in what git is asked and in what a missing file means: a
/// pinned tree lacking a listed file is a broken tree (refused), a live tree lacking one has just had it deleted (absent).
/// </remarks>
public abstract record SnapshotSource
{
    private SnapshotSource()
    {
    }

    /// <summary>The directory the files are read from.</summary>
    public abstract string Directory { get; }

    /// <summary>
    /// Whether untracked-not-ignored files are part of the tree. A live checkout's are — the consultant is promised the
    /// working tree; a pinned worktree's are not — a round reviews what the commit holds.
    /// </summary>
    public abstract bool IncludesUntracked { get; }

    /// <summary>
    /// Whether the tree can be edited while it is copied — and so whether the whole inventory is listed and stamped
    /// AGAIN after the last file (D6 as amended by the plan gate, 2026-10-12: per-file stamps let a copy publish a mix
    /// of times). A live checkout is the person's working tree, edited while the consultation runs: yes. A round's
    /// pinned worktree is coai's own detached checkout under its own root, made for the round, populated before the
    /// lease is handed out, and read by nobody but read-only reviewers: no — the per-file stamp still guards it, and a
    /// second git listing plus a stat per file of a tree that cannot move would be paid for nothing on every round.
    /// </summary>
    public abstract bool MovesUnderTheCopy { get; }

    /// <summary>
    /// A round's pinned worktree: what <c>git ls-files</c> lists in it, plus every populated submodule's content at the
    /// revision checked out there — never <c>git archive</c>, whose <c>export-ignore</c> / <c>export-subst</c> attributes
    /// would drop or rewrite reviewed bytes.
    /// </summary>
    public sealed record Pinned(string Worktree) : SnapshotSource
    {
        public override string Directory => Worktree;

        public override bool IncludesUntracked => false;

        public override bool MovesUnderTheCopy => false;
    }

    /// <summary>
    /// A live checkout: tracked plus untracked-not-ignored paths, their bytes as they are NOW, a deleted path absent
    /// (a rename is the old path absent and the new one present). A file that changes while it is copied — or any path
    /// added, removed or changed anywhere in the inventory before the copy ends — makes the whole copy retry once, then
    /// refuse: the tree is moving under the copy.
    /// </summary>
    public sealed record Live(string Checkout) : SnapshotSource
    {
        public override string Directory => Checkout;

        public override bool IncludesUntracked => true;

        public override bool MovesUnderTheCopy => true;
    }
}

/// <summary>What making a snapshot came to: the snapshot, or why there is none — a value, never an exception.</summary>
public abstract record SnapshotOutcome
{
    private SnapshotOutcome()
    {
    }

    public sealed record Made(ReviewSnapshot Snapshot) : SnapshotOutcome;

    /// <summary>Not made, and the sentence a row's failure carries. Nothing was published.</summary>
    public sealed record Refused(string Reason) : SnapshotOutcome;
}

/// <summary>What refreshing a snapshot in place came to: its new manifest, or why the previous one still stands.</summary>
public abstract record SnapshotRefresh
{
    private SnapshotRefresh()
    {
    }

    public sealed record Refreshed(SnapshotManifest Manifest) : SnapshotRefresh;

    /// <summary>Not refreshed; the previous snapshot is whole and its manifest unchanged.</summary>
    public sealed record Refused(string Reason) : SnapshotRefresh;
}

/// <summary>What <see cref="ReviewSnapshot.Resolve"/> answered for a requested path.</summary>
public abstract record SnapshotPath
{
    private SnapshotPath()
    {
    }

    /// <summary>The manifest's stored destination path — never the supplied spelling.</summary>
    public sealed record Inside(string Path) : SnapshotPath;

    public sealed record Refused(string Reason) : SnapshotPath;
}

/// <summary>What <see cref="ReviewSnapshot.Read"/> answered: the bytes the snapshot holds, or a refusal.</summary>
public abstract record SnapshotRead
{
    private SnapshotRead()
    {
    }

    public sealed record Served(byte[] Bytes) : SnapshotRead;

    public sealed record Refused(string Reason) : SnapshotRead;
}
