using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Gate;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The pure halves of <c>todo/PLAN_only_a_person_releases_the_gate.md</c>: the machine takes a PERSON'S decision, judges the caller's
/// claim beside it, and the reader of the answer file honours a Proceed only on the hold's own notice.
/// </summary>
/// <remarks>
/// <para>The guarantees that used to be stated with the boolean are rewritten in place in <c>RoundMachineTests</c>,
/// <c>HumanGateHoldsTests</c>, <c>GateReportingTests</c>, <c>TheAnswerBelongsToTheCurrentHoldTests</c> and
/// <c>TheSecondFeatureRoundTests</c>; this file owns what is new: the claim judged beside the decision, and the reader's notice rule.
/// The attack itself is <c>OnlyAPersonReleasesTheGateTests</c>, through the service.</para>
/// </remarks>
public sealed class OnlyAPersonReleasesTheGateMachineTests : IDisposable
{
    private readonly string _data = Directory.CreateTempSubdirectory("coai-person-releases-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_data, recursive: true);
        }
        catch (IOException) { }
    }

    private static SessionState Fresh(PanelConfig? config = null) =>
        new("s-1", "D:/repo", "feature/x", config ?? new PanelConfig());

    private static Finding F(string title = "finding") =>
        new(Severity.Major, Category.Security, "src/A.cs", 1, title, "why it breaks", "fix", ["codex"]);

    private static GateResult Failing(int count = 3) =>
        new(count, false, [.. Enumerable.Range(0, count).Select(i => F($"finding {i}"))], []);

    private static readonly ReviewerSummary AllSix = ReviewerSummary.AllAnswered(6);

    /// <summary>A plan stage whose last verdict was <c>call_human</c>: the gate is held and a round awaits its decisions.</summary>
    private static SessionState CalledAPerson() =>
        ((Transition.Ok)RoundMachine.CompleteRound(Fresh(PanelConfig.Uniform(1, 2, StagePolicy.Human)), Failing(), AllSix)).State;

    // ---------- the machine ----------

    [Fact]
    public void AFindingAProceedCarriedTheStageOver_IsNotAStandingRejection_AndTheGateDoesNotDiscountIt()
    {
        var held = CalledAPerson();
        var finding = F("the poll has no timeout");

        var moved = (Transition.Moved)RoundMachine.Resolve(held, [new Decision.ProceededOver(finding)], HumanDecision.Proceed);

        moved.State.Stage.Should().Be(Stage.CodeReview, "the person's Proceed advances this one transition");
        moved.State.Rejections.Should().BeEmpty("proceeding over a finding is not arguing with it");
        GateRule.Evaluate([finding], moved.State.Rejections, threshold: 0).GatingCount
            .Should().Be(1, "the same defect raised in a later round gates in full");
    }

    [Fact]
    public void ARejectionStillStands_BesideAProceededOverFinding()
    {
        var held = CalledAPerson();

        var moved = (Transition.Moved)RoundMachine.Resolve(
            held, [new Decision.Rejected(F("argued"), "the caller owns it"), new Decision.ProceededOver(F("left open"))], HumanDecision.Proceed);

        moved.State.Rejections.Should().ContainSingle().Which.Finding.Title.Should().Be("argued");
    }

    [Fact]
    public void AProceededOverRecord_WithNoPersonsProceed_IsRefused()
    {
        // Only the server builds this record, and only while consuming a person's Proceed: the machine refuses it anywhere else.
        var refused = RoundMachine.Resolve(CalledAPerson(), [new Decision.ProceededOver(F())]);

        refused.Should().BeOfType<Transition.Refused>().Which.Sentence.Should().Be(RoundMachine.ProceededOverNeedsAProceed);
    }

    [Fact]
    public void AProceed_ReleasesTheHoldsQuestionsWithTheHold()
    {
        var moved = (Transition.Moved)RoundMachine.Resolve(CalledAPerson() with { HoldQuestions = ["n1", "q2"] }, [], HumanDecision.Proceed);

        moved.State.HumanGate.Should().BeFalse();
        moved.State.Stage.Should().Be(Stage.CodeReview);
        moved.State.PlanProceeded.Should().BeTrue();
        moved.State.HoldQuestions.Should().BeEmpty("the person's Proceed releases the hold the way their other decisions do: its questions go with it");
    }

    [Fact]
    public void AProceedOnAnUnheldGate_IsNotADecision()
    {
        var unheld = CalledAPerson() with { HumanGate = false };

        // Not held, not advancing: a Proceed has nothing to release and must not advance the stage by itself.
        var moved = RoundMachine.Resolve(unheld, [], HumanDecision.Proceed).Should().BeOfType<Transition.Moved>().Subject;

        moved.State.Stage.Should().Be(Stage.PlanReview, "treated as None: recording decisions advances nothing");
        moved.State.PlanProceeded.Should().BeFalse();
    }

    [Fact]
    public void ApplyHumanDecisionLeavesAProceedToResolve()
    {
        var held = CalledAPerson() with { HoldQuestions = ["n1"] };

        RoundMachine.ApplyHumanDecision(held, HumanDecision.Proceed).Should().BeSameAs(held,
            "the stage moves in Resolve only (D8): the engine's pre-round read must not release the hold");
    }

    [Fact]
    public void ARoundlessHeldGate_TakesThePersonsProceed_ButNotAnythingElse()
    {
        var resolved = ((Transition.Moved)RoundMachine.Resolve(CalledAPerson(), [])).State;
        resolved.AwaitingResolve.Should().BeFalse();
        resolved.HumanGate.Should().BeTrue("recording decisions is not a person deciding");

        RoundMachine.Resolve(resolved, [], HumanDecision.Proceed).Should().BeOfType<Transition.Moved>()
            .Which.State.Stage.Should().Be(Stage.CodeReview);
        RoundMachine.Resolve(resolved, [], HumanDecision.Continue).Should().BeOfType<Transition.Refused>()
            .Which.Sentence.Should().Contain("no completed round awaiting decisions");
        RoundMachine.Resolve(resolved, []).Should().BeOfType<Transition.Refused>();
    }

    [Theory]
    [InlineData(HumanDecision.Continue)]
    [InlineData(HumanDecision.Fix)]
    public void ContinueAndFixInResolve_ResetTheRoundCount_AndAdvanceNothing(HumanDecision decision)
    {
        var held = CalledAPerson();
        held.RoundsRunThisStage.Should().BeGreaterThan(0);

        var next = ((Transition.Moved)RoundMachine.Resolve(held, [], decision)).State;

        next.RoundsRunThisStage.Should().Be(0, "a fresh set, the person's doing");
        next.Stage.Should().Be(Stage.PlanReview);
        next.HumanGate.Should().BeTrue("the engine's read before the next round opens the gate");
    }

    // ---------- the caller's claim, judged beside the person's decision ----------

    [Fact]
    public void ACallersClaim_OnAHeldGateWithNoPersonsProceed_IsRefusedWithTheCardSentence()
    {
        var refused = RoundMachine.Resolve(CalledAPerson(), [], HumanDecision.None, callerClaimsProceed: true)
            .Should().BeOfType<Transition.Refused>().Subject;

        refused.Sentence.Should().Contain("Proceed despite the findings").And.Contain("ask_human").And.Contain("never from this argument");
    }

    [Fact]
    public void ACallersClaim_BesideThePersonsProceed_IsIgnored()
    {
        RoundMachine.Resolve(CalledAPerson(), [], HumanDecision.Proceed, callerClaimsProceed: true)
            .Should().BeOfType<Transition.Moved>().Which.State.Stage.Should().Be(Stage.CodeReview);
    }

    [Fact]
    public void ACallersClaim_WhileRoundsRemain_IsRefused_AsItAlwaysWas()
    {
        var patient = new PanelConfig(new Dictionary<string, RoleGate> { ["PlanCritique"] = new(3, 2) });
        var awaiting = ((Transition.Ok)RoundMachine.CompleteRound(Fresh(patient), Failing(), AllSix)).State;

        RoundMachine.Resolve(awaiting, [], HumanDecision.None, callerClaimsProceed: true)
            .Should().BeOfType<Transition.Refused>().Which.Sentence.Should().Contain("call_human");
    }

    [Fact]
    public void ACallersClaim_WhenTheGateAlreadyAdvances_IsIgnored_NotRefused()
    {
        var ok = (Transition.Ok)RoundMachine.CompleteRound(Fresh(), GateResult.Empty, AllSix);

        RoundMachine.Resolve(ok.State, [], HumanDecision.None, callerClaimsProceed: true)
            .Should().BeOfType<Transition.Moved>().Which.State.Stage.Should().Be(Stage.CodeReview);
    }

    [Fact]
    public void ACallersClaim_OnAWithdrawalShapedHold_IsRefused()
    {
        // The hold the cancel plan raises: HumanGate and AwaitingResolve, the last round not call_human. The machine
        // sees the same state, and the same person-only door.
        var withdrawal = Fresh() with { HumanGate = true, AwaitingResolve = true, HoldQuestions = ["w1"] };

        RoundMachine.Resolve(withdrawal, [], HumanDecision.None, callerClaimsProceed: true)
            .Should().BeOfType<Transition.Refused>().Which.Sentence.Should().Contain("Proceed despite the findings");
    }

    [Fact]
    public void TheHoldRefusal_NamesTheCard_NotTheArgument()
    {
        var sentence = RoundMachine.BeginPlanRound(CalledAPerson() with { AwaitingResolve = false })
            .Should().BeOfType<Transition.Refused>().Subject.Sentence;

        sentence.Should().Contain("Proceed despite the findings").And.Contain("ask_human").And.Contain("proceed");
        sentence.Should().NotContain("you pass humanDecision").And.NotContain("humanDecision: \"proceed\"");
    }

    // ---------- the reader of the answer file ----------

    private static readonly DateTime Held = new(2026, 10, 22, 10, 0, 0, DateTimeKind.Utc);

    private void Asked(string id, string kind)
    {
        new Escalations(_data).Notify(new EscalationQuestion(id, "s1", "/repo", "main", "The code review gate needs your decision", "", "en", "", [], Held.ToString("O"))
        {
            Kind = kind,
        });
    }

    private void Answered(string id, string decision)
    {
        File.WriteAllText(
            new Escalations(_data).AnswerPath(id),
            $$"""{"id":"{{id}}","answer":"go","decision":"{{decision}}","answeredUtc":"{{Held.AddMinutes(5):O}}"}""");
    }

    private static PersistedSession HeldOn(params string[] questions) =>
        new(
            new SessionState("s1", "/repo", "main", PanelConfig.Uniform(1, 5))
            {
                Stage = Stage.CodeReview,
                PlanProceeded = true,
                RoundsRunThisStage = 1,
                HumanGate = true,
                HoldQuestions = questions,
            },
            [new RoundRecord(nameof(Stage.CodeReview), 1, "call_human", 2, "all 1 reviewers answered", Held)]);

    [Fact]
    public void AProceedOnTheHoldsNotice_IsTheHoldsProceed()
    {
        Asked("n1", EscalationKinds.Notice);
        Answered("n1", "proceed");

        CurrentAnswer.DecisionFor(HeldOn("n1"), new Escalations(_data)).Should().Be(HumanDecision.Proceed);
    }

    [Fact]
    public void AProceedOnAQuestionCard_IsNotAProceed()
    {
        Asked("n1", EscalationKinds.Notice);
        Asked("q2", EscalationKinds.Question);
        Answered("q2", "proceed");

        CurrentAnswer.DecisionFor(HeldOn("n1", "q2"), new Escalations(_data)).Should().Be(HumanDecision.None,
            "the AI words a question card; the strongest decision is never read from it (G4)");
    }

    [Fact]
    public void AProceedOnAQuestionCard_DoesNotHideAContinueOnTheNotice()
    {
        Asked("n1", EscalationKinds.Notice);
        Asked("q2", EscalationKinds.Question);
        Answered("n1", "continue");
        Answered("q2", "proceed");

        CurrentAnswer.DecisionFor(HeldOn("n1", "q2"), new Escalations(_data)).Should().Be(HumanDecision.Continue);
    }

    [Fact]
    public void AProceedOnAnEarlierHoldsNotice_DoesNotReleaseThisHold()
    {
        Asked("n1", EscalationKinds.Notice);
        Asked("n2", EscalationKinds.Notice);
        Answered("n1", "proceed");

        CurrentAnswer.DecisionFor(HeldOn("n2"), new Escalations(_data)).Should().Be(HumanDecision.None);
    }

    [Fact]
    public void AProceedOnACardAnOlderServerWrote_OrWhoseQuestionIsGone_IsNotAProceed()
    {
        Asked("n1", string.Empty);
        Answered("n1", "proceed");
        Answered("gone", "proceed");

        CurrentAnswer.DecisionFor(HeldOn("n1"), new Escalations(_data)).Should().Be(HumanDecision.None, "no kind is no notice");
        CurrentAnswer.DecisionFor(HeldOn("gone"), new Escalations(_data)).Should().Be(HumanDecision.None, "no question file is no notice");
    }

    [Fact]
    public void TheStatusWordForAProceed_IsProceed_AndOnlyForTheNotice()
    {
        Asked("n1", EscalationKinds.Notice);
        Asked("q2", EscalationKinds.Question);
        Answered("n1", "Proceed");

        CurrentAnswer.WordFor(HumanDecision.Proceed).Should().Be("proceed");
        CurrentAnswer.TheGatesDecision(HeldOn("n1"), new Escalations(_data)).Should().Be(HumanDecision.Proceed, "the word is read case-insensitively, like the other three");
        Answered("q2", "proceed");
        CurrentAnswer.TheGatesDecision(HeldOn("q2"), new Escalations(_data)).Should().Be(HumanDecision.None);
    }

    [Fact]
    public void ANoticeSaysWhichDecisionsItOffers_AndAnOlderCardSaysNone()
    {
        EscalationDecisions.Notice.Should().Equal("continue", "fix", "discuss", "proceed");
        EscalationDecisions.Question.Should().Equal("continue", "fix", "discuss");
        Asked("old", EscalationKinds.Notice);

        new Escalations(_data).Read("old")!.Decisions.Should().BeEmpty("a card written without the list is read as an empty one, never as null");
    }
}
