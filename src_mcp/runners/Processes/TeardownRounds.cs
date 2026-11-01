namespace CoaiMcp.Runners.Processes;

/// <summary>What a confirmation round reads and does, as seams: the product's (the process table, the pidfd kill) or a test's.</summary>
/// <param name="Descendants">The shim's live descendants now — only those whose start time could be read.</param>
/// <param name="Reap">Takes the status of each listed zombie this process did not start.</param>
/// <param name="Liveness">The THREE-state question for one member.</param>
/// <param name="Kill">Ends the members by identity.</param>
/// <param name="Step">Waits between two rounds.</param>
/// <param name="Expired">Whether the confirmation bound has passed.</param>
internal sealed record RoundSeams(
    Func<IReadOnlyList<ProcessIdentity>> Descendants,
    Action<IReadOnlyList<ProcessIdentity>> Reap,
    Func<ProcessIdentity, Liveness> Liveness,
    Action<IReadOnlyList<ProcessIdentity>> Kill,
    Func<Task> Step,
    Func<bool> Expired);

/// <summary>
/// The bounded confirmation of a supervised teardown (DL9): re-list, reap, end again, until nothing is left or the bound passes. <b>Only <see cref="Liveness.Gone"/> drops a member</b> (the E5 risk consultant,
/// 2026-10-17): a member whose start time cannot be read this round is <see cref="Liveness.Unknown"/>, is KEPT — remembered from the earlier listing, since the listing itself omits what it cannot identify — and is a survivor
/// if it is still unknown at the bound, so the marker stays. A two-state "alive?" would drop it, hand an empty list to the marker's clearing, and delete the one record that names a possible refresher.
/// </summary>
internal static class TeardownRounds
{
    /// <summary>The members of <paramref name="members"/> that are not provably gone, one per process (the same pid started within the identity slack is one reading of one process).</summary>
    internal static IReadOnlyList<ProcessIdentity> Kept(IEnumerable<ProcessIdentity> members, Func<ProcessIdentity, Liveness> liveness) =>
        [.. Merged(members).Where(member => liveness(member) is not Liveness.Gone)];

    private static List<ProcessIdentity> Merged(IEnumerable<ProcessIdentity> members) =>
        members.Aggregate(new List<ProcessIdentity>(), (merged, member) => merged.Exists(known => known.Pid == member.Pid && OrphanSweep.Same(known.StartedUtc, member.StartedUtc)) ? merged : [.. merged, member]);

    /// <summary>Rounds until every known member is gone or the bound passes; answers with the members still there (alive or not checkable) — empty only when each is provably gone.</summary>
    internal static async Task<IReadOnlyList<ProcessIdentity>> ConfirmedAsync(IReadOnlyList<ProcessIdentity> known, RoundSeams seams)
    {
        var kept = known;
        while (true)
        {
            var seen = seams.Descendants();
            seams.Reap(seen);
            kept = Kept([.. kept, .. seen], seams.Liveness);
            if (kept.Count == 0 || seams.Expired())
            {
                return kept;
            }

            seams.Kill(kept);
            await seams.Step();
        }
    }
}
