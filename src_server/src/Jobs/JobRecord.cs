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

    public static JobRecord Fail(JobRecord job, FailureKind kind, string reason, DateTimeOffset nowUtc) =>
        job with
        {
            Status = JobStatus.Failed,
            Failure = kind,
            Reason = reason,
            FinishedUtc = nowUtc,
        };

    /// <summary>Has this job run out of the clock that currently applies to it?</summary>
    /// <remarks>
    /// Which clock depends on the state, and conflating them is the defect the plan round caught: a
    /// queued job is judged on how long it has WAITED, a running one on how long it has RUN.
    /// </remarks>
    public static bool IsExpired(JobRecord job, DateTimeOffset nowUtc) => job.Status switch
    {
        JobStatus.Queued => nowUtc >= job.QueueDeadlineUtc,
        JobStatus.Running => job.RunDeadlineUtc is { } deadline && nowUtc >= deadline,
        _ => false,
    };

    /// <summary>The sentence for a job that ran out of time, saying WHICH clock ran out.</summary>
    public static string ExpiryReason(JobRecord job) => job.Status == JobStatus.Queued
        ? $"expired in the queue after {(job.QueueDeadlineUtc - job.SubmittedUtc).TotalMinutes:0} minutes "
            + "without a free account — nothing was sent to the vendor"
        : $"the vendor was still running {job.RunBudget.TotalSeconds:0}s after it started and was stopped";
}
