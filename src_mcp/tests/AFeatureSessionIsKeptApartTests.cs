using System.Text.Json;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A feature session is identified by the PLAN, lives under a branch no git ref can spell, and every
/// key already on disk is byte-identical to what it was.
/// </summary>
/// <remarks>
/// <para>S2.1 of the feature-review plan (§4.3). A feature review is not a branch review: the head
/// moves as fix pull requests land, so the head cannot be part of the key, and it is not a document
/// review either, because the plan is reviewed against CODE. So the session is keyed by the plan's
/// repository-relative path under the constant branch <see cref="SessionKey.FeatureBranch"/> — a
/// colon cannot appear in a git ref, so that segment collides with no branch anybody can have.</para>
/// <para>The load-bearing tests are the dull ones, exactly as they were for the document session: the
/// two- and three-argument keys are pinned by LITERAL, because every session file on this machine and
/// every other one is named by a hash of that string.</para>
/// </remarks>
public sealed class AFeatureSessionIsKeptApartTests
{
    private const string Plan = "todo/PLAN_feature_review.md";

    private static SessionState Fresh(Stage stage = Stage.PlanReview) =>
        new("s1", "/repo", "main", PanelConfig.Uniform(3, 5)) { Stage = stage };

    /// <summary>A feature session: the same state, under the feature branch, carrying its plan.</summary>
    private static SessionState Feature(Stage stage = Stage.FeatureReview) =>
        new("s1", "/repo", SessionKey.FeatureBranch, PanelConfig.Uniform(3, 5)) { Stage = stage, Feature = Plan };

    // ---------- the key ----------

