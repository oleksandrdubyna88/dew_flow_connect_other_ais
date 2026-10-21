namespace CoaiMcp.Runners.Reviewers;

/// <summary>
/// WHY a launch the caller's token killed was killed — the ledger's <c>endCause</c> column, as the Team server spells
/// it (story 3.2 of research/PLAN_every_round_is_counted.md).
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
/// about it. A LOCAL review line carries one too since E3.S2 of todo/PLAN_the_round_counter_tail_E3.md: a local round's clock
/// and the MCP client's own cancellation still reach the executor as one token (<c>RoundEngine</c> links them), so the cause is decided
/// ABOVE it, where the three tokens are held (<see cref="OfLocal"/>), and written on the cancelled turn's last line. Empty is "not said",
/// never a cause — and on a local line it now means the turn was not cancelled.</para>
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

    /// <summary>
    /// The local round's own deadline ended it — the engine's clock fired and neither the caller nor the host had. A LOCAL word: the
    /// Team server never writes it (its backstop is <see cref="Backstop"/>).
    /// </summary>
    public const string RoundClock = "roundClock";

    /// <summary>
    /// A cancellation whose token nobody above the executor can name — a Team-server row whose server ended it, any token linked
    /// elsewhere. A LOCAL word, and the answer for a cancelled turn when none of the three tokens fired: never empty, never guessed.
    /// </summary>
    public const string Unknown = "unknown";

    /// <summary>Every cause this build names, in the order a job meets them.</summary>
    public static IReadOnlyList<string> Known { get; } = [Withdrawn, Abandoned, Backstop, QueueExpired, HostStopped, RoundClock, Unknown];

    /// <summary>The words only a LOCAL line carries — the shape of <c>UsageKinds.LocalOnly</c>. The server writes neither and is unchanged.</summary>
    public static IReadOnlyList<string> LocalOnly { get; } = [RoundClock, Unknown];

    /// <summary>
    /// The cause of a LOCAL cancellation, from which token fired — never from a sentence (E3.S2 of todo/PLAN_the_round_counter_tail_E3.md,
    /// decision D5): <c>hostStopped</c> over <c>withdrawn</c> over <c>roundClock</c> over <c>unknown</c>.
    /// </summary>
    /// <remarks>
    /// A stopping host may cancel the request it was serving too, so the host's stop must not be filed as a person's withdrawal; a person
    /// who cancels as the clock fires is a person — the rule the engine's <c>WhatEndedIt</c> already states for the deadline sentence.
    /// </remarks>
    /// <param name="hostStopped">The server's own stopping token had fired.</param>
    /// <param name="callerCancelled">The MCP client's token for the call had fired.</param>
    /// <param name="clockFired">The round's own deadline timer had fired.</param>
    public static string OfLocal(bool hostStopped, bool callerCancelled, bool clockFired) => (hostStopped, callerCancelled, clockFired) switch
    {
        (true, _, _) => HostStopped,
        (_, true, _) => Withdrawn,
        (_, _, true) => RoundClock,
        _ => Unknown,
    };
}
