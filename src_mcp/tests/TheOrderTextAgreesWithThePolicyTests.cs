using CoaiMcp.Core.Commands;
using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The order text and the gate read ONE constant (<c>todo/PLAN_question_consultant.md</c> §4, S3 acceptance 5):
/// the number the autonomy order tells the AI, the number the tool description tells it, and the number the
/// gate refuses on are <see cref="QuestionPolicy.FreeBatches"/> — never three copies that agree until one moves.
/// </summary>
public sealed class TheOrderTextAgreesWithThePolicyTests
{
    private static IReadOnlyList<string> Orders(QuestionMode mode, bool autonomous = true) =>
        GateCommands.For(new CommandContext(Autonomous: autonomous, PlanStage: true)
        {
            QuestionConsult = new QuestionConsultFacts(mode, QuestionPolicy.FreeBatches),
        });

    private static string TheQuestionOrder(QuestionMode mode) =>
        Orders(mode).Single(order => order.StartsWith(GateCommands.QuestionConsultMarker, StringComparison.Ordinal));

    [Fact]
    public void TheOrdersNumber_IsTheGatesConstant()
    {
        TheQuestionOrder(QuestionMode.Require).Should().Contain($"first {QuestionPolicy.FreeBatches} batches",
            "the order says the number the gate refuses on, filled from the one constant");
        CommandTexts.ShippedText(CommandTexts.QuestionConsult).Should().Contain("{freeBatches}",
            "the shipped text carries the placeholder, never a number a person would have to keep in step");
    }

    [Fact]
    public void TheSettingsDefault_IsTheSameConstant() =>
        QuestionConsultSettings.DefaultFreeBatches.Should().Be(QuestionPolicy.FreeBatches,
            "COAI_QCONSULT_FREE_BATCHES defaults to the policy; a person who changes nothing gets the policy");

    [Fact]
    public void TheToolDescriptionsNumber_IsTheSameConstant() =>
        TheGateSaysWhenToConsultTests.DescriptionOf("ask_human").Should().Contain($"the first {QuestionPolicy.FreeBatches} batches",
            "the third copy of the number — in the description a caller reads before any round — is held to the same constant");

    [Fact]
    public void TheQuestionOrder_SaysWhatTheServerEnforces_AndNamesTheBypass()
    {
        var require = TheQuestionOrder(QuestionMode.Require);

        require.Should().Contain("ask_consultants").And.Contain("ask_human").And.Contain("consultId");
        require.Should().Contain("productionRisk").And.Contain("riskReason", "A8: the bypass, spelled as the operator spelled it");
        require.Should().Contain("release", "and the phase it ends at");
        require.Should().ContainEquivalentOf("refuses", "require says the gate refuses");
    }

    [Fact]
    public void InRemind_TheOrderSaysItReminds_AndDoesNotThreatenARefusal()
    {
        var remind = TheQuestionOrder(QuestionMode.Remind);

        remind.Should().ContainEquivalentOf("remind");
        remind.Should().NotContainEquivalentOf("refuses");
    }

    [Fact]
    public void TheQuestionOrder_NeverSaysCritical()
    {
        foreach (var mode in new[] { QuestionMode.Remind, QuestionMode.Require })
        {
            TheQuestionOrder(mode).Should().NotContainEquivalentOf("critical", "A8: the canonical invented severity");
        }
    }

    [Fact]
    public void TheShippedIds_CarryTheNewText_AndTheFolderHasIt()
    {
        CommandTexts.ShippedIds.Should().Contain(CommandTexts.QuestionConsult);
        CommandTexts.QuestionConsult.Should().Be("command-question-consult");
    }
}
