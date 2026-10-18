using CoaiMcp.Runners.Snapshots;

namespace CoaiMcp.Server;

/// <summary>
/// What one one-shot place has done so far (<see cref="ConsultantPlaces.OneShotAsync"/>): its copies, what it skipped, how many copy
/// targets it has reached, and which repositories the row holds.
/// </summary>
/// <param name="sink">Where every note is said as it is made — the log's, beside the place's own list.</param>
internal sealed class OneShotTally(Action<string> sink)
{
    public List<ReviewSnapshot> Made { get; } = [];

    /// <summary>What the place skipped, in the order it met it — carried on the place for the row's <c>note</c>.</summary>
    public List<string> Notes { get; } = [];

    /// <summary>The notes the model must be told as well as the person — what its copies do not hold (D9).</summary>
    public List<string> Limitations { get; } = [];

    /// <summary>
    /// Copy targets known so far: +1 for each directly granted work tree, +n for the repositories a granted parent is expanded into —
    /// counted BEFORE any is copied, so the budget's "of" is the real total and the bound is the row's.
    /// </summary>
    public int Found { get; set; }

    /// <summary>The repositories the row copies, by full path: a directly granted one first (D6) — whatever its place in the list — then the first parent to find it.</summary>
    public HashSet<string> Planned { get; } = new(GrantedPaths.Comparer);

    /// <summary>Linked worktrees left out because their main checkout is copied, by full path — counted once, whichever grants found them.</summary>
    public HashSet<string> LeftOut { get; } = new(GrantedPaths.Comparer);

    /// <summary>The repositories confirmed so far that no selection can remove — the early evidence that the bound is passed.</summary>
    public HashSet<string> Certain { get; } = new(GrantedPaths.Comparer);

    /// <summary>The common-dir keys of the main checkouts the row copies — what a linked worktree is compared with (D9).</summary>
    public HashSet<string> Mains { get; } = [];

    /// <summary>A skip, said to the log's sink and kept on the place for the row.</summary>
    public void Say(string text)
    {
        Notes.Add(text);
        sink(text);
    }

    /// <summary>A limitation: a note, and also a sentence for the prompt.</summary>
    public void Limit(string text)
    {
        Limitations.Add(text);
        Say(text);
    }

    /// <summary>
    /// A copy that was made is kept, whatever follows; a copy cut short by the budget comes back as a refusal of its own, and one that
    /// finished past it is past it. The refusal's reason — empty when the copy was made.
    /// </summary>
    public string Record(SnapshotOutcome outcome, CancellationToken ct)
    {
        if (outcome is SnapshotOutcome.Made copy)
        {
            Made.Add(copy.Snapshot);
        }

        ct.ThrowIfCancellationRequested();

        return outcome is SnapshotOutcome.Refused refused ? refused.Reason : string.Empty;
    }
}
