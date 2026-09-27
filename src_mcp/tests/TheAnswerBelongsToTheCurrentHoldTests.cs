using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A person's answer applies only to the hold it answered: the one that began with the last round that
/// ran, and only when the answer came AFTER that round completed.
/// </summary>
/// <remarks>
/// <para><b>The defect (D12, found by S3.4).</b> <c>Escalations.AnsweredFor</c> returns the newest ANSWERED
/// question of a session, skipping the unanswered ones — so a <c>call_human</c> notice nobody has answered
/// yet is walked past, and the "keep going" a person gave on an EARLIER hold is found instead. The engine's
/// held-gate reader and <c>resolve</c>'s both applied it: an answer to hold one reopened hold two on its own,
/// which is the exact bypass the human gate exists to prevent. The feature stage's second-round reader
/// already had the clock (<c>PersonsRequest</c>); this is the same rule, in ONE place, for all three.</para>
/// <para>The rule is the round's clock, not the question's: a hold begins when the round completes with
/// <c>call_human</c> (<c>RoundRecord.CompletedUtc</c>), its notice is written after that, and an answer given
/// before it cannot be about it. A skipped row decided nothing and raised no hold, so it is not a round here.</para>
/// </remarks>
public sealed class TheAnswerBelongsToTheCurrentHoldTests : IDisposable
{
    private readonly string _data = Directory.CreateTempSubdirectory("coai-current-answer-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_data, recursive: true);
        }
        catch (IOException) { }
    }

    private static readonly DateTime HoldOne = new(2026, 9, 26, 10, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime HoldTwo = new(2026, 9, 26, 11, 0, 0, DateTimeKind.Utc);

    private Escalations Escalations() => new(_data);

    /// <summary>A code session whose gate is held by the round that completed at <paramref name="held"/>.</summary>
    private static PersistedSession Held(DateTime held, bool skippedAfter = false) =>
        new(
            new SessionState("s1", "/repo", "main", PanelConfig.Uniform(1, 5)) { Stage = Stage.CodeReview, PlanProceeded = true, RoundsRunThisStage = 1, HumanGate = true },
            skippedAfter
                ? [new RoundRecord(nameof(Stage.CodeReview), 1, "call_human", 2, "all 1 reviewers answered", held), new RoundRecord(nameof(Stage.CodeReview), 2, RoundRecord.Skipped, 0, "no reviewer ran", held.AddHours(2))]
                : [new RoundRecord(nameof(Stage.CodeReview), 1, "call_human", 2, "all 1 reviewers answered", held)]);

    private void Answered(string id, string decision, DateTime answeredUtc)
    {
        var escalations = Escalations();
        escalations.Notify(new EscalationQuestion(id, "s1", "/repo", "main", "The code review gate needs your decision", "", "en", "", [], answeredUtc.AddMinutes(-1).ToString("O")));
        File.WriteAllText(escalations.AnswerPath(id), $$"""{"id":"{{id}}","answer":"go","decision":"{{decision}}","answeredUtc":"{{answeredUtc:O}}"}""");
    }

    private void Asked(string id, DateTime askedUtc) =>
        Escalations().Notify(new EscalationQuestion(id, "s1", "/repo", "main", "The code review gate needs your decision", "", "en", "", [], askedUtc.ToString("O")));

    /// <summary>The person answers a question that was asked earlier — the answer file alone, beside the question.</summary>
    private void AnswerTo(string id, string decision, DateTime answeredUtc) =>
        File.WriteAllText(Escalations().AnswerPath(id), $$"""{"id":"{{id}}","answer":"go","decision":"{{decision}}","answeredUtc":"{{answeredUtc:O}}"}""");

    /// <summary>A hold raised by THIS build: the session records the questions asked for it.</summary>
    private static PersistedSession HeldOn(DateTime held, params string[] questions) =>
        Held(held) with { State = Held(held).State with { HoldQuestions = questions } };

    // ---------- the hold is bound to its answer by identity ----------

    /// <summary>
    /// The clock alone let a question asked for an EARLIER hold, left unanswered and answered now, release the
    /// CURRENT hold — the answer was newer than the round, and nothing asked which question it answered
    /// (found by the epic 3 risk consultation 159f0397).
    /// </summary>
    [Fact]
    public void AnOldQuestionAnsweredNow_DoesNotReleaseAHoldItWasNotAskedFor_AndTheHoldsOwnQuestionDoes()
    {
        Asked("q1", HoldOne.AddMinutes(1));
        Asked("q2", HoldTwo.AddMinutes(1));
        var heldTwo = HeldOn(HoldTwo, "q2");

        AnswerTo("q1", "continue", HoldTwo.AddMinutes(5));

        CurrentAnswer.DecisionFor(heldTwo, Escalations()).Should().Be(HumanDecision.None,
            "q1 was asked for hold one; answering it after hold two began is not a decision on hold two");
        CurrentAnswer.For(heldTwo, Escalations()).Should().BeNull();

        AnswerTo("q2", "continue", HoldTwo.AddMinutes(6));

        CurrentAnswer.DecisionFor(heldTwo, Escalations()).Should().Be(HumanDecision.Continue, "hold two's own question was answered");
        CurrentAnswer.For(heldTwo, Escalations())!.Id.Should().Be("q2");
    }

    [Fact]
    public void AnAskHumanQuestionAskedForTheHold_ReleasesItToo()
    {
        Asked("n2", HoldTwo.AddMinutes(1));
        Asked("q3", HoldTwo.AddMinutes(2));
        var held = HeldOn(HoldTwo, "n2", "q3");

        AnswerTo("q3", "fix", HoldTwo.AddMinutes(5));

        CurrentAnswer.DecisionFor(held, Escalations()).Should().Be(HumanDecision.Fix,
            "a question the AI asked while the gate was held was asked for that hold, and the person's answer to it is the hold's");
    }

    [Fact]
    public void TheNewestOfTheHoldsOwnAnswers_Wins()
    {
        Asked("n2", HoldTwo.AddMinutes(1));
        Asked("q3", HoldTwo.AddMinutes(2));
        AnswerTo("n2", "discuss", HoldTwo.AddMinutes(4));
        AnswerTo("q3", "continue", HoldTwo.AddMinutes(5));

        CurrentAnswer.For(HeldOn(HoldTwo, "n2", "q3"), Escalations())!.Id.Should().Be("q3", "the question asked last is the one answered last");
    }

    /// <summary>
    /// A hold an OLDER build raised recorded no question, so there is no id to bind an answer to — and
    /// until 2026-09-26 the round's clock decided instead, which is exactly the bypass the binding closed:
    /// an old question answered after such a hold's round released it (the gate's findings #24/#30). Now
    /// no answer releases such a hold; the engine re-issues its notice, which records an id, and the person
    /// answers THAT.
    /// </summary>
    [Fact]
    public void AHoldRecordedWithoutItsQuestions_IsNotReleasedByATimeMatchedAnswer()
    {
        Asked("q1", HoldOne.AddMinutes(1));
        AnswerTo("q1", "continue", HoldTwo.AddMinutes(5));

        CurrentAnswer.DecisionFor(Held(HoldTwo), Escalations()).Should().Be(HumanDecision.None,
            "a hold with no recorded question cannot tell which answer is its own, and a guess is the bypass");
        CurrentAnswer.For(Held(HoldTwo), Escalations()).Should().BeNull();
    }

    [Fact]
    public void TheHoldsQuestions_AreReleasedWithTheHold()
    {
        var held = HeldOn(HoldTwo, "n2", "q3").State;

        RoundMachine.ApplyHumanDecision(held, HumanDecision.Continue).HoldQuestions.Should().BeEmpty("the hold is over; its questions are spent");
        RoundMachine.ApplyHumanDecision(held, HumanDecision.Discuss).HoldQuestions.Should().Equal(["n2", "q3"], "discuss keeps the hold, and its questions with it");
        ((Transition.Moved)RoundMachine.Resolve(held with { AwaitingResolve = true }, [], humanSaysProceed: true)).State.HoldQuestions
            .Should().BeEmpty("the person's override releases the hold the same way");
    }

    [Fact]
    public void AnAnswerToTheHoldsOwnNotice_IsTheHoldsAnswer()
    {
        Answered("q1", "continue", HoldOne.AddMinutes(5));

        CurrentAnswer.DecisionFor(HeldOn(HoldOne, "q1"), Escalations()).Should().Be(HumanDecision.Continue);
    }

    [Fact]
    public void AnAnswerToAnEarlierHold_IsNotTheCurrentHoldsAnswer_ThoughItIsTheNewestAnswered()
    {
        // Hold one was answered; hold two's notice is open and unanswered. The newest ANSWERED question
        // is hold one's, and it must not open hold two.
        Answered("q1", "continue", HoldOne.AddMinutes(5));
        Asked("q2", HoldTwo.AddMinutes(1));

        CurrentAnswer.DecisionFor(Held(HoldTwo), Escalations()).Should().Be(HumanDecision.None,
            "a 'keep going' given on the earlier hold has been spent; the current hold has no answer yet");
        CurrentAnswer.For(Held(HoldTwo), Escalations()).Should().BeNull();
    }

    [Fact]
    public void ASkippedRowAfterTheHold_ChangesNothingAboutWhoseAnswerItIs()
    {
        Answered("q1", "fix", HoldOne.AddMinutes(5));

        CurrentAnswer.DecisionFor(HeldOn(HoldOne, "q1") with { Rounds = Held(HoldOne, skippedAfter: true).Rounds }, Escalations())
            .Should().Be(HumanDecision.Fix, "a skip decided nothing and raised no hold; the hold is bound to its question, not to the trail");
    }

    [Fact]
    public void AnAnswerWithNoRoundToBelongTo_IsNone()
    {
        Answered("q1", "continue", HoldOne.AddMinutes(5));
        var noRounds = Held(HoldOne) with { Rounds = [] };

        CurrentAnswer.For(noRounds, Escalations()).Should().BeNull("no round ran, so there is no hold to answer");
    }

    [Fact]
    public void AnUnreadableTime_IsNoAnswer()
    {
        var escalations = Escalations();
        escalations.Notify(new EscalationQuestion("q3", "s1", "/repo", "main", "?", "", "en", "", [], "now"));
        File.WriteAllText(escalations.AnswerPath("q3"), """{"id":"q3","answer":"go","decision":"continue","answeredUtc":"now"}""");

        CurrentAnswer.DecisionFor(Held(HoldOne), escalations).Should().Be(HumanDecision.None);
    }
}
