namespace CoaiMcp.Runners.Reviewers;

/// <summary>
/// WHY a launch the caller's token killed was killed — the ledger's <c>endCause</c> column, as the Team server spells
/// it (story 3.2 of todo/PLAN_every_round_is_counted.md).
/// </summary>
/// <remarks>
/// <para>A <see cref="ReviewerOutcome.Cancelled"/> launch cannot say why its token fired: the token does not carry a
/// reason, and the executor is below the store that fired it. The Team server's <c>JobStore</c> is the one place that
/// knows — it fires the token AT the transition — so it writes the cause on the job's record there, and the ledger
/// reads it from the record the store kept, never from the runner's own recomputation. Until this story
/// <c>JobStore.Cancel</c> and <c>JobStore.Expire</c> both stored <c>FailureKind.Cancelled</c>, told apart only by a
/// sentence; a measurement over the ledger could not count withdrawals against backstops at all.</para>
/// <para>Here, in the library both halves compile against, for <see cref="UsageKinds"/>' and
/// <see cref="LaunchAttempts"/>' reason: one spelling of a ledger column, so a writer and a reader cannot disagree
/// about it. Only the server writes one today; a local line's <c>endCause</c> is empty, because a local round's clock
/// and the MCP client's own cancellation reach the executor as one token (<c>RoundEngine</c> links them) and nothing
/// above it records which fired. Empty is "not said", never a cause.</para>
/// </remarks>
public static class EndCauses
{
    /// <summary>The caller withdrew it — a <c>DELETE /api/reviews/{id}</c>, the client's own stop.</summary>
    public const string Withdrawn = "withdrawn";

    /// <summary>Nobody asked about it for <c>JobTransitions.AbandonedAfter</c>, so the server dropped it and freed its account.</summary>
    public const string Abandoned = "abandoned";

    /// <summary>
    /// The server's own run deadline — the job's budget plus <c>JobTransitions.RunSlack</c> — ended it. The reviewer's own
    /// timeout should fire first and say <c>TimedOut</c>; this is the backstop for when it did not.
    /// </summary>
    public const string Backstop = "backstop";

    /// <summary>
    /// It waited its whole queue window for a free account and never launched. A record's cause, never a ledger
    /// line's: nothing was sent to a vendor, so there is nothing to write down as spent.
    /// </summary>
    public const string QueueExpired = "queueExpired";

    /// <summary>
    /// The server stopped while the job ran: the host's token fired, and no store transition said anything else. The
    /// runner's own word, used only when the store kept no cause of its own.
    /// </summary>
    public const string HostStopped = "hostStopped";

    /// <summary>Every cause this build names, in the order a job meets them.</summary>
    public static IReadOnlyList<string> Known { get; } = [Withdrawn, Abandoned, Backstop, QueueExpired, HostStopped];
}
