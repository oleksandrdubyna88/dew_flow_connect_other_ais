using System.Collections.Immutable;
using CoaiMcp.Core.Feature;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Runners.Feature;

/// <summary>
/// Whether a round's feature reviewers get follow-up turns for their source requests, and with what.
/// </summary>
/// <remarks>
/// A closed union rather than a nullable resolver, which is how every other two-state thing here is
/// spelled: <see cref="Off"/> is every stage but the feature stage — and the feature stage under
/// <c>COAI_FEATURE_SOURCE_FOLLOWUPS=0</c>, where the requests are recorded and no turn answers them;
/// <see cref="On"/> carries the round's ONE resolver, built for its pinned head and shared by every
/// reviewer of it, and the follow-up cap.
/// </remarks>
public abstract record SourceTurns
{
    public sealed record Off : SourceTurns;

    /// <param name="Resolver">Built once per round, for the round's head; the spend is per reviewer and travels on the conversation.</param>
    /// <param name="FollowUps">How many follow-up turns a reviewer may have; 0 is <see cref="Off"/> in effect.</param>
    public sealed record On(SourceResolver Resolver, int FollowUps) : SourceTurns;

    public static SourceTurns None { get; } = new Off();

    private SourceTurns() { }
}

/// <summary>
/// One feature reviewer's conversation: after each answered turn, serve what it asked for and hand the
/// scheduler the next turn — the base prompt byte for byte, the tail appended (plan §4.9, D25; S3.2).
/// </summary>
/// <remarks>
/// <para><b>Immutable across turns.</b> Each turn's work carries a NEW conversation with the state
/// advanced — the reviewer's spend, every slice served so far, whether the last tail said FINAL — so a
/// turn that was cancelled or repaired holds nothing half-updated, and two reviewers of one round never
/// share a mutable spend: the resolver is shared, the allowance is not (§4.9, "spend per reviewer per
/// round").</para>
/// <para><b>When it stops.</b> No request in the answer; the follow-up cap (the tail of the last allowed
/// turn said FINAL, and a FINAL turn's requests are ignored); or the source budget spent
/// (<see cref="ServedTurn.Exhausted"/> — the turn that was told so is the final one). A request that
/// cannot be served — a credential file, a symbol the file does not declare, git timing out — is a
/// refusal LINE in the next prompt, never an exception: the turn goes on with what was served.</para>
/// <para><b>What it does not know.</b> Vendors, prompts and directories: the caller hands in
/// <c>buildTurn</c>, which turns a tail into a whole <see cref="ReviewerWork"/> — the launch with
/// <c>base + tail</c>, the repair with <c>base + tail + </c><see cref="RepairInstruction.Text"/> — so the
/// repair of turn N is composed from turn N's own prompt.</para>
/// </remarks>
public sealed class SourceConversation : IReviewerContinuation
{
    private readonly SourceTurns.On _turns;
    private readonly Func<string, ReviewerWork> _buildTurn;
    private readonly SourceSpend _spent;
    private readonly ImmutableList<ServedSlice> _servedSoFar;
    private readonly bool _saidFinal;

    /// <param name="turns">The round's resolver and the follow-up cap.</param>
    /// <param name="buildTurn">A tail → the next turn's whole work; the caller composes <c>base + tail</c> for the launch and its repair.</param>
    public SourceConversation(SourceTurns.On turns, Func<string, ReviewerWork> buildTurn)
        : this(turns, buildTurn, SourceSpend.None, [], saidFinal: false)
    {
    }

    private SourceConversation(
        SourceTurns.On turns,
        Func<string, ReviewerWork> buildTurn,
        SourceSpend spent,
        ImmutableList<ServedSlice> servedSoFar,
        bool saidFinal)
    {
        _turns = turns;
        _buildTurn = buildTurn;
        _spent = spent;
        _servedSoFar = servedSoFar;
        _saidFinal = saidFinal;
    }

    public int FollowUps => _turns.FollowUps;

    /// <summary>One plus the follow-ups: the most turns the conversation may have.</summary>
    public int Turns => 1 + _turns.FollowUps;

    public async Task<TurnDecision> AfterAsync(int turn, ReviewerOutcome.Ok answered, CancellationToken ct)
    {
        var review = answered.Review;
        if (WhyNoNextTurn(turn, review) is { Length: > 0 } why)
        {
            return new TurnDecision.Stop(why);
        }

        var served = await _turns.Resolver.ServeAsync(review.SourceRequests, _spent, ct);
        var next = turn + 1;
        var final = next >= Turns || served.Exhausted;
        var tail = TurnTail.Render(new TurnTailInput(
            next, Turns, review.Findings, review.SourceRequests, review.RejectedSourceRequests, _servedSoFar, served, final));
        var work = _buildTurn(tail) with
        {
            Continue = new SourceConversation(_turns, _buildTurn, served.Spent, _servedSoFar.AddRange(served.Served), final),
        };

        return new TurnDecision.Next(work, $"turn {next}: {Summary(served)}");
    }

    /// <summary>The three stops, in the order they are asked — or empty when the conversation goes on.</summary>
    private string WhyNoNextTurn(int turn, Core.Findings.NormalisedReview review) =>
        _saidFinal ? "the last turn was final"
        : review.SourceRequests.IsEmpty && review.RejectedSourceRequests.IsEmpty ? "no source was asked for"
        : turn >= Turns ? $"the follow-up cap ({FollowUps}) was reached"
        : string.Empty;

    private static string Summary(ServedTurn served) =>
        served.IsEmpty ? "nothing to serve" : served.Summary();
}
