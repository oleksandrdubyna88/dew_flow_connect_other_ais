using System.Runtime.InteropServices;

namespace CoaiMcp.Server;

/// <summary>
/// A serving run asked to stop by a signal ENDS — it does not die (issue #514).
/// </summary>
/// <remarks>
/// <para>An MCP client stops its server with SIGTERM, SIGINT or SIGHUP. With no handler the runtime left
/// at once, the <c>finally</c> of <c>ServeAsync</c> never ran, the run marker stayed, and the next start
/// wrote an unclean exit for a run that had simply been closed — 26 of them on one Linux machine, most
/// from the Claude reviewers' own coai-mcp children.</para>
/// <para>The first signal clears the marker AT ONCE — a signal is an ending by request, never a death, and
/// the flag inside <see cref="RunLife"/> keeps a late heartbeat from writing it back — then cancels the
/// runtime's exit and stops serving, so the host leaves by its ordinary road, draining its notices. With the
/// marker already gone, nothing that cuts that road short can turn the stop into a death: a second signal
/// lets the default exit through, and a shutdown still running at <see cref="Grace"/> — a review in flight,
/// or a drain that hangs — is ended there. The host calls <see cref="Ended"/> only after its drain, so the
/// deadline covers all of it. (gemini and our own code reviewer.) SIGKILL cannot be caught, and a run killed
/// that way WITHOUT a signal first is still, correctly, a death.</para>
/// <para>On Windows the runtime maps the four signals onto console events. Ctrl+C and Ctrl+Break are held
/// as described; for the window closing and the system shutting down, Windows ends the process as soon as
/// the handler returns whatever it answers — so there the marker is cleared, and the drain is not
/// promised. A signal the platform cannot register is skipped.</para>
/// </remarks>
internal sealed class ServeStop(
    CancellationTokenSource stopping, Action clearMarker, Action<int> exit, TimeSpan grace, Serilog.ILogger? log = null)
{
    /// <summary>How long a signalled shutdown may take before it is cut short.</summary>
    internal static readonly TimeSpan Grace = TimeSpan.FromSeconds(3);

    private static readonly PosixSignal[] Signals =
        [PosixSignal.SIGTERM, PosixSignal.SIGINT, PosixSignal.SIGHUP, PosixSignal.SIGQUIT];

    private int _signals;

    private volatile bool _ended;

    /// <summary>A signal arrived: whether the runtime's own exit is to be cancelled.</summary>
    internal bool OnSignal(string signal)
    {
        if (Interlocked.Increment(ref _signals) > 1)
        {
            clearMarker();

            return false;
        }
        clearMarker();
        log?.Information("{Signal} asked this server to stop; it ends the ordinary way", signal);
        stopping.Cancel();
        _ = DeadlineAsync();

        return true;
    }

    /// <summary>The host reached its own end, drain included: no deadline is needed any more.</summary>
    internal void Ended() => _ended = true;

    /// <summary>Every signal this platform can register, handled here until the returned value is disposed.</summary>
    internal IDisposable Register()
    {
        var registrations = new List<IDisposable>();
        foreach (var signal in Signals)
        {
            try
            {
                registrations.Add(PosixSignalRegistration.Create(signal, context => context.Cancel = OnSignal(context.Signal.ToString())));
            }
            catch (PlatformNotSupportedException)
            {
                // Not a signal this platform has; the others still stop the server cleanly.
            }
        }

        return new Registrations(registrations);
    }

    private async Task DeadlineAsync()
    {
        await Task.Delay(grace).ConfigureAwait(false);
        if (_ended)
        {
            return;
        }
        log?.Warning("the shutdown was still running {Grace} after the signal; leaving now", grace);
        exit(0);
    }

    private sealed class Registrations(List<IDisposable> all) : IDisposable
    {
        public void Dispose() => all.ForEach(one => one.Dispose());
    }
}
