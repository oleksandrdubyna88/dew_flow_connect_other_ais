using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The next round's number is one past the highest the journal holds for that stage — and nothing
/// else about the session decides it.
/// </summary>
/// <remarks>
/// The rule behind <c>ARoundsNumberComesFromTheJournalTests</c>, as a unit: that suite proves the
/// three resets (<c>again</c>, the ladder, a person's Continue) no longer overwrite a row; this
/// one pins the arithmetic they rely on.
/// </remarks>
public sealed class RoundNumberTests
{
    private static RoundRecord Round(string stage, int number, string status = RoundRecord.Done) =>
        new(stage, number, "proceed", 0, "all 1 reviewers answered", DateTime.UtcNow) { Status = status };

    [Fact]
    public void AnEmptyJournal_StartsAtOne() =>
        RoundNumber.Next([], Stage.CodeReview).Should().Be(1);

    [Fact]
    public void TheNextNumber_IsOnePastTheHighestOfThatStage() =>
        RoundNumber.Next([Round("PlanReview", 1), Round("PlanReview", 2)], Stage.PlanReview).Should().Be(3);

    /// <summary>The stages are numbered apart: two plan rounds do not push the first code round to 3.</summary>
    [Fact]
    public void AnotherStagesRounds_DoNotCount() =>
        RoundNumber.Next([Round("PlanReview", 1), Round("PlanReview", 2)], Stage.CodeReview).Should().Be(1);

    /// <summary>
    /// The highest, not the count and not the last: a journal with a gap or out of order still
    /// yields a number no row holds.
    /// </summary>
    [Fact]
    public void AGapOrAnUnorderedJournal_StillYieldsAFreshNumber() =>
        RoundNumber.Next([Round("CodeReview", 3), Round("CodeReview", 1)], Stage.CodeReview).Should().Be(4);

    /// <summary>A round that died keeps its number; the retry is the next round, not a rewrite.</summary>
    [Fact]
    public void AnInterruptedRound_KeepsItsNumber_AndTheRetryIsTheNext() =>
        RoundNumber.Next([Round("CodeReview", 1, RoundRecord.Interrupted)], Stage.CodeReview).Should().Be(2);

    /// <summary>
    /// The refuted approach, kept in the suite: the budget counter is reset by <c>again</c>, by the
    /// ladder and by a person's Continue, so a number read from it names the previous round again.
    /// </summary>
    [Fact]
    public void TheBudgetCounter_WouldNameThePreviousRoundAgain_AndThatIsThePoint()
    {
        var reopened = new SessionState("s1", "/repo", "main", PanelConfig.Uniform(3, 5))
        {
            Stage = Stage.CodeReview,
            RoundsRunThisStage = 0,
        };
        var journal = new List<RoundRecord> { Round("CodeReview", 1) };

        (reopened.RoundsRunThisStage + 1).Should().Be(1, "the counter was reset and would rewrite round 1");
        RoundNumber.Next(journal, reopened.Stage).Should().Be(2, "the journal knows round 1 exists");
    }
}
