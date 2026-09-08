using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A reviewer's deadline bounds the REVIEWER, not each launch it happens to make.
/// </summary>
/// <remarks>
/// <para><b>Found in the ledger, not by reading.</b> Over three days of real rounds, exactly one
/// reviewer ran past its budget: <c>remsoftdev-claude/UxDxPerformance</c>, 668.8 s against a
/// ten-minute setting, and it returned <c>ok</c>. The operator had already reported it as "the limit
/// did not work again, more than ten minutes have passed" and was right.</para>
/// <para><b>The same defect, on the other path.</b> `RetryLadder.Remaining` exists precisely for
/// this, and its own docstring describes it: "a retry used to carry the reviewer's whole timeout
/// again, so a first launch that spent nine minutes of a ten-minute deadline could be followed by a
/// second with ten more". That was fixed for the rate-limit retry. The REPAIR launch — the second
/// launch a reviewer makes when its first answer will not parse — still carried a full budget of its
/// own, so a reviewer could take twice its deadline and report success.</para>
/// <para>The scheduler measures a reviewer from when its slot is taken, so those 668.8 s are launch
/// time, not queueing: `watch` starts after all three semaphores are held.</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class RepairRunsInsideTheBudgetTests
{
    private readonly ReviewerExecutor _executor = new(new ProcessLauncher());

    /// <summary>A launch that burns time and returns nothing parseable, so a repair follows.</summary>
    private static ReviewerInvocation Slow(TimeSpan budget, int milliseconds) =>
        FakeCliInvocations.Invoke("vendor", ["sleep", milliseconds.ToString()], budget);

    [Fact]
    public async Task ARepairGetsWhatIsLEFTOfTheDeadline_NotAWholeSecondOne()
    {
        // Two seconds for the reviewer. The first launch spends 1.5 s of it and answers nothing, so
        // the repair is left half a second — and cannot finish its own 1.5 s of work.
        //
        // With the budget reset, the repair got two fresh seconds, finished comfortably, and the
        // reviewer took three seconds against a deadline of two. That is the shape of the 668.8 s
        // run, at a scale a test can wait for.
        var budget = TimeSpan.FromSeconds(2);
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var outcome = await _executor.RunAsync(
            Slow(budget, 1500), Slow(budget, 1500), TestContext.Current.CancellationToken);

        clock.Stop();
        outcome.Should().BeOfType<ReviewerOutcome.TimedOut>(
            "the repair ran out of the reviewer's deadline rather than starting a new one");
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2.9),
            "a reviewer must not take longer than the deadline it was given, whatever it spends it on");
    }

    [Fact]
    public async Task AFastFirstLaunch_LeavesTheRepairPlentyOfTime()
    {
        // The guard on the other side: shortening the repair must not starve one that had time.
        var outcome = await _executor.RunAsync(
            FakeCliInvocations.Invoke("vendor", ["sleep", "10"], TimeSpan.FromSeconds(10)),
            FakeCliInvocations.Invoke("vendor", ["emit", FakeCliInvocations.CleanReview], TimeSpan.FromSeconds(10)),
            TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ReviewerOutcome.Ok>()
            .Which.Repaired.Should().BeTrue("the repair had almost the whole budget and used it");
    }

    [Fact]
    public void ASpentBudget_LeavesNoTimeRatherThanNegativeTime()
    {
        // The arithmetic itself, at the edge a process cannot be given: never below zero.
        RetryLadder.Remaining(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10))
            .Should().Be(TimeSpan.Zero);
        RetryLadder.Remaining(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(10))
            .Should().Be(TimeSpan.FromSeconds(6));
    }
}
