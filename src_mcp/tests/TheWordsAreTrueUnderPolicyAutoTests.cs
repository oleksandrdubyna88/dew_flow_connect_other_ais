using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The static text an AI reads says what v2 does: nothing waits for a person, <c>ask_human</c> is an alias that points back to the
/// caller's own conversation, and four verdicts exist.
/// </summary>
/// <remarks>
/// The tool descriptions are read from the source of <c>Tools.cs</c>, the way <see cref="TheGateSaysWhenToConsultTests"/> reads them: the
/// registry needs a live host to enumerate, and this is a check about TEXT. Each pinned phrase is one a caller would act on.
/// </remarks>
public sealed class TheWordsAreTrueUnderPolicyAutoTests
{
    private static string AskHuman => TheGateSaysWhenToConsultTests.DescriptionOf("ask_human");

    private static string Resolve => TheGateSaysWhenToConsultTests.DescriptionOf("resolve");

    [Fact]
    public void AskHumanSaysItIsRemoved_AndWhatToDoInstead()
    {
        AskHuman.Should().Contain("Removed in v2").And.Contain("never waits for a person and writes no card")
            .And.Contain("`status: \"ask_in_conversation\"`")
            .And.Contain("Ask the person yourself, in this conversation")
            .And.NotContain("call_human").And.NotContain("no_answer_yet").And.NotContain("consult_first");
    }

    [Fact]
    public void ResolveSaysNothingHoldsTheGate_AndNamesNoHumanDecision()
    {
        Resolve.Should().Contain("No verdict holds the gate: `not_reviewed` and `good_enough` are resolved like `proceed`.")
            .And.NotContain("humanDecision").And.NotContain("notice card").And.NotContain("call_human");
    }

    [Fact]
    public void TheInstructionsSayNothingWaitsForAPerson_AndAskHumanIsRemoved()
    {
        Flat(Program.Instructions).Should().Contain("Nothing waits for a person: a round nobody answered says `not_reviewed`")
            .And.Contain("Nothing holds the gate: `not_reviewed` and `good_enough` advance on `resolve` like `proceed`")
            .And.Contain("`ask_human` is removed in v2 and only says so")
            .And.NotContain("call_human").And.NotContain("continue_anyway");
        Program.Instructions.Length.Should().BeLessThanOrEqualTo(2000, "a client keeps about 2 KiB and drops the rest");
    }

    [Fact]
    public void TheInstructionsSayWhichVerdictsLetAStageAdvance_AndReviewCodeWaitsForTheStage()
    {
        Flat(Program.Instructions).Should().Contain("Repeat until the verdict advances the stage (`proceed`, `good_enough` or `not_reviewed`)")
            .And.Contain("`review_code` REFUSES until the plan stage advanced")
            .And.NotContain("until a plan round reached `proceed`", "a round nobody answered ends not_reviewed, and the stage opens on resolve");
        Program.Instructions.Length.Should().BeLessThanOrEqualTo(2000);
    }

    [Fact]
    public void TheReviewDescriptionsNameOnlyTheFourVerdicts()
    {
        var reviewCode = Flat(TheGateSaysWhenToConsultTests.DescriptionOf("review_code"));
        var reviewPlan = Flat(TheGateSaysWhenToConsultTests.DescriptionOf("review_plan"));

        reviewCode.Should().Contain("REFUSES until the plan stage advanced")
            .And.Contain("`proceed`, `good_enough` or `not_reviewed`")
            .And.NotContain("continue_anyway").And.NotContain("until a plan round reached `proceed`");
        reviewPlan.Should().Contain("(proceed | revise | good_enough | not_reviewed)")
            .And.NotContain("call_human").And.NotContain("escalated").And.NotContain("continue_anyway");
    }

    private static string Flat(string text) => System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");

    [Fact]
    public void TheHelpNamesNoPolicySetting() =>
        Program.HelpText.Should().NotContain("COAI_ON_EXHAUSTED").And.NotContain("COAI_ESCALATION");
}
