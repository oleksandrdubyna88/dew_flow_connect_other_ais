using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>The signal-and-wait a product hook uses on a test that drives a <see cref="ManualClock"/> (E4 plan §5.4 points 6 and 11).</summary>
public sealed class ClockHandshakeTests
{
    [Fact]
    public void SignalCompletesReachedOnce_AndSignallingAgainChangesNothing()
    {
        var handshake = new ClockHandshake();
        handshake.Reached.IsCompleted.Should().BeFalse("nothing has reached the point yet");

        handshake.Signal();
        handshake.Signal();

        handshake.Reached.IsCompletedSuccessfully.Should().BeTrue("the hook said it is there");
    }

    [Fact]
    public async Task AParkedHookWaitsForTheRelease_AndReleasingTwiceIsHarmless()
    {
        var handshake = new ClockHandshake();
        var parked = handshake.WaitForReleaseAsync(CancellationToken.None);
        parked.IsCompleted.Should().BeFalse("the hook is parked until the test releases it");

        handshake.Release();
        handshake.Release();

        await parked.WaitAsync(TimeSpan.FromSeconds(20));
        handshake.WaitForReleaseAsync(CancellationToken.None).IsCompletedSuccessfully.Should().BeTrue("a hook that arrives after the release is not parked");
    }

    [Fact]
    public async Task TheTestsGuardEndsTheHooksWait_SoAFailingTestNeverLeavesAProductThreadParked()
    {
        var handshake = new ClockHandshake();
        using var guard = new CancellationTokenSource();
        var parked = handshake.WaitForReleaseAsync(guard.Token);

        await guard.CancelAsync();

        await FluentActions.Awaiting(() => parked).Should().ThrowAsync<OperationCanceledException>("the guard's cancellation ends the wait");
    }
}
