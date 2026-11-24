using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Gate;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What a feature review says to its caller under policy auto: no person is called, and none is asked for a second round
/// (<c>research/PLAN_auto_never_asks_a_person_E1.md</c>, § 8.1 item 4).
/// </summary>
/// <remarks>Pure: <see cref="FeatureSecondRound.Apply"/> over a hand-built answer and the state the round completed into.</remarks>
public sealed class FeatureSecondRoundUnderAutoTests
{
    private static readonly ReviewerSummary Both = ReviewerSummary.AllAnswered(2);

    private static SessionState After(bool auto) => new(
        "s", "D:/r", SessionKey.FeatureBranch, auto ? PanelConfig.Uniform(2, 0) : PanelConfig.Uniform(2, 0, StagePolicy.GoodEnough))
    {
        Stage = Stage.FeatureReview,
        Feature = "todo/PLAN_x.md",
    };

    private static GateResult OneMajor() =>
        GateRule.Evaluate([new Finding(Severity.Major, Category.Reliability, "a.cs", 1, "a major", "why", "fix", ["codex"])], [], 0);

    private static ReviewAnswer Answer(string verdict) => new(verdict, null, 1, 0, "2 of 2", [], [], [], "the engine's own sentence");

    private static string Instruction(RoundVerdict verdict, SessionState after) =>
        FeatureSecondRound.Apply(Answer("good_enough"), verdict, Both, after, []).Instruction;

    [Fact]
    public void AnAutoGoodEnough_SaysNoPersonIsCalled_AndNeverOffersASecondRoundToThePerson()
    {
        var verdict = new RoundVerdict.GoodEnough(OneMajor(), Both, "policy auto: a blocking finding still stands");

        var instruction = Instruction(verdict, After(auto: true));

        instruction.Should().StartWith("policy auto: a blocking finding still stands. ").And.Contain("no person is called");
        instruction.Should().NotContain("none is blocking", "a blocking finding is the very reason it ended here")
            .And.NotContain("the person asks", "policy auto asks no person for a second round");
    }

    [Fact]
    public void AGoodEnoughWithNoReason_UnderANonAutoPolicy_KeepsTodaysSentence()
    {
        var instruction = Instruction(new RoundVerdict.GoodEnough(OneMajor(), Both), After(auto: false));

        instruction.Should().Contain("none is blocking").And.Contain("or when the person asks for one").And.NotContain("policy auto");
    }

    [Fact]
    public void ARoundThatClosesOnResolve_UnderAuto_SaysNoPersonIsAskedForASecondRound()
    {
        var instruction = Instruction(new RoundVerdict.Proceed(GateResult.Empty, Both), After(auto: true));

        instruction.Should().Contain("under policy auto no person is asked for one").And.NotContain("the person asks");
    }

    [Theory]
    [InlineData(SecondRoundGround.BlockingFinding)]
    [InlineData(SecondRoundGround.ReviewerFailure)]
    public void ARevise_UnderAuto_DoesNotPromiseAPerson_AndUnderAnotherPolicyStillDoes(SecondRoundGround ground)
    {
        var revise = new RoundVerdict.Revise(OneMajor(), new ReviewerSummary(2, 1, ["gemini: exit 1"]), 1);

        Instruction(revise, After(auto: true) with { SecondRound = ground }).Should().NotContain("calls a person").And.Contain("policy auto");
        Instruction(revise, After(auto: false) with { SecondRound = ground }).Should().Contain("calls a person");
    }

    [Fact]
    public void ANotReviewed_LeavesTheEnginesOwnSentenceStanding()
    {
        var instruction = Instruction(new RoundVerdict.NotReviewed(GateResult.Empty, new ReviewerSummary(2, 0, ["codex: exit 1"]), "policy auto: x"), After(auto: true));

        instruction.Should().Be("the engine's own sentence");
    }
}
