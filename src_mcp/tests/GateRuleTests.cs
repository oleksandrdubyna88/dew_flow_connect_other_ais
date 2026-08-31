using System.Collections.Immutable;
using Xunit;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Gate;
using FluentAssertions;

namespace CoaiMcp.Tests;

public sealed class GateRuleTests
{
    private static Finding F(
        Severity severity,
        string title = "finding",
        string why = "because of the timing side channel",
        string file = "src/A.cs",
        int line = 10) =>
        new(severity, Category.Security, file, line, title, why, "fix", ["codex"]);

    [Fact]
    public void MinorsAndNits_NeverGate()
    {
        var tenNits = Enumerable.Range(0, 10)
            .Select(i => F(Severity.Nit, title: $"nit number {i}", line: i * 100))
            .ToImmutableArray();

        var result = GateRule.Evaluate(tenNits, [], threshold: 2);

        result.Passed.Should().BeTrue();
        result.GatingCount.Should().Be(0);
    }

    [Fact]
    public void ThresholdBoundary_AtThresholdPasses_OneOverRevises()
    {
        var two = ImmutableArray.Create(F(Severity.Major, title: "a"), F(Severity.Blocking, title: "b", line: 200));
        var three = two.Add(F(Severity.Major, title: "c", file: "src/B.cs"));

        GateRule.Evaluate(two, [], threshold: 2).Passed.Should().BeTrue();
        GateRule.Evaluate(three, [], threshold: 2).Passed.Should().BeFalse();
    }

    [Fact]
    public void RejectedWithReason_NotReRaised_DoesNotCount()
    {
        // Round 1: the finding was rejected with a reason. Round 2: the reviewer repeats it with
        // the same argument. Without rule 3 one disputed opinion blocks forever.
        var repeat = F(Severity.Major, why: "because of the timing side channel");
        var rejection = new PriorRejection(repeat, "constant-time comparison is not required for this token");

        var result = GateRule.Evaluate([repeat], [rejection], threshold: 0);

        result.Passed.Should().BeTrue();
        result.Discounted.Should().ContainSingle();
        result.Gating.Should().BeEmpty();
    }

    [Fact]
    public void RejectedButReRaisedWithNewArgument_CountsAgain()
    {
        var original = F(Severity.Major, why: "because of the timing side channel");
        var reRaised = original with { Why = "the comparison also runs before rate limiting, enabling brute force enumeration" };

        var result = GateRule.Evaluate([reRaised], [new PriorRejection(original, "not required here")], threshold: 0);

        result.Passed.Should().BeFalse();
        result.Gating.Should().ContainSingle();
        result.Discounted.Should().BeEmpty();
    }

    [Fact]
    public void DiscountsAreReported_NeverSilentlyDropped()
    {
        var finding = F(Severity.Blocking);
        var result = GateRule.Evaluate([finding], [new PriorRejection(finding, "disputed")], threshold: 0);

        result.Discounted.Should().ContainSingle().Which.Title.Should().Be(finding.Title);
    }
}
