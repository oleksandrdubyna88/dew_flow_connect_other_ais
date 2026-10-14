using CoaiMcp.Runners.Snapshots;

namespace CoaiMcp.Server;

/// <summary>
/// The live snapshot of every conversation this process holds one for — ONE stable path per conversation, refreshed in
/// place before each turn (D6 of <c>todo/PLAN_vibe_and_kimi_runtimes.md</c>), keyed by the conversation's folder.
/// </summary>
/// <remarks>
/// <para><b>Process-wide, not per service.</b> A settings reload builds a new <see cref="ConsultationService"/>; a registry
/// that lived in the old one would have kept the old snapshot's bytes reserved in <see cref="SnapshotBudget.Shared"/> for
/// ever, and handed the next turn a fresh copy made over a tree it still held. Everything that touches one folder's entry
/// does so under that folder's turn (<c>VibeHomeTurns</c>, taken by <see cref="ConsultantPlaces"/> for the turn and by the
/// home sweep before it deletes), so the lock here only guards the dictionary.</para>
/// <para><b>A restarted server</b> holds no entry and finds the tree its predecessor left at the conversation's root:
/// <see cref="ReviewSnapshots.MakeAtAsync"/> builds a fresh copy beside it and swaps it in at the SAME path, so the
/// resumed session's folder is still its folder (research/RESULTS_vibe_confinement.md §22 B, E).</para>
/// </remarks>
internal static class ConversationSnapshots
{
    /// <summary>The snapshot's root inside a conversation's folder.</summary>
    public const string TreeName = "tree";

    private static readonly Dictionary<string, ReviewSnapshot> Live =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    /// <summary>
    /// The conversation's snapshot as the live tree is NOW: the held one refreshed in place, or — on a first turn, or the
    /// first turn this process sees — one made at the conversation's root. A refusal leaves the previous tree as it was.
    /// </summary>
    public static async Task<SnapshotOutcome> FreshAsync(ReviewSnapshots maker, string folder, string repo, CancellationToken ct)
    {
        var source = new SnapshotSource.Live(repo);
        if (Held(folder) is { } held && Directory.Exists(held.Root))
        {
            return await held.RefreshAsync(source, ct) switch
            {
                SnapshotRefresh.Refreshed => new SnapshotOutcome.Made(held),
                SnapshotRefresh.Refused refused => new SnapshotOutcome.Refused(refused.Reason),
                _ => throw new InvalidOperationException("the union is closed"),
            };
        }

        // A held snapshot whose root went (swept, deleted by hand) is let go before a new one takes its place.
        Release(folder);
        var made = await maker.MakeAtAsync(source, Path.Combine(folder, TreeName), ct);
        if (made is SnapshotOutcome.Made fresh)
        {
            Hold(folder, fresh.Snapshot);
        }

        return made;
    }

    /// <summary>The conversation's snapshot let go: deleted, and its bytes given back to the budget. Nothing held is nothing done.</summary>
    public static void Release(string folder)
    {
        ReviewSnapshot? held;
        lock (Live)
        {
            if (!Live.Remove(Key(folder), out held))
            {
                return;
            }
        }

        held.Dispose();
    }

    /// <summary>Whether this process holds a snapshot for <paramref name="folder"/> — for a test that a release left none.</summary>
    internal static bool Holds(string folder)
    {
        lock (Live)
        {
            return Live.ContainsKey(Key(folder));
        }
    }

    private static ReviewSnapshot? Held(string folder)
    {
        lock (Live)
        {
            return Live.GetValueOrDefault(Key(folder));
        }
    }

    private static void Hold(string folder, ReviewSnapshot snapshot)
    {
        lock (Live)
        {
            Live[Key(folder)] = snapshot;
        }
    }

    private static string Key(string folder) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
}
