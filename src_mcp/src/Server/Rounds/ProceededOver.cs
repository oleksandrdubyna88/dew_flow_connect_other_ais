using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Store;

namespace CoaiMcp.Server;

/// <summary>
/// What a person's Proceed does to the findings nobody decided: they are RECORDED as proceeded over, never silently dropped
/// (<c>todo/PLAN_only_a_person_releases_the_gate.md</c> §11.5).
/// </summary>
/// <remarks>
/// <para>A <c>resolve</c> that consumes a person's Proceed may carry no decisions at all — the person decided, the AI has nothing
/// to add — and the round's pending findings would otherwise leave the session unrecorded: the log would show a stage that
/// advanced over findings with no trace of what became of them. Each still-pending finding gets a rejection whose reason is
/// <see cref="RoundMachine.ProceededOver"/>, through the same road every decision takes (the session's standing rejections, the
/// projection, the log page), so it reads like any other disagreement: declined, with the person named as the reason.</para>
/// <para>Decisions the AI recorded itself are kept exactly as given, and only the findings it left undecided are added.
/// Pure, so it is a table in the tests.</para>
/// </remarks>
internal static class ProceededOver
{
    /// <summary>The decisions the caller gave, plus a record for every pending finding it left undecided — when a Proceed is being consumed.</summary>
    public static List<DecisionAt> Recorded(
        IReadOnlyList<Finding> pending, IReadOnlyList<DecisionAt> decided, HumanDecision personsDecision, bool gateHeld) =>
        personsDecision == HumanDecision.Proceed && gateHeld
            ? [.. decided, .. Undecided(pending, decided)]
            : [.. decided];

    /// <summary>
    /// What <c>resolve</c> tells the AI once a stage advanced: the stage that completed owns the sentence, and when a person's
    /// Proceed advanced it the decision is named as theirs.
    /// </summary>
    public static string Instruction(SessionState before, HumanDecision personsDecision) =>
        personsDecision == HumanDecision.Proceed && before.HumanGate
            ? "a person pressed Proceed on the held gate — the stage advances with the findings open, and every finding you did not "
              + $"decide is recorded as {RoundMachine.ProceededOver}. {Stages.Of(before.Stage).CompletedSentence}"
            : Stages.Of(before.Stage).CompletedSentence;

    private static IEnumerable<DecisionAt> Undecided(IReadOnlyList<Finding> pending, IReadOnlyList<DecisionAt> decided)
    {
        var taken = decided.Select(d => d.Ordinal).ToHashSet();

        foreach (var ordinal in Enumerable.Range(0, pending.Count).Where(o => !taken.Contains(o)))
        {
            if (DecisionAt.Reject(pending, ordinal, RoundMachine.ProceededOver) is { } record)
            {
                yield return record;
            }
        }
    }
}
