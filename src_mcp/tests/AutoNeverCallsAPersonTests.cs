using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Gate;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The round machine never calls a person: nobody answering is <c>not_reviewed</c>, a feature review at its cap
/// is <c>good_enough</c> (or <c>not_reviewed</c> when nobody answered), and the rounds running out is <c>good_enough</c> with its reason
/// (<c>research/PLAN_auto_never_asks_a_person_E1.md</c>, § 8.2).
/// </summary>
/// <remarks>
/// Pure machine: every state is built in code (<see cref="PanelConfig.Uniform"/>).
/// </remarks>
public sealed class AutoNeverCallsAPersonTests
{
    private const string Plan = "todo/PLAN_x.md";

    private static readonly ReviewerSummary Nobody = new(2, 0, ["codex: exit 1", "gemini: exit 1"]);

    private static SessionState Auto(int maxRounds, Stage stage) => stage switch
    {
        Stage.DocumentReview => new SessionState("s", "D:/r", "main", PanelConfig.Uniform(maxRounds, 0)) { Stage = stage, Document = "doc.md" },
        Stage.FeatureReview => new SessionState("s", "D:/r", SessionKey.FeatureBranch, PanelConfig.Uniform(maxRounds, 0)) { Stage = stage, Feature = Plan },
        _ => new SessionState("s", "D:/r", "main", PanelConfig.Uniform(maxRounds, 0)) { Stage = stage, PlanProceeded = stage == Stage.CodeReview },
    };

    private static Finding F(Severity severity, string title) =>
        new(severity, Category.Reliability, "src/A.cs", 1, title, "why", "fix", ["codex"]);

    private static GateResult Gate(params Finding[] findings) => GateRule.Evaluate([.. findings], [], 0);

    private static GateResult TwoMajors() => Gate(F(Severity.Major, "token compared with =="), F(Severity.Major, "no timeout on the fetch"));

    private static Transition.Ok Completed(SessionState s, GateResult gate, ReviewerSummary reviewers) =>
        RoundMachine.CompleteRound(s, gate, reviewers).Should().BeOfType<Transition.Ok>().Subject;

    /// <summary>A feature session whose first round already ran, for the round-2 cases.</summary>
    private static SessionState SecondRoundOf(SecondRoundGround ground) =>
        Auto(2, Stage.FeatureReview) with { RoundsRunThisStage = 1, SecondRound = ground };

    // ---------- S1: nobody answered ----------

    [Theory]
    [InlineData(Stage.PlanReview, 1)]
    [InlineData(Stage.PlanReview, 3)]
    [InlineData(Stage.CodeReview, 1)]
    [InlineData(Stage.CodeReview, 3)]
    [InlineData(Stage.DocumentReview, 1)]
    [InlineData(Stage.DocumentReview, 3)]
    public void NobodyAnswered_IsNotReviewed_AtOnce(Stage stage, int budget)
    {
        var ok = Completed(Auto(budget, stage), GateResult.Empty, Nobody);

        var verdict = ok.Verdict.Should().BeOfType<RoundVerdict.NotReviewed>($"policy auto asks nobody, whatever {budget} round(s) are left").Subject;
        verdict.Reason.Should().Contain("no reviewer answered").And.Contain("codex: exit 1");
        ok.State.AwaitingResolve.Should().BeTrue("the round completed, and resolve is the door the stage moves through");
        ok.State.AdvanceOnResolve.Should().BeTrue("not_reviewed advances like good_enough");
        ok.State.RoundsRunThisStage.Should().Be(1);
    }

    [Fact]
    public void NotReviewed_ThenAnEmptyResolve_OpensTheCodeStage()
    {
        var ok = Completed(Auto(1, Stage.PlanReview), GateResult.Empty, Nobody);
        ok.Verdict.Should().BeOfType<RoundVerdict.NotReviewed>();

        var moved = RoundMachine.Resolve(ok.State, []).Should().BeOfType<Transition.Moved>().Subject;

        moved.State.Stage.Should().Be(Stage.CodeReview);
        moved.State.PlanProceeded.Should().BeTrue();
        RoundMachine.BeginCodeRound(moved.State).Should().BeOfType<Transition.Moved>(
            "an unattended plan stage reaches the code stage — the begin is not refused with 'plan gate comes first'");
    }

    // ---------- S2: a feature review at its cap ----------

