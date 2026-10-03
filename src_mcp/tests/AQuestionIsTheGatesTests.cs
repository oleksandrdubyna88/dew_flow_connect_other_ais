using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Core.Rounds;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Which <c>ask_human</c> questions are the GATE's (<c>todo/PLAN_ask_human_is_for_the_gate.md</c>, G1, G7): the one
/// predicate that decides both whether a question becomes a card and whether its answer binds to the session, and
/// what the gate in front of the person does on each door.
/// </summary>
/// <remarks>
/// The operator, 2026-10-03: an AI's A-or-B question about its own work became a card under "a review is waiting on
/// you", answered with the gate's three buttons. A card is the gate's; everything else is asked in the AI's chat.
/// </remarks>
public sealed class AQuestionIsTheGatesTests
{
    private const string Plan = "todo/PLAN_x.md";

    private static SessionState Branch(bool held = false) =>
        new("s1", "/repo", "main", PanelConfig.Uniform(3, 2)) { PlanProceeded = true, Stage = Stage.CodeReview, HumanGate = held };

    private static SessionState Feature(int rounds, SecondRoundGround ground = SecondRoundGround.None, bool held = false) =>
        new("s2", "/repo", SessionKey.FeatureBranch, PanelConfig.Uniform(2, 0))
        {
            Stage = Stage.FeatureReview,
            Feature = Plan,
            RoundsRunThisStage = rounds,
            SecondRound = ground,
            HumanGate = held,
        };

    /// <summary>Every state the table names, with the answer the predicate must give — one list, read by both tests below.</summary>
    public static TheoryData<string, SessionState, bool> States => new()
    {
        { "a branch session, not held — the AI's own question", Branch(), false },
        { "a branch session held by call_human", Branch(held: true), true },
        { "a feature review before its first round", Feature(rounds: 0), false },
        { "a feature review after round 1, no ground yet — the person may ask for round 2", Feature(rounds: 1), true },
        { "a feature review after round 1 with a ground already set", Feature(rounds: 1, SecondRoundGround.BlockingFinding), false },
        { "a feature review after its second round", Feature(rounds: 2), false },
        { "a held feature review", Feature(rounds: 2, held: true), true },
    };

    [Theory]
    [MemberData(nameof(States))]
    public void AQuestionIsTheGates_OnlyOnAHeldGate_OrWhereAFeatureReviewCanStillBeAskedForItsSecondRound(string name, SessionState state, bool gates) =>
        RoundMachine.AsksForTheGate(state).Should().Be(gates, name);

    /// <summary>
    /// The binding and the card are ONE decision: a question is recorded on the session exactly when it is the gate's.
    /// The binding used to cover every feature question after round 1 — two rounds run, or a ground already set —
    /// states in which no answer can admit anything, so a card there would be the same dead button.
    /// </summary>
    [Theory]
    [MemberData(nameof(States))]
    public void AQuestionIsRecordedOnTheSession_ExactlyWhereItIsTheGates(string name, SessionState state, bool gates) =>
        ReferenceEquals(RoundMachine.RecordQuestion(state, "q1"), state).Should().Be(!gates, name);

    // ---------- the gate in front of the person, per door ----------

    private static AskGateInput PastTheFreeBatches(AskDoor door) =>
        new() { Mode = QuestionMode.Require, Phase = AskPhase.Building, BatchesAsked = 5, Door = door };

    [Fact]
    public void OnTheAisOwnQuestion_ThePhaseRuleStillSendsItToTheConsultants()
    {
        AskGate.Decide(PastTheFreeBatches(AskDoor.Conversation)).Should().BeOfType<AskDecision.Refused>()
            .Which.Sentence.Should().Contain("ask_consultants");
    }

    /// <summary>G7: the gate asking the person is not one of the AI's questions — no consultant can release a hold.</summary>
    [Fact]
    public void OnAHeldGatesQuestion_ThePhaseRuleDoesNotApply_AndNothingIsCounted()
    {
        var allowed = AskGate.Decide(PastTheFreeBatches(AskDoor.Card)).Should().BeOfType<AskDecision.Allowed>().Subject;

        allowed.Counted.Should().BeFalse("the batches count the AI's own questions");
        allowed.ConsultBeside.Should().BeFalse();
        allowed.Note.Should().BeEmpty();
    }

    [Fact]
    public void AProductionRiskOnAHeldGate_KeepsD8_ConsultantsBesideTheCard()
    {
        var allowed = AskGate.Decide(PastTheFreeBatches(AskDoor.Card) with { ProductionRisk = true, RiskReason = "drops a column" })
            .Should().BeOfType<AskDecision.Allowed>().Subject;

        allowed.ConsultBeside.Should().BeTrue();
        allowed.Counted.Should().BeFalse();
        allowed.Note.Should().Contain("folded under its card");
    }

    /// <summary>G3: in the conversation there is no card — the consultants are asked first and the note says so.</summary>
    [Fact]
    public void AProductionRiskInTheConversation_AsksTheConsultantsFirst()
    {
        var allowed = AskGate.Decide(PastTheFreeBatches(AskDoor.Conversation) with { ProductionRisk = true, RiskReason = "drops a column" })
            .Should().BeOfType<AskDecision.Allowed>().Subject;

        allowed.ConsultBeside.Should().BeTrue();
        allowed.Counted.Should().BeTrue("the person is asked — in the conversation");
        allowed.Note.Should().Contain("asked first").And.NotContain("card");
    }

    [Fact]
    public void TheDefaultDoor_IsTheAisOwnQuestion() =>
        new AskGateInput().Door.Should().Be(AskDoor.Conversation, "every default of the input describes the ordinary call");
}
