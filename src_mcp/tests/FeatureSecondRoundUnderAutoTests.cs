using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Gate;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What a feature review says to its caller: no person is called, and none is asked for a second round.
/// </summary>
/// <remarks>Pure: <see cref="FeatureSecondRound.Apply"/> over a hand-built answer and the state the round completed into.</remarks>
public sealed class FeatureSecondRoundUnderAutoTests
{
    private static readonly ReviewerSummary Both = ReviewerSummary.AllAnswered(2);

    private static SessionState After() => new(
        "s", "D:/r", SessionKey.FeatureBranch, PanelConfig.Uniform(2, 0))
    {
        Stage = Stage.FeatureReview,
        Feature = "todo/PLAN_x.md",
    };

    private static GateResult OneMajor() =>
        GateRule.Evaluate([new Finding(Severity.Major, Category.Reliability, "a.cs", 1, "a major", "why", "fix", ["codex"])], [], 0);

    private static ReviewAnswer Answer(string verdict) => new(verdict, 1, 0, "2 of 2", [], [], [], "the engine's own sentence");

    private static string Instruction(RoundVerdict verdict, SessionState after) =>
        FeatureSecondRound.Apply(Answer("good_enough"), verdict, Both, after, []).Instruction;

    [Fact]
    public void AnAutoGoodEnough_SaysNoPersonIsCalled_AndNeverOffersASecondRoundToThePerson()
    {
        var verdict = new RoundVerdict.GoodEnough(OneMajor(), Both, "policy auto: a blocking finding still stands");

        var instruction = Instruction(verdict, After());

        instruction.Should().StartWith("policy auto: a blocking finding still stands. ").And.Contain("no person is called");
        instruction.Should().NotContain("none is blocking", "a blocking finding is the very reason it ended here")
            .And.NotContain("the person asks", "policy auto asks no person for a second round");
    }

    [Fact]
    public void AGoodEnoughWithNoReason_SaysOneRoundWasTheBudget_AndNoPersonIsAsked()
    {
        var instruction = Instruction(new RoundVerdict.GoodEnough(OneMajor(), Both), After());

        instruction.Should().Contain("none is blocking").And.Contain("no person is asked for one").And.NotContain("or when the person asks for one");
    }

    [Fact]
    public void ARoundThatClosesOnResolve_UnderAuto_SaysNoPersonIsAskedForASecondRound()
    {
        var instruction = Instruction(new RoundVerdict.Proceed(GateResult.Empty, Both), After());

        instruction.Should().Contain("no person is asked for one").And.NotContain("the person asks");
    }

    [Theory]
    [InlineData(SecondRoundGround.BlockingFinding)]
    [InlineData(SecondRoundGround.ReviewerFailure)]
    public void ARevise_DoesNotPromiseAPerson_AndSaysHowAFailingSecondRoundEnds(SecondRoundGround ground)
    {
        var revise = new RoundVerdict.Revise(OneMajor(), new ReviewerSummary(2, 1, ["gemini: exit 1"]), 1);

        Instruction(revise, After() with { SecondRound = ground }).Should().NotContain("calls a person").And.Contain("closes the review as good_enough");
    }

    [Fact]
    public void ANotReviewed_LeavesTheEnginesOwnSentenceStanding()
    {
        var instruction = Instruction(new RoundVerdict.NotReviewed(GateResult.Empty, new ReviewerSummary(2, 0, ["codex: exit 1"]), "policy auto: x"), After());

        instruction.Should().Be("the engine's own sentence");
    }
}
