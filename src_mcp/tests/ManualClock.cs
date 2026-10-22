namespace CoaiMcp.Tests;

/// <summary>
/// The suite's ONE manual clock: a wall clock and a monotonic reading a test moves by hand, and timers that fire only when it does
/// (<c>todo/PLAN_the_suite_is_deterministic_and_leaves_no_temp_E4.md</c> §5.4; <c>ManualClockTests</c> holds each point).
/// </summary>
/// <remarks>
/// <para>Shaped on wsl_care's <c>ManualTimeProvider</c>, with its two flags gone (a hand-moved clock whose monotonic reading or timers
/// are real is the defect class E4 removes) and two additions: <see cref="WhenTimersAsync"/>, the event a test waits on before it
/// advances, and the refusal of a nested or concurrent <see cref="Advance"/>. It subsumes the old <c>MovableClock</c> (same constructor,
/// same <see cref="Advance"/>) and replaces nothing else: <c>FixedClock</c> and <c>SteppingClock</c> do other jobs.</para>
/// <para>Contract in one breath: <see cref="Advance"/> moves both readings, then fires every due timer — earliest due first, ties in
/// arming order — each callback synchronously on the advancing thread and outside the clock's lock. <see cref="Advance"/> is the
/// TEST DRIVER's call: a product hook only signals (<see cref="ClockHandshake"/>); an advance entered while another runs throws.
/// <c>ITimer.Dispose()</c> retires a timer at once, even from inside its own callback (a <see cref="CancellationTokenSource"/> disposes
/// its timer from there); only <c>DisposeAsync()</c> waits for a callback already picked.</para>
/// </remarks>
internal sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    private readonly Lock _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private readonly List<(int AtLeast, TaskCompletionSource Done)> _waiters = [];
    private DateTimeOffset _now = start;

    /// <summary>One day in, so no due-time arithmetic crosses zero.</summary>
    private TimeSpan _monotonic = TimeSpan.FromDays(1);

    private long _armings;
    private int _advancingThread;
    private string _advancingName = string.Empty;

    /// <summary>
    /// A hook between a timer's SELECTION and its callback, on the advancing thread — the one window a test cannot otherwise reach
    /// (M14: a timer disposed there never runs). A no-op by default; a test sets it and puts it back.
    /// </summary>
    internal Action AfterPick { get; set; } = static () => { };

    /// <summary>How many timers made on this clock are scheduled.</summary>
    public int ActiveTimers
    {
        get
        {
            lock (_gate)
            {
                return _timers.Count;
            }
        }
    }

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _now;
        }
    }

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp()
    {
        lock (_gate)
        {
            return _monotonic.Ticks;
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    /// <summary>
    /// Completes when at least <paramref name="atLeast"/> timers are scheduled, at once if they already are: the EVENT a test waits on
    /// ("the product has armed its deadline") before it advances.
    /// </summary>
    public Task WhenTimersAsync(int atLeast)
    {
        lock (_gate)
        {
            if (_timers.Count >= atLeast)
            {
                return Task.CompletedTask;
            }

            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Add((atLeast, done));
            return done.Task;
        }
    }

    /// <summary>The wall clock alone moves (the monotonic reading does not): a host that slept, a clock stepped forward.</summary>
    public void JumpWallClock(TimeSpan by)
    {
        lock (_gate)
        {
            _now += by;
        }
    }

    /// <summary>
    /// Moves both readings by <paramref name="by"/>, then fires every timer that is due. The TEST DRIVER's call — never a product
    /// thread's: a second advance, nested in a callback or from another thread, throws and moves nothing.
    /// </summary>
    public void Advance(TimeSpan by)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(by, TimeSpan.Zero);
        Move(by);
        try
        {
            FireDueTimers();
        }
        finally
        {
            lock (_gate)
            {
                _advancingThread = 0;
            }
        }
    }

    private void Move(TimeSpan by)
    {
        lock (_gate)
        {
            if (_advancingThread != 0)
            {
                throw new InvalidOperationException(
                    $"Advance was entered while thread {_advancingThread} ({_advancingName}) is advancing this clock: Advance is the test driver's call, "
                    + "never a product hook's or a callback's — a hook signals a ClockHandshake and the test advances.");
            }

            // Both new readings FIRST: an advance the clock cannot hold throws here, before the advancing mark is set, so a refused advance moves
            // nothing and wedges nothing (judge round L4a) — the next valid Advance is not "nested".
            var now = _now + by;
            var monotonic = _monotonic + by;
            _advancingThread = Environment.CurrentManagedThreadId;
            _advancingName = Thread.CurrentThread.Name ?? "unnamed";
            _now = now;
            _monotonic = monotonic;
        }
    }

    /// <summary>Each due timer fires on this thread, outside the lock, and the next is picked only after it returned.</summary>
    private void FireDueTimers()
    {
        while (Pick() is [var due])
        {
            AfterPick();
            due.Fire();
        }
    }

    /// <summary>The earliest due timer (ties: the one armed first), already marked as picked — or none.</summary>
    private ManualTimer[] Pick()
    {
        lock (_gate)
        {
            var due = _timers.Where(t => t.DueAt <= _monotonic).OrderBy(t => t.DueAt).ThenBy(t => t.Arming).Take(1).ToArray();
            due.ToList().ForEach(t => t.Picked());
            return due;
        }
    }

    /// <summary>Under the lock: a timer was added or removed; releases the waiters that now have enough timers.</summary>
    private void CountChanged()
    {
        var met = _waiters.Where(w => _timers.Count >= w.AtLeast).ToList();
        foreach (var waiter in met)
        {
            _waiters.Remove(waiter);
            waiter.Done.TrySetResult();
        }
    }

    /// <summary>A timer on this clock: its due time is on the monotonic reading, and it fires only inside <see cref="Advance"/>.</summary>
    private sealed class ManualTimer(ManualClock clock, TimerCallback callback, object? state) : ITimer
    {
        /// <summary>The longest due time or period the system's timer takes: 0xfffffffe ms.</summary>
        private static readonly TimeSpan Longest = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

        private bool _disposed;
        private int _running;
        private int _runningOn;
        private TaskCompletionSource _idle = Idle();

        public TimeSpan DueAt { get; private set; }

        public TimeSpan Period { get; private set; }

        public long Arming { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            Refuse(dueTime, nameof(dueTime));
            Refuse(period, nameof(period));
            lock (clock._gate)
            {
                return !_disposed && Reschedule(dueTime, period);
            }
        }

        private static void Refuse(TimeSpan time, string name)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(time, Timeout.InfiniteTimeSpan, name);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(time, Longest, name);
        }

        /// <summary>Under the lock: due <paramref name="dueTime"/> from now, or not scheduled when it is infinite.</summary>
        private bool Reschedule(TimeSpan dueTime, TimeSpan period)
        {
            clock._timers.Remove(this);
            Period = period;
            if (dueTime != Timeout.InfiniteTimeSpan)
            {
                DueAt = clock._monotonic + dueTime;
                Arming = ++clock._armings;
                clock._timers.Add(this);
            }

            clock.CountChanged();
            return true;
        }

        /// <summary>Under the lock, when an advance selects this timer: a one-shot leaves, a periodic one is due a period later.</summary>
        public void Picked()
        {
            if (Period <= TimeSpan.Zero || Period == Timeout.InfiniteTimeSpan)
            {
                clock._timers.Remove(this);
                clock.CountChanged();
            }
            else
            {
                DueAt += Period;
            }

            Begin();
        }

        private void Begin()
        {
            if (_running++ == 0)
            {
                _idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            _runningOn = Environment.CurrentManagedThreadId;
        }

        /// <summary>The callback, on the advancing thread and outside the lock; skipped when the timer was disposed after it was picked.</summary>
        public void Fire()
        {
            try
            {
                if (!Disposed())
                {
                    callback(state);
                }
            }
            finally
            {
                Returned();
            }
        }

        private bool Disposed()
        {
            lock (clock._gate)
            {
                return _disposed;
            }
        }

        private void Returned()
        {
            lock (clock._gate)
            {
                if (--_running == 0)
                {
                    _runningOn = 0;
                    _idle.TrySetResult();
                }
            }
        }

        /// <summary>Retires the timer AT ONCE and returns — even from inside its own callback, where a wait would be a wait for itself.</summary>
        public void Dispose()
        {
            lock (clock._gate)
            {
                Retire();
            }
        }

        /// <summary>Retires the timer, then completes once a callback already picked has returned (at once from inside that callback).</summary>
        public ValueTask DisposeAsync()
        {
            lock (clock._gate)
            {
                Retire();
                return _runningOn == Environment.CurrentManagedThreadId ? ValueTask.CompletedTask : new ValueTask(_idle.Task);
            }
        }

        private void Retire()
        {
            _disposed = true;
            if (clock._timers.Remove(this))
            {
                clock.CountChanged();
            }
        }

        private static TaskCompletionSource Idle()
        {
            var idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            idle.SetResult();
            return idle;
        }
    }
}
