using CoaiMcp.Core.Rounds;

namespace CoaiMcp.Server;

/// <summary>
/// Which number the next round of a stage is written under: one past the highest this session's
/// journal already holds for that stage.
/// </summary>
/// <remarks>
/// <para>The journal, not the budget counter. <c>SessionState.RoundsRunThisStage</c> counts rounds
/// AGAINST A BUDGET and is reset to zero whenever a fresh budget is granted — <c>review_code</c>
/// with <c>again: true</c>, the escalation ladder, a person's Continue or Fix — while the database
/// upserts a round on <c>(session_id, stage, number)</c>. Numbering rounds from that counter made
/// the round after any reset REPLACE the round before it: finish code round 1, resolve, commit,
/// run <c>again</c>, and the log showed one round where two had run (§9.6 of the feature-review
/// plan).</para>
/// <para>A pure function over the trail, so the rule is a unit test. It is called once per round,
/// under the session claim, by the one place that starts rounds — two rounds cannot read the same
/// journal and compute the same number, because the claim serialises them.</para>
/// <para>An interrupted round keeps its number: a retry after a crash is the NEXT round, and the
/// row that says a round died stays on the record beside it.</para>
/// </remarks>
public static class RoundNumber
{
    /// <param name="journal">The session's rounds as persisted — every stage, in the order they ran.</param>
    /// <param name="stage">The stage whose next round is being started.</param>
    public static int Next(IReadOnlyList<RoundRecord> journal, Stage stage)
    {
        var name = stage.ToString();
        var highest = 0;
        foreach (var round in journal)
        {
            if (round.Stage == name && round.Number > highest)
            {
                highest = round.Number;
            }
        }

        return highest + 1;
    }
}
