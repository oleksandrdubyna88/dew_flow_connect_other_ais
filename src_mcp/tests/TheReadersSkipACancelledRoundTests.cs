using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// D9 of <c>todo/PLAN_a_cancelled_review_stops_its_round.md</c>: a round the caller withdrew judged nothing, so every reader that picks "the last
/// round that ran" to compare against skips it — a commit it never read is still new, a reviewer it cut is not "the one that failed in round one",
/// an epic it did not close is not closed by it.
/// </summary>
public sealed class TheReadersSkipACancelledRoundTests
{
    private static RoundRecord Round(string stage, int number, string status, string sha = "", params ReviewerState[] states) =>
        new(stage, number, status == RoundRecord.Cancelled ? "withdrawn" : "proceed", 0, "", DateTime.UtcNow)
        {
            Status = status,
            Sha = sha,
            ReviewerStates = [.. states],
        };

    private static PersistedSession Session(params RoundRecord[] rounds) =>
        new(new SessionState("s", "/repo", "feature", new PanelConfig()), [.. rounds]);

    [Fact]
    public void ARoundTwoRosterIsBuiltFromTheLastRoundThatRan_NotFromTheCutReviewers()
    {
        var session = Session(
            Round(nameof(Stage.FeatureReview), 1, RoundRecord.Done, states: new ReviewerState("codex", "Features", ReviewerState.Failed)),
            Round(nameof(Stage.FeatureReview), 2, RoundRecord.Cancelled, states: new ReviewerState("gemini", "Features", ReviewerState.Failed)));

        FeatureSecondRound.FailedInRoundOne(session).Should().BeEquivalentTo([("codex", "Features")]);
    }

    [Fact]
    public void TheHeadIsComparedWithTheLastFeatureRoundThatRead_NotWithAWithdrawnOne()
    {
        var session = Session(
            Round(nameof(Stage.FeatureReview), 1, RoundRecord.Done, sha: "aaaa"),
            Round(nameof(Stage.FeatureReview), 2, RoundRecord.Cancelled, sha: "bbbb"));

        FeatureStage.LastReviewed(session)!.Number.Should().Be(1);
    }

    [Fact]
    public void AnEpicIsReconciledAgainstTheLastCodeRoundThatJudged_NotAWithdrawnOne()
    {
        var session = Session(
            Round(nameof(Stage.CodeReview), 1, RoundRecord.Done),
            Round(nameof(Stage.CodeReview), 2, RoundRecord.Cancelled));

        CadenceDesk.LastJudgedCodeRound(session)!.Number.Should().Be(1, "a round that was withdrawn closed no epic");
    }

    [Fact]
    public void AWithdrawnFirstCodeRound_StillCountsAsTheEpicsFirst()
    {
        // Deliberately NOT skipped (as an interrupted round is not): the epic's first code round has been ASKED for, and the consultation the
        // cadence owes for it is owed once, not once more per withdrawal.
        var withdrawn = Round(nameof(Stage.CodeReview), 1, RoundRecord.Cancelled) with { PlanKey = Core.Cadence.EpicRef.PlanKey("todo/PLAN_a.md"), EpicNumber = 2 };
        var call = new CadenceCall { Plan = "todo/PLAN_a.md", Facts = Core.Commands.CadenceFacts.Off with { Epic = 2 } };

        CadenceDesk.FirstCodeRound(Session(), call).Should().BeTrue("no code round of the epic is on record");
        CadenceDesk.FirstCodeRound(Session(withdrawn), call).Should().BeFalse("the withdrawn round is on record: the second call is not the epic's first");
    }
}
