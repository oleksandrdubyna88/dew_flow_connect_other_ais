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

    [Fact]
    public void AReleaseOrPullRequestIsMade_AndItsAutomaticCommentsAreRead() =>
        TheOrder().Should().Contain("pull request").And.Contain("five minutes").And.ContainEquivalentOf("automatic comments");

    [Fact]
    public void ADeployIsVerifiedAgainstTheEnvironment_AndItsLogsRead() =>
        TheOrder().Should().Contain("deploy").And.Contain("logs").And.ContainEquivalentOf("dev, stage or test");

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
