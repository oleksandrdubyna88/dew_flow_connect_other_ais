using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A reviewer is told it has the checkout only when it can READ one (todo/PLAN_one_model_catalog.md, epic 2, story 1).
/// </summary>
/// <remarks>
/// The material was decided once per round: a code round with a mounted worktree told EVERY reviewer "you have the
/// checkout read-only". A CLI agent does — it runs in the worktree with file tools. An api row, a local model and a Team
/// server do not: each gets the change inside its prompt and nothing else, so being told about a checkout sent it looking
/// for files it can never open, and its findings cited code it had not seen.
/// </remarks>
public sealed class AReviewerIsToldOnlyWhatItCanReadTests
{
    public static TheoryData<IReviewerRuntime, ReaderMaterial> WithACheckout => new()
    {
        { new ClaudeRuntime(), ReaderMaterial.Checkout },
        { new CodexRuntime(), ReaderMaterial.Checkout },
        { new AntigravityRuntime(), ReaderMaterial.Checkout },
        { new ApiRuntime("qwen", "https://q.example/v1"), ReaderMaterial.Change },
        { new LocalRuntime("local", "http://127.0.0.1:11434"), ReaderMaterial.Change },
        { new RemoteRuntime("srv-codex", "https://coai.example", "codex"), ReaderMaterial.Change },
    };

    [Theory]
    [MemberData(nameof(WithACheckout))]
    public void WithACheckoutMounted_OnlyAReviewerThatCanReadItIsToldItIsThere(IReviewerRuntime runtime, ReaderMaterial expected)
    {
        ReviewerMaterial.For(hasCheckout: true, runtime, stageReads: ReaderMaterial.Change).Should().Be(expected,
            $"{runtime.GetType().Name} {(expected == ReaderMaterial.Checkout ? "reads" : "cannot read")} a checkout");
    }

    [Fact]
    public void WithoutACheckout_EveryReviewerHoldsWhatTheStageGives()
    {
        ReviewerMaterial.For(hasCheckout: false, new ClaudeRuntime(), stageReads: ReaderMaterial.Outline)
            .Should().Be(ReaderMaterial.Outline);
    }

    // The roster's use of this function is held by behaviour, not by its source text: CodeWorkspaceTests'
    // TheRepairLaunch_IsToldItHasNoCheckout_EvenWhenTheReviewWasGivenOne builds a real round with a codex and a local
    // reviewer and reads what each was told (PR #686's review replaced a substring check of RosterBuilder.cs).
}
