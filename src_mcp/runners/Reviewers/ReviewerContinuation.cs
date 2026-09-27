using CoaiMcp.Core.Findings;

namespace CoaiMcp.Runners.Reviewers;

/// <summary>
/// The seam a stage plugs a CONVERSATION into: asked after every answered turn whether there is a next
/// one, and handed the whole work for it (plan §4.9, story S3.2).
/// </summary>
/// <remarks>
/// <para>The scheduler owns the loop — the slot held across turns, the ladder per turn, the cap, the
/// one terminal outcome — and knows nothing about source, prompts or vendors. A stage owns what a turn
/// IS: the feature stage serves the reviewer's source requests and composes the next prompt
/// (<c>SourceConversation</c>); every other stage is <see cref="ReviewerContinuation.None"/>.</para>
/// <para>It answers a whole <see cref="ReviewerWork"/> — the launch AND its repair — because the repair
/// of turn N must be composed from turn N's prompt: a malformed turn-2 answer repaired against turn 1's
/// prompt would be asked to fix an answer to a question it was never shown.</para>
/// <para>The seam is what would let the loop be driven from ABOVE the scheduler instead — a caller
/// holding no slot could ask the same question after each launch — without rewriting the turn logic.</para>
/// </remarks>
public interface IReviewerContinuation
{
    /// <summary>The most follow-up turns this conversation may have; zero is a single turn.</summary>
    int FollowUps { get; }

    /// <summary>
    /// After turn <paramref name="turn"/> answered: the next turn's work, or why there is none.
    /// </summary>
    /// <remarks>
    /// May throw the round's cancellation, and nothing else: a failure to serve one request is that
    /// request's refusal, said in the next turn's prompt, never an exception out of the conversation.
    /// </remarks>
    Task<TurnDecision> AfterAsync(int turn, ReviewerOutcome.Ok answered, CancellationToken ct);
}

/// <summary>What a continuation decided: another turn, or the end of the conversation.</summary>
public abstract record TurnDecision
{
    /// <param name="Work">The next turn, whole: launch, repair, prompt id, size — and its own continuation.</param>
    /// <param name="Note">What this turn was served and refused, one line, for the audit and the reviewer's note.</param>
    public sealed record Next(ReviewerWork Work, string Note) : TurnDecision;

    /// <param name="Why">A sentence for the audit: no request, the cap, the budget.</param>
    public sealed record Stop(string Why) : TurnDecision;

    private TurnDecision() { }
}

/// <summary>The continuation every stage but one has: a single turn.</summary>
public static class ReviewerContinuation
{
    public static IReviewerContinuation None { get; } = new SingleTurn();

    private sealed class SingleTurn : IReviewerContinuation
    {
        public int FollowUps => 0;

        public Task<TurnDecision> AfterAsync(int turn, ReviewerOutcome.Ok answered, CancellationToken ct) =>
            Task.FromResult<TurnDecision>(new TurnDecision.Stop("a single-turn reviewer"));
    }
}

/// <summary>
/// One EARLIER turn of a conversation, as it is billed: which turn, what it consumed, how long it ran.
/// </summary>
/// <remarks>
/// Carried on every terminal outcome (<see cref="ReviewerOutcome.EarlierTurns"/>) so a failed or
/// cancelled later turn never makes an answered earlier one look free — the ledger writes one line per
/// turn from these, and the round total adds them. An earlier turn is always one that ANSWERED: only an
/// <c>Ok</c> continues.
/// </remarks>
public sealed record TurnUsage(int Turn, Usage Usage, TimeSpan Elapsed);
