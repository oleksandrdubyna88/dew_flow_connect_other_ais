using System.Net;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The scenario fixtures put a real HTTP stub on a real socket, and the port they get is one the OS
/// said was free a moment ago. This is what happens when it no longer is.
/// </summary>
/// <remarks>
/// <para>The failure being pinned is not hypothetical and not local: it took the linux-x64 leg of the
/// `mcp-v0.25.0` release matrix on 2026-09-15, which left that release a DRAFT carrying ten assets
/// instead of twelve. The binaries were correct; a stub could not get a socket.</para>
/// <para><b>Why the second case is the one with teeth.</b> A retry cannot be proved red by deleting
/// it — deleting it leaves the OLD code, which is a single attempt, and a single attempt is exactly
/// what <c>attempts: 1</c> asks for here. So the second test IS the unfixed behaviour, run against
/// the same lost port, asserting it fails. Run them together and they say: this input broke the
/// build, and this is the line that stops it doing so again.</para>
/// </remarks>
public sealed class AStubSurvivesALostPortTests
{
    [Fact]
    public void APortTakenBeforeTheBind_CostsTheCandidateAndNotTheRun()
    {
        using var held = LoopbackStub.Start().Server;
        var taken = PortOf(held);

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
        using var held = LoopbackStub.Start().Server;
        var taken = PortOf(held);

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
        using var first = LoopbackStub.Start().Server;
        using var second = LoopbackStub.Start().Server;

        var failure = Record.Exception(
            () => LoopbackStub.Start([PortOf(first), PortOf(second)], attempts: 2));

        failure.Should().BeOfType<InvalidOperationException>()
            .Which.Message
            .Should().Contain("2 candidates were taken",
                "the count is what separates a lost race from a machine out of ports")
            .And.Contain("not a race a retry can win",
                "and the reader is told which of the two cures to reach for");
    }

    private static int PortOf(HttpListener listener) =>
        new Uri(listener.Prefixes.Single()).Port;
}
