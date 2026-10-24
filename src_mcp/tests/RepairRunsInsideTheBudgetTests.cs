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
/// <para><b>On the manual clock (E5 S8, C35).</b> The two real 1.5 s launches against a 2 s budget, and the 3.5 s wall bound around
/// them, measured the scheduler rather than the property. The executor now reads one clock, the in-process
/// <see cref="ClockedLauncher"/> spends no real time, and the property is asserted as the exact number the repair is handed. That proves
/// budget ALLOCATION — what the executor hands each launch — and not the production timer's ENFORCEMENT, which
/// <c>ProcessLauncherTests.AChildThatNeverReadsItsStdinIsStillKilledOnTime</c> and its siblings keep.</para>
/// </remarks>
public sealed class RepairRunsInsideTheBudgetTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 10, 22, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A bound on the WAIT of a test, never a decision: a correct run reaches none of it (E4 plan §5.4 point 6).</summary>
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(30);

    private const string Junk = "not json at all";

    /// <summary>A launch that takes <paramref name="milliseconds"/> and says nothing, so a repair follows.</summary>
    private static ClockedStep Silent(int milliseconds) => new(TimeSpan.FromMilliseconds(milliseconds));

    private static ClockedStep Answers(int milliseconds) =>
        new(TimeSpan.FromMilliseconds(milliseconds), FakeCliInvocations.CleanReview);

    private static ReviewerInvocation Invocation(TimeSpan budget) =>
        FakeCliInvocations.Invoke("vendor", ["not run: the stand-in answers"], budget);

    /// <summary>
    /// Runs the reviewer to its outcome on the manual clock: this test's thread, never the launcher's, moves the clock once per launch
    /// (E4 plan §5.4 point 6). The ONE clock is handed to the executor and the driver alike — an executor built without it would read
    /// system time while the stand-in spends none.
    /// </summary>
    private static async Task<ReviewerOutcome> RunAsync(
        ClockedLauncher launcher, ManualClock clock, ReviewerInvocation first, ReviewerInvocation repair, CancellationToken ct)
    {
        using var guard = CancellationTokenSource.CreateLinkedTokenSource(ct);
        guard.CancelAfter(Guard);
        var run = new ReviewerExecutor(launcher, clock: clock).RunAsync(first, repair, guard.Token);
        try
        {
            await launcher.DriveAsync(run, clock, guard.Token);
        }
        finally
        {
            launcher.ReleaseAll();
        }

        return await run;
    }

    [Fact]
    public async Task ARepairGetsWhatIsLEFTOfTheDeadline_NotAWholeSecondOne()
    {
        // Two seconds for the reviewer. The first launch spends 1.5 s of it and answers nothing, so
        // the repair is left half a second — and cannot finish its own 1.5 s of work.
        //
        // With the budget reset, the repair got two fresh seconds, finished comfortably, and the
        // reviewer took three seconds against a deadline of two. That is the shape of the 668.8 s
        // run, at a scale a test can wait for.
        //
        // On the manual clock (E5 S8, C35): the stand-in spends no real time, so what the repair is handed is
        // the executor's arithmetic and nothing else — the request's own timeout IS the assertion, exact.
        var clock = new ManualClock(Epoch);
        var launcher = new ClockedLauncher([Silent(1500), Silent(1500)]);
        var budget = TimeSpan.FromSeconds(2);

        var outcome = await RunAsync(launcher, clock, Invocation(budget), Invocation(budget), TestContext.Current.CancellationToken);

        launcher.Timeouts[1].Should().Be(TimeSpan.FromMilliseconds(500),
            "the repair gets what is LEFT of a 2s budget after a 1.5s first launch");
        outcome.Should().BeOfType<ReviewerOutcome.TimedOut>(
            "the repair ran out of the reviewer's deadline rather than starting a new one");
        (clock.GetUtcNow() - Epoch).Should().Be(TimeSpan.FromSeconds(2), "1.5 s for the first launch and the 0.5 s the repair was allowed: never a second whole budget");
    }

    [Fact]
    public async Task AFastFirstLaunch_LeavesTheRepairPlentyOfTime()
    {
        // The guard on the other side: shortening the repair must not starve one that had time.
        var clock = new ManualClock(Epoch);
        var launcher = new ClockedLauncher([Silent(50), Answers(100)]);
        var budget = TimeSpan.FromSeconds(10);

        var outcome = await RunAsync(launcher, clock, Invocation(budget), Invocation(budget), TestContext.Current.CancellationToken);

        launcher.Timeouts[1].Should().Be(TimeSpan.FromMilliseconds(9950), "the repair had what the 50 ms first launch left of ten seconds");
        outcome.Should().BeOfType<ReviewerOutcome.Ok>()
            .Which.Repaired.Should().BeTrue("the repair had almost the whole budget and used it");
    }

    /// <summary>The first launch's duration and the repair's are recorded from the SAME clock the remainder is read from.</summary>
    [Fact]
    public async Task TheTwoLaunchDurationRecords_ReflectTheSameAdvancesTheRemainderWasReadFrom()
    {
        var clock = new ManualClock(Epoch);
        var launcher = new ClockedLauncher([Silent(300), Answers(200)]);
        var budget = TimeSpan.FromSeconds(10);

        var outcome = await RunAsync(launcher, clock, Invocation(budget), Invocation(budget), TestContext.Current.CancellationToken);

        var ok = outcome.Should().BeOfType<ReviewerOutcome.Ok>().Subject;
        ok.Launches.Select(part => part.Elapsed).Should().Equal(
            [TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(200)],
            "the first part's duration is what the driver advanced for the first launch, the second's what it advanced for the repair");
        launcher.Timeouts[1].Should().Be(TimeSpan.FromMilliseconds(9700), "and the remainder moved with the same 300 ms");
    }

    /// <summary>
    /// A deadline already spent means NO repair — not a repair given nothing.
    /// </summary>
    /// <remarks>
    /// <para>Raised by two reviewers on the plan round, and it is the sharper half of the fix.
    /// `ProcessLauncher` calls `CancelAfter(request.Timeout)`, so a repair handed `TimeSpan.Zero`
    /// would start a process and cancel it in the same breath — and report `TimedOut`, which
    /// describes the repair rather than the answer that needed one. The first launch's complaint is
    /// the useful one: it says WHY the answer could not be parsed, which is what somebody reading
    /// the round is looking for.</para>
    /// <para><b>Through the launcher seam, because the real one cannot reach this state.</b> The
    /// first draft used a real process and a 100 ms budget — and the code round pointed out that
    /// the launcher cancels at the deadline, so the first launch returns `TimedOut` and `RunAsync`
    /// exits before the new branch is ever reached. A vendor that answers slowly with junk is a
    /// thing the real launcher will not do on request; a fake is how it is asked. On the manual clock
    /// (E5 S8) that vendor is a step that <see cref="ClockedStep.Overruns"/> its timeout: it answers
    /// junk 120 ms in, against a 50 ms budget.</para>
    /// </remarks>
    [Fact]
    public async Task WithTheBudgetAlreadySpent_TheRepairIsSkipped_AndTheFirstComplaintSurvives()
    {
        var clock = new ManualClock(Epoch);
        var launcher = new ClockedLauncher([new ClockedStep(TimeSpan.FromMilliseconds(120), Junk, Overruns: true)]);
        var budget = TimeSpan.FromMilliseconds(50);

        var outcome = await RunAsync(launcher, clock, Invocation(budget), Invocation(budget), TestContext.Current.CancellationToken);

        launcher.Count.Should().Be(1, "there was no time left to launch a repair");
        var unparseable = outcome.Should().BeOfType<ReviewerOutcome.Unparseable>().Subject;
        unparseable.Reason.Should().Contain("deadline was spent",
            "the round summary renders this reason, so it is where a person learns time ran out");
        unparseable.Reason.Should().StartWith("the answer was not the schema's JSON",
            "the first launch's own complaint leads, because it says WHY a repair was wanted at all");
    }

    [Fact]
    public async Task ARepairAlreadyShortened_KeepsItsShorterBudget()
    {
        // The scheduler shortens a repair on a retry, and this method computes its own remainder.
        // Whichever is smaller has to win, or the executor would hand back the time the ladder took
        // away. Here the reviewer has ten seconds and the repair carries a tenth of one: the repair
        // must run out, not be given the ten.
        var clock = new ManualClock(Epoch);
        var launcher = new ClockedLauncher([Silent(10), Silent(800)]);

        var outcome = await RunAsync(
            launcher, clock, Invocation(TimeSpan.FromSeconds(10)), Invocation(TimeSpan.FromMilliseconds(100)), TestContext.Current.CancellationToken);

        launcher.Timeouts[1].Should().Be(TimeSpan.FromMilliseconds(100), "the repair's own, shorter, budget is the one it is launched with — not the 9.99 s the reviewer had left");
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
