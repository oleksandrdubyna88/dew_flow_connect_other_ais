namespace CoaiServer;

/// <summary>
/// The two numbers the budget rule is configured by — story 4.3 of <c>todo/PLAN_every_round_is_counted.md</c>.
/// </summary>
/// <param name="SafetyMargin">
/// <c>Coai:DeadlineSafetyMarginSeconds</c>: taken off every caller's stated budget at receipt. It is the TRANSIT allowance
/// as well as the safety margin — the server anchors <c>remainingSeconds</c> at receipt and cannot see how long the
/// request travelled (a client timestamp would bring clock skew back), so a request delayed in transit by MORE than this
/// can still be claimed after its caller gave up, and is then withdrawn by the caller's own DELETE. A stated limit.
/// </param>
/// <param name="MinStart">
/// <c>Coai:MinStartSeconds</c>: the least a launch's start-up needs. A job is ended at claim when its caller's remaining
/// time, after the margin, cannot hold even that — NOT a "least useful run": a 60 s reviewer's shim advertises at most
/// 50 s before any margin, and a floor of a minute would refuse every healthy short review (the risk consultation for
/// epic 4).
/// </param>
public sealed record ClientBudgetPolicy(TimeSpan SafetyMargin, TimeSpan MinStart)
{
    public const int DefaultSafetyMarginSeconds = 5;

    public const int DefaultMinStartSeconds = 10;

    public static ClientBudgetPolicy Default { get; } =
        new(TimeSpan.FromSeconds(DefaultSafetyMarginSeconds), TimeSpan.FromSeconds(DefaultMinStartSeconds));

    /// <summary>The policy from configuration — refused at boot, like <c>Coai:SessionTtlDays</c>, when a number could never be right.</summary>
    public static ClientBudgetPolicy From(int safetyMarginSeconds, int minStartSeconds)
    {
        if (safetyMarginSeconds < 0)
        {
            throw new InvalidOperationException(
                $"Coai:DeadlineSafetyMarginSeconds is {safetyMarginSeconds}; it is the seconds taken off every caller's stated "
                + "budget for transit and clock slack, and a negative one would credit a caller with time it never had. "
                + $"Set it to 0 or more (the default is {DefaultSafetyMarginSeconds}).");
        }

        if (minStartSeconds <= 0)
        {
            throw new InvalidOperationException(
                $"Coai:MinStartSeconds is {minStartSeconds}; it is the least a launch's start-up needs, and at 0 or less a job "
                + "whose caller has already gone would still be started on the company's account. Set it to 1 or more "
                + $"(the default is {DefaultMinStartSeconds}).");
        }

        return new(TimeSpan.FromSeconds(safetyMarginSeconds), TimeSpan.FromSeconds(minStartSeconds));
    }
}

/// <summary>
/// No start without budget — the pure half of story 4.3: when a caller's stated budget ends on THIS server's clock, whether
/// a job can still be started, what it may run for, and the sentence a refusal carries.
/// </summary>
/// <remarks>
/// <para><b>From the server's clock only.</b> The caller sends <c>remainingSeconds</c>, relative; the deadline is the
/// receipt (<see cref="JobRecord.SubmittedUtc"/>, stamped by the endpoint from this process's clock) plus that number,
/// less the margin. No client instant is ever read, so a client clock ten minutes off gets the same deadline as one that
/// is right.</para>
/// <para><b>Capped at the job's own lifetime.</b> A client cannot buy a longer-lived job than one without the field:
/// the stated number is capped at the job's <c>timeoutSeconds</c> plus its queue window (the code round of 2026-10-09).</para>
/// <para><b>Absent is an old client</b> — <see cref="JobRecord.RemainingSeconds"/> of 0 — and gets no deadline at all:
/// the rule never refuses a request that did not state a budget.</para>
/// </remarks>
public static class ClientBudget
{
    /// <summary>When the caller's budget ends on this clock — or null for a request that stated none.</summary>
    public static DateTimeOffset? DeadlineOf(JobRecord job, TimeSpan safetyMargin) =>
        job.RemainingSeconds <= 0 ? null : job.SubmittedUtc + Stated(job) - safetyMargin;

    /// <summary>The stated budget, capped at the job's own lifetime: its vendor budget plus its queue window.</summary>
    private static TimeSpan Stated(JobRecord job)
    {
        var lifetime = (job.QueueDeadlineUtc - job.SubmittedUtc) + job.RunBudget;
        var stated = TimeSpan.FromSeconds(job.RemainingSeconds);

        return stated < lifetime ? stated : lifetime;
    }

    /// <summary>What is left of the caller's budget at <paramref name="nowUtc"/>, or null when it stated none.</summary>
    public static TimeSpan? Left(JobRecord job, DateTimeOffset nowUtc) =>
        job.ClientDeadlineUtc is { } deadline ? deadline - nowUtc : null;

    /// <summary>Whether the caller can still wait for a launch: no budget stated, or at least the start-up allowance left.</summary>
    public static bool CanStart(JobRecord job, DateTimeOffset nowUtc, TimeSpan minStart) =>
        Left(job, nowUtc) is not { } left || left >= minStart;

    /// <summary>The run budget a claimed job gets: its own, capped at what is left before its caller's deadline, never below nought.</summary>
    public static TimeSpan RunBudgetOf(JobRecord job, DateTimeOffset nowUtc) =>
        Left(job, nowUtc) is { } left && left < job.RunBudget ? (left < TimeSpan.Zero ? TimeSpan.Zero : left) : job.RunBudget;

    /// <summary>
    /// The refusal's sentence, interpolating the configured allowance. It names what the caller had and what a launch
    /// needs; a test holds it clear of every rate-limit phrase the client's ladder reads, so the client never waits
    /// out a refusal that cannot clear.
    /// </summary>
    public static string NoBudgetReason(JobRecord job, DateTimeOffset nowUtc, TimeSpan minStart) =>
        $"not started: its caller could wait {Math.Max(0, (Left(job, nowUtc) ?? TimeSpan.Zero).TotalSeconds):0}s more when an "
        + $"account came free, and a launch needs at least {minStart.TotalSeconds:0}s to start — nothing was sent to the vendor";
}
