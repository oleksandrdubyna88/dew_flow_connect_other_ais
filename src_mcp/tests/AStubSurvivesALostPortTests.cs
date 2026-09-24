using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The scenario fixtures put a real HTTP stub on a real socket, and the port they get is one the OS
/// said was free a moment ago. This is what happens when it no longer is.
/// </summary>
/// <remarks>
/// <para>The failure being pinned is not hypothetical and not local: it took the linux-x64 leg of the
/// mcp-v0.25.0 release matrix on 2026-09-15, which left that release a DRAFT carrying ten assets
/// instead of twelve. The binaries were correct; a stub could not get a socket.</para>
/// <para><b>Which test is the red one.</b>
/// <see cref="APortTakenBeforeTheBind_CostsTheCandidateAndNotTheRun"/>, and only that one. It was
/// verified by setting <c>Attempts</c> to 1 — the behaviour that shipped — rebuilding and running it:
/// <code>
/// failed …APortTakenBeforeTheBind_CostsTheCandidateAndNotTheRun (122ms)
///   InvalidOperationException : no loopback port could be held long enough to start the stub…
///   ---- HttpListenerException : Failed to listen on prefix 'http://127.0.0.1:63422/' because it
///        conflicts with an existing registration on the machine.
/// </code>
/// Same call and same condition as CI, worded the way Windows words it. Restored, green.
/// <see cref="WithASingleAttempt_IsRefusedRatherThanRetried"/> below does NOT prove the fix and is
/// not claimed to — deleting the retry leaves it reaching the same exception — it pins the BOUND, so
/// that a future edit cannot make the retry unlimited without a test noticing. The code round was
/// right to say the first draft of this comment claimed otherwise. (codex, Conventions.)</para>
/// <para><b>The port is held by an ordinary socket, not by another listener</b>, because that is the
/// CI case and the two are not the same failure: a socket gives 98 on Linux and 32 on Windows, while
/// a second HttpListener gives 400 and 183. Holding it the convenient way would have exercised a
/// collision CI has never had.</para>
/// </remarks>
public sealed class AStubSurvivesALostPortTests
{
    [Fact]
    public void APortTakenBeforeTheBind_CostsTheCandidateAndNotTheRun()
    {
        using var thief = Hold(bySecondListener: false, out var taken);

        var (server, prefix) = LoopbackStub.Start(
            LoopbackStub.FreePorts().Prepend(taken), LoopbackStub.Attempts);

        using var _ = server;
        server.IsListening.Should().BeTrue(
            "a candidate lost between the probe and the bind is the ordinary case on a loaded "
            + "runner, and the stub's job is to get a socket, not to get that particular one");
        prefix.Should().NotContain($":{taken}/",
            "and it must answer on the port it actually took — a prefix naming a port held by "
            + "somebody else sends the shim's requests to a stranger");
    }

    /// <summary>
    /// The bound, not the fix. See the note on the class about what this does and does not prove.
    /// </summary>
    [Fact]
    public void WithASingleAttempt_IsRefusedRatherThanRetried()
    {
        using var thief = Hold(bySecondListener: false, out var taken);

        var failure = Record.Exception(() => LoopbackStub.Start([taken], attempts: 1));

        failure.Should().BeOfType<InvalidOperationException>(
            "a run allowed one attempt stops after one, whatever the retry would have done")
            .Which.Message
            .Should().Contain(taken.ToString(),
                "the port that was lost is the first thing a reader needs")
            .And.Contain("the one candidate offered",
                "and a single loss is described as the race it is, not as the machine being out "
                + "of ports — the two cures are different");
    }

    [Fact]
    public void EveryCandidateTaken_SaysItIsTheMachineAndNotTheRace()
    {
        var failure = Record.Exception(
            () => LoopbackStub.Start([1, 2], attempts: 2, bind: _ => throw Taken(98)));

        failure.Should().BeOfType<InvalidOperationException>()
            .Which.Message
            .Should().Contain("all 2 candidates",
                "the count is what separates a lost race from a machine out of ports")
            .And.Contain("not a race a retry can win",
                "and the reader is told which of the two cures to reach for");
    }

    /// <summary>
    /// The classifier, checked against what THIS platform actually reports rather than against the
    /// list the classifier was written from.
    /// </summary>
    /// <remarks>
    /// Asked for by the code round, in these words: a synthetic theory "passes even if a release
    /// leg's real HttpListener.Start() reports an unlisted code". It is the only test here that can
    /// fail on macOS, where nobody has measured anything — and if it does, it names the code, which
    /// is how the set is meant to grow. It holds the port BOTH ways because the way it is held
    /// changes the code on both platforms measured so far.
    /// </remarks>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheCodeThisPlatformActuallyReports_IsOneTheStubRetries(bool bySecondListener)
    {
        using var thief = Hold(bySecondListener, out var taken);
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{taken}/");

        var failure = Record.Exception(listener.Start);

        failure.Should().NotBeNull(
            "the port is held, so this bind must fail — a platform that shares it instead is one "
            + "where the premise of this whole file is different, and that is worth knowing");
        LoopbackStub.Retries(failure!).Should().BeTrue(
            $"this platform reports a taken port as {Describe(failure!)}, and the stub must know "
            + "that code: one it does not know is rethrown rather than retried, which is exactly "
            + "the release failure this file exists for");
    }

    /// <summary>
    /// Every code the classifier claims, honoured. Derived from the classifier itself, so the two
    /// cannot drift apart — a code added there is exercised here without anyone remembering to.
    /// </summary>
    public static TheoryData<int> RetryableCodes => [.. LoopbackStub.RetryableCodes];

