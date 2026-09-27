using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog.Core;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The person's request for a feature review's second round (D23, the third ground) travels the ONE
/// road a person writes — the escalation answer file the panel and the phone produce — and nothing else.
/// </summary>
/// <remarks>
/// <para>What the engine reads before a feature round begins: the newest answer of the session, with its
/// button and its time. <c>continue</c> and <c>fix</c> are the request; a typed sentence and
/// <c>discuss</c> are not; and an answer OLDER than the round it would admit was about something else —
/// a person who granted a fresh set after a <c>call_human</c> has not asked for a second round of the
/// review that fresh set then ran. Without that clock an old "keep going" would reopen every later
/// review of the same plan on its own.</para>
/// <para>No MCP tool writes an answer file: <c>ask_human</c> writes the QUESTION, <c>resolve</c>'s
/// <c>humanDecision</c> is read by <c>RoundMachine.Resolve</c> and touches no ground. The end-to-end
/// proof that the caller cannot claim the request is in <c>AFeatureIsReviewedEndToEndTests</c>.</para>
/// </remarks>
public sealed class ThePersonAsksForASecondFeatureRoundTests : IDisposable
{
    private const string Plan = "todo/PLAN_feature_review.md";

    private readonly string _data = Directory.CreateTempSubdirectory("coai-persons-request-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_data, recursive: true);
        }
        catch (IOException) { }
    }

    private Escalations Escalations() => new(_data);

    /// <summary>
    /// A feature review closed after one round at the given moment, with no ground of its own — and the
    /// questions <c>ask_human</c> asked on it after that round, recorded as they were asked.
    /// </summary>
    private static PersistedSession ClosedAfterOneRound(DateTime completedUtc, params string[] askedAfterIt) =>
        new(
            new SessionState("s1", "/repo", SessionKey.FeatureBranch, PanelConfig.Uniform(2, 5)) { Feature = Plan, Stage = Stage.Done, RoundsRunThisStage = 1, RequestQuestions = askedAfterIt },
            [new RoundRecord(nameof(Stage.FeatureReview), 1, "good_enough", 3, "all 1 reviewers answered", completedUtc) { Sha = "abc" }]);

    private void Answered(string id, string decision, DateTime answeredUtc)
    {
        var escalations = Escalations();
        escalations.Notify(new EscalationQuestion(id, "s1", "/repo", SessionKey.FeatureBranch, "Review the feature again?", "", "en", "", [], answeredUtc.ToString("O")));
        File.WriteAllText(escalations.AnswerPath(id), $$"""{"id":"{{id}}","answer":"go","decision":"{{decision}}","answeredUtc":"{{answeredUtc:O}}"}""");
    }

    /// <summary>
    /// The request is bound to a question asked FOR it — one the AI asked on the feature session after
    /// round 1, recorded on the session as it was asked. A "continue" to any other question of the session
    /// — one asked before round 1, one an older build filed — is not the request, however new it is (the
    /// gate's finding #27, 2026-09-26: the clock alone let an unrelated answer admit round 2).
    /// </summary>
    [Theory]
    [InlineData("continue")]
    [InlineData("fix")]
    public void AnAnswerToAQuestionNotAskedForTheSecondRound_IsNotTheRequest_HoweverNewItIs(string decision)
    {
        var closed = ClosedAfterOneRound(new DateTime(2026, 9, 26, 10, 0, 0, DateTimeKind.Utc));
        Answered("q1", decision, new DateTime(2026, 9, 26, 10, 5, 0, DateTimeKind.Utc));

        PersonsRequest.Apply(closed, Escalations(), Logger.None).Should().BeSameAs(closed,
            "q1 is filed under the session but was not asked for its second round: nothing binds the answer to the request");
    }

    [Theory]
    [InlineData("continue")]
    [InlineData("fix")]
    public void AnAnswerToAQuestionAskedAfterRoundOne_IsThePersonsRequest_AndSpendsTheQuestions(string decision)
    {
        var closed = ClosedAfterOneRound(new DateTime(2026, 9, 26, 10, 0, 0, DateTimeKind.Utc), "q0", "q1");
        Answered("q1", decision, new DateTime(2026, 9, 26, 10, 5, 0, DateTimeKind.Utc));

        var asked = PersonsRequest.Apply(closed, Escalations(), Logger.None);

        asked.State.SecondRound.Should().Be(SecondRoundGround.PersonAsked);
        asked.State.RequestQuestions.Should().BeEmpty("the request is spent with the round it admits; nothing asked before it can admit another");
    }

    [Fact]
    public void TheNewestAnswerAmongTheSessionsOwnQuestions_IsTheOneRead()
    {
        var closed = ClosedAfterOneRound(new DateTime(2026, 9, 26, 10, 0, 0, DateTimeKind.Utc), "q1", "q2");
        Answered("q1", "continue", new DateTime(2026, 9, 26, 10, 5, 0, DateTimeKind.Utc));
        Answered("q2", "discuss", new DateTime(2026, 9, 26, 10, 6, 0, DateTimeKind.Utc));

        PersonsRequest.Apply(closed, Escalations(), Logger.None).Should().BeSameAs(closed,
            "the person's latest word on the session's own questions is 'discuss', which requests nothing");
    }

    [Fact]
    public void AnAnswerOlderThanTheRound_WasAboutSomethingElse()
    {
        var closed = ClosedAfterOneRound(new DateTime(2026, 9, 26, 10, 0, 0, DateTimeKind.Utc), "q2");
        Answered("q2", "continue", new DateTime(2026, 9, 26, 9, 55, 0, DateTimeKind.Utc));

        PersonsRequest.Apply(closed, Escalations(), Logger.None).Should().BeSameAs(closed,
            "a 'keep going' given before this round completed cannot be a request for its second — the one clock kept, as a fail-safe");
    }

    [Theory]
    [InlineData("discuss")]
    [InlineData("")]
    public void AnAnswerThatIsNotARequest_ChangesNothing(string decision)
    {
        var closed = ClosedAfterOneRound(new DateTime(2026, 9, 26, 10, 0, 0, DateTimeKind.Utc), "q3");
        Answered("q3", decision, new DateTime(2026, 9, 26, 10, 5, 0, DateTimeKind.Utc));

        PersonsRequest.Apply(closed, Escalations(), Logger.None).Should().BeSameAs(closed);
    }

    [Fact]
    public void NoAnswer_IsNoRequest()
    {
        var closed = ClosedAfterOneRound(DateTime.UtcNow, "q1");

        PersonsRequest.Apply(closed, Escalations(), Logger.None).Should().BeSameAs(closed);
    }

    /// <summary>The clock's fail-safe measures against the last round that RAN: a skipped row after it decided nothing.</summary>
    [Fact]
    public void ASkippedRowAfterRoundOne_IsNotTheRoundTheRequestIsMeasuredAgainst()
    {
        var completed = new DateTime(2026, 9, 26, 10, 0, 0, DateTimeKind.Utc);
        var closed = ClosedAfterOneRound(completed, "q1");
        var withASkip = closed with
        {
            Rounds = [.. closed.Rounds, new RoundRecord(nameof(Stage.FeatureReview), 2, RoundRecord.Skipped, 0, "no reviewer ran", completed.AddHours(2))],
        };
        Answered("q1", "continue", completed.AddMinutes(5));

        PersonsRequest.Apply(withASkip, Escalations(), Logger.None).State.SecondRound.Should().Be(SecondRoundGround.PersonAsked);
    }

    // ---------- where a question is recorded, and where it is not ----------

    [Fact]
    public void AQuestionAskedOnAFeatureSessionAfterRoundOne_IsRecordedAsARequestQuestion()
    {
        var state = ClosedAfterOneRound(DateTime.UtcNow, "q1").State;

        RoundMachine.RecordQuestion(state, "q2").RequestQuestions.Should().Equal(["q1", "q2"]);
        RoundMachine.RecordQuestion(state with { Stage = Stage.FeatureReview, SecondRound = SecondRoundGround.BlockingFinding }, "q2")
            .RequestQuestions.Should().Equal(["q1", "q2"], "recorded whatever the ground; whether it may be applied is the request's own check");
    }

    [Fact]
    public void AQuestionAskedBeforeRoundOne_OrOnAnotherKindOfSession_IsNotRecorded()
    {
        var beforeRoundOne = ClosedAfterOneRound(DateTime.UtcNow).State with { Stage = Stage.FeatureReview, RoundsRunThisStage = 0 };
        var code = new SessionState("s2", "/repo", "main", PanelConfig.Uniform(2, 5)) { Stage = Stage.CodeReview, RoundsRunThisStage = 1 };

        RoundMachine.RecordQuestion(beforeRoundOne, "q").Should().BeSameAs(beforeRoundOne, "an answer to a question asked before the round is about whatever it asked");
        RoundMachine.RecordQuestion(code, "q").Should().BeSameAs(code, "a code session has no second-round request");
    }

    [Fact]
    public void AQuestionAskedUnderAHold_IsTheHolds_NotARequests()
    {
        var held = ClosedAfterOneRound(DateTime.UtcNow).State with { Stage = Stage.FeatureReview, HumanGate = true };

        var recorded = RoundMachine.RecordQuestion(held, "q");

        recorded.HoldQuestions.Should().Equal(["q"]);
        recorded.RequestQuestions.Should().BeEmpty();
    }

    [Fact]
    public void TheRequestQuestions_AreResetWithTheCount()
    {
        var state = ClosedAfterOneRound(DateTime.UtcNow, "q1").State;

        RoundMachine.FreshFeatureReview(state).RequestQuestions.Should().BeEmpty("another base is another review");
        RoundMachine.ApplyHumanDecision(state with { HumanGate = true }, HumanDecision.Continue).RequestQuestions.Should().BeEmpty("a fresh set starts over");
    }

    [Fact]
    public void AnUnreadableTime_IsNoRequest()
    {
        var closed = ClosedAfterOneRound(new DateTime(2026, 9, 26, 10, 0, 0, DateTimeKind.Utc), "q4");
        var escalations = Escalations();
        escalations.Notify(new EscalationQuestion("q4", "s1", "/repo", SessionKey.FeatureBranch, "?", "", "en", "", [], "now"));
        File.WriteAllText(escalations.AnswerPath("q4"), """{"id":"q4","answer":"go","decision":"continue","answeredUtc":"now"}""");

        PersonsRequest.Apply(closed, escalations, Logger.None).Should().BeSameAs(closed, "a time that cannot be read cannot be newer");
    }
}
