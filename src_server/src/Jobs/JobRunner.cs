using CoaiMcp.Runners.Reviewers;

namespace CoaiServer;

/// <summary>
/// The background half: it takes queued jobs, finds them an account, runs them, and records what
/// happened — and it is the only thing that moves a job out of Queued.
/// </summary>
/// <remarks>
/// <para><b>Every path ends in a terminal state.</b> A job that is Running and never finished is a
/// caller polling for ever and a slot nobody releases, so the run is wrapped so that an exception
/// from anywhere — the adapter, the executor, the ledger — becomes <c>Failed</c> with the reason,
/// and the slot and the temp directory are released in a <c>finally</c>. The plan round raised this:
/// the six outcomes cover what the VENDOR does, and nothing covered what the infrastructure does.</para>
/// <para><b>A rate limit rotates before it waits.</b> A vendor that says "come back at 9:30pm" is not
/// a throttle to sleep through — it is that ACCOUNT being out. The slot is parked until the time it
/// named and the job goes to the next account at once; only when no account is free does the retry
/// ladder apply.</para>
/// </remarks>
public sealed class JobRunner(
    JobStore jobs,
    VendorCatalogHost catalog,
    SlotRegistry slots,
    IReviewLauncher launcher,
    UsageLedger? ledger = null,
    Action<string, Exception>? onFailure = null)
{
    /// <summary>A slot that refused with no time named is parked this long.</summary>
    /// <remarks>
    /// A circuit breaker rather than a guess at the vendor's window: it stops the NEXT job landing on
    /// the account that just refused, which without it would fail in the same way a second later.
    /// </remarks>
    public static readonly TimeSpan TransientCooldown = TimeSpan.FromMinutes(5);

    private readonly Action<string, Exception> _report = onFailure ?? ((_, _) => { });

    /// <summary>Take one job for this vendor, if there is one that can run. False when idle.</summary>
    /// <param name="started">
    /// Signalled as soon as it is known whether a job was CLAIMED — before the review itself runs.
    /// The pump needs that answer to decide whether to try again, and waiting for the review would
    /// mean one start per ten minutes rather than one per free account.
    /// </param>
    public async Task<bool> PumpAsync(
        string vendorId, CancellationToken ct, TaskCompletionSource<bool>? started = null)
    {
        if (catalog.Current.Find(vendorId) is not { } vendor)
        {
            started?.TrySetResult(false);

            return false;
        }

        var now = DateTimeOffset.UtcNow;
        if (SlotSelector.Pick(slots.SlotsOf(vendor), now) is not { } candidate)
        {
            started?.TrySetResult(false);

            return false;
        }

        // The lease FIRST, then the claim: taking the job before the account would leave a Running
        // job with nowhere to run if another process held the slot.
        using var lease = await slots.AcquireAsync(candidate, TimeSpan.Zero, ct: ct);
        if (lease is null)
        {
            started?.TrySetResult(false);

            return false;
        }

        // Claimed under the store's own lock, so a DELETE that answered 204 cannot be overtaken by
        // this dispatch. The token comes back with it: it is the one a cancel or the deadline sweep
        // fires, and it is what actually stops the vendor process.
        if (jobs.TryClaim(vendorId, candidate.Name, DateTimeOffset.UtcNow, ct) is not { } claimed)
        {
            started?.TrySetResult(false);

            return false;
        }

        started?.TrySetResult(true);
        await RunAsync(vendor, candidate, claimed.Job, claimed.Token);

        return true;
    }

    private async Task RunAsync(VendorConfig vendor, AccountSlot slot, JobRecord job, CancellationToken jobToken)
    {
        try
        {
            var environment = SlotEnvironment.For(vendor.Runtime, slot.Directory, slots.TokenFor(slot, vendor.Runtime));
            var attempt = await launcher.RunAsync(vendor, slot, job, environment, jobToken);
            Record(vendor, slot, job, attempt);
        }
        catch (OperationCanceledException)
        {
            // A cancel or the deadline. The store already wrote the terminal state and the reason,
            // and Finish ignores a job that is already finished — so this only covers the case where
            // the HOST is stopping, where nothing else will.
            jobs.Finish(JobTransitions.Fail(
                job, FailureKind.Cancelled, "stopped before it finished", DateTimeOffset.UtcNow));
        }
        catch (Exception e)
        {
            // Anything the infrastructure throws. A Running job that never reaches a terminal state
            // is a caller polling for ever and an account nobody releases, so this catch is
            // deliberately broad and the detail goes to the log rather than to the caller.
            _report($"job {job.Id} ({vendor.Id}/{slot.Name}) failed outside the vendor", e);
            jobs.Finish(JobTransitions.Fail(
                job, FailureKind.NotStarted, "the server could not run this review — see the server log",
                DateTimeOffset.UtcNow));
        }
    }
    private void Record(VendorConfig vendor, AccountSlot slot, JobRecord job, ReviewAttempt attempt)
    {
        var now = DateTimeOffset.UtcNow;
        JobRecord? finished = attempt.Outcome switch
        {
            ReviewerOutcome.RateLimited limited => RateLimited(vendor, slot, job, limited, now),
            ReviewerOutcome.NonZeroExit exit =>
                JobTransitions.Fail(job, FailureKind.NonZeroExit, Tail(exit.StdErrTail, exit.ExitCode), now),
            ReviewerOutcome.TimedOut =>
                JobTransitions.Fail(job, FailureKind.TimedOut, JobTransitions.ExpiryReason(job), now),
            ReviewerOutcome.Unparseable bad =>
                JobTransitions.Fail(job, FailureKind.UnparseableByVendor, bad.Reason, now),
            ReviewerOutcome.NotStarted missing =>
                JobTransitions.Fail(job, FailureKind.NotStarted, missing.Reason, now),
            _ => Succeeded(slot, job, attempt, now),
        };

        if (finished is null)
        {
            // Requeued onto another account. Nothing terminal happened, so nothing is recorded: a
            // usage line per rate-limited attempt would count one review several times.
            return;
        }

        jobs.Finish(finished);
        Ledger(vendor, job, finished);
    }

    private JobRecord Succeeded(AccountSlot slot, JobRecord job, ReviewAttempt attempt, DateTimeOffset now)
    {
        // A success is also what clears a slot's back-off counter: an account that worked is not
        // three refusals into an exponential wait.
        slots.MarkSucceeded(slot);

        return JobTransitions.Succeed(job, attempt.Answer, attempt.TokensIn, attempt.TokensOut, now);
    }

    /// <summary>
    /// The account said no. Park it, and give the job back to the queue if another one could serve it.
    /// </summary>
    /// <remarks>
    /// The first version failed the review outright, which contradicted the whole point of having
    /// more than one account: with a second slot signed in and idle, a caller was told "rate limited"
    /// while an account that could have run it sat there. Now the job returns to Queued and the next
    /// pump picks it up on a different account — and only when NO account can take it does it fail,
    /// carrying the vendor's own sentence. (codex, code round.)
    /// </remarks>
    private JobRecord? RateLimited(
        VendorConfig vendor, AccountSlot slot, JobRecord job, ReviewerOutcome.RateLimited limited, DateTimeOffset now)
    {
        // The vendor's own words decide how long, and CooldownParser reads them. A refusal with no
        // time in it still parks the account, or the next job lands on it a second later.
        slots.MarkCoolingDown(slot, limited.Reason.Length > 0 ? limited.Reason : "rate limited", now);

        // Only an account that has NOT already refused this job. Rotating back onto one that has is
        // how a saturated vendor spends the whole queue window re-asking the same exhausted accounts
        // before failing anyway.
        var refused = new HashSet<string>(job.RefusedBy, StringComparer.Ordinal) { slot.Name };
        var another = SlotSelector.Pick(
            slots.SlotsOf(vendor).Where(s => !refused.Contains(s.Name)).ToList(), now);
        if (another is not null && !JobTransitions.IsExpired(job, now))
        {
            jobs.Requeue(job, slot.Name, $"{slot.Name} is rate-limited; trying {another.Name}");

            return null;
        }

        return JobTransitions.Fail(
            job,
            FailureKind.RateLimited,
            limited.Reason.Length > 0
                ? limited.Reason
                : "every account for this vendor is rate-limited",
            now);
    }
    private void Ledger(VendorConfig vendor, JobRecord job, JobRecord finished)
    {
        if (ledger is null)
        {
            return;
        }

        try
        {
            ledger.RecordJob(
                finished.Email,
                vendor.Id,
                job.Model,
                job.Role,
                finished.Status == JobStatus.Done ? "ok" : finished.Failure.ToString(),
                finished.Elapsed,
                finished.TokensIn,
                finished.TokensOut);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A review that ran and answered must not be reported as failed because its accounting
            // line could not be written. The job's own state is already terminal and correct; what is
            // lost is one row of a usage report, and the log says which.
            _report($"the usage line for job {finished.Id} could not be written", e);
        }
    }

    private static string Tail(string stdErr, int exitCode) =>
        stdErr.Length > 0 ? stdErr.Trim() : $"the vendor exited {exitCode} without saying why";
}
