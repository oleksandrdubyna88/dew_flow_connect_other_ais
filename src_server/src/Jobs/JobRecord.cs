namespace CoaiServer;

/// <summary>Where a review is in its life.</summary>
public enum JobStatus
{
    Queued,
    Running,
    Done,
    Failed,
}

/// <summary>Why a job ended badly, in the vocabulary the client already speaks.</summary>
/// <remarks>
/// A pure map off <c>ReviewerOutcome</c> rather than a second vocabulary invented here — the shim
/// and the panel already render these, and a server-only synonym would be a translation table
/// nobody maintains.
/// </remarks>
public enum FailureKind
{
    None,
    NonZeroExit,
    TimedOut,
    UnparseableByVendor,
    RateLimited,
    NotStarted,
    Cancelled,
    Lost,
}

/// <summary>One review, as the server knows it.</summary>
/// <param name="Id">
/// <c>&lt;serverEpoch&gt;-&lt;guid&gt;</c>. The epoch is this process's start time, which is what lets a
/// poll after a restart say <c>lost</c> rather than <c>unknown</c> — see <see cref="JobId"/>.
/// </param>
/// <param name="Email">The only person who may see this job. Ownership is checked on every read.</param>
/// <param name="QueueDeadlineUtc">
/// When this job gives up WAITING. Separate from <paramref name="RunDeadlineUtc"/>, and that
/// separation is the correction the plan round made: a single deadline stamped at submit burns down
/// while the job queues, so with a global concurrency of one the second job of a pair arrives at its
/// slot with almost no execution window — or expires having never run at all. Queueing and running
/// are different clocks.
/// </param>
/// <param name="RunDeadlineUtc">
/// When a RUNNING job is killed. Null until it starts, because it is measured from the start: the
/// caller asked for N seconds of vendor time, not N seconds of wall clock that a queue may eat.
/// </param>
/// <param name="Kind">
/// What this job IS — see <see cref="JobKind"/>. It is what lets a spending report answer "what did
/// the gate cost me" and "what did asking cost me" separately, which one total answers neither of.
/// </param>
/// <param name="LastPolledUtc">
/// When somebody last asked about it. Null means never, and it is then read as
/// <paramref name="SubmittedUtc"/>: a client that posts and never polls is abandoned from the moment
/// it was accepted, not from the moment it would first have been due.
/// </param>
/// <param name="IdempotencyKey">
/// The caller's own name for this ATTEMPT, so a submit whose answer was lost can be repeated without
/// making a second job. Empty when they did not choose one.
/// </param>
/// <param name="Fingerprint">
/// What the key was first used for. A key repeated with a DIFFERENT question is a mistake, not a
/// retry, and returning the first question's job for it would answer something nobody asked.
/// </param>
public sealed record JobRecord(
    string Id,
    string Email,
    string Vendor,
    string Model,
    string Role,
    string Prompt,
    JobStatus Status,
    DateTimeOffset SubmittedUtc,
    DateTimeOffset QueueDeadlineUtc,
    TimeSpan RunBudget,
    JobKind Kind = JobKinds.WhenNotSaid,
    DateTimeOffset? LastPolledUtc = null,
    string IdempotencyKey = "",
    string Fingerprint = "",
    DateTimeOffset? StartedUtc = null,
    DateTimeOffset? RunDeadlineUtc = null,
    DateTimeOffset? FinishedUtc = null,
    string Answer = "",
    string Slot = "",
    FailureKind Failure = FailureKind.None,
    string Reason = "",
    long TokensIn = 0,
    long TokensOut = 0,

    /// <summary>The accounts that have already refused this job with a rate limit.</summary>
    /// <remarks>
    /// Rotation is bounded by this rather than by the queue clock alone. Without it a job whose
    /// vendor is saturated cycles its accounts for the whole ten-minute queue window, re-asking the
    /// same exhausted ones — the review fails either way, and the difference is whether the caller
    /// waits ten minutes to be told. (Two reviewers, second code round.)
    /// </remarks>
    IReadOnlySet<string>? Refused = null)
{
    public bool IsTerminal => Status is JobStatus.Done or JobStatus.Failed;

    /// <summary>The accounts that have refused it, never null.</summary>
    public IReadOnlySet<string> RefusedBy => Refused ?? new HashSet<string>(StringComparer.Ordinal);

    /// <summary>How long the vendor actually ran, or zero while it is still queued.</summary>
    public TimeSpan Elapsed =>
        StartedUtc is { } started ? (FinishedUtc ?? DateTimeOffset.UtcNow) - started : TimeSpan.Zero;

    /// <summary>When somebody last showed an interest, which before the first poll is the submit.</summary>
    public DateTimeOffset HeardFromUtc => LastPolledUtc ?? SubmittedUtc;
}

