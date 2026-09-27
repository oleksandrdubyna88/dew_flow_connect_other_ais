using System.Collections.Immutable;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Gate;
using CoaiMcp.Core.Rounds;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The feature stage's second round runs only when it is needed (D23 of the feature-review plan, S3.4).
/// </summary>
/// <remarks>
/// <para>At most two rounds, and round 2 is admitted by the machine on exactly one of three grounds,
/// recorded on the state: round 1 had a reviewer failure (some or all), round 1 carried a
/// <c>blocking</c> finding, or the person asked. A round 1 whose findings gate but none of them
/// blocking CLOSES the review on <c>resolve</c> — the accepted fixes land without a second review — and
/// <c>again</c> over the same base is then refused naming the grounds. A round 2 that still carries a
/// blocking finding or fails again calls a person; a third round is refused.</para>
/// <para>The person's request has a provenance the caller cannot forge: it is a field only
/// <see cref="RoundMachine.ApplyPersonsRequest"/> writes, from a <see cref="HumanDecision"/> the person's
/// surfaces produced — never from <c>again</c>, never from <c>humanDecision: proceed</c>.</para>
/// </remarks>
public sealed class TheSecondFeatureRoundTests
{
    private const string Plan = "todo/PLAN_feature_review.md";

    /// <summary>A feature session with the shipped feature budget: two rounds, at most five open.</summary>
    private static SessionState Feature(int maxRounds = 2, int threshold = 5) =>
        new("s1", "/repo", SessionKey.FeatureBranch, PanelConfig.Uniform(maxRounds, threshold)) { Stage = Stage.FeatureReview, Feature = Plan };

    private static Finding F(Severity severity, string title) =>
        new(severity, Category.Reliability, "src/A.cs", 1, title, "why", "fix", ["codex"]);

    private static GateResult Gate(params Finding[] findings) =>
        GateRule.Evaluate([.. findings], [], _ => 5);

    private static GateResult OneMajor() => Gate(F(Severity.Major, "a major"));

    private static GateResult SixMajors() => Gate([.. Enumerable.Range(0, 6).Select(i => F(Severity.Major, $"major {i}"))]);

    private static GateResult OneBlocking() => Gate(F(Severity.Blocking, "a broken contract"));

    private static readonly ReviewerSummary Both = ReviewerSummary.AllAnswered(2);

    private static readonly ReviewerSummary OneOfTwoFailed = new(2, 1, ["gemini/FeatureReview: exit 1"]);

    private static readonly ReviewerSummary Nobody = new(2, 0, ["codex/FeatureReview: 429", "gemini/FeatureReview: 429"]);

    private static SessionState Completed(SessionState s, GateResult gate, ReviewerSummary reviewers) =>
        ((Transition.Ok)RoundMachine.CompleteRound(s, gate, reviewers)).State;

    private static SessionState Resolved(SessionState s) =>
        ((Transition.Moved)RoundMachine.Resolve(s, [])).State;

    [Fact]
    public void TheShippedFeatureBudget_IsTwoRounds()
    {
        PanelConfig.FeatureDefault.MaxRounds.Should().Be(2, "D23: at most two rounds, and the second only when it is needed");
        PanelConfig.FeatureDefault.Threshold.Should().Be(5, "the threshold did not move");
    }

    // ---------- a non-blocking round closes on resolve ----------

    [Fact]
    public void AGatingRoundWithNothingBlocking_ClosesOnResolve_HoweverManyFindingsGate()
    {
        var ok = RoundMachine.CompleteRound(Feature(), SixMajors(), Both).Should().BeOfType<Transition.Ok>().Subject;

        ok.Verdict.Should().BeOfType<RoundVerdict.GoodEnough>("six majors over a threshold of five gate, and none is blocking — one round is the budget for them");
        ok.State.AdvanceOnResolve.Should().BeTrue("the review closes on resolve");
        ok.State.SecondRound.Should().Be(SecondRoundGround.None);
        Resolved(ok.State).Stage.Should().Be(Stage.Done);
    }

    [Fact]
    public void ARoundUnderTheThreshold_StillProceeds()
    {
        var ok = RoundMachine.CompleteRound(Feature(), OneMajor(), Both).Should().BeOfType<Transition.Ok>().Subject;

        ok.Verdict.Should().BeOfType<RoundVerdict.Proceed>();
        ok.State.AdvanceOnResolve.Should().BeTrue();
        Resolved(ok.State).Stage.Should().Be(Stage.Done);
    }

