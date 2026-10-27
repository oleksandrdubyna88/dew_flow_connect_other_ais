using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The manual clock's contract (<c>todo/PLAN_the_suite_is_deterministic_and_leaves_no_temp_E4.md</c> §5.4, M1-M17): a clock a test
/// moves by hand whose timers fire ONLY inside <see cref="ManualClock.Advance"/>, on the advancing thread, in due order, outside the
/// clock's lock — so a deadline a test must race is a step the test takes, never a race with the machine's load.
/// </summary>
/// <remarks>
/// The reds are seen against a stub shaped like the deleted <c>MovableClock</c> (a hand-moved clock with REAL timers). A guard below
/// (<see cref="Guard"/>, <see cref="Within"/>) is a bound a correct run never reaches, never the thing that decides an outcome.
/// </remarks>
public sealed class ManualClockTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 21, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A bound a correct run never reaches: it turns a hang into a failure that names the broken rule.</summary>
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(20);

    private static TimeSpan Ms(int ms) => TimeSpan.FromMilliseconds(ms);

    private static TimeSpan S(int s) => TimeSpan.FromSeconds(s);

    /// <summary>Whether <paramref name="task"/> ended within <paramref name="limit"/>: an upper bound for a read that must NOT block, never a decision.</summary>
    private static async Task<bool> CompletesWithin(Task task, TimeSpan limit)
    {
        await Task.WhenAny(task, Task.Delay(limit));
        return task.IsCompleted;
    }

    private static async Task Within(Action arm, string sentence)
    {
        var running = Task.Run(arm);
        try
        {
            await running.WaitAsync(Guard);
        }
        catch (TimeoutException)
        {
            Assert.Fail(sentence);
        }
    }

    [Fact]
    public void ASourceWithADelayOnTheClock_IsCancelledByTheAdvanceThatReachesIt_AndNotBefore()
    {
        var clock = new ManualClock(T0);
        using var source = new CancellationTokenSource(S(2), clock);

        clock.Advance(Ms(1999));
        source.IsCancellationRequested.Should().BeFalse("1999 ms of a 2 s delay is not the delay");
        clock.Advance(Ms(1));

        source.IsCancellationRequested.Should().BeTrue("the clock was advanced past the delay");
    }

    [Fact]
    public async Task ATaskDelayOnTheClock_CompletesOnTheAdvanceThatReachesIt_AndNotBefore()
    {
        var clock = new ManualClock(T0);
        var delay = Task.Delay(Ms(700), clock, CancellationToken.None);

        clock.Advance(Ms(699));
        delay.IsCompleted.Should().BeFalse("699 ms of a 700 ms delay is not the delay");
        clock.Advance(Ms(1));

        delay.IsCompleted.Should().BeTrue("the clock was advanced past the delay");
        await delay;
    }

    [Fact]
    public async Task AWaitAsyncOnTheClock_ThrowsTimeoutOnTheAdvanceThatReachesIt_AndNotBefore()
    {
        var clock = new ManualClock(T0);
        var waiting = new TaskCompletionSource().Task.WaitAsync(S(1), clock);

        clock.Advance(Ms(999));
        waiting.IsCompleted.Should().BeFalse("999 ms of a 1 s wait is not the wait");
        clock.Advance(Ms(1));

        waiting.IsCompleted.Should().BeTrue("the clock was advanced past the wait");
        await FluentActions.Awaiting(() => waiting).Should().ThrowAsync<TimeoutException>("a wait that ran out says so");
    }

    [Fact]
    public void TwoTimersDueInOneAdvance_BothFire_EarliestDueFirst_WhateverOrderTheyWereArmedIn()
    {
        for (var run = 0; run < 20; run++)
        {
            var clock = new ManualClock(T0);
            var fired = new List<int>();
            using var later = clock.CreateTimer(_ => fired.Add(2), null, S(2), Timeout.InfiniteTimeSpan);
            using var sooner = clock.CreateTimer(_ => fired.Add(1), null, S(1), Timeout.InfiniteTimeSpan);

            clock.Advance(S(5));

            fired.Should().Equal([1, 2], "one advance fires every timer it reaches, earliest due first, not in the order they were armed");
        }
    }

    [Fact]
    public void ATimerArmedByACallbackWithADueTimeAlreadyReached_FiresInTheSameAdvance_AfterTheOneThatArmedIt()
    {
        var clock = new ManualClock(T0);
        var order = new List<string>();
        var made = new List<ITimer>();
        using var outer = clock.CreateTimer(
            _ =>
            {
                order.Add("outer");
                made.Add(clock.CreateTimer(_ => order.Add("inner"), null, TimeSpan.Zero, Timeout.InfiniteTimeSpan));
            },
            null,
            S(1),
            Timeout.InfiniteTimeSpan);

        clock.Advance(S(1));

        order.Should().Equal(["outer", "inner"], "a timer a callback arms with a due time already reached fires in the same advance, after the armer");
        made.ForEach(t => t.Dispose());
    }

    [Fact]
    public async Task ACallbackRunsOutsideTheClocksLock_OnTheAdvancingThread()
    {
        var clock = new ManualClock(T0);
        using var entered = new ManualResetEventSlim();
        using var gate = new ManualResetEventSlim();
        var callbackThread = 0;
        var advancerThread = 0;
        using var timer = clock.CreateTimer(
            _ =>
            {
                callbackThread = Environment.CurrentManagedThreadId;
                entered.Set();
                gate.Wait(Guard);
            },
            null,
            S(1),
            Timeout.InfiniteTimeSpan);
        var advancing = Task.Run(() =>
        {
            advancerThread = Environment.CurrentManagedThreadId;
            clock.Advance(S(1));
        });

        try
        {
            entered.Wait(Guard).Should().BeTrue("the due callback ran");
            var reading = Task.Run(() => (clock.GetUtcNow(), clock.ActiveTimers));

            (await CompletesWithin(reading, S(10))).Should().BeTrue("a callback blocked on a gate must not hold the clock's lock: another thread's read returns");
            callbackThread.Should().Be(advancerThread, "a callback runs on the thread that advanced the clock, never on the pool");
        }
        finally
        {
            gate.Set();
            await advancing;
        }
    }

    [Fact]
    public async Task DisposeRetiresATimerAtOnceWhileItsCallbackRuns_AndOnlyDisposeAsyncWaitsForTheCallbackToReturn()
    {
        var clock = new ManualClock(T0);
        using var entered = new ManualResetEventSlim();
        using var gate = new ManualResetEventSlim();
        var returned = false;
        var timer = clock.CreateTimer(
            _ =>
            {
                entered.Set();
                gate.Wait(Guard);
                returned = true;
            },
            null,
            S(1),
            Timeout.InfiniteTimeSpan);
        var advancing = Task.Run(() => clock.Advance(S(1)));

        try
        {
            entered.Wait(Guard).Should().BeTrue("the due callback ran");
            (await CompletesWithin(Task.Run(timer.Dispose), S(10))).Should().BeTrue("Dispose retires the timer at once: it does not wait for the running callback");
            var disposing = timer.DisposeAsync();

            disposing.IsCompleted.Should().BeFalse("DisposeAsync waits for a callback that has been picked and has not returned");
            gate.Set();
            await advancing;
            await disposing.AsTask().WaitAsync(Guard);
            returned.Should().BeTrue("DisposeAsync completed only after the callback returned");
        }
        finally
        {
            gate.Set();
        }
    }

    [Fact]
    public async Task AfterDispose_ChangeSchedulesNothingAndSaysFalse_AndAPeriodicTimerEndsItsWaitingTick()
    {
        var clock = new ManualClock(T0);
        var fired = 0;
        var timer = clock.CreateTimer(_ => fired++, null, S(1), Timeout.InfiniteTimeSpan);
        timer.Dispose();

        timer.Change(S(1), Timeout.InfiniteTimeSpan).Should().BeFalse("a disposed timer is not rescheduled, and says so");
        clock.ActiveTimers.Should().Be(0, "nothing was scheduled");
        clock.Advance(S(2));
        fired.Should().Be(0);

        using var periodic = new PeriodicTimer(Ms(100), clock);
        var tick = periodic.WaitForNextTickAsync().AsTask();
        clock.Advance(Ms(100));
        tick.IsCompleted.Should().BeTrue("the clock reached the period, so the tick arrives");
        (await tick).Should().BeTrue();

        var pending = periodic.WaitForNextTickAsync().AsTask();
        periodic.Dispose();
        (await pending.WaitAsync(Guard)).Should().BeFalse("disposing the periodic timer ends the wait with false");
    }

    [Fact]
    public void TheMonotonicReadingMovesWithAdvanceAndNotWithJumpWallClock_TheWallClockMovesWithBoth()
    {
        var clock = new ManualClock(T0);
        var stamp = clock.GetTimestamp();

        clock.JumpWallClock(TimeSpan.FromHours(1));

        clock.GetElapsedTime(stamp).Should().Be(TimeSpan.Zero, "a stepped wall clock is not elapsed monotonic time");
        (clock.GetUtcNow() - T0).Should().Be(TimeSpan.FromHours(1));
        clock.Advance(S(5));
        clock.GetElapsedTime(stamp).Should().Be(S(5));
        (clock.GetUtcNow() - T0).Should().Be(TimeSpan.FromHours(1) + S(5));
    }

    [Fact]
    public void ADueTimeOrPeriodTheSystemRefuses_IsRefusedByTheManualClockToo()
    {
        var tooLong = TimeSpan.FromMilliseconds(uint.MaxValue);
        var tooEarly = TimeSpan.FromMilliseconds(-2);
        foreach (TimeProvider provider in new TimeProvider[] { TimeProvider.System, new ManualClock(T0) })
        {
            foreach (var bad in new[] { tooLong, tooEarly })
            {
                FluentActions.Invoking(() => provider.CreateTimer(_ => { }, null, bad, Timeout.InfiniteTimeSpan))
                    .Should().Throw<ArgumentOutOfRangeException>($"{provider.GetType().Name} refuses a due time of {bad.TotalMilliseconds} ms");
                FluentActions.Invoking(() => provider.CreateTimer(_ => { }, null, S(1), bad))
                    .Should().Throw<ArgumentOutOfRangeException>($"{provider.GetType().Name} refuses a period of {bad.TotalMilliseconds} ms");
            }
        }
    }

    /// <summary>
    /// Judge round G3. A wait for timers the product never arms must not hang the test forever: it faults with a TimeoutException that names
    /// what was waited for and how many are armed. The OUTER bound here is the test's own net — a hang would surface as ITS message, not the named one.
    /// </summary>
    [Fact]
    public async Task WhenTimersAsync_FaultsNamingTheCountsWhenTheTimersAreNeverArmed_InsteadOfWaitingForever()
    {
        var clock = new ManualClock(T0);
        using var one = clock.CreateTimer(_ => { }, null, S(1), Timeout.InfiniteTimeSpan);

        var waiting = async () => await clock.WhenTimersAsync(2, within: Ms(200)).WaitAsync(Guard);

        (await waiting.Should().ThrowAsync<TimeoutException>()).WithMessage("expected at least 2 timer(s) armed within 0.2s; 1 are armed");
    }

    [Fact]
    public async Task WhenTimersAsync_IsCompletedByTheTimerArmedInTime_AndItsBoundIsTheSuitesGuardByDefault()
    {
        var clock = new ManualClock(T0);
        var waiting = clock.WhenTimersAsync(1, within: S(10));
        waiting.IsCompleted.Should().BeFalse("nothing is armed yet");

        using var timer = clock.CreateTimer(_ => { }, null, S(1), Timeout.InfiniteTimeSpan);
        await waiting.WaitAsync(Guard);

        ManualClock.DefaultTimerWait.Should().Be(TimeSpan.FromSeconds(30), "a wait without a bound of its own is held to the suite's 30 s guard");
    }

    [Fact]
    public void WhenTimersAsync_IsPendingUntilTheTimersAreArmed_AndTheCountDropsWhenOneFiresOrIsDisposed()
    {
        var clock = new ManualClock(T0);
        var first = clock.WhenTimersAsync(1);
        var second = clock.WhenTimersAsync(2);
        first.IsCompleted.Should().BeFalse("nothing is armed yet");
        clock.ActiveTimers.Should().Be(0);

        using var a = clock.CreateTimer(_ => { }, null, S(1), Timeout.InfiniteTimeSpan);
        first.IsCompleted.Should().BeTrue("the first timer was armed");
        second.IsCompleted.Should().BeFalse("only one of two is armed");
        using var b = clock.CreateTimer(_ => { }, null, S(2), Timeout.InfiniteTimeSpan);
        second.IsCompleted.Should().BeTrue("two are armed");
        clock.ActiveTimers.Should().Be(2);
        clock.WhenTimersAsync(2).IsCompleted.Should().BeTrue("already that many: it completes at once");

        clock.Advance(S(1));
        clock.ActiveTimers.Should().Be(1, "a timer that fired leaves the count");
        b.Dispose();
        clock.ActiveTimers.Should().Be(0, "a timer that was disposed leaves the count");
    }

    [Fact]
    public void APeriodicTimer_FiresOncePerPeriodReachedInOneAdvance_AndStaysScheduled()
    {
        var clock = new ManualClock(T0);
        var fired = 0;
        using var timer = clock.CreateTimer(_ => fired++, null, Ms(100), Ms(100));

        clock.Advance(Ms(300));
        fired.Should().Be(3, "three periods were reached inside one advance");
        clock.Advance(Ms(100));

        fired.Should().Be(4);
        clock.ActiveTimers.Should().Be(1, "a periodic timer stays scheduled");
    }

    [Fact]
    public async Task ASourceOnTheClockLinkedToACallersToken_ReachesItsDeadline_AndAdvanceReturns()
    {
        var clock = new ManualClock(T0);
        using var caller = new CancellationTokenSource();
        using var deadline = new CancellationTokenSource(S(2), clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller.Token, deadline.Token);

        await Within(
            () => clock.Advance(S(2)),
            "Advance never returned: the source disposes its own timer from inside the timer's callback, and Dispose must not wait for that callback");

        linked.IsCancellationRequested.Should().BeTrue("the deadline reached the linked token");
        caller.IsCancellationRequested.Should().BeFalse("the caller never cancelled");
    }

    [Fact]
    public async Task ATimerDisposedBetweenItsSelectionAndItsCallback_NeverRunsIt_AndDisposeAsyncOnAnotherThreadWaitsForARunningOne()
    {
        var clock = new ManualClock(T0);
        var ran = false;
        var selected = clock.CreateTimer(_ => ran = true, null, S(1), Timeout.InfiniteTimeSpan);
        clock.AfterPick = selected.Dispose;

        await Within(() => clock.Advance(S(1)), "Advance never returned: Dispose between the pick and the call must return at once");
        ran.Should().BeFalse("a disposed timer's callback does not run");

        clock.AfterPick = static () => { };
        using var entered = new ManualResetEventSlim();
        using var gate = new ManualResetEventSlim();
        var returned = false;
        var running = clock.CreateTimer(
            _ =>
            {
                entered.Set();
                gate.Wait(Guard);
                returned = true;
            },
            null,
            S(1),
            Timeout.InfiniteTimeSpan);
        var advancing = Task.Run(() => clock.Advance(S(1)));
        try
        {
            entered.Wait(Guard).Should().BeTrue("the due callback ran");
            var disposing = running.DisposeAsync();

            disposing.IsCompleted.Should().BeFalse("DisposeAsync from a thread other than the advancing one waits while the callback runs");
            gate.Set();
            await advancing;
            await disposing.AsTask().WaitAsync(Guard);
            returned.Should().BeTrue("and completes only after the callback returned");
        }
        finally
        {
            gate.Set();
        }
    }

    [Fact]
    public async Task DisposeAsyncFromInsideTheTimersOwnCallback_CompletesAtOnce()
    {
        var clock = new ManualClock(T0);
        var completedInside = false;
        var made = new List<ITimer>();
        made.Add(clock.CreateTimer(_ => completedInside = made[0].DisposeAsync().IsCompleted, null, S(1), Timeout.InfiniteTimeSpan));

        await Within(() => clock.Advance(S(1)), "Advance never returned: a timer's DisposeAsync from its own callback waited for itself");

        completedInside.Should().BeTrue("a disposal from inside the callback has nothing else to wait for");
    }

    [Fact]
    public async Task AnAdvanceInsideACallback_OrFromASecondThreadWhileOneRuns_IsRefusedNamingTheThreadThatAdvances()
    {
        var clock = new ManualClock(T0);
        var refused = new List<InvalidOperationException>();
        var fired = new List<string>();
        var advancerThread = 0;
        using var nested = clock.CreateTimer(
            _ =>
            {
                try
                {
                    clock.Advance(Ms(1));
                }
                catch (InvalidOperationException e)
                {
                    refused.Add(e);
                }

                fired.Add("nested");
            },
            null,
            S(1),
            Timeout.InfiniteTimeSpan);
        using var later = clock.CreateTimer(_ => fired.Add("later"), null, S(2), Timeout.InfiniteTimeSpan);
        using var entered = new ManualResetEventSlim();
        using var gate = new ManualResetEventSlim();
        using var parked = clock.CreateTimer(
            _ =>
            {
                entered.Set();
                gate.Wait(Guard);
            },
            null,
            S(3),
            Timeout.InfiniteTimeSpan);
        var advancing = Task.Run(() =>
        {
            advancerThread = Environment.CurrentManagedThreadId;
            clock.Advance(S(3));
        });

        try
        {
            entered.Wait(Guard).Should().BeTrue("the advance reached the third timer");
            var second = FluentActions.Invoking(() => Task.Run(() => clock.Advance(Ms(1))).GetAwaiter().GetResult());
            second.Should().Throw<InvalidOperationException>("a second thread may not advance while one advance runs")
                .Which.Message.Should().Contain(advancerThread.ToString(), "the refusal names the thread that holds the advance");
        }
        finally
        {
            gate.Set();
            await advancing;
        }

        refused.Should().ContainSingle("the nested advance was refused").Which.Message.Should().Contain(advancerThread.ToString());
        fired.Should().Equal(["nested", "later"], "the first advance still fired every timer it reached");
        (clock.GetUtcNow() - T0).Should().Be(S(3), "a refused advance moved nothing");
    }

    [Fact]
    public void AZeroDelay_IsAlreadyCancelledBeforeAnyAdvance_AndAZeroTimerFiresOnAdvanceZeroNotBefore()
    {
        var clock = new ManualClock(T0);
        using var source = new CancellationTokenSource(TimeSpan.Zero, clock);
        source.IsCancellationRequested.Should().BeTrue("a zero delay cancels in the constructor: nothing is scheduled");

        var fired = 0;
        using var timer = clock.CreateTimer(_ => fired++, null, TimeSpan.Zero, Timeout.InfiniteTimeSpan);
        fired.Should().Be(0, "a timer never fires on creation: only an advance fires it");
        clock.Advance(TimeSpan.Zero);

        fired.Should().Be(1, "Advance(0) fires a timer that is due now");
    }

    [Fact]
    public async Task OnTheTwoSourceShape_TheCallersCancellationAndTheDeadlineAreToldApart_AndADisposedTimerFiresNothing()
    {
        var callerFirst = new ManualClock(T0);
        using (var caller = new CancellationTokenSource())
        using (var timer = new CancellationTokenSource(S(2), callerFirst))
        using (var linked = CancellationTokenSource.CreateLinkedTokenSource(caller.Token, timer.Token))
        {
            await caller.CancelAsync();
            linked.IsCancellationRequested.Should().BeTrue("the caller's cancellation reaches the link");
            caller.Token.IsCancellationRequested.Should().BeTrue("so the product reads it as the caller's");
            timer.IsCancellationRequested.Should().BeFalse("the deadline did not strike");
            callerFirst.ActiveTimers.Should().Be(1, "the deadline's timer is still counted");
        }

        var deadlineFirst = new ManualClock(T0);
        using (var caller = new CancellationTokenSource())
        using (var timer = new CancellationTokenSource(S(2), deadlineFirst))
        using (var linked = CancellationTokenSource.CreateLinkedTokenSource(caller.Token, timer.Token))
        {
            deadlineFirst.Advance(S(2));
            linked.IsCancellationRequested.Should().BeTrue("the deadline reached the link");
            caller.Token.IsCancellationRequested.Should().BeFalse("so the product reads it as the deadline's");
        }

        var disposed = new ManualClock(T0);
        var cancelled = 0;
        using (var caller = new CancellationTokenSource())
        {
            using var timer = new CancellationTokenSource(S(2), disposed);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller.Token, timer.Token);
            using var watch = linked.Token.Register(() => cancelled++);
            disposed.ActiveTimers.Should().Be(1);
            linked.Dispose();
            timer.Dispose();
        }

        disposed.ActiveTimers.Should().Be(0, "disposing the source takes its timer off the clock");
        disposed.Advance(S(3));
        cancelled.Should().Be(0, "a disposed deadline fires nothing");
    }

    /// <summary>
    /// Judge round L4a. An advance the clock cannot hold (past <see cref="DateTimeOffset.MaxValue"/>) is refused BEFORE the clock records that
    /// it is advancing — otherwise the refusal leaves the advancing mark set and every later, valid <c>Advance</c> throws "entered while thread N
    /// is advancing", wedging a clock whose only fault was one bad argument.
    /// </summary>
    [Fact]
    public void AnAdvanceTheClockCannotHold_IsRefusedAndMovesNothing_SoALaterValidAdvanceStillWorks()
    {
        var clock = new ManualClock(T0);
        var fired = 0;
        using var timer = clock.CreateTimer(_ => fired++, null, S(1), Timeout.InfiniteTimeSpan);

        var overflowing = () => clock.Advance(TimeSpan.MaxValue);

        overflowing.Should().Throw<ArgumentOutOfRangeException>("a wall clock cannot be moved past the last instant");
        clock.GetUtcNow().Should().Be(T0, "a refused advance moves neither reading");
        clock.Advance(S(1));
        fired.Should().Be(1, "the clock is not wedged by the refused advance: the next valid advance moves it and fires the due timer");
        clock.GetUtcNow().Should().Be(T0 + S(1));
    }
}
