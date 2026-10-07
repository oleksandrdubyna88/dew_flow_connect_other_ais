using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The background start behind <c>initialize</c> (<c>todo/PLAN_an_idle_server_is_idle_and_starts_at_once.md</c>, D2):
/// a call waits for it without a thread and is served by what it built, a failure is a failure of its own type, a stop is
/// not one, and the sweeper beats only after a successful start. The real-binary order is
/// <see cref="TheServerAnswersInitializeAtOnceTests"/>.
/// </summary>
public sealed class StartingHostTests : IDisposable
{
    private readonly string _data = Directory.CreateTempSubdirectory("coai-starting-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_data, recursive: true);
        }
        catch (IOException) { }
    }

    private sealed class Boom() : Exception("the start broke");

    private PanelServiceHost Host() => new(
        name => name == "COAI_DATA_DIR" ? _data : null,
        VaultKeys.None("no vault in this test"),
        default,
        new Runners.Processes.ProcessLauncher(),
        Serilog.Core.Logger.None,
        Noticing.None);

    [Fact]
    public async Task ACallDuringTheStart_WaitsForIt_AndIsServedByTheServiceItBuilt()
    {
        var host = Host();
        var gate = new TaskCompletionSource<PanelServiceHost>(TaskCreationOptions.RunContinuationsAsynchronously);
        var starting = StartingHost.Start(() => gate.Task, CancellationToken.None);

        var call = starting.CurrentAsync().AsTask();
        await Task.Delay(50);
        call.IsCompleted.Should().BeFalse("the service is not built yet");

        gate.SetResult(host);
        (await call).Should().BeSameAs(host.Current, "the call is served by the host the start built, per call");
        starting.Failed.Should().BeFalse();
    }

    [Fact]
    public async Task ACallDuringAStartThatFails_GetsTheStartsOwnException()
    {
        var gate = new TaskCompletionSource<PanelServiceHost>(TaskCreationOptions.RunContinuationsAsynchronously);
        var starting = StartingHost.Start(() => gate.Task, CancellationToken.None);
        var call = starting.CurrentAsync().AsTask();

        gate.SetException(new Boom());

        await call.Invoking(c => c).Should().ThrowAsync<Boom>();
    }

    /// <summary>
    /// The own review's finding (2026-10-06): a start failing with an <see cref="IOException"/> — the likeliest failure of
    /// a start that is mostly disk work — reached Program's "the client closed the connection" filter and exited 0 with no
    /// crash recorded. The failure is now its own type, carrying the cause, which no serving filter can take for an ending.
    /// </summary>
    [Fact]
    public async Task AStartThatFailsWithAnIoError_IsReportedAsAFailedStart_NotAsAnIoError()
    {
        var starting = StartingHost.Start(() => throw new IOException("the share went away"), CancellationToken.None);
        await StartingHost.Ended(starting.Started, TimeSpan.FromSeconds(5));

        starting.Failed.Should().BeTrue();
        var thrown = starting.Invoking(s => s.ThrowIfFailed()).Should().Throw<ServerStartFailed>().Which;
        thrown.Should().NotBeAssignableTo<IOException>();
        thrown.InnerException.Should().BeOfType<IOException>().Which.Message.Should().Be("the share went away");
    }

    [Fact]
    public async Task AStartCancelledByItsStop_IsAnEnding_NotAFailure()
    {
        using var stop = new CancellationTokenSource();
        var starting = StartingHost.Start(async () =>
        {
            await Task.Delay(Timeout.Infinite, stop.Token);
            throw new InvalidOperationException("unreachable");
        }, stop.Token);

        await stop.CancelAsync();
        await StartingHost.Ended(starting.Started, TimeSpan.FromSeconds(5));

        starting.Failed.Should().BeFalse();
        starting.Invoking(s => s.ThrowIfFailed()).Should().NotThrow();
    }

    [Fact]
    public async Task AStartCancelledWithoutItsStop_IsAFailure()
    {
        using var inner = new CancellationTokenSource();
        await inner.CancelAsync();
        var starting = StartingHost.Start(async () =>
        {
            await Task.Delay(Timeout.Infinite, inner.Token); // a timeout inside the start looks exactly like this
            throw new InvalidOperationException("unreachable");
        }, CancellationToken.None);
        await StartingHost.Ended(starting.Started, TimeSpan.FromSeconds(5));

        starting.Failed.Should().BeTrue("nothing asked the server to stop, so a cancelled start is a start that did not happen");
        starting.Invoking(s => s.ThrowIfFailed()).Should().Throw<ServerStartFailed>().WithInnerException<TimeoutException>();
    }

    /// <summary>
    /// Own review of the branch (2026-10-07): Program's ending ALWAYS cancels the start's stop before it asks whether the
    /// start failed, so a failure decided by reading the stop afterwards turned a timed-out start into "not failed" and the
    /// process exited 0 with nothing recorded. The outcome is fixed inside the start, when it ends.
    /// </summary>
    [Fact]
    public async Task AStartCancelledWithoutItsStop_StaysAFailure_AfterTheStopIsCancelledToo()
    {
        using var stop = new CancellationTokenSource();
        using var inner = new CancellationTokenSource();
        await inner.CancelAsync();
        var starting = StartingHost.Start(async () =>
        {
            await Task.Delay(Timeout.Infinite, inner.Token);
            throw new InvalidOperationException("unreachable");
        }, stop.Token);
        await StartingHost.Ended(starting.Started, TimeSpan.FromSeconds(5));

        await stop.CancelAsync(); // what the ending does before it reads the outcome

        starting.Failed.Should().BeTrue("the start timed out before anything asked the server to stop");
        starting.Invoking(s => s.ThrowIfFailed()).Should().Throw<ServerStartFailed>();
    }

    [Fact]
    public async Task TheSweeper_KeepsBeatingAfterAGoodStart_UntilServingEnds()
    {
        var host = Host();
        var starting = StartingHost.Start(() => Task.FromResult(host), CancellationToken.None);
        using var stop = new CancellationTokenSource();

        var sweeping = ConsultationSweeper.RunAsync(starting, TimeSpan.FromMilliseconds(20), Serilog.Core.Logger.None, stop.Token);

        (await StartingHost.Ended(sweeping, TimeSpan.FromMilliseconds(300))).Should().BeFalse("a good start leaves the sweeper beating");
        await stop.CancelAsync();
        (await StartingHost.Ended(sweeping, TimeSpan.FromSeconds(5))).Should().BeTrue("serving ended, so the sweeper ends");
    }

    [Fact]
    public async Task TheSweeper_NeverBeats_AfterAFailedStart()
    {
        var starting = StartingHost.Start(() => throw new Boom(), CancellationToken.None);
        using var stop = new CancellationTokenSource();

        var sweeping = ConsultationSweeper.RunAsync(starting, TimeSpan.FromMilliseconds(10), Serilog.Core.Logger.None, stop.Token);

        (await StartingHost.Ended(sweeping, TimeSpan.FromSeconds(5))).Should().BeTrue("a failed start ends the sweeper at once, before any beat");
    }
}
