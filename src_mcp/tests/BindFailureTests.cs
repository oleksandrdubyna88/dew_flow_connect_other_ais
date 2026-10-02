using System.Net;
using System.Net.Sockets;
using CoaiMcp.ServiceDefaults;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The predicate both HTTP hosts use to tell "somebody holds the port" from every other startup fault.
/// </summary>
/// <remarks>
/// The fixture is a REAL collision, not a constructed error code: a second socket bound to a port the
/// first one holds, so the <see cref="SocketException"/> under test is whatever this platform actually
/// raises. A hand-built one would prove the predicate agrees with its author.
/// </remarks>
public sealed class BindFailureTests
{
    [Fact]
    public void ARealCollision_WrappedTheWayKestrelWrapsIt_IsAnAddressInUse()
    {
        var collision = ARealCollision();
        var asKestrelThrowsIt = new IOException(
            "Failed to bind to address http://127.0.0.1:1: address already in use.",
            new InvalidOperationException("Only one usage of each socket address", collision));

        BindFailure.IsAddressInUse(asKestrelThrowsIt).Should().BeTrue(
            "the socket error sits two levels down, and the walk must reach it");
        BindFailure.IsAddressInUse(collision).Should().BeTrue("and at depth zero too");
    }

    [Fact]
    public void AnyOtherStartupFault_IsNotMistakenForATakenPort()
    {
        // A missing directory, a refused permission, a socket error of another kind: each of these is
        // the server's own problem, and calling it a taken port would send a restart loop after it.
        BindFailure.IsAddressInUse(new IOException("the data directory is not writable")).Should().BeFalse();
        BindFailure.IsAddressInUse(
            new IOException("denied", new SocketException((int)SocketError.AccessDenied))).Should().BeFalse();
        BindFailure.IsAddressInUse(new InvalidOperationException("no keywords")).Should().BeFalse();
    }

    [Fact]
    public void TheLine_NamesTheAddress_AndKeepsKestrelsWordsForTheRetryThatReadsThem()
    {
        var failure = new IOException("Failed to bind to address http://127.0.0.1:60772: address already in use.");

        var line = BindFailure.Explained("coai-bugs", failure);

        line.Should().StartWith("[coai-bugs] cannot listen");
        line.Should().Contain("http://127.0.0.1:60772", "the operator needs to know WHICH address");
        line.Should().Contain("Failed to bind to address", "a harness's race classifier matches this phrase");
        line.Should().NotContain("\n", "one line");
    }

    [Fact]
    public void TheExitCode_IsTempfail_NotConfig()
    {
        BindFailure.ExitCode.Should().Be(75, "78 is EX_CONFIG, which a deploy unit treats as permanent");
    }

    private static SocketException ARealCollision()
    {
        using var holder = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        holder.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        holder.Listen();
        var port = ((IPEndPoint)holder.LocalEndPoint!).Port;

        using var second = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var act = () => second.Bind(new IPEndPoint(IPAddress.Loopback, port));

        return act.Should().Throw<SocketException>().Which;
    }
}
