using CoaiMcp.Core.Notices;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>
/// What watches the jobs of a data directory from outside their runners: the next <c>role_status</c>, <c>role_cancel</c> and start-up sweep call it. It writes the notices of spec A
/// (<c>role-job-stalled</c>, <c>role-job-lost</c>), closes a job whose lease nobody holds as <c>lost</c>, and stops a runner a person cancelled (spec section E.3).
/// </summary>
/// <remarks>
/// <para><b>Lost only when the lease is free</b> (decision 2). The keeper takes a dead runner's job through <see cref="RoleJobStore.TryTake"/> — the same door the runner used, which adds one
/// to the generation — and then, holding the lease, stops the CLI child the dead runner recorded (by pid AND start time, so a reused number is never killed), writes <c>lost</c>, and writes the
/// notice. Of two keepers looking at the same dead job one gets the lease: the transition and its notice happen once.</para>
/// <para><b>A stalled runner is left alone.</b> It holds the lease; the keeper only says so — one notice per stall, marked by an exclusive file — and clears the mark when the heartbeat is fresh again.</para>
/// <para><b>Cancel never trusts the runner to answer</b> (E.3): it writes <c>cancel</c> and waits the grace for a runner that is listening; if the job is not over then, it fences the generation FIRST
/// (so a runner that is only stopped refuses to publish when it is resumed), then stops the recorded runner tree and its child, then takes the lease and writes <c>cancelled</c>.</para>
/// </remarks>
internal sealed class RoleJobKeeper(RoleJobStore store, Func<ServerNotice, bool> notice, Serilog.ILogger log)
{
    /// <summary>The job as it now is after one look: a dead runner's job closed as <c>lost</c>, a stall noticed. Null for no such job.</summary>
    public async Task<RoleJobObservation?> InspectAsync(string jobId, CancellationToken ct = default)
    {
        if (store.Observe(jobId) is not { } seen)
        {
            return null;
        }

        if (seen.Dead)
        {
            await CloseAsLostAsync(jobId, ct);

            return store.Observe(jobId);
        }

        Noticed(seen);

        return seen;
    }

    /// <summary>One look at every job that is not over — the start-up sweep and the cap's count.</summary>
    public async Task SweepAsync(CancellationToken ct = default)
    {
        foreach (var id in store.JobIds())
        {
            if (store.Read(id) is { Terminal: false })
            {
                await InspectAsync(id, ct);
            }
        }
    }

    private void Noticed(RoleJobObservation seen)
    {
        if (!seen.Stalled)
        {
            store.ClearStalled(seen.Job.JobId);

            return;
        }

        if (store.TryMarkStalled(seen.Job.JobId))
        {
            var now = store.Clock.GetUtcNow().UtcDateTime;
            Offer(RoleJobNotices.Stalled(seen.Job, now, now - RoleJobLiveness.LastSign(seen.Job, seen.Heartbeat)));
        }
    }

    private async Task CloseAsLostAsync(string jobId, CancellationToken ct)
    {
        using var lease = store.TryTake(jobId);
        if (lease is null || store.Read(jobId) is not { } job)
        {
            // Somebody else took it (another keeper, or a runner that started at the last moment), or it is over. Not ours to close.
            return;
        }

        await StopAsync(job.Child, ct);
        var now = store.Clock.GetUtcNow().UtcDateTime;
        var closed = lease.Write(current => current with
        {
            State = RoleJobStates.Lost,
            Stop = "lost",
            Reason = "the runner ended without a result: nothing held the job's lease any more",
            EndedUtc = now,
            Child = null,
        });
        if (closed is RoleJobWrite.Written written)
        {
            Offer(RoleJobNotices.Lost(written.Job, now));
        }
    }

    /// <summary>
    /// <c>role_cancel</c> (spec A and E.3): asks the runner to stop, and when it does not in <paramref name="grace"/> stops it — and its child — by identity and closes the job as <c>cancelled</c>.
    /// A job that is already over is answered as it is. Null for no such job.
    /// </summary>
    public async Task<RoleJobRecord?> CancelAsync(string jobId, TimeSpan grace, CancellationToken ct = default)
    {
        if (store.FolderOf(jobId) is not { } folder || store.Read(jobId) is not { } first)
        {
            return null;
        }

        if (first.Terminal)
        {
            return first;
        }

        folder.RequestCancel();
        if (store.Observe(jobId) is { LeaseHeld: true } && await WaitUntilOverAsync(jobId, grace, ct) is { } over)
        {
            return over;
        }

        // Not over: the runner is dead, or does not answer. Fence first, so a runner that is only stopped cannot publish when it is resumed.
        store.BumpGeneration(jobId);
        var job = store.Read(jobId) ?? first;
        await StopAsync(job.Runner, ct);
        await StopAsync(job.Child, ct);

        return await CloseAsCancelledAsync(jobId, ct) ?? store.Read(jobId);
    }

    private async Task<RoleJobRecord?> WaitUntilOverAsync(string jobId, TimeSpan grace, CancellationToken ct)
    {
        var until = store.Clock.GetTimestamp() + (long)(grace.TotalSeconds * store.Clock.TimestampFrequency);
        while (store.Clock.GetTimestamp() < until)
        {
            if (store.Read(jobId) is { Terminal: true } over)
            {
                return over;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), store.Clock, ct);
        }

        return store.Read(jobId) is { Terminal: true } last ? last : null;
    }

    private async Task<RoleJobRecord?> CloseAsCancelledAsync(string jobId, CancellationToken ct)
    {
        using var lease = await store.TakeWithinAsync(jobId, null, ct);
        if (lease is null)
        {
            return null;
        }

        var now = store.Clock.GetUtcNow().UtcDateTime;

        return lease.Write(current => current with
        {
            State = RoleJobStates.Cancelled,
            Stop = "cancelled",
            Reason = "cancelled by role_cancel",
            EndedUtc = now,
            Child = null,
        }) is RoleJobWrite.Written written ? written.Job : null;
    }

    /// <summary>Stops the process <paramref name="who"/> names — and everything under it — if it is still that process; confirms it, and says so in the log when something survived.</summary>
    private async Task StopAsync(RoleChild? who, CancellationToken ct)
    {
        if (who is null)
        {
            return;
        }

        var members = new List<ProcessIdentity> { who.Identity };
        members.AddRange(ProcessTree.DescendantsOf(who.Identity));
        ProcessTree.Terminate(who.Identity);
        var survivors = await ProcessTree.ConfirmGoneAsync(members);
        ct.ThrowIfCancellationRequested();
        if (survivors.Count > 0)
        {
            log.Warning("role job: {Survivors} process(es) of the tree of pid {Pid} were still alive after the kill", survivors.Count, who.Pid);
        }
    }

    private void Offer(ServerNotice what)
    {
        try
        {
            notice(what);
        }
        catch (Exception e)
        {
            // A notice is a view; the transition it reports is already on disk.
            log.Warning(e, "the {Code} notice for a role job could not be written", what.Code);
        }
    }
}
