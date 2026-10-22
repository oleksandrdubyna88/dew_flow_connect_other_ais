using System.Threading.Channels;
using CoaiMcp.Server;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A client that closes stdin is the HOST ending, not a person withdrawing the call — E3.S2 of <c>todo/PLAN_the_round_counter_tail_E3.md</c>.
/// The host's stopping token (what <c>RoundEngine</c> files as <c>hostStopped</c>) must fire BEFORE the SDK sees its input end, because the SDK
/// cancels the in-flight requests' tokens as it winds down and a round killed that way would otherwise be filed <c>withdrawn</c>.
/// </summary>
public sealed class TheHostIsToldTheClientLeftBeforeTheSdkIsTests
{
    /// <summary>A transport whose input the test ends.</summary>
    private sealed class Stdin : ITransport
    {
        public Channel<JsonRpcMessage> Input { get; } = Channel.CreateUnbounded<JsonRpcMessage>();

        public string? SessionId => null;

        public ChannelReader<JsonRpcMessage> MessageReader => Input.Reader;

        public Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;

        /// <summary>How many times the transport's cleanup reached this inner transport.</summary>
        public int Disposals => _disposals;

        private int _disposals;

        public async ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposals);
            // Slow on purpose: a second, overlapping disposal has time to run its own cleanup while this one is still going.
            await Task.Delay(200);
            Input.Writer.TryComplete();
        }
    }

    [Fact]
    public async Task TheStopIsSignalled_BeforeTheSdkSeesTheInputEnd()
    {
        var inner = new Stdin();
        var told = false;
        await using var transport = new InputEndSignallingTransport(inner, async () =>
        {
            // Slow on purpose: a signal sent AFTER the reader completed would lose this race.
            await Task.Delay(300);
            told = true;
        });
        var sdkSawTheEnd = false;
        var toldWhenTheSdkSawTheEnd = false;
        var reading = Task.Run(async () =>
        {
            await foreach (var _ in transport.MessageReader.ReadAllAsync(TestContext.Current.CancellationToken)) { }

            toldWhenTheSdkSawTheEnd = told;
            sdkSawTheEnd = true;
        }, TestContext.Current.CancellationToken);

        inner.Input.Writer.TryComplete();
        await reading.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        sdkSawTheEnd.Should().BeTrue();
        toldWhenTheSdkSawTheEnd.Should().BeTrue("the host token fires before the SDK's read loop ends, so it is first of the sources to fire");
    }

    [Fact]
    public async Task EveryMessage_PassesThroughUnchanged_AndInOrder()
    {
        var inner = new Stdin();
        await using var transport = new InputEndSignallingTransport(inner, () => Task.CompletedTask);
        var messages = Enumerable.Range(1, 5).Select(i => (JsonRpcMessage)new JsonRpcNotification { Method = $"m{i}" }).ToList();

        foreach (var message in messages)
        {
            await inner.Input.Writer.WriteAsync(message, TestContext.Current.CancellationToken);
        }

        inner.Input.Writer.TryComplete();
        var seen = new List<JsonRpcMessage>();
        await foreach (var message in transport.MessageReader.ReadAllAsync(TestContext.Current.CancellationToken))
        {
            seen.Add(message);
        }

        seen.Should().Equal(messages);
    }

    [Fact]
    public async Task AnInputThatFails_StillSignalsTheStop_AndFailsTheReaderTheSameWay()
    {
        var inner = new Stdin();
        var told = false;
        await using var transport = new InputEndSignallingTransport(inner, () => { told = true; return Task.CompletedTask; });

        inner.Input.Writer.TryComplete(new IOException("stdin broke"));
        var read = async () =>
        {
            await foreach (var _ in transport.MessageReader.ReadAllAsync(TestContext.Current.CancellationToken)) { }
        };

        await read.Should().ThrowAsync<IOException>().WithMessage("stdin broke");
        told.Should().BeTrue();
    }

    [Fact]
    public async Task ASlowReader_AppliesBackpressure_AndStillGetsEveryMessageInOrder()
    {
        const int total = 300;
        var inner = new Stdin();
        await using var transport = new InputEndSignallingTransport(inner, () => Task.CompletedTask);
        var messages = Enumerable.Range(1, total).Select(i => (JsonRpcMessage)new JsonRpcNotification { Method = $"m{i}" }).ToList();
        foreach (var message in messages)
        {
            await inner.Input.Writer.WriteAsync(message, TestContext.Current.CancellationToken);
        }

        // Nobody is reading yet. The pump fills its buffer and then waits for the reader instead of draining stdin into memory.
        SpinWait.SpinUntil(() => inner.Input.Reader.Count <= total - InputEndSignallingTransport.Capacity, TimeSpan.FromSeconds(30))
            .Should().BeTrue("the pump takes messages while its buffer has room");
        await Task.Delay(300, TestContext.Current.CancellationToken);
        inner.Input.Reader.Count.Should().BeGreaterThanOrEqualTo(total - InputEndSignallingTransport.Capacity - 2,
            "an unread buffer that is full stops the pump: the rest of the input stays where it was");

        inner.Input.Writer.TryComplete();
        var seen = new List<JsonRpcMessage>();
        await foreach (var message in transport.MessageReader.ReadAllAsync(TestContext.Current.CancellationToken))
        {
            seen.Add(message);
            await Task.Yield();
        }

        seen.Should().Equal(messages);
    }

    [Fact]
    public async Task AHostCallbackThatThrows_NeverFaultsThePump_TheReaderCompletes_AndDisposeIsQuiet()
    {
        var inner = new Stdin();
        var transport = new InputEndSignallingTransport(inner, () => throw new InvalidOperationException("the host could not be told"));

        inner.Input.Writer.TryComplete();
        var read = async () =>
        {
            await foreach (var _ in transport.MessageReader.ReadAllAsync(TestContext.Current.CancellationToken)) { }
        };

        await read.Should().NotThrowAsync("the SDK must still see its input end, whatever the host's callback did");
        var dispose = async () => await transport.DisposeAsync();
        await dispose.Should().NotThrowAsync("a callback that failed was logged, not left to fault the pump DisposeAsync awaits");
    }

    [Fact]
    public async Task DisposingWhileTheReaderIsGone_DoesNotHangOnAFullBuffer()
    {
        var inner = new Stdin();
        var transport = new InputEndSignallingTransport(inner, () => Task.CompletedTask);
        for (var i = 0; i < InputEndSignallingTransport.Capacity * 3; i++)
        {
            await inner.Input.Writer.WriteAsync(new JsonRpcNotification { Method = $"m{i}" }, TestContext.Current.CancellationToken);
        }

        await Task.Delay(100, TestContext.Current.CancellationToken);
        var dispose = async () => await transport.DisposeAsync();

        await dispose.Should().CompleteWithinAsync(TimeSpan.FromSeconds(30), "nobody reads: the pump is parked on a full buffer and the dispose must release it");
    }

    [Fact]
    public async Task ALocalDisposal_DoesNotTellTheHostTheClientLeft()
    {
        var inner = new Stdin();
        var told = false;
        var transport = new InputEndSignallingTransport(inner, () => { told = true; return Task.CompletedTask; });
        // The input is still open and the pump is parked reading it: only THIS side ends it.
        await Task.Delay(100, TestContext.Current.CancellationToken);

        await transport.DisposeAsync();

        told.Should().BeFalse("a local disposal is not the client leaving, and signalling would re-enter the host's shutdown");
        var read = async () =>
        {
            await foreach (var _ in transport.MessageReader.ReadAllAsync(TestContext.Current.CancellationToken)) { }
        };
        await read.Should().NotThrowAsync("the reader still completes, whatever ended the pump");
    }

    [Fact]
    public async Task DisposingTwice_IsSafe_AndCleansUpOnce()
    {
        var inner = new Stdin();
        var transport = new InputEndSignallingTransport(inner, () => Task.CompletedTask);
        await Task.Delay(100, TestContext.Current.CancellationToken);

        await transport.DisposeAsync();
        var again = async () => await transport.DisposeAsync();

        await again.Should().NotThrowAsync("a stop registration and `await using` can both dispose the same transport");
        inner.Disposals.Should().Be(1, "the second disposal finds the cleanup already done");
    }

    [Fact]
    public async Task TwoConcurrentDisposals_RunTheCleanupOnce()
    {
        var inner = new Stdin();
        var transport = new InputEndSignallingTransport(inner, () => Task.CompletedTask);
        await Task.Delay(100, TestContext.Current.CancellationToken);

        var first = transport.DisposeAsync().AsTask();
        var second = transport.DisposeAsync().AsTask();
        var both = async () => await Task.WhenAll(first, second);

        await both.Should().NotThrowAsync("overlapping disposals share the first one's cleanup");
        inner.Disposals.Should().Be(1, "only the first caller runs the cleanup; the other waits for it");
    }

    /// <summary>An inner transport whose disposal fails while its input is still open.</summary>
    private sealed class FailingStdin : ITransport
    {
        private readonly Channel<JsonRpcMessage> _input = Channel.CreateUnbounded<JsonRpcMessage>();

        public string? SessionId => null;

        public ChannelReader<JsonRpcMessage> MessageReader => _input.Reader;

        public Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.FromException(new IOException("the pipe would not close"));
    }

    private sealed class Captured(List<Serilog.Events.LogEvent> into) : Serilog.Core.ILogEventSink
    {
        public void Emit(Serilog.Events.LogEvent logEvent) => into.Add(logEvent);
    }

    /// <remarks>
    /// The log assertion is the one with teeth (red when the catch only stores the failure). The reader-complete assertion is a GUARD: the
    /// pump also ends on the cancel alone, so it holds before and after the finally that now awaits the pump on a failed inner disposal —
    /// that ordering cannot be raced deterministically from here.
    /// </remarks>
    [Fact]
    public async Task AnInnerDisposalThatFails_IsLogged_SaysWhyItFailed_AndTheReaderStillCompletes()
    {
        var events = new List<Serilog.Events.LogEvent>();
        var log = new Serilog.LoggerConfiguration().WriteTo.Sink(new Captured(events)).CreateLogger();
        var transport = new InputEndSignallingTransport(new FailingStdin(), () => Task.CompletedTask, log);
        await Task.Delay(100, TestContext.Current.CancellationToken);

        var dispose = async () => await transport.DisposeAsync();

        await dispose.Should().ThrowAsync<IOException>("every caller observes the cleanup's failure");
        events.Should().ContainSingle(e => e.Level == Serilog.Events.LogEventLevel.Error && e.Exception is IOException,
            "a cleanup failure is logged once, with the exception, not only handed to the awaiting callers");
        transport.MessageReader.Completion.IsCompleted.Should().BeTrue("the reader the SDK waits on is complete once disposal returns");
    }
}
