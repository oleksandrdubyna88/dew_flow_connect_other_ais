using CoaiMcp.Core.Commands;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Every <see cref="Stage"/> answers by name — its roster, its phrase, its next stage, its
/// sentences — from one table, and the answers the two existing stages have always given are
/// pinned by literal.
/// </summary>
/// <remarks>
/// <para>§4.1 and §9 of the feature-review plan. Two switches were exhaustive and four swallowed an
/// unknown stage with a discard: <c>StageName</c> said "done", <c>ReviewKindOf</c> said "code",
/// <c>CommandStageOf</c> said <c>Any</c>, <c>Finish</c> said "The code stage is complete". A fourth
/// stage would have inherited every one of those without a compile error or an exception. The
/// enum walk below is what turns that into a red test.</para>
/// <para>The literal pins are the load-bearing half: every session file, round record and notice
/// already written carries these strings and buckets, and a test that derived the expected value
/// from the table under test would protect nothing.</para>
/// </remarks>
public sealed class StagesTests
{
    [Fact]
    public void EveryStage_HasARow_AndTheRowIsItsOwn()
    {
        foreach (var stage in Enum.GetValues<Stage>())
        {
            Stages.Of(stage).Stage.Should().Be(stage, $"{stage} must answer by name");
        }
    }

    [Fact]
    public void TheTable_HoldsEveryStageOnce_AndNothingElse() =>
        Stages.All.Select(d => d.Stage).Should().BeEquivalentTo(Enum.GetValues<Stage>(),
            "a row per stage, and no row for a stage the enum does not have");

    /// <summary>A stage nobody mapped is a defect, not a plan round.</summary>
    [Fact]
    public void AStageWithNoRow_Throws()
    {
        var act = () => Stages.Of((Stage)999);

        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*map it here*");
    }

    // ---------- the buckets, pinned by literal ----------

    [Fact]
    public void TheBuckets_AreExactlyWhatTheyHaveAlwaysBeen()
    {
        Stages.Of(Stage.PlanReview).Bucket.Should().Be(RoleBuckets.PlanCode);
        Stages.Of(Stage.CodeReview).Bucket.Should().Be(RoleBuckets.ResultCode);
        Stages.Of(Stage.DocumentReview).Bucket.Should().Be(RoleBuckets.ResultDocument);
        // Never asked for — a finished session runs no round — and answered rather than thrown
        // because `status` reads a finished session's budget through it.
        Stages.Of(Stage.Done).Bucket.Should().Be(RoleBuckets.PlanCode);
        // The fourth stage's own bucket: a programming task at a stage of its own (S2.1).
        Stages.Of(Stage.FeatureReview).Bucket.Should().Be(RoleBuckets.FeatureCode);
    }

    /// <summary>The one bridge to the string a role is persisted with still reads the table.</summary>
    [Fact]
    public void BucketFor_ReadsTheTable()
    {
        foreach (var stage in Enum.GetValues<Stage>())
        {
            PanelConfig.BucketFor(stage).Should().Be(Stages.Of(stage).Bucket);
        }
    }

    // ---------- the phrases ----------

    [Theory]
    [InlineData(Stage.PlanReview, "plan review")]
    [InlineData(Stage.CodeReview, "code review")]
    [InlineData(Stage.DocumentReview, "document review")]
    [InlineData(Stage.FeatureReview, "feature review")]
    [InlineData(Stage.Done, "done")]
    public void ThePhrase_IsHowAPersonSaysIt(Stage stage, string phrase)
    {
        Stages.Of(stage).Phrase.Should().Be(phrase);
        Stages.PhraseOf(stage.ToString()).Should().Be(phrase, "the persisted name resolves to the same row");
    }

    /// <summary>
    /// A persisted stage this build has no row for is said as ITSELF. It used to be "done", which
    /// is what a round written by a newer build would have been called.
    /// </summary>
    /// <remarks>
    /// The example was <c>FeatureReview</c> until S2.1 gave it a row; the fifth stage, whatever it
    /// is called, is the case this guards.
    /// </remarks>
    [Theory]
    [InlineData("ReleaseReview")]
    [InlineData("planreview")]
    [InlineData("")]
    public void APersistedStageWithNoRow_IsSaidAsItself_NeverAsDone(string persisted) =>
        Stages.PhraseOf(persisted).Should().Be(persisted);

    // ---------- the kind, the commands, the next stage, the sentences ----------

    [Theory]
    [InlineData(Stage.PlanReview, "plan")]
    [InlineData(Stage.CodeReview, "code")]
    [InlineData(Stage.DocumentReview, "document")]
    [InlineData(Stage.FeatureReview, "feature")]
    // What the discard answered for it; a finished session is asked for no refusal.
    [InlineData(Stage.Done, "code")]
    public void TheKind_IsTheAdjectiveInARefusal(Stage stage, string kind)
    {
        Stages.Of(stage).Kind.Should().Be(kind);
        RoundRefusals.ReviewKindOf(stage).Should().Be(kind, "the refusal reads the row");
    }