/// <summary>Every move a job can make, as pure functions.</summary>
/// <remarks>
/// Separate from the store so the rules can be tested exhaustively without a queue, a clock or a
/// filesystem — and so the store has exactly one way to change a job.
/// </remarks>
public static class JobTransitions
{
    /// <summary>Slack added to a caller's timeout before the server kills the process itself.</summary>
    /// <remarks>
    /// The reviewer's own timeout should fire first and produce a proper <c>TimedOut</c> outcome with
    /// whatever the vendor had already said. This deadline is the backstop for when that does not
    /// happen, so it must be strictly later — thirty seconds is enough for a process to be reaped and
    /// its output drained, and small enough that a wedged CLI is not holding an account for minutes.
    /// </remarks>
    public static readonly TimeSpan RunSlack = TimeSpan.FromSeconds(30);

    /// <summary>A queued job that has waited this long gives up, having spent nothing.</summary>
    /// <remarks>
    /// Ten minutes, and it is a QUEUE clock: it bounds how long a caller waits for a free account, not
    /// how long the vendor may take. A client that has gone away — killed, asleep, network gone —
    /// stops costing anything here, which is the whole point: without it an abandoned job wins a slot
    /// ten minutes later and spends the team's subscription on an answer nobody collects.
    /// </remarks>
    public static readonly TimeSpan DefaultQueueWait = TimeSpan.FromMinutes(10);

    /// <summary>How long a QUEUED job nobody has asked about goes on holding its place.</summary>
    /// <remarks>
    /// <para>The client that submitted it can die without saying so — an extension host killed, a
    /// laptop closed — and no promise survives that, which is why the cancel on tab close is
    /// best-effort by construction. Then the job wins an account minutes later and spends the team's
    /// subscription on an answer nobody will ever collect.</para>
    /// <para>Three minutes, and the number is derived rather than chosen: every client here polls in
    /// a loop bounded by <see cref="ReviewEndpoints.MaxWait"/> (25 s) on this side and 8 s on the
    /// extension's, so three minutes is seven missed polls for the chattiest and four for the
    /// quietest. Nothing has been spent on a queued job, so expiring one costs a resubmit and saves a
    /// whole run.</para>
    /// </remarks>
    public static readonly TimeSpan QueuedAbandonedAfter = TimeSpan.FromMinutes(3);

    /// <summary>The same for a RUNNING job, and deliberately far longer.</summary>
    /// <remarks>
    /// <para><b>The two are not the same question, and treating them as one was the plan round's
    /// blocking finding.</b> A queued job has cost nothing, so killing it early is free. A running one
    /// has ALREADY been billed for whatever it has done, so killing it for a network blip destroys
    /// work somebody paid for — and the saving is only the remainder.</para>
    /// <para>Ten minutes, which is still far below the thirty a review may ask for, and well past any
    /// transient disruption a client recovers from. A chat's own run budget is three minutes, so this
    /// never fires on one; where it earns its place is a long review whose caller went away.
    /// (gemini, plan round.)</para>
    /// </remarks>
    public static readonly TimeSpan RunningAbandonedAfter = TimeSpan.FromMinutes(10);

    /// <summary>How long since anybody asked about this job, before it is treated as abandoned.</summary>
    public static TimeSpan AbandonedAfter(JobStatus status) => status == JobStatus.Running
        ? RunningAbandonedAfter
        : QueuedAbandonedAfter;

