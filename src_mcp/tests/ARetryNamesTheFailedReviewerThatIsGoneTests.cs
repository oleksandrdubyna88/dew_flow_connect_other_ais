using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The D23 retry asks the reviewers that failed in round 1 — and when one of them has been switched OFF
/// since, the retry says so rather than letting its failure vanish from the round.
/// </summary>
/// <remarks>
/// <para><b>The defect</b> (found by the epic 3 risk consultation 159f0397, verified against the code):
/// <c>FeatureSecondRound.OnlyTheFailed</c> kept the failed reviewers still on the roster and named the
/// rest of the ROSTER as not asked. A failed reviewer no longer on the roster was in neither list: with
/// codex and grok failed in round 1 and grok since switched off, the retry asked codex alone and nothing
/// in the reply or the audit line said what became of grok — a round 1 failure that dissolved by itself.</para>
/// <para>Switching a reviewer off is the person's decision, so the retry does not block on it; it makes
/// it explicit — the reviewer is named in <c>NotAsked</c> with the reason, which is what the reply's
/// <c>reviewers</c> line and the round's audit line render. The rule "no failed reviewer left → everyone"
/// stands, and the gone ones are named there too.</para>
/// </remarks>
public sealed class ARetryNamesTheFailedReviewerThatIsGoneTests
{
    private const string Plan = "todo/PLAN_feature_review.md";

    private static ReviewerWork Work(string provider) =>
        new(new ReviewerInvocation(provider, RoleCatalog.FeatureRole, new ProcessRequest("x", [], ".")));

    private static RoundWork Roster(params string[] enabled) => new([.. enabled.Select(Work)], []);

    private static ReviewerState State(string provider, string status) => new(provider, RoleCatalog.FeatureRole, status);

    /// <summary>A feature session admitted to its retry: round 1 recorded these reviewers as failed, the rest as done.</summary>
    private static PersistedSession Retrying(string[] failed, params string[] answered) =>
        new(
            new SessionState("s1", "/repo", SessionKey.FeatureBranch, PanelConfig.Uniform(2, 5))
            {
                Feature = Plan,
                Stage = Stage.FeatureReview,
                RoundsRunThisStage = 1,
                SecondRound = SecondRoundGround.ReviewerFailure,
            },
            [new RoundRecord(nameof(Stage.FeatureReview), 1, "revise", 0, "round one", new DateTime(2026, 9, 26, 10, 0, 0, DateTimeKind.Utc))
            {
                ReviewerStates = [.. failed.Select(p => State(p, ReviewerState.Failed)), .. answered.Select(p => State(p, ReviewerState.Done))],
            }]);

    private static IEnumerable<string> Providers(RoundWork work) => work.Reviewers.Select(w => w.Invocation.Provider);

    [Fact]
    public void AFailedReviewerSwitchedOffBeforeTheRetry_IsNamedAsNotAsked_WithTheReason()
    {
        var retry = FeatureSecondRound.OnlyTheFailed(Roster("codex"), Retrying(failed: ["codex", "grok"]));

        Providers(retry).Should().Equal(["codex"], "the failed reviewer that is still enabled is retried");
        var gone = retry.NotAsked.Should().ContainSingle(r => r.Role == "grok/FeatureReview",
            "grok failed in round 1 too; switching it off is the person's decision, and the retry must say what became of it").Subject;
        gone.Reason.Should().Contain("failed in round 1").And.Contain("no longer enabled").And.Contain("not retried");
    }

    /// <summary>What the caller and the log read: the reviewer sentence, which both the reply and the audit line render.</summary>
    [Fact]
    public void TheReplyAndTheAuditLine_NameIt()
    {
        var retry = FeatureSecondRound.OnlyTheFailed(Roster("codex"), Retrying(failed: ["codex", "grok"]));
        var codexAnswered = (Work("codex").Invocation, (ReviewerOutcome)new ReviewerOutcome.Ok(new NormalisedReview([], []), false));

        var sentence = ReviewerSummaryFactory.From([codexAnswered], null, retry.NotAsked).Sentence;

        sentence.Should().Contain("all 1 reviewers answered", "the retry itself went well");
        sentence.Should().Contain("grok/FeatureReview was not asked: failed in round 1 and is no longer enabled",
            "`reviewers` in the reply and the round's closing audit line are this sentence");
    }

    [Fact]
    public void WhenNoFailedReviewerIsLeftOnTheRoster_EveryoneIsAsked_AndTheGoneAreStillNamed()
    {
        var retry = FeatureSecondRound.OnlyTheFailed(Roster("gemini"), Retrying(failed: ["codex", "grok"], answered: "gemini"));

        Providers(retry).Should().Equal(["gemini"], "a roster edited between the rounds must not turn the retry into a proceed over nobody");
        retry.NotAsked.Select(r => r.Role).Should().BeEquivalentTo(["codex/FeatureReview", "grok/FeatureReview"],
            "the retry asks everyone AND says which failures it could not retry");
        retry.NotAsked.Should().OnlyContain(r => r.Reason.Contains("no longer enabled"));
    }

    [Fact]
    public void WhenEveryFailedReviewerIsStillEnabled_NobodyIsNamedGone()
    {
        var retry = FeatureSecondRound.OnlyTheFailed(Roster("codex", "grok", "gemini"), Retrying(failed: ["codex", "grok"], answered: "gemini"));

        Providers(retry).Should().Equal("codex", "grok");
        var notAsked = retry.NotAsked.Should().ContainSingle().Subject;
        notAsked.Role.Should().Be("gemini/FeatureReview");
        notAsked.Reason.Should().Contain("answered in round 1", "the one reviewer not asked is the one whose findings are carried");
    }

    [Fact]
    public void ARoundThatIsNotARetry_IsUntouched()
    {
        var fresh = Retrying(failed: ["codex"]) with { State = Retrying(failed: ["codex"]).State with { RoundsRunThisStage = 0, SecondRound = SecondRoundGround.None } };
        var work = Roster("gemini");

        FeatureSecondRound.OnlyTheFailed(work, fresh).Should().BeSameAs(work);
    }
}
