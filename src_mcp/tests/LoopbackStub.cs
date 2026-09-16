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
/// <para><b>What it cost.</b> mcp-v0.25.0, 2026-09-15 19:03 UTC. The linux-x64 leg of the release
/// matrix failed in
/// <c>RemoteShimScenarioTests.WithoutATokenItRefusesBeforeItAsksAnybody (1ms)</c> with
/// <c>System.Net.HttpListenerException : Address already in use</c> — thrown out of the fixture's
/// <c>InitializeAsync</c>, before the test it is named after ran a line, which is why the sentence
/// blames a test that has nothing to do with tokens. Five of six legs passed. One missing leg leaves
/// the release short a platform, and a release short a platform is never published: 0.25.0 sat as a
/// DRAFT with ten assets instead of twelve while the binaries in it were correct.
/// </para>
/// <para>So a lost port is treated as ordinary and the next one is taken. Only a whole run of
/// candidates disappearing is reported, because that is a machine out of ports rather than a race,
/// and the two want different cures — so the message says which one it saw.
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

    /// <summary>
    /// Every way a platform says that address is taken, measured rather than assumed.
    /// </summary>
    /// <remarks>
    /// <para>This set is the whole reason the catch below is narrow. Retrying on ANY
    /// <see cref="HttpListenerException"/> would turn an access-denied prefix or a malformed
    /// registration into ten attempts and a sentence blaming ports, which is a worse failure than
    /// the one being fixed: it is wrong AND it takes ten times as long to be wrong.</para>
    /// <para>Measured on this family's own platforms, with the port held two different ways —
    /// because the way it is held changes the code, which is not obvious and is why this list is
    /// longer than one entry:</para>
    /// <list type="table">
    ///   <item><description>Windows, held by another HttpListener: 183, ERROR_ALREADY_EXISTS</description></item>
    ///   <item><description>Windows, held by an ordinary socket: 32, ERROR_SHARING_VIOLATION</description></item>
    ///   <item><description>Linux, held by another HttpListener: 400, the managed listener's own registration clash</description></item>
    ///   <item><description>Linux, held by an ordinary socket: 98, EADDRINUSE — this is the one that failed CI</description></item>
    /// </list>
    /// <para>48 (BSD and macOS EADDRINUSE) and 10048 (WSAEADDRINUSE) are carried on the same terms
    /// but were NOT measured here — there is no macOS on this machine, and the Winsock code did not
    /// surface in either Windows case. They are unambiguous spellings of the same condition, so
    /// including them cannot widen the catch to something else; and if a platform ever answers with a
    /// code that is not in this list, the retry test goes red on that leg and names it, which is the
    /// intended way to find out.</para>
    /// </remarks>
    private static readonly HashSet<int> PortIsTaken = [32, 48, 98, 183, 400, 10048];

    /// <summary>A stub on a port nobody else holds.</summary>
    internal static (HttpListener Server, string Prefix) Start() => Start(FreePorts(), Attempts);

    /// <summary>
    /// The same, over a caller's candidates and a caller's way of binding one — which is what makes
    /// both halves testable, since neither a lost port nor a denied prefix can be arranged through
    /// the OS on demand.
    /// </summary>
    /// <param name="candidates">Ports to try, in order.</param>
    /// <param name="attempts">How many may be lost before this is reported as a failure.</param>
    /// <param name="bind">Starts a listener on one prefix; the real one when omitted.</param>
    internal static (HttpListener Server, string Prefix) Start(
        IEnumerable<int> candidates, int attempts, Func<string, HttpListener>? bind = null)
    {
        bind ??= Bind;
        var lost = new List<int>();

        foreach (var port in candidates)
        {
            var prefix = $"http://127.0.0.1:{port}/";

            try
            {
                return (bind(prefix), prefix);
            }
            catch (Exception e) when (IsTaken(e))
            {
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
    /// Whether this failure is the port being gone — and NOT anything else, which is rethrown
    /// unchanged so it arrives as itself rather than as a story about ports.
    /// </summary>
    private static bool IsTaken(Exception e) => e switch
    {
        HttpListenerException h => PortIsTaken.Contains(h.ErrorCode),
        SocketException s => s.SocketErrorCode == SocketError.AddressAlreadyInUse,
        _ => false,
    };

    /// <summary>A started listener, or whatever went wrong trying.</summary>
    private static HttpListener Bind(string prefix)
    {
        var server = new HttpListener();
        server.Prefixes.Add(prefix);

        try
        {
            server.Start();
        }
        catch
        {
            // A listener that did not start still holds a registration and a handle, and ten of
            // those over a retry loop is a leak that makes the next attempt fail for a new reason.
            server.Close();

            throw;
        }

        return server;
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
