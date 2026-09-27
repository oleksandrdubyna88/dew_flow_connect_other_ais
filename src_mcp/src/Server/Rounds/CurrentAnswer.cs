using System.Globalization;
using CoaiMcp.Core.Rounds;

namespace CoaiMcp.Server;

/// <summary>
/// The person's answer that belongs to what the session is waiting on — its CURRENT hold, or a feature
/// review's second round — the one rule every reader of the answer file applies before acting on it
/// (D12, found by S3.4 of the feature-review plan; bound by identity since the epic 3 risk consultation,
/// 2026-09-26; identity ALONE since epic 3's code round the same day).
/// </summary>
/// <remarks>
/// <para><b>Why a rule at all.</b> <see cref="Escalations.AnsweredFor"/> answers the newest ANSWERED
/// question of a session and walks past the unanswered ones — so a <c>call_human</c> notice nobody has
/// answered yet is skipped and the "keep going" a person gave on an EARLIER hold is found instead. Three
/// readers act on that file: the engine before a round (a held gate opened), <c>resolve</c> (a held gate's
/// fresh count), and the feature stage's second-round admission (the person's request). Two of them
/// applied whatever came back, and an answer to hold one opened hold two on its own — the exact bypass
/// the human gate exists to prevent.</para>
/// <para><b>The rule.</b> An answer counts only for the QUESTION it answers. A hold is bound to its
/// questions: the <c>call_human</c> notice the round that raised it wrote, and every <c>ask_human</c>
/// question asked while it stood — recorded on the session as <see cref="SessionState.HoldQuestions"/>
/// the moment each is raised, released with the hold. The person's request for round 2 is bound to the
/// <c>ask_human</c> questions asked on the feature session after round 1
/// (<see cref="SessionState.RequestQuestions"/>). In each case the answer that counts is the newest
/// answer to one of THEM.</para>
/// <para><b>No clock, any more.</b> The round's clock decided before the binding, and stayed as the
/// residual for a session that recorded no question — an older build's hold, or a feature session with
/// no hold. Both residuals were the bypass in a smaller coat: an old question answered late released an
/// unrecorded hold, and any "continue" of the session admitted round 2 (the gate's findings #24, #27,
/// #30). So a hold with no recorded question is released by NO answer — the engine re-issues its notice,
/// which records an id — and a request needs a recorded question. The one clock left is a fail-safe on
/// the request: the answer must be newer than the last round that ran, which a question asked after it
/// cannot fail except by a clock nobody trusts.</para>
/// <para>ONE place, on purpose: the check was written first for the feature stage's reader, and the two
/// older readers had the defect it prevents — the shape <c>reuse-first</c> calls a decision applied at some
/// of its sites.</para>
/// </remarks>
internal static class CurrentAnswer
{
    /// <summary>The answer to the current hold — the newest among its own questions' — or null: none answered, or none recorded.</summary>
    public static EscalationAnswer? For(PersistedSession session, Escalations escalations) =>
        NewestAnswerAmong(session.State.HoldQuestions, escalations);

    /// <summary>What the person chose for the CURRENT hold, or <see cref="HumanDecision.None"/>.</summary>
    public static HumanDecision DecisionFor(PersistedSession session, Escalations escalations) =>
        Escalations.DecisionOf(For(session, escalations));

    /// <summary>
    /// The answer that may be the person's request for a feature review's second round: the newest among
    /// the questions asked for it, given after the last round that ran — or null.
    /// </summary>
    public static EscalationAnswer? ForRequest(PersistedSession session, Escalations escalations) =>
        NewestAnswerAmong(session.State.RequestQuestions, escalations) is { } answer && IsNewerThanTheLastRound(answer, session)
            ? answer
            : null;

    /// <summary>The newest answer among the questions, the one asked last first — or null while none is answered.</summary>
    private static EscalationAnswer? NewestAnswerAmong(IReadOnlyList<string> questions, Escalations escalations) =>
        questions
            .Reverse()
            .Select(escalations.ReadAnswer)
            .OfType<EscalationAnswer>()
            .OrderByDescending(answer => AnsweredAt(answer) ?? DateTime.MinValue)
            .FirstOrDefault();

    private static bool IsNewerThanTheLastRound(EscalationAnswer answer, PersistedSession session)
    {
        var last = session.Rounds.LastOrDefault(r => r.Verdict != RoundRecord.Skipped);

        return last is not null && AnsweredAt(answer) is { } answered && answered > last.CompletedUtc;
    }

    private static DateTime? AnsweredAt(EscalationAnswer answer) =>
        DateTime.TryParse(answer.AnsweredUtc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at)
            ? at
            : null;
}
