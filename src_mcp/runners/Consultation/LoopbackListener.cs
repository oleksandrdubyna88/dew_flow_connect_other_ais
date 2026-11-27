using System.Net;
using System.Net.Sockets;

namespace CoaiMcp.Runners.Consultation;

/// <summary>
/// A listener coai opens on the host loopback for the length of one preflight (epoch 3 E3 s5 t3, id CX-V4): the unit has the host network, so it reaches this port, and the vendor sandbox — a namespace of its own with no network — must not. It accepts and closes every connection and keeps nothing.
/// </summary>
internal sealed class LoopbackListener : IDisposable
{
    private readonly TcpListener _listener;

    private LoopbackListener(TcpListener listener) => _listener = listener;

    /// <summary>The port the listener is bound to on 127.0.0.1.</summary>
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>Starts a listener on the loopback address only, on a port the system chooses.</summary>
    public static LoopbackListener Start()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var made = new LoopbackListener(listener);
        _ = Task.Run(made.AcceptAsync);

        return made;
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (true)
            {
                using var client = await _listener.AcceptTcpClientAsync();
            }
        }
        catch (Exception e) when (e is SocketException or ObjectDisposedException or InvalidOperationException)
        {
            // Stopped: the unit has ended.
        }
    }

    public void Dispose() => _listener.Stop();
}
