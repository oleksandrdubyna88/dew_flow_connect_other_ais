using FluentAssertions;
using Xunit;

namespace CoaiBugs.Tests;

/// <summary>
/// One server per data directory — enforced by the lock, not assumed by the plan.
/// </summary>
/// <remarks>
/// The limiter is per process, so a second server on the same directory would admit the whole limit
/// again. That the real binary exits 78 on it is proved over the executable in
/// <c>TheBuiltBinariesTests</c>; this covers the primitive and the one thing that must NOT be locked
/// out — a one-shot mode run while the service serves.
/// </remarks>
public sealed class TheServeLockTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "coai-lock-" + Guid.NewGuid().ToString("N")[..8]);

    [Fact]
    public void ASecondServerOnTheSameDirectoryIsRefusedUntilTheFirstLetsGo()
    {
        var first = ServeLock.Take(_dir).Should().BeOfType<ServeLock.Taken.Held>().Subject;

        var second = ServeLock.Take(_dir).Should().BeOfType<ServeLock.Taken.Refused>().Subject;
        second.Why.Should().Contain(ServeLock.FileName).And.Contain("one server per data directory");

        first.Lock.Dispose();

        ServeLock.Take(_dir).Should().BeOfType<ServeLock.Taken.Held>("a released lock is free to take")
            .Subject.Lock.Dispose();
    }

    [Fact]
    public void TwoDirectoriesAreTwoServers()
    {
        var other = _dir + "-other";
        using var first = ServeLock.Take(_dir).Should().BeOfType<ServeLock.Taken.Held>().Subject.Lock;

        var second = ServeLock.Take(other);

        second.Should().BeOfType<ServeLock.Taken.Held>("the lock is per data directory, not per machine");
        ((ServeLock.Taken.Held)second).Lock.Dispose();
        Scratch.Delete(other);
    }

    /// <summary>A one-shot is not a server, and runs while the server holds the lock.</summary>
    /// <remarks>
    /// `--revoke` while the service serves is the ordinary case, and the whole reason the database
    /// has a busy timeout. A lock that stopped it would make the operator stop the service to stop
    /// a key.
    /// </remarks>
    [Fact]
    public void AOneShotRunsWhileTheServerHoldsTheLock()
    {
        using var serving = ServeLock.Take(_dir).Should().BeOfType<ServeLock.Taken.Held>().Subject.Lock;
        var clock = new FrozenClock(new DateTimeOffset(2026, 9, 17, 12, 0, 0, TimeSpan.Zero));

        Admin.Run(["--waiting"], "a-secret", _dir, clock).Should().Be(0, "a one-shot never takes the lock");
    }

    public void Dispose() => Scratch.Delete(_dir);
}
