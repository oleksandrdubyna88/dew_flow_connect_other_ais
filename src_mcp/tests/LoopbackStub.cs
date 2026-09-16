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
    /// Every way a platform says that address is taken.
    /// </summary>
    /// <remarks>
    /// <para>This set is the whole reason the catch is narrow. Retrying on ANY
    /// <see cref="HttpListenerException"/> would turn an access-denied prefix or a malformed
    /// registration into ten attempts and a sentence blaming ports, which is a worse failure than
    /// the one being fixed: it is wrong AND it takes ten times as long to be wrong.</para>
    /// <para>Measured on this family's own platforms, with the port held two different ways —
    /// because the way it is held changes the code, which is not obvious and is why this list is
    /// longer than one entry:</para>
    /// <list type="table">
    ///   <item><description>Windows, held by an ordinary socket: 32, ERROR_SHARING_VIOLATION</description></item>
    ///   <item><description>Windows, held by another HttpListener: 183, ERROR_ALREADY_EXISTS</description></item>
    ///   <item><description>Linux, held by an ordinary socket: 98, EADDRINUSE — this is the one that failed CI</description></item>
    ///   <item><description>Linux, held by another HttpListener: 400, the managed listener's own registration clash</description></item>
    /// </list>
    /// <para><b>48 and 10048 were not measured here, and are not guesses either.</b> The Linux
    /// measurement is what licenses them: the managed listener handed back the RAW ERRNO, 98, rather
    /// than a .NET error of its own — so a platform on that same managed path reports its own errno,
    /// and on BSD and macOS <c>EADDRINUSE</c> is 48. 10048 is <c>WSAEADDRINUSE</c>, the Winsock
    /// spelling of the identical condition. Both are the same fact in another dialect, so neither can
    /// widen the catch to a different failure.</para>
    /// <para>And the reasoning is not load-bearing:
    /// <c>AStubSurvivesALostPortTests.TheCodeThisPlatformActuallyReports_IsOneTheStubRetries</c>
    /// provokes a REAL collision, both ways, on whatever platform it is running, and asserts that
    /// whatever comes back is in this set. A platform whose code is missing reddens that test on its
    /// own leg and names the code, which is how this list is meant to grow.</para>
    /// </remarks>
    private static readonly HashSet<int> PortIsTaken = [32, 48, 98, 183, 400, 10048];

    /// <summary>
    /// The codes above, for the test that checks each one is honoured. Exposed so the theory cannot
    /// drift from the classifier: a code added here without a case is still exercised, and a case
    /// with no code fails to compile a row.
    /// </summary>
    internal static IReadOnlyCollection<int> RetryableCodes => PortIsTaken;

    /// <summary>A stub on a port nobody else holds.</summary>
    internal static (HttpListener Server, string Prefix) Start() => Start(FreePorts(), Attempts);

    /// <summary>The same, over a caller's candidates — which is what makes a lost port testable.</summary>
    /// <param name="candidates">Ports to try, in order.</param>
    /// <param name="attempts">How many may be lost before this is reported as a failure.</param>
    internal static (HttpListener Server, string Prefix) Start(IEnumerable<int> candidates, int attempts) =>
        Start(candidates, attempts, Bind);

    /// <summary>
    /// The same again, over a caller's way of binding one — which is what makes the OTHER half
    /// testable, since a denied prefix cannot be arranged through the OS on demand.
    /// </summary>
    /// <param name="candidates">Ports to try, in order.</param>
    /// <param name="attempts">How many may be lost before this is reported as a failure.</param>
    /// <param name="bind">Starts a listener on one prefix.</param>
    internal static (HttpListener Server, string Prefix) Start(
        IEnumerable<int> candidates, int attempts, Func<string, HttpListener> bind)
    {
        var lost = new List<int>();
        Exception? last = null;

        foreach (var port in candidates)
        {
            var prefix = $"http://127.0.0.1:{port}/";

            try
            {
                return (bind(prefix), prefix);
            }
            catch (Exception e) when (Retries(e))
            {
                lost.Add(port);
                last = e;

                if (lost.Count >= attempts)
                {
                    throw new InvalidOperationException(NoPortHeld(lost), e);
                }
            }
        }

        // Reached when the caller's candidates run out before the attempt bound does. The last
        // failure is carried rather than dropped: without it the caller gets our sentence and none
        // of the platform's, which is the half that names the actual errno.
        throw new InvalidOperationException(NoPortHeld(lost), last);
    }

    /// <summary>
    /// Whether this failure is the port being gone — and NOT anything else, which is rethrown
    /// unchanged so it arrives as itself rather than as a story about ports.
    /// </summary>
    /// <remarks>
    /// Both arms are reachable and neither is the other's fallback. Every collision measured so far
    /// arrived as an <see cref="HttpListenerException"/>, including on Linux where the managed
    /// listener wraps the errno itself; the <see cref="SocketException"/> arm is there because the
    /// bind underneath is a socket, and a platform that let one through unwrapped would otherwise
    /// take down the whole fixture rather than retry. It is narrowed to the one
    /// <see cref="SocketError"/> that means this, so it cannot swallow anything else.
    /// </remarks>
    internal static bool Retries(Exception e) => e switch
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
            // Closed on the FAILURE path only. A listener that did not start still holds a
            // registration and a handle, and ten of those over a retry loop is a leak that makes the
            // next attempt fail for a new reason — but a finally here would close the one being
            // returned, which is why this is a catch.
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

    /// <summary>
    /// What was lost, and which of the two cures it points at.
    /// </summary>
    /// <remarks>
    /// The two cases are written separately because one sentence covering both said, of a single
    /// lost port, that it had been retried and that it meant the machine was out of ports — two
    /// claims that contradict each other and are each wrong half the time. (gemini, the code round.)
    /// </remarks>
    private static string NoPortHeld(IReadOnlyCollection<int> lost) => lost.Count == 1
        ? "no loopback port could be held long enough to start the stub: the one candidate offered "
          + $"({lost.Single()}) was taken between the probe closing and the listener binding. That is "
          + "the ordinary race and it is what the retry is for — this run had no second candidate to "
          + "take."
        : $"no loopback port could be held long enough to start the stub: all {lost.Count} candidates "
          + $"({string.Join(", ", lost)}) were taken between the probe closing and the listener "
          + "binding. Losing one is the ordinary race and is retried; losing this many means "
          + "something on this machine is taking ports as fast as the OS hands them out, which is "
          + "not a race a retry can win.";
}
