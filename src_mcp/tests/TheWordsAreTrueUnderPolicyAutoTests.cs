using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The static text an AI reads says what policy auto does (<c>todo/PLAN_auto_never_asks_a_person_E1.md</c> § 9.3, the parent's § 4.7): the
/// instructions are sent once at <c>initialize</c> and the policy can change mid-session, so every sentence is true under every policy.
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
    public void AskHumanSaysItNeverWaitsUnderAuto_AndWhatItAnswersInstead()
    {
        AskHuman.Should().Contain("UNDER POLICY `auto`").And.Contain("never waits and writes no card")
            .And.Contain("`status: \"consult_first\"`", "the AI's own question without a consultId")
            .And.Contain("ask the consultants", "the sentence says what to do next")
            .And.Contain("A held gate is answered `no_answer_yet` at once")
            .And.Contain("a hold left by a build before auto that could not be released yet says why — call again");
    }

    [Fact]
    public void AskHumanQualifiesTheWait_ForEveryOtherPolicy()
    {
        AskHuman.Should().Contain("under any other policy THIS CALL BLOCKS", "the wait is the other policies'")
            .And.Contain("under any other policy; under `auto` it comes at once, and its instruction says what to do instead")
            .And.NotContain("and THIS CALL BLOCKS until they answer", "that is the unqualified sentence this one replaces");
    }

    [Fact]
    public void AskHumanNamesFourStatuses_AndKeepsTheThingsOtherTestsPin()
    {
        AskHuman.Should().Contain("`status: \"consult_first\"`").And.Contain("`status: \"ask_in_conversation\"`")
            .And.Contain("`status: \"answered\"`").And.Contain("`status: \"no_answer_yet\"`");
        AskHuman.Should().Contain("15 minutes by default").And.Contain("expired");
    }

    [Fact]
    public void ResolveSaysWhatAutoDoesToAHold_AndKeepsTheRuleItAlwaysSaid()
    {
        Resolve.Should().Contain("Under a policy that asks a person, a call_human verdict holds the gate; policy `auto` raises no hold, "
                + "and `not_reviewed` and `good_enough` are resolved like `proceed`.")
            .And.Contain("Policy auto also releases a hold left by a build before auto, and every review call then says so: call resolve — "
                + "with your decisions, or none — and the stage advances, once.")
            .And.Contain("is not a decision").And.Contain("refused while a gate is held").And.Contain("answer on the notice card");
    }

    [Fact]
    public void TheInstructionsSayCallHumanComesOnlyFromAPolicyThatAsksAPerson()
    {
        Flat(Program.Instructions).Should().Contain("`call_human` comes only from a policy asking a person")
            .And.Contain("under the default `auto` an unreviewed round says `not_reviewed`")
            .And.Contain("an older build's hold is released, saying so");
        Program.Instructions.Length.Should().BeLessThanOrEqualTo(2000, "a client keeps about 2 KiB and drops the rest");
    }

    [Fact]
    public void TheInstructionsSayWhichVerdictsLetAStageAdvance_AndReviewCodeWaitsForTheStage()
    {
        Flat(Program.Instructions).Should().Contain("Repeat until the verdict advances the stage (`proceed`, `good_enough` or `not_reviewed`)")
            .And.Contain("`review_code` REFUSES until the plan stage advanced")
            .And.NotContain("until a plan round reached `proceed`", "under the default policy a round nobody answered ends not_reviewed, and the stage opens on resolve");
        Program.Instructions.Length.Should().BeLessThanOrEqualTo(2000);
    }

    [Fact]
    public void TheReviewCodeDescriptionDoesNotWaitForProceed_ItWaitsForTheStage()
    {
        var reviewCode = Flat(TheGateSaysWhenToConsultTests.DescriptionOf("review_code"));

        reviewCode.Should().Contain("REFUSES until the plan stage advanced")
            .And.Contain("`proceed`, `good_enough` or `not_reviewed`")
            .And.NotContain("until a plan round reached `proceed`");
    }

    private static string Flat(string text) => System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");

    [Fact]
    public void TheHelpNamesAllFiveValues_AndWhatAbsentMeans() =>
        Program.HelpText.Should().Contain("COAI_ON_EXHAUSTED (auto|human|continue|good_enough|escalate; absent = auto)");
}
