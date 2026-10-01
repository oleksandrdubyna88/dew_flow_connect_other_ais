using System.Collections.Immutable;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Core.Security;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

public sealed class SecurityCoverageTests
{
    private static ReviewerWork Work(bool engine = true) => new(new("qwen", "redteam-general",
        new ProcessRequest("fixture", [], "."), SharedResource: engine ? "engine" : ""), PromptBytes: 8000)
    { IsSecurity = true };

    [Theory]
    [InlineData(0, "unverified")]
    [InlineData(400, "incomplete")]
    [InlineData(3000, "unverified")]
    public void Plausible_or_missing_usage_never_proves_local_coverage(int reported, string expected)
    {
        var work = Work();
        var ok = new ReviewerOutcome.Ok(new([], []), false, new Usage(reported, 10, null));
        InputCoverage.Of(work, ok).Should().Contain(expected);
        SecurityRound.Clause([(work.Invocation, ok)], new([work], []) { SecurityActive = true })
            .Should().StartWith("Security lane incomplete");
    }

    [Fact]
    public void Aggregate_reproduction_overflow_is_a_failure_before_gate_counting()
    {
        var item = new Finding(Severity.Blocking, Category.Security, "fixture.cs", 1, "fixture", "reason", "fix", ["qwen"])
        {
            Reproduction = new(new string('x', 2000), new string('x', 2000), new string('x', 2000), new string('x', 2000)),
            AttackEvidence = new("fixture input", "fixture missing check", "fixture unintended output"),
        };
        var answer = new ReviewerOutcome.Ok(new(Enumerable.Repeat(item, 17).ToImmutableArray(), []), false);
        SecurityAnswerLimit.Apply(Work(), answer).Should().BeOfType<ReviewerOutcome.Unparseable>();
        var admitted = answer with { Review = new(Enumerable.Repeat(item, 16).ToImmutableArray(), []) { SecurityStatus = "FINDINGS" } };
        SecurityAnswerLimit.Apply(Work(), admitted).Should().BeOfType<ReviewerOutcome.Ok>();
    }

    [Fact]
    public void A_lane_cannot_impersonate_missing_ordinary_work()
    {
        var work = Work(false);
        (ReviewerInvocation, ReviewerOutcome)[] results = [(work.Invocation, new ReviewerOutcome.Ok(new([], []), false))];
        var summary = SecurityRound.DecisionSummary(results, new([work], []) { OrdinaryDue = true }, ReviewerSummaryFactory.From(results));
        summary.Answered.Should().Be(0);
    }

    [Fact]
    public void Engine_queue_and_lane_pairings_extend_the_outer_round_deadline()
    {
        var settings = new PanelSettings
        {
            Providers = [new("qwen") { Runtime = "local" }],
            LocalConcurrency = 1,
            ReviewerTimeout = TimeSpan.FromMinutes(2),
            SecurityLane = new() { Enabled = true, Runs = [new("qwen", "redteam-general", "slice", 24000, ["code"])] },
        };
        RoundDeadline.For(settings, Stage.CodeReview, 2, 0, Serilog.Core.Logger.None)
            .Should().BeGreaterThanOrEqualTo(TimeSpan.FromMinutes(6));
    }
}
