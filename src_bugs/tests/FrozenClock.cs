namespace CoaiBugs.Tests;

/// <summary>A clock a test sets, so a month boundary is a number rather than a date on the wall.</summary>
/// <remarks>
/// The server takes its <see cref="TimeProvider"/> from the container, and this is what the harness
/// registers: the month a key is stamped with and the limiter's window both read it. Guarded,
/// because request threads read it while the test thread moves it.
/// </remarks>
internal sealed class FrozenClock(DateTimeOffset at) : TimeProvider
{
    private readonly Lock _gate = new();
    private DateTimeOffset _now = at;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _now;
        }
    }

    public void Advance(TimeSpan by)
    {
        lock (_gate)
        {
            _now += by;
        }
    }

    public void Set(DateTimeOffset to)
    {
        lock (_gate)
        {
            _now = to;
        }
    }
}
