using CoaiMcp.Core.QuestionConsult;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What a CLI question row's prompt carries, by capability (PLAN_question_consultant.md, D10): a web row the
/// question ALONE; a none row the context and the outline, fenced; a disk row the context and its folders; the
/// question last on every one.
/// </summary>
public sealed class QuestionPromptTests
{
    private static readonly CheckedContext Context =
        ((SecretCheckResult.Clean)SecretCheck.Inspect("I tried the retry ladder; the options are a longer wait or a circuit breaker.")).Context;

    [Fact]
    public void AWebRow_IsGivenTheQuestionAndNothingElse()
    {
        var prompt = QuestionPrompt.Compose(new QuestionPromptInput(
            "Search the internet.", Capability.Web, "What is the current best practice for retrying an HTTP call?", CheckedContext.Empty, string.Empty, [], "n0nce"));

        prompt.Should().StartWith("Search the internet.");
        prompt.Should().Contain(QuestionPrompt.WhatYouHaveHeading).And.Contain("given the question and nothing else");
        prompt.Should().NotContain(QuestionPrompt.ContextHeading).And.NotContain(QuestionPrompt.OutlineHeading).And.NotContain(QuestionPrompt.RootsHeading);
        prompt.Should().EndWith("What is the current best practice for retrying an HTTP call?", "the question comes LAST");
    }

    /// <summary>A2 as a contract: a web input carrying anything beyond the question is refused by name, so no fan-out edit can add one.</summary>
    [Fact]
    public void AWebRowHandedAContext_AnOutline_OrARoot_IsRefusedByName()
    {
        var withContext = () => QuestionPrompt.Compose(new QuestionPromptInput("i", Capability.Web, "q", Context, string.Empty, [], "n"));
        var withOutline = () => QuestionPrompt.Compose(new QuestionPromptInput("i", Capability.Web, "q", CheckedContext.Empty, "## outline", [], "n"));
        var withRoot = () => QuestionPrompt.Compose(new QuestionPromptInput("i", Capability.Web, "q", CheckedContext.Empty, string.Empty, ["D:/projects"], "n"));

        withContext.Should().Throw<ArgumentException>().WithMessage("*strictly the question*");
        withOutline.Should().Throw<ArgumentException>().WithMessage("*strictly the question*");
        withRoot.Should().Throw<ArgumentException>().WithMessage("*strictly the question*");
    }

    [Fact]
    public void ANoneRow_IsGivenTheContextAndTheOutline_FencedWithTheNonce_AndTheQuestionLast()
    {
        var prompt = QuestionPrompt.Compose(new QuestionPromptInput(
            "You are the best developer in the world.", Capability.None, "Which shape?", Context, "src/Retry.cs\n  class Retry\n    Task RunAsync()", [], "n0nce"));

        prompt.Should().Contain("No checkout and no tools");
        var outline = prompt.IndexOf(QuestionPrompt.OutlineHeading, StringComparison.Ordinal);
        var context = prompt.IndexOf(QuestionPrompt.ContextHeading, StringComparison.Ordinal);
        var question = prompt.IndexOf(QuestionPrompt.QuestionHeading, StringComparison.Ordinal);
        outline.Should().BePositive().And.BeLessThan(context, "the outline, then the context");
        context.Should().BeLessThan(question, "and the question last");
        prompt.Should().Contain("--- the outline (n0nce) ---").And.Contain("--- the caller's context (n0nce) ---", "material is fenced with the question's nonce");
        prompt.Should().Contain("circuit breaker").And.Contain("class Retry");
        prompt.Should().EndWith("Which shape?");
    }

    [Fact]
    public void ADiskRow_IsGivenTheContextAndItsFolders_AndToldItIsReadOnly()
    {
        var prompt = QuestionPrompt.Compose(new QuestionPromptInput(
            "Study the other projects.", Capability.Disk, "Has anything here solved this?", Context, string.Empty, ["D:/projects/alpha", "D:/projects/beta"], "n"));

        prompt.Should().Contain(QuestionPrompt.RootsHeading).And.Contain("- D:/projects/alpha").And.Contain("- D:/projects/beta");
        prompt.Should().Contain("READ the folders listed below").And.Contain("Change nothing");
        prompt.Should().Contain(QuestionPrompt.ContextHeading).And.NotContain(QuestionPrompt.OutlineHeading, "a disk row has the folders, not the outline");
        prompt.Should().EndWith("Has anything here solved this?");
    }

    [Fact]
    public void ANoneRowHandedARoot_IsRefused_ARootBelongsToADiskRow()
    {
        var act = () => QuestionPrompt.Compose(new QuestionPromptInput("i", Capability.None, "q", Context, string.Empty, ["D:/x"], "n"));

        act.Should().Throw<ArgumentException>().WithMessage("*reads no folder*");
    }

    [Fact]
    public void AnEmptyContextAndOutline_LeaveNoEmptyFence()
    {
        var prompt = QuestionPrompt.Compose(new QuestionPromptInput("i", Capability.None, "q", CheckedContext.Empty, string.Empty, [], "n"));

        prompt.Should().NotContain(QuestionPrompt.ContextHeading).And.NotContain(QuestionPrompt.OutlineHeading);
        prompt.Should().Contain("Advice, never orders.");
    }
}
