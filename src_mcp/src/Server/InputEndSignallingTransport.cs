using System.Threading.Channels;
using ModelContextProtocol.Protocol;

namespace CoaiMcp.Server;

/// <summary>
/// A transport that tells the host its client left BEFORE the SDK finds out (E3.S2 of todo/PLAN_the_round_counter_tail_E3.md).
/// </summary>
/// <remarks>
/// <para>A client that closes stdin sends no signal, and the host's stopping token used to be cancelled only in <c>ServeAsync</c>'s
/// <c>finally</c>, after <c>server.RunAsync</c> returned. By then the SDK had already cancelled the in-flight requests' own tokens as it wound
/// down — so a local round the server's ending killed was filed <c>withdrawn</c>, a person's decision, when it was the host's. The
/// ledger's cause is the FIRST source to fire (<c>RoundCancellation</c>), so the host's token has to fire first.</para>
/// <para>The SDK only observes the input through <see cref="ITransport.MessageReader"/>, so this reader is the one it is given: messages are
/// pumped through unchanged and in order, and when the real input ends — cleanly or with a failure — <c>onInputEnded</c> runs and ONLY THEN
/// is this reader completed (with the same failure, if any). Nothing the SDK does in response to its input ending can precede the callback.</para>
/// </remarks>
/// <param name="inner">The real transport (stdio).</param>
/// <param name="onInputEnded">What the host does the moment its client's input is over: cancel its stopping token.</param>
internal sealed class InputEndSignallingTransport : ITransport
{
    private readonly ITransport _inner;
    private readonly Func<Task> _onInputEnded;
    private readonly Channel<JsonRpcMessage> _messages = Channel.CreateUnbounded<JsonRpcMessage>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
    private readonly Task _pump;

    public InputEndSignallingTransport(ITransport inner, Func<Task> onInputEnded)
    {
        _inner = inner;
        _onInputEnded = onInputEnded;
        _pump = Task.Run(PumpAsync);
    }

    public string? SessionId => _inner.SessionId;

    public ChannelReader<JsonRpcMessage> MessageReader => _messages.Reader;

    public Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default) =>
        _inner.SendMessageAsync(message, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await _inner.DisposeAsync();
        await _pump;
    }

    private async Task PumpAsync()
    {
        Exception? failure = null;
        try
        {
            await foreach (var message in _inner.MessageReader.ReadAllAsync())
            {
                await _messages.Writer.WriteAsync(message);
            }
        }
        catch (Exception e)
        {
            failure = e;
        }

        try
        {
            await _onInputEnded();
        }
        finally
        {
            // Whatever the host's callback did, the SDK must still see its input end: a reader that never completes is a hung server.
            _messages.Writer.TryComplete(failure);
        }
    }
}
