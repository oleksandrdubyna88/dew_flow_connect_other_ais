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

        public ValueTask DisposeAsync()
        {
            Input.Writer.TryComplete();

            return ValueTask.CompletedTask;
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
}
