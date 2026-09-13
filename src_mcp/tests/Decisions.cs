using CoaiMcp.Core.Rounds;

namespace CoaiMcp.Tests;

/// <summary>
/// Decisions for a caller that resolved a round top to bottom.
/// </summary>
/// <remarks>
/// <para>Most tests that record decisions are about something else — paging, a query, a projection —
/// and pass them in the order the round listed them because that is the ordinary case, not because
/// the order matters to what they assert. This says that once, so those tests read as "the usual
/// way" instead of repeating a number each that a reader then has to check.</para>
/// <para>A test that IS about which finding a decision lands on writes its <see cref="DecisionAt"/>
/// out with the number in it — see <c>RoundsDbTests.ADecisionSentOutOfOrder_StillLandsOnItsOwnFinding</c>,
/// where the whole point is that the number and the position disagree.</para>
/// </remarks>
internal static class Decisions
{
    internal static IReadOnlyList<DecisionAt> InOrder(params Decision[] made) =>
        [.. made.Select((decision, ordinal) => new DecisionAt(ordinal, decision))];
}