    [Fact]
    public void AfterANonBlockingRound_AgainOverTheSameBase_IsRefusedNamingTheGrounds()
    {
        var done = Resolved(Completed(Feature(), SixMajors(), Both));

        var refused = RoundMachine.BeginFeatureRoundAgain(done).Should().BeOfType<Transition.Refused>().Subject;

        refused.Sentence.Should().Contain("reviewer failure").And.Contain("blocking").And.Contain("person",
            "the three grounds of D23 are what the caller is missing, and the sentence names them");
        refused.Sentence.Should().Contain("ask_human", "a refusal with no door is a stall: the person's request is the door");
    }

    // ---------- a blocking finding admits round 2 ----------

    [Fact]
    public void ABlockingFinding_AdmitsTheSecondRound_AndIsRecordedAsItsGround()
    {
        var ok = RoundMachine.CompleteRound(Feature(), OneBlocking(), Both).Should().BeOfType<Transition.Ok>().Subject;

        ok.Verdict.Should().BeOfType<RoundVerdict.Revise>().Which.RoundsLeft.Should().Be(1);
        ok.State.AdvanceOnResolve.Should().BeFalse("the review stays open for its second round");
        ok.State.SecondRound.Should().Be(SecondRoundGround.BlockingFinding);

        var open = Resolved(ok.State);
        open.Stage.Should().Be(Stage.FeatureReview);
        RoundMachine.BeginFeatureRound(open).Should().BeOfType<Transition.Moved>();
    }

    [Fact]
    public void ABlockingFindingInRoundTwo_CallsAPerson()
    {
        var open = Resolved(Completed(Feature(), OneBlocking(), Both));

        var ok = RoundMachine.CompleteRound(open, OneBlocking(), Both).Should().BeOfType<Transition.Ok>().Subject;

        ok.Verdict.Should().BeOfType<RoundVerdict.CallHuman>().Which.Reason.Should().Contain("blocking").And.Contain("second round");
        ok.State.HumanGate.Should().BeTrue();
        ok.State.RoundsRunThisStage.Should().Be(2);
    }

    [Fact]
    public void ACleanRoundTwo_ClosesOnResolve_AndAThirdIsRefused()
    {
        var open = Resolved(Completed(Feature(), OneBlocking(), Both));

        var ok = RoundMachine.CompleteRound(open, OneMajor(), Both).Should().BeOfType<Transition.Ok>().Subject;
        ok.Verdict.Should().BeOfType<RoundVerdict.Proceed>();
        var done = Resolved(ok.State);

        done.Stage.Should().Be(Stage.Done);
        RoundMachine.BeginFeatureRoundAgain(done).Should().BeOfType<Transition.Refused>()
            .Which.Sentence.Should().Contain("two rounds", "D23: at most two, ever");
    }

    // ---------- a reviewer failure admits a retry ----------

    [Fact]
    public void APartlyFailedRound_AdmitsARetry_EvenWhenTheAnsweredReviewersFoundNothing()
    {
        var ok = RoundMachine.CompleteRound(Feature(), GateResult.Empty, OneOfTwoFailed).Should().BeOfType<Transition.Ok>().Subject;

        ok.Verdict.Should().BeOfType<RoundVerdict.Revise>("a passed gate with a failed reviewer is a retry, not a proceed");
        ok.State.SecondRound.Should().Be(SecondRoundGround.ReviewerFailure);
        ok.State.AdvanceOnResolve.Should().BeFalse();
        RoundMachine.BeginFeatureRound(Resolved(ok.State)).Should().BeOfType<Transition.Moved>();
    }

    [Fact]
    public void WhenEveryReviewerFailsInRoundOne_TheRetryIsAdmitted_NotAPerson()
    {
        var ok = RoundMachine.CompleteRound(Feature(), GateResult.Empty, Nobody).Should().BeOfType<Transition.Ok>().Subject;

        ok.Verdict.Should().BeOfType<RoundVerdict.Revise>("D23: some OR all failed admits round 2, which asks everyone");
        ok.State.HumanGate.Should().BeFalse();
        ok.State.SecondRound.Should().Be(SecondRoundGround.ReviewerFailure);
    }