    [Theory]
    [InlineData(Stage.PlanReview, CommandStage.Plan)]
    [InlineData(Stage.CodeReview, CommandStage.Code)]
    [InlineData(Stage.DocumentReview, CommandStage.Any)]
    [InlineData(Stage.FeatureReview, CommandStage.Any)]
    [InlineData(Stage.Done, CommandStage.Any)]
    public void TheCommands_ARoundOfThisStageIsGiven(Stage stage, CommandStage commands)
    {
        Stages.Of(stage).Commands.Should().Be(commands);
        RoundCommands.CommandStageOf(stage).Should().Be(commands, "the panel service reads the row");
    }

    [Theory]
    [InlineData(Stage.PlanReview, Stage.CodeReview)]
    [InlineData(Stage.CodeReview, Stage.Done)]
    [InlineData(Stage.DocumentReview, Stage.Done)]
    [InlineData(Stage.FeatureReview, Stage.Done)]
    [InlineData(Stage.Done, Stage.Done)]
    public void WhereResolveMovesIt(Stage stage, Stage next) =>
        Stages.Of(stage).AdvancesTo.Should().Be(next);

    /// <summary>The state machine's advance reads the same row.</summary>
    [Theory]
    [InlineData(Stage.PlanReview, Stage.CodeReview)]
    [InlineData(Stage.CodeReview, Stage.Done)]
    [InlineData(Stage.DocumentReview, Stage.Done)]
    [InlineData(Stage.FeatureReview, Stage.Done)]
    public void Resolve_AdvancesToTheRowsNextStage(Stage stage, Stage next)
    {
        var awaiting = new SessionState("s1", "/repo", "main", PanelConfig.Uniform(3, 5))
        {
            Stage = stage,
            AwaitingResolve = true,
            AdvanceOnResolve = true,
            PlanProceeded = stage != Stage.PlanReview,
            Document = stage == Stage.DocumentReview ? "docs/spec.md" : string.Empty,
            Feature = stage == Stage.FeatureReview ? "todo/PLAN_x.md" : string.Empty,
        };

        RoundMachine.Resolve(awaiting, []).Should().BeOfType<Transition.Moved>()
            .Which.State.Stage.Should().Be(next);
    }

    [Fact]
    public void TheCompletedSentences_AreExactlyWhatTheyHaveAlwaysBeen()
    {
        Stages.Of(Stage.PlanReview).CompletedSentence.Should().Be(
            "The plan stage is complete. Implement the plan on the branch, then call review_code.");
        Stages.Of(Stage.CodeReview).CompletedSentence.Should().Be(
            "The code stage is complete. This session is done.");
    }

    /// <summary>The document stage's sentence names the document stage — §9.2's fix, pinned.</summary>
    [Fact]
    public void TheDocumentStagesCompletedSentence_NamesTheDocumentStage() =>
        Stages.Of(Stage.DocumentReview).CompletedSentence.Should()
            .Contain("document stage is complete").And.NotContain("code");

    /// <summary>Every stage a round can run has a sentence for the moment it passes.</summary>
    [Fact]
    public void EveryReviewStage_HasACompletedSentence()
    {
        foreach (var stage in Enum.GetValues<Stage>().Where(s => s != Stage.Done))
        {
            Stages.Of(stage).CompletedSentence.Should().NotBeEmpty($"{stage} passes and must say what comes next");
        }
    }

    /// <summary>What a `revise` verdict tells the caller — the same words for every stage whose fixes land on the branch.</summary>
    [Fact]
    public void TheReviseInstruction_IsWhatItHasAlwaysBeen()
    {
        foreach (var stage in Enum.GetValues<Stage>().Where(s => s != Stage.FeatureReview))
        {
            Stages.Of(stage).ReviseInstruction.Should().Be("fix the accepted ones, then run this review again");
        }
    }

    /// <summary>
    /// The feature stage is the one whose fixes land ELSEWHERE — as new pull requests, after the
    /// epics have merged — so its `revise` says so and says what to run again with.
    /// </summary>
    [Fact]
    public void TheFeatureStagesReviseInstruction_SendsTheFixesOutAsPullRequests() =>
        Stages.Of(Stage.FeatureReview).ReviseInstruction.Should()
            .Contain("pull request").And.Contain("head").And.NotBe(Stages.Of(Stage.CodeReview).ReviseInstruction);

    [Fact]
    public void TheFeatureStagesCompletedSentence_NamesTheFeatureStage() =>
        Stages.Of(Stage.FeatureReview).CompletedSentence.Should()
            .Contain("feature stage is complete").And.NotContain("code");

    [Theory]
    [InlineData(Stage.PlanReview, false)]
    [InlineData(Stage.CodeReview, true)]
    [InlineData(Stage.DocumentReview, false)]
    // The head it reviewed, so a later `again` can be refused when the head has not moved (D14).
    [InlineData(Stage.FeatureReview, true)]
    [InlineData(Stage.Done, false)]
    public void ARoundOverACommit_RecordsTheCommitItReviewed(Stage stage, bool recordsSha) =>
        Stages.Of(stage).RecordsSha.Should().Be(recordsSha);
}
