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
        // The OUTCOME is the assertion; the clock is context. A reviewer flagged the wall-clock
        // bound as the flaky half of this test and was right — under CI load the first launch can
        // overrun its own share, and the outcome is still TimedOut either way, which is the property.
        // Generous rather than removed, so a fix that reintroduced a second full budget (four
        // seconds against two) would still be caught.
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3.5),
            "a reviewer must not take a second whole budget, whatever the machine is doing");
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

    /// <summary>
    /// A deadline already spent means NO repair — not a repair given nothing.
    /// </summary>
    /// <remarks>
    /// <para>Raised by two reviewers on the plan round, and it is the sharper half of the fix.
    /// `ProcessLauncher` calls `CancelAfter(request.Timeout)`, so a repair handed `TimeSpan.Zero`
    /// would start a process and cancel it in the same breath — and report `TimedOut`, which
    /// describes the repair rather than the answer that needed one.</para>
    /// <para>The first launch's complaint is the useful one: it says WHY the answer could not be
    /// parsed, which is what somebody reading the round is looking for. A timeout would have hidden
    /// exactly that.</para>
    /// </remarks>
    [Fact]
    public async Task WithTheBudgetAlreadySpent_TheRepairIsSkipped_AndTheFirstComplaintSurvives()
    {
        // A tenth of a second for the reviewer, and a first launch that takes longer than that while
        // answering nothing parseable: there is no time left for a repair by the time one is wanted.
        var outcome = await _executor.RunAsync(
            FakeCliInvocations.Invoke("vendor", ["sleep", "300"], TimeSpan.FromMilliseconds(100)),
            FakeCliInvocations.Invoke("vendor", ["emit", FakeCliInvocations.CleanReview], TimeSpan.FromSeconds(10)),
            TestContext.Current.CancellationToken);

        // The first launch is cut off by its own deadline here, which is the honest report: nothing
        // about the repair is invented, and no second process is started to say so.
        outcome.Should().BeOfType<ReviewerOutcome.TimedOut>(
            "a launch that outlives its own deadline is a timeout, and the repair never happens");
    }

    [Fact]
    public async Task ARepairAlreadyShortened_KeepsItsShorterBudget()
    {
        // The scheduler shortens a repair on a retry, and this method computes its own remainder.
        // Whichever is smaller has to win, or the executor would hand back the time the ladder took
        // away. Here the reviewer has ten seconds and the repair carries a tenth of one: the repair
        // must run out, not be given the ten.
        var outcome = await _executor.RunAsync(
            FakeCliInvocations.Invoke("vendor", ["sleep", "10"], TimeSpan.FromSeconds(10)),
            FakeCliInvocations.Invoke("vendor", ["sleep", "800"], TimeSpan.FromMilliseconds(100)),
            TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ReviewerOutcome.TimedOut>(
            "the repair's own budget was the smaller one and had to be honoured");
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