    [Fact]
    public void ABlockingFindingOutranksAFailure_AsTheGround()
    {
        var ok = RoundMachine.CompleteRound(Feature(), OneBlocking(), OneOfTwoFailed).Should().BeOfType<Transition.Ok>().Subject;

        ok.State.SecondRound.Should().Be(SecondRoundGround.BlockingFinding, "the fix needs every reviewer's eyes, not only the one that failed");
    }

    [Fact]
    public void ARetryThatFailsAgain_CallsAPerson()
    {
        var open = Resolved(Completed(Feature(), GateResult.Empty, OneOfTwoFailed));

        var ok = RoundMachine.CompleteRound(open, GateResult.Empty, OneOfTwoFailed).Should().BeOfType<Transition.Ok>().Subject;

        ok.Verdict.Should().BeOfType<RoundVerdict.CallHuman>().Which.Reason.Should().Contain("second round").And.Contain("gemini/FeatureReview");
        ok.State.HumanGate.Should().BeTrue();
    }

    [Fact]
    public void WhenNobodyAnswersTwice_APersonIsCalled_AndTheGateNeverFailsOpen()
    {
        var open = Resolved(Completed(Feature(), GateResult.Empty, Nobody));

        var ok = RoundMachine.CompleteRound(open, GateResult.Empty, Nobody).Should().BeOfType<Transition.Ok>().Subject;

        ok.Verdict.Should().BeOfType<RoundVerdict.CallHuman>().Which.Reason.Should().Contain("no reviewer answered");
        ok.State.HumanGate.Should().BeTrue();
    }

    [Fact]
    public void ABudgetOfOneRound_RunsNoRetry_AFailureIsAPersonsCall()
    {
        var ok = RoundMachine.CompleteRound(Feature(maxRounds: 1), GateResult.Empty, OneOfTwoFailed).Should().BeOfType<Transition.Ok>().Subject;

        ok.Verdict.Should().BeOfType<RoundVerdict.CallHuman>("the operator's budget is one round; D23 caps, it never adds");
    }

    // ---------- a third round is refused ----------

    [Fact]
    public void AThirdRound_IsRefused_WhateverTheGround()
    {
        var twoRun = Feature() with { RoundsRunThisStage = 2, SecondRound = SecondRoundGround.BlockingFinding };

        RoundMachine.BeginFeatureRound(twoRun).Should().BeOfType<Transition.Refused>()
            .Which.Sentence.Should().Contain("two rounds");
    }

    [Fact]
    public void ARoundTwoWithoutAGround_IsRefused()
    {
        var oneRun = Feature() with { RoundsRunThisStage = 1 };

        RoundMachine.BeginFeatureRound(oneRun).Should().BeOfType<Transition.Refused>()
            .Which.Sentence.Should().Contain("blocking");
    }

    [Fact]
    public void AFreshReview_StartsOverItsGround()
    {
        var closed = Feature(maxRounds: 2) with { Stage = Stage.Done, RoundsRunThisStage = 2, SecondRound = SecondRoundGround.PersonAsked };

        var fresh = RoundMachine.FreshFeatureReview(closed);

        fresh.RoundsRunThisStage.Should().Be(0);
        fresh.SecondRound.Should().Be(SecondRoundGround.None, "a different base is a different review");
    }

    // ---------- the person's request, and the caller's inability to claim it ----------

    [Fact]
    public void TheCallerCannotClaimThePersonsRequest_WithAnyArgumentItHas()
    {
        var awaiting = Completed(Feature(), SixMajors(), Both);

        // `humanDecision: proceed` is the caller's own argument: it never opens a second round.
        var done = RoundMachine.Resolve(awaiting, [], humanSaysProceed: true).Should().BeOfType<Transition.Moved>().Subject.State;

        done.Stage.Should().Be(Stage.Done);
        done.SecondRound.Should().Be(SecondRoundGround.None, "no argument of the caller's writes the person's field");
        RoundMachine.BeginFeatureRoundAgain(done).Should().BeOfType<Transition.Refused>("again: true alone never admits round 2 on the person's ground");
    }

    [Theory]
    [InlineData(HumanDecision.Continue)]
    [InlineData(HumanDecision.Fix)]
    public void ThePersonsRequest_AdmitsTheSecondRound_AsRoundTwoOfTheSameReview(HumanDecision decision)
    {
        var done = Resolved(Completed(Feature(), SixMajors(), Both));

        var asked = RoundMachine.ApplyPersonsRequest(done, decision);

        asked.SecondRound.Should().Be(SecondRoundGround.PersonAsked);
        var reopened = RoundMachine.BeginFeatureRoundAgain(asked).Should().BeOfType<Transition.Moved>().Subject.State;
        reopened.Stage.Should().Be(Stage.FeatureReview);
        reopened.RoundsRunThisStage.Should().Be(1, "round two of this review, not round one of a fresh budget");
        reopened.Rejections.Should().BeEquivalentTo(done.Rejections, "what the review agreed on still stands");
    }

