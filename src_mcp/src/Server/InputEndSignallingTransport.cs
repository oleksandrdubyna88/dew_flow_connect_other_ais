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
/// is this reader completed (with the same failure, if any). Nothing the SDK does in response to its input ending can precede the callback.
/// A pump ended by this transport's own disposal does not run it: no client left, and the host is already shutting down.</para>
/// <para>The hand-off is BOUNDED (<see cref="Capacity"/> messages): a reader that falls behind makes the pump wait, so the rest of the input stays
/// in the pipe. The pump never faults: a callback that throws is logged, the reader is completed in a <c>finally</c> that always runs, and
/// disposal releases a pump parked on a full buffer.</para>
/// </remarks>
/// <param name="inner">The real transport (stdio).</param>
/// <param name="onInputEnded">What the host does the moment its client's input is over: cancel its stopping token.</param>
/// <param name="log">Where a failing <paramref name="onInputEnded"/> is said; none logs nothing.</param>
internal sealed class InputEndSignallingTransport : ITransport
{
    /// <summary>How many messages the pump may hold ahead of the reader.</summary>
    internal const int Capacity = 64;

    private readonly ITransport _inner;
    private readonly Func<Task> _onInputEnded;
    private readonly Serilog.ILogger _log;
    private readonly CancellationTokenSource _disposing = new();
    // Bounded: a reader that falls behind makes the pump wait, which leaves the rest of the input in the pipe, rather than the host
    // buffering an unbounded backlog of a client's requests in memory.
    private readonly Channel<JsonRpcMessage> _messages = Channel.CreateBounded<JsonRpcMessage>(
        new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true, SingleWriter = true });
    private readonly Task _pump;
    // The first disposal's outcome; non-null once disposal has started. See DisposeAsync.
    private TaskCompletionSource? _disposal;

    public InputEndSignallingTransport(ITransport inner, Func<Task> onInputEnded, Serilog.ILogger? log = null)
    {
        _inner = inner;
        _onInputEnded = onInputEnded;
        _log = log ?? Serilog.Core.Logger.None;
        // Explicitly NOT the disposing token: a task cancelled before it starts never runs PumpAsync, so its `finally` would never complete the
        // reader the SDK is waiting on, and DisposeAsync's `await _pump` would throw. The pump observes the token itself, where it waits.
        _pump = Task.Run(PumpAsync, CancellationToken.None);
    }

    public string? SessionId => _inner.SessionId;

    public ChannelReader<JsonRpcMessage> MessageReader => _messages.Reader;

    public Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default) =>
        _inner.SendMessageAsync(message, cancellationToken);

    /// <summary>
    /// Disposes once, however many callers and however they overlap: a stop registration, a failed-start continuation and <c>await using</c>
    /// can all reach here. The first caller runs the cleanup; every other caller — later or concurrent, even one re-entered from a callback the
    /// cleanup itself fires — awaits that same cleanup's outcome instead of running its own (a second run would reach a disposed token source).
    /// </summary>
    public ValueTask DisposeAsync()
    {
        var mine = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = Interlocked.CompareExchange(ref _disposal, mine, null);

        if (first is not null)
        {
            return new ValueTask(first.Task);
        }

        _ = RunDisposalAsync(mine);

        return new ValueTask(mine.Task);
    }

    /// <summary>The one cleanup. Never throws: its outcome — success or the failure — is the task every caller awaits.</summary>
    private async Task RunDisposalAsync(TaskCompletionSource done)
    {
        try
        {
            // A pump parked on a full buffer has no reader left to wait for: release it before waiting on it.
            await _disposing.CancelAsync();
            await _inner.DisposeAsync();
            await _pump;
            _disposing.Dispose();
            done.SetResult();
        }
        catch (Exception e)
        {
            done.SetException(e);
        }
    }

    private async Task PumpAsync()
    {
        Exception? failure = null;
        try
        {
            failure = await CopyAsync();
            // Only the REAL input ending tells the host: a pump that ended because THIS transport was disposed saw no client leave, and
            // signalling then would re-enter the host's shutdown, which is what is disposing us.
            if (!_disposing.IsCancellationRequested)
            {
                await SignalAsync();
            }
        }
        finally
        {
            // Whatever happened above, the SDK must still see its input end: a reader that never completes is a hung server.
            _messages.Writer.TryComplete(failure);
        }
    }

    /// <summary>Pumps until the real input ends; returns how it ended — null for a clean end or this transport's own disposal.</summary>
    private async Task<Exception?> CopyAsync()
    {
        try
        {
            await foreach (var message in _inner.MessageReader.ReadAllAsync(_disposing.Token))
            {
                await _messages.Writer.WriteAsync(message, _disposing.Token);
            }

            return null;
        }
        catch (OperationCanceledException) when (_disposing.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception e)
        {
            return e;
        }
    }

    /// <summary>Tells the host. A host that cannot be told is logged, never allowed to fault the pump the disposal awaits.</summary>
    private async Task SignalAsync()
    {
        try
        {
            await _onInputEnded();
        }
        catch (Exception e)
        {
            _log.Error(e, "the host could not be told its client's input ended");
        }
    }
}
