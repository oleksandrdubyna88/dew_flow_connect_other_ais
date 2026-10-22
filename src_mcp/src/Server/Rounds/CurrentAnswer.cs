using System.Globalization;
using CoaiMcp.Core.Rounds;
using Serilog;
using Serilog.Core;

namespace CoaiMcp.Server;

/// <summary>
/// The person's answer that belongs to what the session is waiting on — its CURRENT hold, or a feature
/// review's second round — the one rule every reader of the answer file applies before acting on it
/// (D12, found by S3.4 of the feature-review plan; bound by identity since the epic 3 risk consultation,
/// 2026-09-26; identity ALONE since epic 3's code round the same day).
/// </summary>
/// <remarks>
/// <para><b>Why a rule at all.</b> The session-wide read (<c>Escalations.AnsweredFor</c>, removed 2026-10-03 with its
/// last reader, <c>status</c>) answered the newest ANSWERED question of a session and walked past the unanswered ones — so a <c>call_human</c> notice nobody has
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
        NewestHearingAmong(session.State.HoldQuestions, escalations, Logger.None)?.Answer;

    /// <summary>What the person chose for the CURRENT hold, or <see cref="HumanDecision.None"/>.</summary>
    public static HumanDecision DecisionFor(PersistedSession session, Escalations escalations) =>
        DecisionFor(session, escalations, Logger.None);

    /// <summary>
    /// What the person chose for the CURRENT hold — a Proceed only when it answers the hold's own NOTICE
    /// (<c>todo/PLAN_only_a_person_releases_the_gate.md</c>, G4); a Proceed written against anything else is logged and read as prose.
    /// </summary>
    public static HumanDecision DecisionFor(PersistedSession session, Escalations escalations, ILogger log) =>
        NewestHearingAmong(session.State.HoldQuestions, escalations, log)?.Decision ?? HumanDecision.None;

    /// <summary>
    /// The answer that may be the person's request for a feature review's second round: the newest among
    /// the questions asked for it, given after the last round that ran — or null.
    /// </summary>
    public static EscalationAnswer? ForRequest(PersistedSession session, Escalations escalations) =>
        RequestHearing(session, escalations, Logger.None)?.Answer;

    /// <summary>
    /// The answer <c>status</c> reports: the current hold's, else the feature review's request — the only answers the
    /// gate acts on (<c>research/PLAN_ask_human_is_for_the_gate.md</c>, G5). Null when the person has answered neither.
    /// </summary>
    /// <remarks>
    /// <c>status</c> used to read the newest answered card of the session, whatever it was, and a button pressed on an
    /// AI's own question surfaced as a gate decision nobody had made (2026-10-03). It is the third reader of the answer
    /// file, moved onto the same rule as the other two.
    /// </remarks>
    public static EscalationAnswer? TheGatesAnswer(PersistedSession session, Escalations escalations) =>
        TheGatesHearing(session, escalations, Logger.None)?.Answer;

    /// <summary>The decision <c>status</c> reports: the current hold's, else the request's, by the one rule — <see cref="HumanDecision.None"/> when neither is answered.</summary>
    public static HumanDecision TheGatesDecision(PersistedSession session, Escalations escalations) =>
        TheGatesHearing(session, escalations, Logger.None)?.Decision ?? HumanDecision.None;

    /// <summary>A decision as <c>status</c> spells it — <c>continue</c>, <c>fix</c>, <c>discuss</c>, <c>proceed</c>, or empty.</summary>
    public static string WordFor(HumanDecision decision) => decision switch
    {
        HumanDecision.Continue => "continue",
        HumanDecision.Fix => "fix",
        HumanDecision.Discuss => "discuss",
        HumanDecision.Proceed => "proceed",
        _ => string.Empty,
    };

    /// <summary>An answer, the question it answers, and what it decides — the decision already judged by the notice rule.</summary>
    private sealed record Hearing(EscalationAnswer Answer, HumanDecision Decision);

    private static Hearing? TheGatesHearing(PersistedSession session, Escalations escalations, ILogger log) =>
        NewestHearingAmong(session.State.HoldQuestions, escalations, log) ?? RequestHearing(session, escalations, log);

    private static Hearing? RequestHearing(PersistedSession session, Escalations escalations, ILogger log) =>
        NewestHearingAmong(session.State.RequestQuestions, escalations, log) is { } hearing && IsNewerThanTheLastRound(hearing.Answer, session)
            ? hearing
            : null;

    /// <summary>
    /// The newest answer among the questions that carries a DECISION, else the newest at all — or null while none is
    /// answered.
    /// </summary>
    /// <remarks>
    /// <para>Words are not a decision, and they must not hide one either: a hold's question card offers "Type an answer…"
    /// first (<c>research/PLAN_ask_human_is_for_the_gate.md</c>, G4), and a person who pressed "Keep going" on the notice and
    /// then typed a reply to the AI's question left a word-only answer newest — the hold read as unanswered (the cadence
    /// consultation b6e9df3c, 2026-10-03).</para>
    /// <para>"Carries a decision" is judged AFTER the notice rule, so a Proceed written against an <c>ask_human</c> card is
    /// prose here too: it neither releases the hold nor hides a Continue the person pressed on the notice.</para>
    /// </remarks>
    private static Hearing? NewestHearingAmong(IReadOnlyList<string> questions, Escalations escalations, ILogger log) =>
        questions
            .Reverse()
            .Select(id => HearingOf(id, escalations, log))
            .OfType<Hearing>()
            .OrderByDescending(hearing => hearing.Decision != HumanDecision.None)
            .ThenByDescending(hearing => AnsweredAt(hearing.Answer) ?? DateTime.MinValue)
            .FirstOrDefault();

    private static Hearing? HearingOf(string questionId, Escalations escalations, ILogger log) =>
        escalations.ReadAnswer(questionId) is { } answer
            ? new Hearing(answer, DecisionOnTheNotice(questionId, answer, escalations, log))
            : null;

    /// <summary>
    /// The answer's decision, with Proceed honoured only when the answered question is the gate's NOTICE: the card that lists the
    /// findings being overridden is the card that can override them, and a question file that is gone or says anything else is not it.
    /// </summary>
    private static HumanDecision DecisionOnTheNotice(string questionId, EscalationAnswer answer, Escalations escalations, ILogger log)
    {
        var decision = Escalations.DecisionOf(answer);
        if (decision != HumanDecision.Proceed || escalations.Read(questionId) is { Kind: EscalationKinds.Notice })
        {
            return decision;
        }

        log.Warning("a Proceed was written against {Id}, which is not a readable notice of this gate; it is read as prose", questionId);

        return HumanDecision.None;
    }

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