    [Theory]
    [InlineData(HumanDecision.Discuss)]
    [InlineData(HumanDecision.None)]
    public void AnAnswerThatIsNotARequest_ChangesNothing(HumanDecision decision)
    {
        var done = Resolved(Completed(Feature(), SixMajors(), Both));

        RoundMachine.ApplyPersonsRequest(done, decision).Should().BeSameAs(done);
    }

    [Fact]
    public void ThePersonsRequest_IsNotAThirdRound_AndNotAFreshSetOnAnOpenGate()
    {
        var twoRun = Feature() with { Stage = Stage.Done, RoundsRunThisStage = 2 };
        RoundMachine.ApplyPersonsRequest(twoRun, HumanDecision.Continue).Should().BeSameAs(twoRun, "two rounds have run; a third is never admitted");

        var held = Feature() with { RoundsRunThisStage = 2, HumanGate = true };
        RoundMachine.ApplyPersonsRequest(held, HumanDecision.Continue).Should().BeSameAs(held,
            "a held gate is answered by ApplyHumanDecision, which grants a fresh set — a different promise");

        var code = new SessionState("s2", "/repo", "main", PanelConfig.Uniform(2, 5)) { Stage = Stage.Done, RoundsRunThisStage = 1 };
        RoundMachine.ApplyPersonsRequest(code, HumanDecision.Continue).Should().BeSameAs(code, "only a feature review has this ground");
    }

    [Fact]
    public void APersonsFreshSet_AfterRoundTwoCalledThem_StartsTheGroundOver()
    {
        var open = Resolved(Completed(Feature(), OneBlocking(), Both));
        var held = Completed(open, OneBlocking(), Both);
        held.HumanGate.Should().BeTrue();

        var fresh = RoundMachine.ApplyHumanDecision(held, HumanDecision.Continue);

        fresh.RoundsRunThisStage.Should().Be(0);
        fresh.SecondRound.Should().Be(SecondRoundGround.None);
    }

    [Fact]
    public void TheHumanOverride_StillCannotCloseARoundThatAdmittedASecond()
    {
        var awaiting = Completed(Feature(), OneBlocking(), Both);

        RoundMachine.Resolve(awaiting, [], humanSaysProceed: true).Should().BeOfType<Transition.Refused>()
            .Which.Sentence.Should().Contain("call_human", "the override is honoured only after a person was called");
    }

    /// <summary>
    /// Doctrine §4: a sentence this round has none of is <c>string.Empty</c>, never <c>null</c> — read by
    /// length at the one caller. Asserted over every string-valued method of the class rather than the one
    /// that carried the <c>?</c>, so the next one added is held to the same rule.
    /// </summary>
    [Fact]
    public void NoInstructionOfTheSecondRound_IsNullable()
    {
        var nullability = new System.Reflection.NullabilityInfoContext();
        var stringValued = typeof(CoaiMcp.Server.FeatureSecondRound)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            .Where(m => m.ReturnType == typeof(string))
            .ToList();

        stringValued.Should().NotBeEmpty("a scan of no methods would pass for ever");
        foreach (var method in stringValued)
        {
            nullability.Create(method.ReturnParameter).ReadState.Should().Be(System.Reflection.NullabilityState.NotNull,
                "{0} answers a sentence, and an absent sentence is empty (doctrine §4)", method.Name);
        }
    }

    /// <summary>The other stages are untouched: a plan round over budget still climbs its own ladder.</summary>
    [Fact]
    public void ThePlanAndCodeStages_KeepTheirOwnRule()
    {
        var plan = new SessionState("s3", "/repo", "main", PanelConfig.Uniform(2, 5));

        var ok = RoundMachine.CompleteRound(plan, SixMajors(), Both).Should().BeOfType<Transition.Ok>().Subject;

        ok.Verdict.Should().BeOfType<RoundVerdict.Revise>();
        ok.State.SecondRound.Should().Be(SecondRoundGround.None, "the ground is the feature stage's alone");
    }
}
