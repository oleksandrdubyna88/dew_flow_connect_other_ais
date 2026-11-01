namespace CoaiMcp.Tests;

/// <summary>
/// A clock that moves one <paramref name="step"/> per read, for a time budget that must run out mid-read. <c>FixedClock</c> is
/// read-only by design, so a test that needs a moving clock has this one: the first read says <paramref name="start"/>.
/// </summary>
internal sealed class SteppingClock(DateTimeOffset start, TimeSpan step) : TimeProvider
{
    private long _reads;

    public override DateTimeOffset GetUtcNow() => start + TimeSpan.FromTicks(step.Ticks * Interlocked.Increment(ref _reads) - step.Ticks);
}
