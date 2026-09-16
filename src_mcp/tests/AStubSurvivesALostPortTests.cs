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
/// <para><b>Why the second test is the one with teeth.</b> A retry cannot be proved red by deleting
/// it — deleting it leaves the OLD code, which is a single attempt, and a single attempt is exactly
/// what <c>attempts: 1</c> asks for here. So that test IS the unfixed behaviour, run against the same
/// lost port, asserting it fails. The two together say: this input broke the build, and this is the
/// line that stops it doing so again.</para>
/// <para><b>The port is held by an ordinary socket, not by another listener</b>, because that is the
/// CI case and the two are not the same failure: a socket gives 98 on Linux and 32 on Windows, while
/// a second HttpListener gives 400 and 183. Holding it the convenient way would have exercised a
/// collision that CI has never had.</para>
/// </remarks>
public sealed class AStubSurvivesALostPortTests
{
    [Fact]
    public void APortTakenBeforeTheBind_CostsTheCandidateAndNotTheRun()
    {
        using var thief = Hold(out var taken);

        var (server, prefix) = LoopbackStub.Start(
            new[] { taken }.Concat(LoopbackStub.FreePorts()), LoopbackStub.Attempts);

        using var _ = server;
        server.IsListening.Should().BeTrue(
            "a candidate lost between the probe and the bind is the ordinary case on a loaded "
            + "runner, and the stub's job is to get a socket, not to get that particular one");
        prefix.Should().NotContain($":{taken}/",
            "and it must answer on the port it actually took — a prefix naming a port held by "
            + "somebody else sends the shim's requests to a stranger");
    }

    [Fact]
    public void WithASingleAttempt_WhichIsWhatTheStubHadBefore_TheSameLostPortFailsTheRun()
    {
        using var thief = Hold(out var taken);

        var failure = Record.Exception(() => LoopbackStub.Start([taken], attempts: 1));

        failure.Should().BeOfType<InvalidOperationException>(
            "one attempt is the behaviour that cost mcp-v0.25.0 its linux-x64 leg")
            .Which.Message
            .Should().Contain(taken.ToString(),
                "the port that was lost is the first thing a reader needs")
            .And.Contain("1 candidate was taken",
                "and losing exactly one names itself as the race rather than the machine")
            .And.Contain("retried",
                "so the sentence says what the code does about it, not only that it happened");
    }

    [Fact]
    public void EveryCandidateTaken_SaysItIsTheMachineAndNotTheRace()
    {
        var failure = Record.Exception(
            () => LoopbackStub.Start([1, 2], attempts: 2, bind: _ => throw Taken(98)));

        failure.Should().BeOfType<InvalidOperationException>()
            .Which.Message
            .Should().Contain("2 candidates were taken",
                "the count is what separates a lost race from a machine out of ports")
            .And.Contain("not a race a retry can win",
                "and the reader is told which of the two cures to reach for");
    }

    /// <summary>
    /// Every spelling of "that address is taken" this family's six release legs can produce.
    /// </summary>
    /// <remarks>
    /// 32/183 and 98/400 were measured on this machine, on Windows and under WSL, with the port held
    /// by a socket and by a second listener. 48 and 10048 were not — there is no macOS here — and are
    /// carried because they are unambiguous spellings of the same condition. A code missing from the
    /// set turns a lost port into a hard failure on that platform only, which is exactly the shape of
    /// the bug this file exists for, so each one is pinned rather than trusted.
    /// </remarks>
    [Theory]
    [InlineData(32)]
    [InlineData(48)]
    [InlineData(98)]
    [InlineData(183)]
    [InlineData(400)]
    [InlineData(10048)]
    public void EveryMeasuredSpellingOfATakenPort_CostsTheCandidateAndNotTheRun(int code)
    {
        var calls = 0;

        var (server, _) = LoopbackStub.Start(
            LoopbackStub.FreePorts(),
            LoopbackStub.Attempts,
            prefix => ++calls == 1 ? throw Taken(code) : Really(prefix));

        using var _unused = server;
        calls.Should().Be(2, $"error {code} means the port is gone, so the next candidate is taken");
        server.IsListening.Should().BeTrue();
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

    /// <summary>
    /// A port held by an ordinary socket — the CI case, not a second listener.
    /// </summary>
    /// <remarks>
    /// It binds port 0 and KEEPS it, rather than asking for a free port and then racing to take it.
    /// Doing it the other way would have put the bug under test into the test that proves it fixed.
    /// </remarks>
    private static TcpListener Hold(out int port)
    {
        var thief = new TcpListener(IPAddress.Loopback, 0);
        thief.Start();
        port = ((IPEndPoint)thief.LocalEndpoint).Port;

        return thief;
    }

    private static HttpListenerException Taken(int code) => new(code);

    private static HttpListener Really(string prefix)
    {
        var server = new HttpListener();
        server.Prefixes.Add(prefix);
        server.Start();

        return server;
    }
}
