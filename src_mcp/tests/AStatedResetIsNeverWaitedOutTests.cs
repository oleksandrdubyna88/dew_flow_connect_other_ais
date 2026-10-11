using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A stated reset is never waited out locally — story 5.3c of <c>todo/PLAN_every_round_is_counted.md</c>, as E5's plan
/// round narrowed it.
/// </summary>
/// <remarks>
/// <para><b>What was wrong.</b> The rate-limit ladder (<c>BoundedScheduler.LadderAsync</c>) stops only for a SPENT
/// allowance, recognised by <c>RateLimit.Spent</c>'s words — <i>daily</i>, <i>exhausted</i>, <i>hit your usage
/// limit</i>. agy's verbatim refusal, <i>"Individual quota reached … Resets in 88h49m9s."</i>, says none of them, so a
/// local agy row climbed the whole ladder — four launches, every one refused — into a reset almost four days away.</para>
/// <para><b>What must hold.</b> A refusal whose STATED reset lies beyond what is left of the reviewer's own budget is
/// hopeless: one launch, no retry. A short stated reset inside the budget is still waited out by the existing ladder,
/// and a refusal that states no reset is treated exactly as before. The launcher is faked IN PROCESS
/// (<see cref="ScriptedResults"/>), so the real executor and the real ladder run — the seam rule of the plan's test plan.</para>
/// </remarks>
public sealed class AStatedResetIsNeverWaitedOutTests
{
    /// <summary>agy 1.3.3's refusal on stderr, verbatim (RESULTS_vendor_quota_gauges.md §3.1).</summary>
    private const string AgyQuotaLine =
        "error: Individual quota reached. Please upgrade your subscription to increase your limits. Resets in 88h49m9s.";

    private const string CleanReview = """{"findings": [], "input_tokens": 200, "output_tokens": 7}""";

    private static readonly IReadOnlyList<TimeSpan> QuickLadder =
        [TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1)];

    private static ReviewerInvocation Agy(TimeSpan budget) =>
        new("antigravity", RoleCatalog.ArchitectureRole, new ProcessRequest("agy", [], ".") { Timeout = budget });

    private static ProcessResult Refused(string stdErr) => new(3, string.Empty, stdErr, TimedOut: false);

    private static ProcessResult Answered() => new(0, CleanReview, string.Empty, TimedOut: false);

    private static async Task<ReviewerOutcome> Run(ScriptedResults launcher, TimeSpan budget)
    {
        var results = await new BoundedScheduler(retryLadder: QuickLadder)
            .RunAllAsync([new ReviewerWork(Agy(budget))], new ReviewerExecutor(launcher), TestContext.Current.CancellationToken);

        return results.Single().Outcome;
    }

    [Fact]
    public async Task AgysResetDaysAway_IsNotRetried_OneLaunchAndTheRefusal()
    {
        var launcher = new ScriptedResults(Refused(AgyQuotaLine));

        var outcome = await Run(launcher, TimeSpan.FromMinutes(10));

        launcher.Launched.Should().Be(1, "the vendor said the quota comes back in 88h49m9s, and the reviewer has ten minutes");
        var limited = outcome.Should().BeOfType<ReviewerOutcome.RateLimited>().Subject;
        limited.Attempts.Should().Be(1);
        limited.Reason.Should().Contain("Resets in 88h49m9s", "the person reads the vendor's own reset");
    }

    [Fact]
    public async Task AShortStatedReset_InsideTheBudget_IsStillWaitedOut()
    {
        var launcher = new ScriptedResults(Refused("error: Individual quota reached. Resets in 2s."), Answered());

        var outcome = await Run(launcher, TimeSpan.FromMinutes(1));

        launcher.Launched.Should().Be(2, "two seconds lie well inside a one-minute budget — the existing ladder waits them out");
        outcome.Should().BeOfType<ReviewerOutcome.Ok>();
    }

    [Fact]
    public async Task ARefusalThatStatesNoReset_ClimbsTheLadderAsBefore()
    {
        var launcher = new ScriptedResults(Refused("429 Too Many Requests"));

        var outcome = await Run(launcher, TimeSpan.FromMinutes(1));

        launcher.Launched.Should().Be(4, "a throttle that names no reset is what the ladder exists for: the first launch and three retries");
        outcome.Should().BeOfType<ReviewerOutcome.RateLimited>().Which.Attempts.Should().Be(4);
    }

    // ---------- the decision itself, pure ----------

    private static readonly DateTimeOffset Now = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(AgyQuotaLine, 600, true)]
    [InlineData("error: Individual quota reached. Resets in 2s.", 60, false)]
    [InlineData("Resets in 61s", 60, true)]
    [InlineData("Resets in 59s", 60, false)]
    [InlineData("429 Too Many Requests", 0, false)]
    [InlineData("resets 9:30pm (UTC)", 600, true)]
    public void AResetBeyondWhatIsLeft_OutlastsTheBudget(string reason, int leftSeconds, bool outlasts) =>
        RetryLadder.ResetOutlasts(reason, TimeSpan.FromSeconds(leftSeconds), Now).Should().Be(outlasts);
}