    [Fact]
    public void FeatureAtItsCap_ABlockingFinding_IsGoodEnough_WhenTheBudgetIsOne()
    {
        var ok = Completed(Auto(1, Stage.FeatureReview), Gate(F(Severity.Blocking, "a broken contract")), ReviewerSummary.AllAnswered(2));

        AssertGoodEnoughNamingBlocking(ok);
    }

    [Fact]
    public void FeatureAtItsCap_ABlockingFinding_IsGoodEnough_InTheSecondRound()
    {
        var ok = Completed(SecondRoundOf(SecondRoundGround.BlockingFinding), Gate(F(Severity.Blocking, "a broken contract")), ReviewerSummary.AllAnswered(2));

        AssertGoodEnoughNamingBlocking(ok);
    }

    private static void AssertGoodEnoughNamingBlocking(Transition.Ok ok)
    {
        var verdict = ok.Verdict.Should().BeOfType<RoundVerdict.GoodEnough>("a person is not called, and the findings are the work").Subject;
        verdict.Reason.Should().StartWith("policy auto:").And.Contain("blocking");
        ok.State.SecondRound.Should().Be(SecondRoundGround.None);
        ok.State.AdvanceOnResolve.Should().BeTrue("the review closes on resolve");
    }

    [Fact]
    public void FeatureAtItsCap_AFailure_IsGoodEnough()
    {
        var ok = Completed(SecondRoundOf(SecondRoundGround.ReviewerFailure), Gate(F(Severity.Major, "a major")), new ReviewerSummary(2, 1, ["gemini/FeatureReview: exit 1"]));

        var verdict = ok.Verdict.Should().BeOfType<RoundVerdict.GoodEnough>().Subject;
        verdict.Reason.Should().StartWith("policy auto:").And.Contain("reviewer failure").And.Contain("gemini/FeatureReview");
        ok.State.SecondRound.Should().Be(SecondRoundGround.None);
        ok.State.AdvanceOnResolve.Should().BeTrue();
    }

    [Fact]
    public void FeatureAtItsCap_NobodyAnswered_IsNotReviewed_WhenTheBudgetIsOne()
    {
        var ok = Completed(Auto(1, Stage.FeatureReview), GateResult.Empty, Nobody);

        AssertNotReviewed(ok);
    }

    [Fact]
    public void FeatureAtItsCap_NobodyAnswered_IsNotReviewed_InTheSecondRound()
    {
        var ok = Completed(SecondRoundOf(SecondRoundGround.ReviewerFailure), GateResult.Empty, Nobody);

        AssertNotReviewed(ok);
    }

    private static void AssertNotReviewed(Transition.Ok ok)
    {
        var verdict = ok.Verdict.Should().BeOfType<RoundVerdict.NotReviewed>().Subject;
        verdict.Reason.Should().StartWith("policy auto:").And.Contain("no reviewer answered");
        ok.State.SecondRound.Should().Be(SecondRoundGround.None);
        ok.State.AdvanceOnResolve.Should().BeTrue();
    }

    [Fact]
    public void FeatureRoundOne_NobodyAnswered_UnderItsCap_StillRetries()
    {
        // A guard on the unchanged path: below its cap a feature round with nobody answering is a retry, not a call_human site.
        var ok = Completed(Auto(2, Stage.FeatureReview), GateResult.Empty, Nobody);

        ok.Verdict.Should().BeOfType<RoundVerdict.Revise>();
        ok.State.SecondRound.Should().Be(SecondRoundGround.ReviewerFailure);
    }

    // ---------- S3: the rounds ran out ----------

    [Fact]
    public void Exhausted_IsGoodEnough_WithItsReason()
    {
        var ok = Completed(Auto(1, Stage.PlanReview), TwoMajors(), ReviewerSummary.AllAnswered(2));

        var verdict = ok.Verdict.Should().BeOfType<RoundVerdict.GoodEnough>().Subject;
        verdict.Reason.Should().Contain("still gate after 1 round(s)");
        ok.State.AdvanceOnResolve.Should().BeTrue();
    }

    [Fact]
    public void NotReviewedMayProceed() =>
        RoundCommands.MayProceed(new RoundVerdict.NotReviewed(GateResult.Empty, Nobody, "x")).Should().BeTrue(
            "or an operator's split order vanishes after an auto plan round");

    /// <summary>The extension writes nothing for auto only when this binary lists it (the parent's § 4.6): the entry is the last commit of the epic.</summary>
    [Fact]
    public void TheBinaryListsOnExhaustedAuto() =>
        FeaturesMode.Listed.Should().Contain("onExhaustedAuto", "the extension asks --features before it leaves COAI_ON_EXHAUSTED unwritten for auto");
}
