namespace CoaiMcp.Server.RoleJobs;

/// <summary>What one look at a job found: its record, the heartbeat of ITS generation, and the two verdicts the lease and the heartbeat give.</summary>
/// <param name="LeaseHeld">The lease file is held open by somebody right now. False for a job that is over.</param>
/// <param name="Stalled">The lease is held and the heartbeat is older than <see cref="RoleJobLimits.StalledAfter"/>: a runner that is stopped or hung — still the runner, never to be taken from.</param>
/// <param name="Dead">The lease is FREE, the job is not over and <see cref="RoleJobLimits.LostGrace"/> has passed: the runner is gone. The only road to <c>lost</c>.</param>
internal sealed record RoleJobObservation(RoleJobRecord Job, RoleHeartbeat? Heartbeat, bool LeaseHeld, bool Stalled, bool Dead);

/// <summary>
/// The judgement of a job, as a pure function of what was read — so every case of decision 2 (spec section 0) is a unit test with a clock in the test's hand.
/// </summary>
/// <remarks>
/// <para><b>A stale heartbeat is not a death.</b> The only fact that proves a runner dead is a lease nobody holds; a heartbeat can be old because the runner is stopped (a laptop lid, a
/// SIGSTOP, a debugger) and it will go on. Taking the job from it would give one job two writers; the generation fence would refuse the old one's writes, but its CLI child would have run on
/// against the repository meanwhile.</para>
/// <para>The reference instant for "how long since a sign of life" is the newer of the heartbeat (of THIS generation) and the record's own update stamp, so a runner that has just taken the
/// lease and not yet beaten is measured from its take-over, not from the job's creation.</para>
/// </remarks>
internal static class RoleJobLiveness
{
    public static RoleJobObservation Judge(RoleJobRecord job, RoleHeartbeat? heartbeat, bool leaseHeld, DateTime nowUtc)
    {
        if (job.Terminal)
        {
            return new RoleJobObservation(job, heartbeat, LeaseHeld: false, Stalled: false, Dead: false);
        }

        var silent = nowUtc - LastSign(job, heartbeat);

        return new RoleJobObservation(
            job,
            heartbeat,
            leaseHeld,
            Stalled: leaseHeld && silent > RoleJobLimits.StalledAfter,
            Dead: !leaseHeld && silent > RoleJobLimits.LostGrace);
    }

    /// <summary>The newest sign of life: the heartbeat of this generation, or the record's last write.</summary>
    internal static DateTime LastSign(RoleJobRecord job, RoleHeartbeat? heartbeat) =>
        heartbeat is { } beat && beat.Utc > job.UpdatedUtc ? beat.Utc : job.UpdatedUtc;
}
