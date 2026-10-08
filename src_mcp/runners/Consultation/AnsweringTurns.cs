using System.Collections.Immutable;
using CoaiMcp.Core.Feature;
using CoaiMcp.Runners.Feature;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Runners.Consultation;

/// <summary>
/// What an <c>api</c> question row is given beside the question (PLAN_question_consultant.md, A9): the
/// outline of the repository at HEAD, and the resolver that serves source from it on request.
/// </summary>
/// <param name="Outline">The rendered outline section, or empty when none was built.</param>
/// <param name="Source"><see cref="SourceTurns.On"/> with the round's resolver and the follow-up cap, or <see cref="SourceTurns.None"/>.</param>
public sealed record QuestionMaterial(string Outline, SourceTurns Source)
{
    public static QuestionMaterial None { get; } = new(string.Empty, SourceTurns.None);
}

/// <summary>
/// One question row's conversation so far — immutable across turns, as the feature reviewer's
/// <c>SourceConversation</c> is: a turn that was cancelled or failed holds nothing half-updated.
/// </summary>
/// <param name="Base">The first turn's prompt, resent byte for byte under every tail (D25).</param>
/// <param name="Turn">The turn whose answer is being read; 1 for the first.</param>
/// <param name="Spent">How much source this row has been served over its turns — the resolver is shared, the allowance is not.</param>
/// <param name="ServedSoFar">Every slice served in earlier turns, so a model three turns in still holds what it read.</param>
/// <param name="SaidFinal">Whether the tail of the turn being read said FINAL — the cap, or the budget.</param>
public sealed record AnsweringMemory(string Base, int Turn, SourceSpend Spent, ImmutableList<ServedSlice> ServedSoFar, bool SaidFinal)
{
    /// <summary>
    /// The vendor's conversation id after the turn being read — empty for a runtime with none (api). An agy row's
    /// lookup continues THIS conversation rather than resending the prompt (research/PLAN_agy_searches_through_coai.md, S2).
    /// </summary>
    public string Handle { get; init; } = string.Empty;

    /// <summary>The invocation the turn being read was launched with — what a continuation is built from; null before the first.</summary>
    public ReviewerInvocation? Last { get; init; }

    /// <summary>Before the first answer: the base prompt, turn 1, nothing served.</summary>
    public static AnsweringMemory Start(string basePrompt) => new(basePrompt, 1, SourceSpend.None, [], SaidFinal: false);
}

/// <summary>What a row's answer led to: another turn with a longer prompt, or the advice.</summary>
public abstract record AnsweringTurn
{
    /// <param name="Launch">The next turn's launch — the same planned launch with the tail appended to its prompt.</param>
    /// <param name="Note">What this turn was served and refused, one line, for the row's record.</param>
    public sealed record Next(ConsultantLaunch Launch, AnsweringMemory Memory, string Note) : AnsweringTurn
    {
        /// <summary>
        /// A ready invocation the driver launches AS IS — a continuation of the vendor's own conversation — instead of
        /// building <see cref="Launch"/> again; null for the api rows, whose next turn is a fresh request.
        /// </summary>
        public ReviewerInvocation? Invocation { get; init; }
    }

    /// <param name="Advice">The answer, as the route reads it — the envelope's <c>answer</c>, or the prose.</param>
    /// <param name="Note">Why the conversation ended, when it ended for a reason other than an answer with no request.</param>
    public sealed record Done(string Advice, string Note = "") : AnsweringTurn;

    private AnsweringTurn() { }
}

/// <summary>
/// The seam a one-shot answering runtime with source follow-ups implements: asked after every answer
/// whether there is a next turn, and handed the state to decide with. The driver — S2's fan-out —
/// owns the loop, the deadline and the record; this owns what a turn IS.
/// </summary>
public interface IAnsweringFollowUps
{
    /// <summary>The most follow-up turns this row may have; zero is a single turn.</summary>
    int FollowUps { get; }

    /// <summary>
    /// After the turn <paramref name="memory"/> names answered <paramref name="raw"/>: the next turn, or the advice.
    /// </summary>
    /// <remarks>
    /// May throw the caller's cancellation and nothing else: a request that cannot be served is a
    /// refusal LINE in the next prompt, never an exception out of the conversation.
    /// </remarks>
    Task<AnsweringTurn> AfterAsync(ConsultantLaunch launch, AnsweringMemory memory, string raw, CancellationToken ct);
}
