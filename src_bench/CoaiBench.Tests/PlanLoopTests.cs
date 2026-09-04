using Xunit;
using FluentAssertions;
using CoaiBench.Running;

namespace CoaiBench.Tests;

/// <summary>
/// The plan stage repeats until it passes, because the code stage is refused until it has.
/// </summary>
/// <remarks>
/// The bench's first real campaign lost three of four code rounds to
/// <c>no plan round has reached 'proceed' in this session</c> — the product refusing correctly and
/// the harness measuring its own mistake. A caller that gets `revise` fixes the plan and asks again;
/// a bench cannot fix a plan, so it asks again until the SERVER's own round budget stops it.
/// </remarks>
public sealed class PlanLoopTests
{
    [Theory]
    [InlineData("proceed")]
    [InlineData("good_enough")]
    [InlineData("continue_anyway")]
    public void TheVerdictsThatOpenTheCodeStage(string verdict) =>
        RoundRunner.Passed(verdict).Should().BeTrue();

    [Theory]
    [InlineData("revise")]
    [InlineData("call_human")]
    [InlineData("")]
    public void AndTheOnesThatDoNot(string verdict) =>
        RoundRunner.Passed(verdict).Should().BeFalse(
            "asking the code gate after one of these is asking to be refused");
}
