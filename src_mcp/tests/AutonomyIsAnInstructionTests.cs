using Xunit;
using FluentAssertions;
using CoaiMcp.Core.Commands;

namespace CoaiMcp.Tests;

/// <summary>
/// "Work autonomously" is a set of concrete orders, not a mood.
/// </summary>
/// <remarks>
/// <para>The operator, 2026-09-05, over the checkbox: <i>"эта галочка должна говорить не просто работать
/// автономно, а давать чёткие инструкции"</i> — and listed them. An AI told only to "work
/// autonomously" fills in its own idea of what that means, and the idea that gets filled in is the
/// cheapest one: skip the tests, skip the docs, ship. Each order below is one the operator has had to
/// give by hand this week.</para>
/// <para><b>A6 (2026-10-01, <c>todo/PLAN_question_consultant.md</c> §0):</b> orders (4) and (5) — "do the
/// pull request process" and "verify an automatic deploy" — became ONE stage sequence the AI carries out
/// itself, end to end: the pull request, CodeRabbit's comments, the merge, the deploy, and the verification
/// ON THE SERVER. The two facts that held them apart are replaced by the three below; a sequence is asserted
/// as a sequence, because five per-word facts can all pass while the order is wrong.</para>
/// </remarks>
public sealed class AutonomyIsAnInstructionTests
{
    private static string TheOrder() =>
        GateCommands.For(new CommandContext(Autonomous: true, PlanStage: true, FirstPlanRound: true))
            .Single(c => c.Contains("AUTONOMOUSLY", StringComparison.Ordinal));

    [Fact]
    public void EveryBugGetsARedGreenTest() =>
        TheOrder().Should().ContainEquivalentOf("red").And.ContainEquivalentOf("green")
            .And.Contain("test", "a fix without a failing test first is a guess that compiled");

    [Fact]
    public void DocumentationReadmeAndManifestAreUpdated() =>
        TheOrder().Should().Contain("documentation").And.Contain("README").And.Contain("manifest");

    [Fact]
    public void EveryTestRunsBeforeARelease() =>
        TheOrder().Should().ContainEquivalentOf("ALL the tests").And.Contain("release");

    /// <summary>A6: the pull request is opened, CodeRabbit's comments are read and fixed, and it is MERGED — by the AI.</summary>
    [Fact]
    public void ThePullRequestIsOpened_CodeRabbitsCommentsAreFixed_AndItIsMerged() =>
        TheOrder().Should().Contain("pull request").And.Contain("five minutes").And.Contain("CodeRabbit")
            .And.Contain("merge", "a pull request left open is half the work handed back to the person it was meant to spare");

    /// <summary>A6: the deploy runs and is verified ON THE SERVER — no degradation, the new behaviour works, the logs read.</summary>
    [Fact]
    public void TheDeployRuns_AndIsVerifiedOnTheServer_NoDegradation_TheLogsRead() =>
        TheOrder().Should().Contain("deploy").And.Contain("on the server").And.Contain("no degradation")
            .And.Contain("works as expected").And.Contain("logs");

    /// <summary>A6 is a SEQUENCE: pull request → CodeRabbit → merge → deploy → verify, in that order, as one order.</summary>
    [Fact]
    public void TheStageSequence_IsOneOrder_InThatOrder()
    {
        var order = TheOrder();
        var at = new[] { "create the pull request", "CodeRabbit", "merge", "deploy", "verify" }
            .Select(word => (word, index: order.IndexOf(word, StringComparison.Ordinal)))
            .ToList();

        at.Should().AllSatisfy(found => found.index.Should().BeGreaterThan(-1, $"the order names '{found.word}'"));
        at.Select(found => found.index).Should().BeInAscendingOrder("the steps are carried out in the order they are written");
        order.Should().NotContain("dev, stage or test",
            "that was order (5)'s wording when the deploy was somebody else's and the AI only looked at it");
        order.Should().NotContain("automatic comments", "the comments are CodeRabbit's, named, and fixing what they name is the order");
    }

    [Fact]
    public void TheCodeIsReReadAgainstTheRules() =>
        TheOrder().Should().Contain("rules").And.ContainEquivalentOf("re-read");

    [Fact]
    public void ItSaysItIsAutonomous_AndWhatItIsWritingNow() =>
        TheOrder().Should().ContainEquivalentOf("say that you are working autonomously")
            .And.ContainEquivalentOf("what you are writing right now");

    [Fact]
    public void TheQuestionRuleIsStillThere() =>
        TheOrder().Should().Contain("END of your final summary", "the batching of questions was the original point of the switch");
}