    /// <summary>Pinned as LITERALS: the thing protected is every session file already written.</summary>
    [Fact]
    public void EveryExistingKey_IsExactlyWhatItHasAlwaysBeen()
    {
        SessionKey.For(@"C:\Work\Repo\", "feature/x").Should().Be("c:/work/repo#feature/x");
        SessionKey.For("/home/me/repo", " main ").Should().Be("/home/me/repo#main");
        SessionKey.For("/repo", "main", "docs/spec.md").Should().Be("/repo#main#docs/spec.md");
    }

    [Fact]
    public void AnEmptyFeature_ChangesNoKey()
    {
        SessionKey.For("/repo", "main", "", "").Should().Be(SessionKey.For("/repo", "main"));
        SessionKey.For("/repo", "main", "", "   ").Should().Be(SessionKey.For("/repo", "main"));
        SessionKey.For("/repo", "main", "docs/spec.md", "").Should().Be(SessionKey.For("/repo", "main", "docs/spec.md"));
    }

    [Fact]
    public void AFeatureSessionsKey_IsTheBranchKeyPlusItsOwnSegment()
    {
        var key = SessionKey.For("/repo", SessionKey.FeatureBranch, "", Plan);

        // The plan's path is keyed the way a document's is — folded on the filesystem that folds —
        // so the tail is the same helper's answer; the prefix and the segment marker are literal.
        key.Should().Be($"/repo#:feature#feature:{DocumentId.KeyOf(Plan)}");
        key.Should().StartWith("/repo#:feature#feature:");
    }

    /// <summary>The whole point of the segment: it can meet nothing already on disk.</summary>
    [Fact]
    public void AFeatureKey_CollidesWithNoBranchOrDocumentKey()
    {
        var feature = SessionKey.For("/repo", SessionKey.FeatureBranch, "", Plan);

        feature.Should().NotBe(SessionKey.For("/repo", "main"), "the branch's own session");
        feature.Should().NotBe(SessionKey.For("/repo", "main", Plan), "the plan reviewed as a DOCUMENT on main");
        feature.Should().NotBe(SessionKey.For("/repo", "feature"), "a branch somebody called 'feature'");
        feature.Should().NotBe(SessionKey.For("/repo", SessionKey.FeatureBranch), "the feature branch with no plan");
        feature.Should().NotBe(SessionKey.For("/repo", SessionKey.FeatureBranch, Plan),
            "the plan as a document under the feature branch is not the feature review of it");
    }

    [Fact]
    public void TwoPlans_AreTwoSessions() =>
        SessionKey.For("/repo", SessionKey.FeatureBranch, "", "todo/PLAN_a.md")
            .Should().NotBe(SessionKey.For("/repo", SessionKey.FeatureBranch, "", "todo/PLAN_b.md"));

    /// <summary>
    /// The branch segment is a string git refuses as a ref name, so no checkout can ever have it.
    /// </summary>
    /// <remarks>
    /// `git check-ref-format` forbids a colon anywhere in a ref (it is the refspec separator), which
    /// is why the constant carries one. Pinned by literal: it is written into every feature session
    /// file and every feature round's database row.
    /// </remarks>
    [Fact]
    public void TheFeatureBranch_IsAStringNoGitRefCanBe()
    {
        SessionKey.FeatureBranch.Should().Be(":feature");
        SessionKey.FeatureBranch.Should().Contain(":", "a colon is not allowed in a git ref name");
    }

    [Fact]
    public void ASessionWithNoFeature_IsNotAFeatureSession()
    {
        Fresh().Feature.Should().BeEmpty();
        Fresh().IsFeatureSession.Should().BeFalse();

        Feature().IsFeatureSession.Should().BeTrue();
        Feature().IsDocumentSession.Should().BeFalse("a feature session is not a document session either");
    }

    // ---------- the stage machine: BeginFeatureRound and its four refusals ----------

    [Fact]
    public void AFeatureRound_RunsOnAFeatureSession() =>
        RoundMachine.BeginFeatureRound(Feature()).Should().BeOfType<Transition.Moved>();

    /// <summary>There is no plan round before a feature review: the plan IS the input.</summary>
    [Fact]
    public void AFeatureRound_DoesNotWaitForAPlanRound() =>
        RoundMachine.BeginFeatureRound(Feature() with { PlanProceeded = false }).Should().BeOfType<Transition.Moved>();

    [Fact]
    public void AFeatureRound_OnACodeSession_IsRefusedByName() =>
        RoundMachine.BeginFeatureRound(Fresh(Stage.CodeReview) with { PlanProceeded = true })
            .Should().BeOfType<Transition.Refused>()
            .Which.Sentence.Should().Contain("review_feature");

    [Fact]
    public void AFeatureRound_RefusesWhileTheLastOneIsUnresolved() =>
        RoundMachine.BeginFeatureRound(Feature() with { AwaitingResolve = true })
            .Should().BeOfType<Transition.Refused>()
            .Which.Sentence.Should().Contain("resolve");

    [Fact]
    public void AFeatureRound_RefusesWhileAPersonIsBeingWaitedFor() =>
        RoundMachine.BeginFeatureRound(Feature() with { HumanGate = true })
            .Should().BeOfType<Transition.Refused>()
            .Which.Sentence.Should().Contain("ask_human");

    /// <summary>A finished review is finished — and the refusal names the door, `again: true`.</summary>
    [Fact]
    public void AFinishedFeatureReview_RefusesAndNamesTheDoor() =>
        RoundMachine.BeginFeatureRound(Feature(Stage.Done))
            .Should().BeOfType<Transition.Refused>()
            .Which.Sentence.Should().Contain("again: true").And.Contain("review_feature");

    /// <summary>Four refusals, four different sentences — nobody is sent to fix the wrong thing.</summary>
    [Fact]
    public void TheFourRefusals_AreFourDifferentSentences()
    {
        var sentences = new[]
        {
            RoundMachine.BeginFeatureRound(Fresh()),
            RoundMachine.BeginFeatureRound(Feature() with { HumanGate = true }),
            RoundMachine.BeginFeatureRound(Feature() with { AwaitingResolve = true }),
            RoundMachine.BeginFeatureRound(Feature(Stage.Done)),
        }.Select(t => t.Should().BeOfType<Transition.Refused>().Subject.Sentence).ToList();

        sentences.Distinct().Should().HaveCount(4);
    }

    /// <summary>The held gate is asked BEFORE the finished stage: un-ticking nothing reopens a person's decision.</summary>
    [Fact]
    public void AHeldGate_OutranksAFinishedStage() =>
        RoundMachine.BeginFeatureRound(Feature(Stage.Done) with { HumanGate = true })
            .Should().BeOfType<Transition.Refused>()
            .Which.Sentence.Should().Contain("ask_human");

    // ---------- again ----------

    [Fact]
    public void Again_ReopensAFinishedFeatureReview_WithAFreshBudget()
    {
        var finished = Feature(Stage.Done) with { RoundsRunThisStage = 2, EscalationsUsed = 1, AdvanceOnResolve = true };

        var reopened = RoundMachine.BeginFeatureRoundAgain(finished)
            .Should().BeOfType<Transition.Moved>().Subject.State;

        reopened.Stage.Should().Be(Stage.FeatureReview);
        reopened.RoundsRunThisStage.Should().Be(0, "round one of a fresh budget, not a round past an exhausted one");
        reopened.EscalationsUsed.Should().Be(0);
        reopened.AdvanceOnResolve.Should().BeFalse();
        reopened.Feature.Should().Be(Plan, "what the review is OF is kept");
        reopened.Rejections.Should().BeEquivalentTo(finished.Rejections, "and so are the standing rejections");
    }

    [Fact]
    public void Again_OnAnUnfinishedFeatureSession_IsSimplyBegun()
    {
        var open = Feature() with { RoundsRunThisStage = 1 };

        RoundMachine.BeginFeatureRoundAgain(open)
            .Should().BeOfType<Transition.Moved>()
            .Which.State.Should().BeSameAs(open, "asking again of a session that never closed changes nothing");
    }

    [Fact]
    public void Again_StillRefusesAHeldGate_AnUnresolvedRound_AndTheWrongKindOfSession()
    {
        RoundMachine.BeginFeatureRoundAgain(Feature(Stage.Done) with { HumanGate = true })
            .Should().BeOfType<Transition.Refused>().Which.Sentence.Should().Contain("ask_human");
        RoundMachine.BeginFeatureRoundAgain(Feature(Stage.Done) with { AwaitingResolve = true })
            .Should().BeOfType<Transition.Refused>().Which.Sentence.Should().Contain("resolve");
        RoundMachine.BeginFeatureRoundAgain(Fresh(Stage.Done) with { PlanProceeded = true })
            .Should().BeOfType<Transition.Refused>().Which.Sentence.Should().Contain("review_feature");
    }

    /// <summary>
    /// A round of another kind on a feature session is refused by NAME, as a document session refuses
    /// a code round — a session that changed kind under the caller would count one budget against
    /// two different jobs.
    /// </summary>
    [Fact]
    public void ARoundOfAnotherKind_OnAFeatureSession_IsRefusedByName()
    {
        RoundMachine.BeginPlanRound(Feature(Stage.PlanReview))
            .Should().BeOfType<Transition.Refused>().Which.Sentence.Should().Contain("review_feature");
        RoundMachine.BeginCodeRound(Feature(Stage.CodeReview) with { PlanProceeded = true })
            .Should().BeOfType<Transition.Refused>().Which.Sentence.Should().Contain("review_feature");
        RoundMachine.BeginDocumentRound(Feature() with { Document = "docs/spec.md" })
            .Should().BeOfType<Transition.Refused>().Which.Sentence.Should().Contain("review_feature");
    }

    // ---------- the recorded base (plan round, 2026-09-25) ----------

    [Fact]
    public void TheSameBase_IsTheSameReview() =>
        FeatureBases.WhyNot(recorded: "abc123", asked: "abc123", again: false).Should().BeEmpty();

    [Fact]
    public void ADifferentBase_IsRefused_NamingBothShas()
    {
        var why = FeatureBases.WhyNot(recorded: "abc123", asked: "def456", again: false);

        why.Should().Contain("abc123").And.Contain("def456", "a path alone cannot tell two release trains of one plan apart");
        why.Should().Contain("again: true", "a refusal with no door is a stall");
    }

    [Fact]
    public void NoRecordedBase_RefusesNothing() =>
        FeatureBases.WhyNot(recorded: "", asked: "def456", again: false).Should().BeEmpty(
            "the first round of a review has nothing to compare with");

    [Fact]
    public void Again_StartsAFreshReviewAgainstTheNewBase() =>
        FeatureBases.WhyNot(recorded: "abc123", asked: "def456", again: true).Should().BeEmpty();

    [Fact]
    public void TheBase_IsComparedAsASha_NotAsText() =>
        FeatureBases.WhyNot(recorded: "ABC123", asked: "abc123", again: false).Should().BeEmpty();

    // ---------- the store, the claim and the record ----------

    private static PersistedSession FeatureFile(string repo, string plan = Plan) =>
        new(new SessionState("f1", repo, SessionKey.FeatureBranch, new PanelConfig()) { Feature = plan, Stage = Stage.FeatureReview }, [])
        {
            OpenedUtc = DateTime.UtcNow,
            PlanText = "# the plan",
        };

    [Fact]
    public void AFeatureSession_IsSavedUnderItsOwnFile_AndLoadedBackByItsPlan()
    {
        using var dir = TempDir.For("coai-feature-store-");
        var store = new SessionStore(dir);

        store.Save(FeatureFile("/repo"));

        store.Load("/repo", SessionKey.FeatureBranch, "", Plan).Should().NotBeNull("the feature argument finds it");
        store.Exists("/repo", SessionKey.FeatureBranch, "", Plan).Should().BeTrue();
        store.Load("/repo", SessionKey.FeatureBranch).Should().BeNull("the branch's own session is a different file");
        store.Exists("/repo", SessionKey.FeatureBranch).Should().BeFalse();
        store.Load("/repo", SessionKey.FeatureBranch, "", "todo/PLAN_other.md").Should().BeNull("another plan is another session");
        store.FileFor("/repo", SessionKey.FeatureBranch, "", Plan)
            .Should().NotBe(store.FileFor("/repo", SessionKey.FeatureBranch));
    }

    /// <summary>
    /// The startup sweep re-saves a swept session under the key it was READ from — `Save` takes the
    /// feature off the state, so a feature session with a dead round lands back in its own file.
    /// </summary>
    [Fact]
    public void TheOrphanSweep_ReSavesAFeatureSessionUnderItsOwnKey()
    {
        using var dir = TempDir.For("coai-feature-sweep-");
        var store = new SessionStore(dir);
        var running = new RoundRecord("FeatureReview", 1, "running", 0, "0 of 1 answered", DateTime.UtcNow)
        {
            Status = RoundRecord.Running,
            RunnerPid = 999_999,
        };
        store.Save(FeatureFile("/repo") with { Rounds = [running] });

        store.SweepOrphanedRounds(_ => false).Should().Be(1);

        var swept = store.Load("/repo", SessionKey.FeatureBranch, "", Plan);
        swept.Should().NotBeNull("the sweep wrote it back where it read it");
        swept!.Rounds.Should().ContainSingle().Which.Status.Should().Be(RoundRecord.Interrupted);
        Directory.GetFiles(Path.Combine(dir, "sessions"), "session-*.json")
            .Should().ContainSingle("one session, one file — a sweep that saved under the branch key would leave two");
    }

    [Fact]
    public void AClaimOnAFeatureSession_IsItsOwn()
    {
        using var dir = TempDir.For("coai-feature-claim-");

        var feature = SessionClaim.FileFor(dir, "/repo", SessionKey.FeatureBranch, "", Plan);

        feature.Should().NotBe(SessionClaim.FileFor(dir, "/repo", SessionKey.FeatureBranch));
        feature.Should().NotBe(SessionClaim.FileFor(dir, "/repo", "main"));
        using var held = SessionClaim.TryTake(dir, "/repo", SessionKey.FeatureBranch, "", Plan);
        held.Should().NotBeNull();
        SessionClaim.TryTake(dir, "/repo", SessionKey.FeatureBranch, "", "todo/PLAN_other.md")
            .Should().NotBeNull("a claim on one plan does not hold another plan's review");
    }

    [Fact]
    public void TheRecordedBase_SurvivesTheFile_AndIsEmptyBeforeItWasRecorded()
    {
        var json = JsonSerializer.Serialize(FeatureFile("/repo") with { FeatureBase = "abc123" }, ServerJsonContext.Default.PersistedSession);
        var back = JsonSerializer.Deserialize(json, ServerJsonContext.Default.PersistedSession)!;

        back.FeatureBase.Should().Be("abc123");
        back.State.Feature.Should().Be(Plan);
        JsonSerializer.Deserialize("""{"state":{"sessionId":"s","repoPath":"/r","branch":"main","config":{}},"rounds":[]}""",
                ServerJsonContext.Default.PersistedSession)!
            .FeatureBase.Should().BeEmpty("a file written before the field said nothing about it");
    }
}