    /// <summary>
    /// Issue #520, made deterministic: the candidate after the injected loss is one somebody else holds —
    /// what a parallel test did to this theory on a linux-x64 CI leg ("expected 2, found 3").
    /// </summary>
    [Theory]
    [MemberData(nameof(RetryableCodes))]
    public void ACandidateAnotherProcessTakes_IsCountedAsALossOfItsOwn(int code)
    {
        using var thief = Hold(bySecondListener: false, out var held);
        var binds = new CountedBind(code);

        var (server, prefix) = LoopbackStub.Start(
            LoopbackStub.FreePorts().Take(1).Append(held).Concat(LoopbackStub.FreePorts()),
            LoopbackStub.Attempts,
            binds.Bind);

        using var _unused = server;
        binds.LostForReal.Should().ContainSingle("the held port is lost for real, once")
            .Which.Should().Contain($":{held}/");
        binds.ShouldHaveRetriedEachLossOnce(code);
        prefix.Should().NotContain($":{held}/", "and the stub answers on the port it actually took");
    }

    [Theory]
    [MemberData(nameof(RetryableCodes))]
    public void EveryCodeTheStubClaims_CostsTheCandidateAndNotTheRun(int code)
    {
        var binds = new CountedBind(code);

        var (server, _) = LoopbackStub.Start(LoopbackStub.FreePorts(), LoopbackStub.Attempts, binds.Bind);

        using var _unused = server;
        binds.ShouldHaveRetriedEachLossOnce(code);
        server.IsListening.Should().BeTrue();
    }

    /// <summary>
    /// A bind that loses its FIRST candidate to the code under test, binds for real after that, and writes
    /// down every real attempt that lost its port to somebody else.
    /// </summary>
    /// <remarks>
    /// Issue #520. The theory above pinned exactly two calls, which is true only while nothing else on the
    /// machine binds a port between <see cref="LoopbackStub.FreePorts"/> listing a candidate and the stub
    /// binding it — and the suite runs in parallel. The product's promise is one retry per LOSS, so the
    /// count stays exact: two, plus one for every candidate that was lost for real. A real failure that is
    /// not a taken port still escapes the bind and fails the test.
    /// </remarks>
    private sealed class CountedBind(int injected)
    {
        private readonly List<string> _lost = [];

        public int Calls { get; private set; }

        /// <summary>The prefixes a real bind lost to another process, in order.</summary>
        public IReadOnlyList<string> LostForReal => _lost;

        public HttpListener Bind(string prefix)
        {
            Calls++;
            if (Calls == 1)
            {
                throw Taken(injected);
            }
            try
            {
                return Really(prefix);
            }
            catch (HttpListenerException lost) when (LoopbackStub.Retries(lost))
            {
                _lost.Add(prefix);
                throw;
            }
        }

        public void ShouldHaveRetriedEachLossOnce(int code) =>
            Calls.Should().Be(2 + _lost.Count,
                $"error {code} costs its candidate — one retry — and each candidate another process took "
                + $"meanwhile costs one more (lost for real: {(_lost.Count == 0 ? "none" : string.Join(", ", _lost))})");
    }

    [Fact]
    public void AFailureThatIsNotATakenPort_ArrivesAsItselfAndIsNotRetried()
    {
        var calls = 0;

        // 5 is ERROR_ACCESS_DENIED: a prefix this process may not register. Retrying it nine more
        // times cannot help, and reporting it as ports being taken sends the reader hunting for a
        // contention problem that is not there.
        var failure = Record.Exception(() => LoopbackStub.Start(
            LoopbackStub.FreePorts(),
            LoopbackStub.Attempts,
            _ =>
            {
                calls++;

                throw Taken(5);
            }));

        calls.Should().Be(1, "a failure a retry cannot fix is not retried");
        failure.Should().BeOfType<HttpListenerException>(
            "and it arrives as itself rather than wrapped in a sentence about ports")
            .Which.ErrorCode.Should().Be(5);
    }

    [Fact]
    public void CandidatesRunningOut_KeepsWhatThePlatformSaid()
    {
        var failure = Record.Exception(
            () => LoopbackStub.Start([1, 2], attempts: 99, bind: _ => throw Taken(98)));

        failure.Should().BeOfType<InvalidOperationException>()
            .Which.InnerException.Should().BeOfType<HttpListenerException>(
                "a caller whose candidates ran out still needs the errno — our sentence names the "
                + "ports and the platform's names the reason")
            .Which.ErrorCode.Should().Be(98);
    }

    /// <summary>
    /// A port that is genuinely held, one of the two ways a port gets held.
    /// </summary>
    /// <remarks>
    /// The socket binds port 0 and KEEPS it, rather than asking for a free port and then racing to
    /// take it. Doing it the other way would have put the bug under test inside the test that proves
    /// it fixed.
    /// </remarks>
    private static IDisposable Hold(bool bySecondListener, out int port)
    {
        if (bySecondListener)
        {
            var (server, prefix) = LoopbackStub.Start();
            port = new Uri(prefix).Port;

            return server;
        }

        var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        port = ((IPEndPoint)socket.LocalEndpoint).Port;

        return socket;
    }

    private static string Describe(Exception e) => e switch
    {
        HttpListenerException h => $"{e.GetType().Name} {h.ErrorCode} ({e.Message})",
        SocketException s => $"{e.GetType().Name} {s.SocketErrorCode} ({e.Message})",
        _ => $"{e.GetType().Name} ({e.Message})",
    };

    private static HttpListenerException Taken(int code) => new(code);

    private static HttpListener Really(string prefix)
    {
        var server = new HttpListener();
        server.Prefixes.Add(prefix);
        server.Start();

        return server;
    }
}
