using CoaiMcp.Core.Findings;
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
/// <param name="gauge">
/// The vendor's gauge read after each job, INSIDE the slot's lease (story 6.1a of todo/PLAN_every_round_is_counted.md) —
/// <see cref="NoGaugeCapture"/> by default, which is what every test of something else gets.
/// </param>
public sealed class JobRunner(
    JobStore jobs,
    VendorCatalogHost catalog,
    SlotRegistry slots,
    IReviewLauncher launcher,
    UsageLedger? ledger = null,
    Action<string, Exception>? onFailure = null,
    IGaugeCapture? gauge = null)
{
    /// <summary>A slot that refused with no time named is parked this long.</summary>
    /// <remarks>
    /// A circuit breaker rather than a guess at the vendor's window: it stops the NEXT job landing on
    /// the account that just refused, which without it would fail in the same way a second later.
    /// </remarks>
    public static readonly TimeSpan TransientCooldown = TimeSpan.FromMinutes(5);

    private readonly Action<string, Exception> _report = onFailure ?? ((_, _) => { });

    private readonly IGaugeCapture _gauge = gauge ?? NoGaugeCapture.Instance;

    /// <summary>Take one job for this vendor, if there is one that can run. False when idle.</summary>
    /// <param name="started">
    /// Signalled as soon as it is known whether a job was CLAIMED — before the review itself runs.
    /// The pump needs that answer to decide whether to try again, and waiting for the review would
    /// mean one start per ten minutes rather than one per free account.
    /// </param>
    /// <remarks>
    /// <para><b>The promise is kept on EVERY exit, and that is what the <c>finally</c> is for.</b>
    /// It used to be answered at each of the four ordinary returns and nowhere else — and between the
    /// first of them and the last sit three calls that touch the filesystem: reading every slot's
    /// <c>state.json</c>, creating the account directory, chmod-ing it, opening <c>.lock</c>. A throw
    /// from any of them left the pump waiting on a promise nobody would ever keep, which stopped that
    /// tick for good: no later vendor pumped, nothing ever expired again, and <c>/api/health</c> still
    /// answering while the server accepted reviews and ran none. Product audit of 2026-09-09,
    /// finding 6; reproduced with a directory where the lock file goes.</para>
    /// <para>After a <c>TrySetResult(true)</c> the <c>finally</c> is a no-op, so the contract above is
    /// unchanged: the pump still hears "claimed" before the review runs rather than after it. And the
    /// exception still travels — the caller's continuation is what logs it.</para>
    /// </remarks>
    public async Task<bool> PumpAsync(
        string vendorId, CancellationToken ct, TaskCompletionSource<bool>? started = null)
    {
        try
        {
            return await ClaimAndRunAsync(vendorId, started, ct);
        }
        finally
        {
            started?.TrySetResult(false);
        }
    }

    private async Task<bool> ClaimAndRunAsync(
        string vendorId, TaskCompletionSource<bool>? started, CancellationToken ct)
    {
        if (catalog.Current.Find(vendorId) is not { } vendor)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        if (SlotSelector.Pick(slots.SlotsOf(vendor), now) is not { } candidate)
        {
            return false;
        }

        // The lease FIRST, then the claim: taking the job before the account would leave a Running
        // job with nowhere to run if another process held the slot. Busy and blocked alike are "not now":
        // a blocked account is excluded by the selector, and reconciled again on its next acquisition.
        if (await slots.AcquireAsync(candidate, TimeSpan.Zero, ct: ct) is not Acquisition.Taken taken)
        {
            return false;
        }

        using var lease = taken.Lease;

        // Claimed under the store's own lock, so a DELETE that answered 204 cannot be overtaken by
        // this dispatch. The token comes back with it: it is the one a cancel or the deadline sweep
        // fires, and it is what actually stops the vendor process.
        var claim = jobs.TryClaim(vendorId, candidate.Name, DateTimeOffset.UtcNow, ct);
        RefusedForBudget(vendor, claim.Refused);
        if (claim.Job is not { } job)
        {
            return false;
        }

        started?.TrySetResult(true);
        // The host's token for what runs AFTER the job — the gauge read inside this same lease — never the job's: a
        // withdrawn job is no reason to skip the slot's own reading, and a stopping host is.
        await RunAsync(vendor, candidate, job, claim.Token, ct);

        return true;
    }

    /// <summary>Nothing launched and nothing ran: the attempt a refused job's one ledger line is written from.</summary>
    private static readonly ReviewAttempt NothingLaunched =
        new ReviewAttempt.Failed(new ReviewerOutcome.NotStarted("ended at claim: its caller could no longer wait for a launch"));

    /// <summary>
    /// The refusal is counted (story 4.3 of todo/PLAN_every_round_is_counted.md): one line per job the claim pass ended
    /// for want of budget — outcome <c>NoBudget</c>, an honest zero with no usage note, no seconds, no account, the job's
    /// kind and round kept — so the effect of the rule can be seen in the same ledger as what it saves.
    /// </summary>
    /// <remarks>
    /// Written from the record the STORE ended, as every line is; the attempt is <see cref="NothingLaunched"/>, so the
    /// line carries no launch options — nothing ran with anything — and no slot: no account carried it.
    /// </remarks>
    private void RefusedForBudget(VendorConfig vendor, IReadOnlyList<JobRecord> ended)
    {
        foreach (var job in ended)
        {
            Ledger(new(vendor, string.Empty, job, NothingLaunched), job, job.RefusedBy.Count == 0 ? LaunchAttempts.First : LaunchAttempts.Rotated, GaugeOnLine.None);
        }
    }

    private async Task RunAsync(VendorConfig vendor, AccountSlot slot, JobRecord job, CancellationToken jobToken, CancellationToken hostToken)
    {
        try
        {
            var environment = SlotEnvironment.For(vendor.Runtime, slot.Directory, slots.TokenFor(slot, vendor.Runtime));
            var attempt = await launcher.RunAsync(vendor, slot, job, environment, jobToken);
            await RecordAsync(vendor, slot, job, attempt, hostToken);
        }
        catch (OperationCanceledException)
        {
            // The THROWN shape of a cancellation — a launcher that throws rather than reporting
            // `ProcessResult.Cancelled` — recorded exactly as the returned shape is: one road, so a cancel,
            // an expiry and a host stop each write their one ledger line here too (story 3.2 of
            // todo/PLAN_every_round_is_counted.md; this arm used to finish the job and write nothing).
            // The store already wrote the terminal state and its cause when it fired the token, and `Finish`
            // hands that record back; the runner's own `hostStopped` stands only when nothing else ended it.
            await RecordAsync(vendor, slot, job, new ReviewAttempt.Failed(new ReviewerOutcome.Cancelled()), hostToken);
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

    /// <summary>
    /// The job's ending, then the slot's gauge, then the lines — in that order, all inside the lease.
    /// </summary>
    /// <remarks>
    /// The job is made terminal in the store FIRST, so a caller polling it is answered before the reader runs and
    /// nothing the reader does can change what it is told; the gauge is read AFTER the job, on the account the job
    /// just used, while this lease still excludes every other user of its token set; and the ledger line is written
    /// LAST, carrying the reading (story 6.1a of todo/PLAN_every_round_is_counted.md). A capture that produced nothing
    /// writes a line that says nothing about the gauge — never 0 % — and the job's result is what the store already
    /// holds. A requeued attempt is not the job's end: its line is written at once and no gauge is read.
    /// </remarks>
    private async Task RecordAsync(VendorConfig vendor, AccountSlot slot, JobRecord job, ReviewAttempt attempt, CancellationToken hostToken)
    {
        if (Ended(vendor, slot, job, attempt) is not { } finished)
        {
            return;
        }

        var kept = jobs.Finish(finished);
        var captured = hostToken.IsCancellationRequested
            ? new GaugeCaptured.Skipped(GaugeCodes.NoAnswer)
            : await _gauge.CaptureAsync(vendor, slot, hostToken);
        Ledger(new(vendor, slot.Name, job, attempt), kept, job.RefusedBy.Count == 0 ? LaunchAttempts.First : LaunchAttempts.Rotated, captured.OnLine);
    }

    /// <summary>The job's terminal record for this attempt — or null when the attempt only sent the job back to the queue.</summary>
    private JobRecord? Ended(VendorConfig vendor, AccountSlot slot, JobRecord job, ReviewAttempt attempt)
    {
        var now = DateTimeOffset.UtcNow;

        // The success arm is FIRST, and it is a shape of its own rather than a `_` fallthrough. As a
        // fallthrough it was unreachable for the whole life of this server — the launcher could not
        // build the outcome it was matching on — and an arm nothing can reach reads exactly like an
        // arm that works.
        JobRecord? finished = attempt switch
        {
            ReviewAttempt.Answered answered => Succeeded(slot, job, answered, now),
            ReviewAttempt.Failed { Outcome: ReviewerOutcome.RateLimited limited } =>
                RateLimited(vendor, slot, job, limited, now),
            // The usage travels with EVERY failure — whatever the launch reported, the attempt carries
            // (story 3.1 of todo/PLAN_every_round_is_counted.md). It used to travel with Unparseable alone,
            // so a vendor that crashed after it was billed was written as one that cost nothing.
            ReviewAttempt.Failed failed => Failed(job, failed, now),
            _ => JobTransitions.Fail(
                job, FailureKind.NotStarted, "the executor returned a verdict this server cannot read", now),
        };

        if (finished is null)
        {
            // Requeued onto another account. The JOB is not over, but this attempt is: it ran on an account and was
            // refused there, so it is written as a launch of its own — `RateLimited`, `rotated` — and the attempt that
            // finally runs the job is another line of the same turn. It used to write nothing, on the reasoning that a
            // line per refused attempt would count one review several times; a REVIEW is counted by its turn (the job's
            // id, on every line), and what one line counts now is one launch (story 3.3 of
            // todo/PLAN_every_round_is_counted.md — row 10 of the paid-path census).
            Ledger(new(vendor, slot.Name, job, attempt), Refused(job, attempt, now), LaunchAttempts.Rotated, GaugeOnLine.None);
        }

        // The ledger line is written from the record the STORE kept, not from this method's own `finished`: a cancel or
        // an expiry that fired the token wrote its cause first, under the store's lock, and `Finish` hands that back —
        // carrying what the attempt REPORTED, because a vendor that answered a second before the withdrawal was billed
        // for the answer nobody collected (story 3.2 of todo/PLAN_every_round_is_counted.md).
        return finished;
    }

    /// <summary>
    /// What every line of one attempt shares: the vendor, the NAME of the account it ran on — empty for an attempt that ran
    /// on none (a refusal at claim) — the job, and the attempt itself.
    /// </summary>
    private sealed record AttemptOn(VendorConfig Vendor, string Slot, JobRecord Job, ReviewAttempt Attempt);

    /// <summary>
    /// The refused attempt as a terminal record of its own, for its ledger line only — never stored: the job itself went
    /// back to the queue. Its seconds run from its claim to now, and its usage is what the refused launch reported —
    /// usually nothing, which reads as 0/0 with whatever note the usage carries.
    /// </summary>
    private static JobRecord Refused(JobRecord job, ReviewAttempt attempt, DateTimeOffset now) =>
        JobTransitions.Fail(job, FailureKind.RateLimited, "requeued", now, attempt is ReviewAttempt.Failed failed ? failed.Usage : Usage.None);

    private JobRecord Succeeded(
        AccountSlot slot, JobRecord job, ReviewAttempt.Answered answered, DateTimeOffset now)
    {
        // A success is also what clears a slot's back-off counter: an account that worked is not
        // three refusals into an exponential wait.
        slots.MarkSucceeded(slot);

        return JobTransitions.Succeed(job, answered.Raw, answered.Usage, now);
    }

    /// <summary>The runner's own sentence for a launch the token killed when no store transition said why.</summary>
    private const string StoppedReason = "stopped before it finished";

    /// <summary>A failure the vendor decided — or a launch the caller's token killed — with what its launches reported beside it.</summary>
    /// <remarks>
    /// A cancelled launch is the one ending the runner does not name by itself: the store that fired the token wrote
    /// WHY (withdrawn, abandoned, the backstop) on its own record first, and <c>JobStore.Finish</c> hands that record
    /// back over this one. <c>hostStopped</c> is this record's word for the case nothing else ended — the host's own
    /// token — and it stands only then (story 3.2 of todo/PLAN_every_round_is_counted.md).
    /// </remarks>
    private static JobRecord Failed(JobRecord job, ReviewAttempt.Failed failed, DateTimeOffset now)
    {
        if (failed.Outcome is ReviewerOutcome.Cancelled)
        {
            return JobTransitions.Cancel(job, EndCauses.HostStopped, StoppedReason, now, failed.Usage);
        }

        var (kind, reason) = WhyItFailed(job, failed.Outcome);

        return JobTransitions.Fail(job, kind, reason, now, failed.Usage);
    }

    private static (FailureKind Kind, string Reason) WhyItFailed(JobRecord job, ReviewerOutcome outcome) => outcome switch
    {
        ReviewerOutcome.NonZeroExit exit => (FailureKind.NonZeroExit, Tail(exit.StdErrTail, exit.ExitCode)),
        // Its own sentence rather than the expiry one. This is the reviewer's OWN timeout firing,
        // which is the case the server's backstop exists to be later than — and since abandonment
        // joined the expiry reasons, borrowing that sentence could have described a job nobody
        // was polling as one the vendor was too slow for. Two different events, two sentences.
        ReviewerOutcome.TimedOut =>
            (FailureKind.TimedOut, $"the vendor did not answer within the {job.RunBudget.TotalSeconds:0}s it was given"),
        ReviewerOutcome.Unparseable bad => (FailureKind.UnparseableByVendor, bad.Reason),
        ReviewerOutcome.NotStarted missing => (FailureKind.NotStarted, missing.Reason),
        _ => (FailureKind.NotStarted, "the executor returned a verdict this server cannot read"),
    };

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
    /// <summary>
    /// The job's lines: one per launch of the attempt that ended it — a follow-up is its own — every one carrying the
    /// job's id as its turn and saying which attempt it was (story 3.3 of todo/PLAN_every_round_is_counted.md).
    /// </summary>
    /// <param name="firstAttempt">
    /// What the attempt's first launch is called: <c>first</c> when this account was the only one the job met, else
    /// <c>rotated</c> — on a refused attempt and on the one that finally ran a job other accounts had refused.
    /// </param>
    /// <remarks>
    /// <para>The job id is the turn on this server, and deliberately: a job IS one reviewer turn — the client's whole request,
    /// whichever accounts it took and whatever its adapter continued — and the id is already unique, already in the
    /// server's log beside the job, and what a person asking "what happened to this review" holds.</para>
    /// <para>Every line also says what its launch ran with, which job it was and which account SLOT carried it — by the
    /// slot's name from <c>vendors.json</c>, which <c>VendorConfig.Refusal</c> holds to letters, digits, <c>-</c> and
    /// <c>_</c>; the account's own sign-in is never named (story 3.4 of todo/PLAN_every_round_is_counted.md).</para>
    /// </remarks>
    /// <param name="gauge">
    /// The vendor's gauge as read on the account right after this job (story 6.1a), on every line of the job —
    /// <see cref="GaugeOnLine.None"/> when nothing was read, which the line then says by saying nothing.
    /// </param>
    private void Ledger(AttemptOn on, JobRecord finished, string firstAttempt, GaugeOnLine gauge)
    {
        if (ledger is null)
        {
            return;
        }

        try
        {
            // The whole-Usage overload: cached and reasoning tokens, a missing price and a usage nobody
            // captured are written as a reviewer's local line writes them (story 3.1).
            foreach (var line in LaunchLines.Of(on.Attempt.Launches, finished.Usage, finished.Elapsed, OutcomeOf(finished), firstAttempt))
            {
                ledger.RecordJob(
                    finished.Email, on.Vendor.Id, on.Job.Model, on.Job.Role, line.Outcome, line.Elapsed, line.Usage,
                    kind: JobKinds.Wire(on.Job.Kind), stage: "TeamServer", turn: on.Job.Id, attempt: line.Attempt,
                    ranWith: on.Attempt.RanWith, job: on.Job.Id, slot: on.Slot, gauge: gauge,
                    // WHY the store killed it, on the line that says Cancelled — a follow-up pair's first launch ended on
                    // its own and says so (story 3.2 of todo/PLAN_every_round_is_counted.md).
                    endCause: line.Outcome == nameof(FailureKind.Cancelled) ? finished.EndCause : string.Empty,
                    // What the REQUEST carried, on every line of the job — a requeued launch's included — so a round is
                    // counted once however many launches it took; the client's word beside the server's own attempt,
                    // never over it (story 4.2 of todo/PLAN_every_round_is_counted.md).
                    round: on.Job.Round, clientAttempt: on.Job.ClientAttempt);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A review that ran and answered must not be reported as failed because its accounting
            // line could not be written. The job's own state is already terminal and correct; what is
            // lost is one row of a usage report, and the log says which.
            _report($"the usage line for job {finished.Id} could not be written", e);
        }
    }

    /// <summary>The ledger's word for how a job ended: <c>ok</c>, or its failure kind's name.</summary>
    private static string OutcomeOf(JobRecord finished) =>
        finished.Status == JobStatus.Done ? LaunchLines.Answered : finished.Failure.ToString();

    private static string Tail(string stdErr, int exitCode) =>
        stdErr.Length > 0 ? stdErr.Trim() : $"the vendor exited {exitCode} without saying why";
}
