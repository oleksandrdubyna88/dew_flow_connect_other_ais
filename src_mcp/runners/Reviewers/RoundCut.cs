namespace CoaiMcp.Runners.Reviewers;

/// <summary>
/// What the scheduler asks to tell a turn the ROUND'S token ended from one that ended on its own terms — the mark behind
/// <see cref="ReviewerOutcome.EndedByRoundToken"/> (D2 and §10.2 of todo/PLAN_a_cancelled_review_stops_its_round*.md).
/// </summary>
/// <remarks>
/// The criterion is never the TIMING of a report: it is whether the round's token had fired when the ending was OBSERVED. An exception
/// is the one ending that needs more — an adapter can throw its own cancellation (a Team-server client's HTTP timeout) a moment before the
/// caller cancels, and the throw reaches the scheduler after the cancel — so the moment the executor first saw the exception travels with
/// it (<see cref="LaunchCancelledException"/>) and is compared with the moment the round's first cancellation source fired.
/// </remarks>
/// <param name="Token">The round's token — the one <c>RunAllAsync</c> was handed.</param>
/// <param name="FiredAt">
/// The moment (a <see cref="System.Diagnostics.Stopwatch.GetTimestamp"/> value) the round's first cancellation source fired, or null while
/// none has. Null as a delegate (a scheduler driven without a round, as most tests do) falls back to the token alone.
/// </param>
internal readonly record struct RoundCut(CancellationToken Token, Func<long?>? FiredAt = null)
{
    /// <summary>
    /// A turn the launcher reported cancelled (shape 1): marked when the round's token had fired, else a cancellation nobody here can name
    /// — a remote row the server ended — which stays unmarked.
    /// </summary>
    public ReviewerOutcome WhenKilled(ReviewerOutcome outcome) =>
        outcome is ReviewerOutcome.Cancelled && Token.IsCancellationRequested ? outcome with { EndedByRoundToken = true } : outcome;

    /// <summary>A launch that threw (shape 2): whether the ROUND'S token is what ended it. See <see cref="Ended"/>.</summary>
    public bool Ended(OperationCanceledException thrown) =>
        Ended(thrown.CancellationToken, Token, FiredAt?.Invoke(), thrown is LaunchCancelledException seen ? seen.ObservedAt : System.Diagnostics.Stopwatch.GetTimestamp());

    /// <summary>
    /// The rule, pure: the exception carried the round's own token, or the round's token had fired by the time the exception was first seen —
    /// an adapter's cancellation thrown BEFORE the round's first source fired is its own and is not a cut.
    /// </summary>
    internal static bool Ended(CancellationToken thrownWith, CancellationToken round, long? firedAt, long observedAt) =>
        (round.CanBeCanceled && thrownWith == round) || (round.IsCancellationRequested && (firedAt is not { } at || at <= observedAt));
}

/// <summary>
/// An <see cref="OperationCanceledException"/> that remembers WHEN the executor first saw it — before the abandon courtesy that can
/// take up to ten seconds — so the scheduler can tell an adapter's own cancellation from the round's (§10.2).
/// </summary>
public sealed class LaunchCancelledException(OperationCanceledException inner, long observedAt)
    : OperationCanceledException(inner.Message, inner, inner.CancellationToken)
{
    /// <summary>A <see cref="System.Diagnostics.Stopwatch.GetTimestamp"/> value.</summary>
    public long ObservedAt { get; } = observedAt;
}

/// <summary>What a scheduler run came to: the outcomes, and the moment the scheduler returned them.</summary>
/// <param name="Results">The outcomes, in the work's own order.</param>
/// <param name="ReturnedAt">
/// A <see cref="System.Diagnostics.Stopwatch.GetTimestamp"/> value stamped INSIDE the scheduler as its last act — the one moment the engine
/// compares the round's recorded first cause against, so a cancel that fires after the scheduler returned cannot withdraw the round (§10.3).
/// </param>
public sealed record SchedulerRun(IReadOnlyList<(ReviewerInvocation Invocation, ReviewerOutcome Outcome)> Results, long ReturnedAt);
