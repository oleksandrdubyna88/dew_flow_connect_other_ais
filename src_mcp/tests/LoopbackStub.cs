using System.Net;
using System.Net.Sockets;

namespace CoaiMcp.Tests;

/// <summary>
/// A started <see cref="HttpListener"/> on a loopback port, and the prefix it answers on.
/// </summary>
/// <remarks>
/// <para><b>Asking the OS for a free port does not reserve it.</b> A probe binds port 0, reads the
/// number the OS picked and closes; from that instant until <see cref="HttpListener.Start"/> binds
/// the same number, the port belongs to nobody. Anything else on the machine asking the same
/// question can be handed the same answer, and there is no atomic hand-off from a probe socket to an
/// <see cref="HttpListener"/> — so a better probe is not the cure.
/// </para>
/// <para><b>What it cost.</b> `mcp-v0.25.0`, 2026-09-15 19:03 UTC. The linux-x64 leg of the release
/// matrix failed in
/// <c>RemoteShimScenarioTests.WithoutATokenItRefusesBeforeItAsksAnybody (1ms)</c> with
/// <c>System.Net.HttpListenerException : Address already in use</c> — thrown out of the fixture's
/// <c>InitializeAsync</c>, before the test it is named after ran a line, which is why the sentence
/// blames a test that has nothing to do with tokens. Five of six legs passed. One missing leg leaves
/// the release short a platform, and a release that is short a platform is never published: 0.25.0
/// sat as a DRAFT with ten assets instead of twelve while the binaries in it were correct.
/// </para>
/// <para>So a lost port is treated as ordinary and the next one is taken. Only a whole run of
/// candidates disappearing is reported, because that is a machine that is out of ports rather than a
/// race that was lost — and the two want different cures, so the message says which one it saw.
/// </para>
/// </remarks>
internal static class LoopbackStub
{
    /// <summary>
    /// How many candidates may be lost before this stops being a race.
    /// </summary>
    /// <remarks>
    /// Losing one is unremarkable on a loaded runner. Losing ten in a row is not a race at all — the
    /// OS is handing out ports that something takes immediately — and retrying past that only turns
    /// a clear failure into a slow one.
    /// </remarks>
    internal const int Attempts = 10;

    /// <summary>A stub on a port nobody else holds.</summary>
    internal static (HttpListener Server, string Prefix) Start() => Start(FreePorts(), Attempts);

    /// <summary>
    /// The same, over a caller's candidates — which is what makes the race testable, since a lost
    /// port cannot be arranged through the OS on demand.
    /// </summary>
    internal static (HttpListener Server, string Prefix) Start(IEnumerable<int> candidates, int attempts)
    {
        var lost = new List<int>();

        foreach (var port in candidates)
        {
            var prefix = $"http://127.0.0.1:{port}/";
            var server = new HttpListener();
            server.Prefixes.Add(prefix);

            try
            {
                server.Start();

                return (server, prefix);
            }
            catch (Exception e) when (e is HttpListenerException or SocketException)
            {
                // The managed listener reports this as HttpListenerException on Linux and Windows
                // both; SocketException is caught because the bind underneath it is a socket and a
                // platform that lets one through would otherwise crash the whole class.
                server.Close();
                lost.Add(port);

                if (lost.Count >= attempts)
                {
                    throw new InvalidOperationException(NoPortHeld(lost), e);
                }
            }
        }

        throw new InvalidOperationException(NoPortHeld(lost));
    }

    /// <summary>
    /// An endless supply of ports that were free a moment ago — which is the strongest claim
    /// anything can make about a port it does not hold.
    /// </summary>
    internal static IEnumerable<int> FreePorts()
    {
        while (true)
        {
            yield return FreePort();
        }
    }

    /// <summary>A free port, taken by asking the OS rather than by guessing one.</summary>
    private static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        return port;
    }

    /// <summary>What was lost, and which of the two cures it points at.</summary>
    private static string NoPortHeld(IReadOnlyCollection<int> lost) =>
        $"no loopback port could be held long enough to start the stub: {lost.Count} candidate"
        + $"{(lost.Count == 1 ? " was" : "s were")} taken between the probe closing and the listener "
        + $"binding ({string.Join(", ", lost)}). Losing one is the ordinary race and is retried; "
        + "losing this many means something on this machine is taking ports as fast as the OS hands "
        + "them out, which is not a race a retry can win.";
}
