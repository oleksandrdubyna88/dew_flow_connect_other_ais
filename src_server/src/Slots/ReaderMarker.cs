using CoaiMcp.Runners.Processes;

namespace CoaiServer;

/// <summary>The fixed codes a slot is blocked with — unavailable until recovery proves it safe.</summary>
/// <remarks>
/// Story 6.1a of todo/PLAN_every_round_is_counted.md, the epic-6 risk consultation: availability is the price of an
/// ambiguous crash. A code, never a sentence, so the catalog, the log and the state file say one word each.
/// </remarks>
public static class BlockedReasons
{
    /// <summary>A reader marker exists with nothing it can be matched against — the server died between writing the marker and registering the child.</summary>
    public const string ReaderIdentityMissing = "reader-identity-missing";

    /// <summary>A reader named by the marker was still alive after the kill and the confirmation bound.</summary>
    public const string ReaderTerminationFailed = "reader-termination-failed";
}

/// <summary>Where a reader marker is in its life.</summary>
public static class ReaderPhases
{
    /// <summary>Written BEFORE the reader is spawned: a reader MAY exist, and nothing names it yet.</summary>
    public const string Spawning = "spawning";

    /// <summary>The child is registered — the marker's tree names it, and later its descendants.</summary>
    public const string Running = "running";

    /// <summary>
    /// The tree was ended, but a descendant could not be written down (epic 6's code round): the tree on disk is PARTIAL,
    /// so reconciling what it names proves nothing about the rest. Unidentified by construction — the slot stays blocked
    /// (<see cref="BlockedReasons.ReaderIdentityMissing"/>) until a person proves it safe.
    /// </summary>
    public const string Partial = "partial";
}

/// <summary>
/// The fail-closed "a gauge reader may exist" marker on a slot's <c>state.json</c>.
/// </summary>
/// <param name="Phase">One of <see cref="ReaderPhases"/>.</param>
/// <param name="OwnerPid">The server that wrote it, and whose death orphans the reader.</param>
/// <param name="OwnerStartedUtc">That server's start time, so the pid is an identity rather than a number.</param>
/// <param name="SinceUtc">When the marker was written.</param>
/// <param name="Tree">
/// The reader and, once they are listed, its descendants — each by pid AND start time. Empty while spawning.
/// </param>
/// <remarks>
/// <para><b>Written before the spawn, cleared after the confirmation, and read under the lock by the next holder.</b>
/// A startup sweep alone cannot contain a reader: <c>SlotRegistry.AcquireAsync</c> hands back a lease the moment the
/// lock opens, and a reader a crashed server left behind would be sharing the account's token set with the next
/// launch. So the marker is what the next holder reconciles FIRST (<see cref="ReaderReconcile"/>) — and a marker with
/// no identity blocks the slot rather than guessing, because a reader that cannot be named cannot be proved gone.</para>
/// <para>The doctrine's omitted-field rule: an older state file has no marker and reads as none; a marker from a
/// torn write never exists, because the state file is written whole or not at all (<c>JsonFileStore</c>).</para>
/// </remarks>
public sealed record ReaderMarkerDto(
    string Phase,
    int OwnerPid,
    DateTime OwnerStartedUtc,
    DateTimeOffset SinceUtc,
    IReadOnlyList<ProcessIdentity> Tree = null!)
{
    /// <summary>Never null, whatever a deserialiser did with the property.</summary>
    public IReadOnlyList<ProcessIdentity> Tree { get; init; } = Tree ?? [];

    /// <summary>A marker that names nothing a kill could be aimed at — spawning, or a record a crash left half-written.</summary>
    public bool Unidentified =>
        Phase != ReaderPhases.Running || Tree.Count == 0 || Tree.Any(member => member.Pid <= 0 || member.StartedUtc == default);
}

/// <summary>What reconciling a previous holder's marker decided.</summary>
public abstract record Reconciled
{
    private Reconciled()
    {
    }

    /// <summary>Every process the marker named is confirmed gone; the lease may be used.</summary>
    public sealed record Cleared : Reconciled;

    /// <summary>The slot stays unavailable, for this <see cref="BlockedReasons"/> code.</summary>
    public sealed record Blocked(string Code) : Reconciled;
}

/// <summary>
/// Reconciles the previous holder's reader marker: kills what it names — the whole tree, pid AND start time matched —
/// and CONFIRMS the exit before the lease is usable.
/// </summary>
/// <remarks>
/// <para>The order the epic-6 risk consultation fixed (todo/PLAN_every_round_is_counted.md): take the lock, reconcile,
/// and only then hand the lease out. A process whose start time no longer matches the record is somebody else's under
/// a reused number and is left alone; a marker that names nothing blocks; a member still alive at the bound blocks.
/// Blocking is re-tried on every later acquisition, so a slot blocked over a member that later dies by itself comes
/// back without a human — and one blocked over a nameless reader does not, which is the price the plan accepted.</para>
/// <para>The process seams are parameters so the decision can be tested without a process that refuses to die, which
/// no test machine can safely make.</para>
/// </remarks>
public static class ReaderReconcile
{
    /// <summary>With the real process control: <see cref="ProcessTree.Terminate"/> and <see cref="ProcessTree.ConfirmGoneAsync"/>.</summary>
    public static Task<Reconciled> RunAsync(ReaderMarkerDto marker) =>
        RunAsync(marker, ProcessTree.Terminate, ProcessTree.ConfirmGoneAsync);

    /// <param name="terminate">Kills the process an identity names, its tree included, when it is still that process.</param>
    /// <param name="confirmGone">Waits for the members to be gone and answers with the survivors.</param>
    public static async Task<Reconciled> RunAsync(
        ReaderMarkerDto marker,
        Func<ProcessIdentity, bool> terminate,
        Func<IReadOnlyList<ProcessIdentity>, Task<IReadOnlyList<ProcessIdentity>>> confirmGone)
    {
        if (marker.Unidentified)
        {
            return new Reconciled.Blocked(BlockedReasons.ReaderIdentityMissing);
        }

        foreach (var member in marker.Tree)
        {
            terminate(member);
        }

        var survivors = await confirmGone(marker.Tree);

        return survivors.Count == 0
            ? new Reconciled.Cleared()
            : new Reconciled.Blocked(BlockedReasons.ReaderTerminationFailed);
    }
}
