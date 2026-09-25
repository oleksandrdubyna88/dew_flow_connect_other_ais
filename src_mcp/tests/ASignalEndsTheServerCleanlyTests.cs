using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What a serving coai-mcp does when it is asked to stop by a signal — issue #514.
/// </summary>
/// <remarks>
/// An MCP client stops a server with SIGTERM (or SIGINT/SIGHUP). With no handler the runtime left at once,
/// the <c>finally</c> that clears the run marker never ran, and the next start reported a run that "never
/// finished" — 26 of them on one Linux machine. The decision is pinned here as values; the real signal is
/// pinned by the process test in <see cref="ARunThatDiesIsRecordedTests"/>.
/// </remarks>
public sealed class ASignalEndsTheServerCleanlyTests
{
    private sealed class Recorder
    {
        public CancellationTokenSource Stopping { get; } = new();

        public int Cleared { get; private set; }

        public List<int> Exits { get; } = [];

        public ServeStop Stop(TimeSpan grace) => new(Stopping, () => Cleared++, code => Exits.Add(code), grace);
    }

    [Fact]
    public void TheFirstSignal_StopsServing_AndLeavesTheEndingToTheHost()
    {
        var run = new Recorder();

        run.Stop(TimeSpan.FromMinutes(1)).OnSignal("SIGTERM")
            .Should().BeTrue("the runtime's own exit is cancelled, so the host's finally runs and clears its marker");

        run.Stopping.IsCancellationRequested.Should().BeTrue("serving stops");
        run.Cleared.Should().Be(0, "the marker is the host's to clear, after the notices drain");
        run.Exits.Should().BeEmpty();
    }

    [Fact]
    public void ASecondSignal_ClearsTheMarker_AndLetsTheProcessEndAtOnce()
    {
        var run = new Recorder();
        var stop = run.Stop(TimeSpan.FromMinutes(1));

        stop.OnSignal("SIGTERM");

        stop.OnSignal("SIGINT").Should().BeFalse("a second signal is somebody insisting, and the default exit goes ahead");
        run.Cleared.Should().Be(1, "but not before the marker is cleared, or the insisted stop is reported as a death");
    }

    [Fact]
    public async Task AShutdownThatHangs_IsEndedAtItsDeadline_WithTheMarkerCleared()
    {
        var run = new Recorder();

        run.Stop(TimeSpan.FromMilliseconds(50)).OnSignal("SIGTERM");

        (await Polls.Until(() => run.Exits.Count == 1, TimeSpan.FromSeconds(10)))
            .Should().BeTrue("a review still running when the signal came must not keep the process past its deadline");
        run.Cleared.Should().Be(1, "and the exit it forces clears the marker first");
    }

    [Fact]
    public async Task AShutdownThatFinishes_IsNotCutShort()
    {
        var run = new Recorder();
        var stop = run.Stop(TimeSpan.FromMilliseconds(200));

        stop.OnSignal("SIGTERM");
        stop.Ended();
        await Task.Delay(600, TestContext.Current.CancellationToken);

        run.Exits.Should().BeEmpty("a host that reached its own end needs no deadline");
        run.Cleared.Should().Be(0);
    }
}
