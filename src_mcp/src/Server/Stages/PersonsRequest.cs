using CoaiMcp.Core.Rounds;

namespace CoaiMcp.Server;

/// <summary>
/// The person's request for a feature review's second round — D23's third ground — read from the ONE
/// road only a person writes, and applied to the session before its round begins (S3.4).
/// </summary>
/// <remarks>
/// <para><b>The road.</b> The escalation answer file the panel and the phone write beside a question
/// (<c>escalations/&lt;id&gt;.answer.json</c>), the same file <c>humanDecision</c> already travels by.
/// No MCP tool writes one: <c>ask_human</c> writes the QUESTION and waits, <c>resolve</c>'s
/// <c>humanDecision: proceed</c> is read by <c>RoundMachine.Resolve</c> and touches no ground. So a caller
/// passing every argument it has cannot open round 2 on this ground — which is the whole point of it being
/// a field with a provenance rather than a flag.</para>
/// <para><b>Which answer</b> is <see cref="CurrentAnswer.ForRequest"/>'s: an answer to a question asked
/// ON this feature session AFTER round 1 (<see cref="SessionState.RequestQuestions"/>, recorded by
/// <c>ask_human</c>). The clock alone — the newest <c>continue</c> newer than round 1 — let any question of
/// the session admit round 2: one asked before the round ran, one an older build filed (the gate's finding
/// #27, 2026-09-26).</para>
/// <para>Nothing is saved here — the request rides with the round that runs, and a call refused for another
/// reason reads the same answer again next time.</para>
/// </remarks>
internal static class PersonsRequest
{
    public static PersistedSession Apply(PersistedSession session, Escalations escalations, Serilog.ILogger log)
    {
        if (!session.State.IsFeatureSession || session.State.HumanGate)
        {
            return session;
        }

        var answer = CurrentAnswer.ForRequest(session, escalations);
        if (answer is null)
        {
            return session;
        }

        var asked = RoundMachine.ApplyPersonsRequest(session.State, Escalations.DecisionOf(answer));
        if (ReferenceEquals(asked, session.State))
        {
            return session;
        }

        log.Information(
            "the person asked for a second feature round of {Feature} (answer {Answer} at {When})",
            session.State.Feature, answer.Id, answer.AnsweredUtc);

        return session with { State = asked };
    }
}