    /// <summary>Has everybody who cared about this job stopped asking?</summary>
    public static bool IsAbandoned(JobRecord job, DateTimeOffset nowUtc) =>
        !job.IsTerminal && nowUtc - job.HeardFromUtc >= AbandonedAfter(job.Status);

    /// <summary>The job starts on a slot: the run clock begins HERE, not at submit.</summary>
    public static JobRecord Start(JobRecord job, string slot, DateTimeOffset nowUtc) =>
        job with
        {
            Status = JobStatus.Running,
            Slot = slot,
            StartedUtc = nowUtc,
            RunDeadlineUtc = nowUtc + job.RunBudget + RunSlack,
        };

    public static JobRecord Succeed(JobRecord job, string answer, long tokensIn, long tokensOut, DateTimeOffset nowUtc) =>
        job with
        {
            Status = JobStatus.Done,
            Answer = answer,
            TokensIn = tokensIn,
            TokensOut = tokensOut,
            FinishedUtc = nowUtc,
        };

    /// <param name="tokensIn">
    /// What the attempt actually cost, when the failure carries it. A vendor that answered unusably
    /// still BILLED for the call, and leaving these at zero writes a usage line saying the company
    /// spent nothing — which is precisely the vendor somebody would want to find in that report.
    /// Defaulted, because most failures genuinely have no usage to carry: nothing ran.
    /// (CodeRabbit, PR 93.)
    /// </param>
    public static JobRecord Fail(
        JobRecord job,
        FailureKind kind,
        string reason,
        DateTimeOffset nowUtc,
        long tokensIn = 0,
        long tokensOut = 0) =>
        job with
        {
            Status = JobStatus.Failed,
            Failure = kind,
            Reason = reason,
            TokensIn = tokensIn,
            TokensOut = tokensOut,
            FinishedUtc = nowUtc,
        };

    /// <summary>Has this job run out of the clock that currently applies to it?</summary>
    /// <remarks>
    /// Which clock depends on the state, and conflating them is the defect the plan round caught: a
    /// queued job is judged on how long it has WAITED, a running one on how long it has RUN.
    /// </remarks>
    public static bool IsExpired(JobRecord job, DateTimeOffset nowUtc) => job.Status switch
    {
        JobStatus.Queued => nowUtc >= job.QueueDeadlineUtc || IsAbandoned(job, nowUtc),
        JobStatus.Running =>
            (job.RunDeadlineUtc is { } deadline && nowUtc >= deadline) || IsAbandoned(job, nowUtc),
        _ => false,
    };

    /// <summary>
    /// The sentence for a job that ran out of time, saying WHICH clock ran out.
    /// </summary>
    /// <remarks>
    /// Three now, and the third is not a variation on the other two: a job killed because nobody was
    /// listening is a different event from one the vendor was too slow for, and a person chasing
    /// either is led in the opposite direction by the other's words. Abandonment is checked FIRST,
    /// because a job that is both abandoned and out of clock was abandoned earlier — the deadline
    /// merely arrived while nobody was watching.
    /// </remarks>
    public static string ExpiryReason(JobRecord job, DateTimeOffset nowUtc)
    {
        if (IsAbandoned(job, nowUtc))
        {
            // Worded for the person who is READING it, who is by definition asking about it: a
            // sentence beginning "nobody asked about this" is answered to somebody doing exactly
            // that, and reads as the server contradicting them. It says when it gave up instead.
            // (gemini, code round.)
            return $"this review went {AbandonedAfter(job.Status).TotalMinutes:0} minutes with nobody "
                + "asking about it, so it was dropped and its account freed — send it again if you still want it";
        }

        return job.Status == JobStatus.Queued
            ? $"expired in the queue after {(job.QueueDeadlineUtc - job.SubmittedUtc).TotalMinutes:0} minutes "
                + "without a free account — nothing was sent to the vendor"
            : $"the vendor was still running {job.RunBudget.TotalSeconds:0}s after it started and was stopped";
    }
}
