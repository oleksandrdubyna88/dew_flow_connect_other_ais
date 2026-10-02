using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>
/// A port somebody else holds ends the real server binary with EX_TEMPFAIL and one line.
/// </summary>
/// <remarks>
/// <para>The same startup failure `coai-bugs` had, in its sibling host: Kestrel's bind error escaped
/// <c>RunAsync</c> as an unhandled <see cref="IOException"/>, so a taken port was a CRASH — the
/// runtime's "Unhandled exception." block, an exit code no restart policy or operator can branch on,
/// and on Windows a `.NET Runtime 1026` event per occurrence. Found while fixing `coai-bugs` and fixed
/// in the same task, because one decision applied at one of its two sites is the defect this family
/// keeps shipping.</para>
/// <para>It runs the BUILT executable, not the in-process <see cref="TeamServer"/>, because the
/// failure is in what the host does when <c>RunAsync</c> throws — a `TestServer` never binds a socket
/// at all, so it cannot see this.</para>
/// </remarks>
public sealed class ATakenPortTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("coai-server-port-").FullName;

    [Fact]
    public async Task APortSomebodyHolds_EndsTheServerWithTempfailAndOneLine_NotACrash()
    {
        var thief = new TcpListener(IPAddress.Loopback, 0);
        thief.Start();
        var taken = ((IPEndPoint)thief.LocalEndpoint).Port;

        try
        {
            var (code, said) = await RunServerAsync($"http://127.0.0.1:{taken}");

            said.Should().NotContain(
                "Unhandled exception",
                $"a taken port is an expected failure and must not end in a crash. It said: {said}");
            code.Should().Be(75, $"EX_TEMPFAIL tells the restart policy to try again. It said: {said}");
            said.Should().Contain($"127.0.0.1:{taken}", "the one line names the address it could not take");
        }
        finally
        {
            thief.Stop();
        }
    }

    /// <summary>Runs the real executable until it exits, with both streams drained concurrently.</summary>
    /// <remarks>
    /// The configuration is the minimum <see cref="TeamServer"/> sets for a server that STARTS — a
    /// local signing key, one domain, an admin — so the only thing standing between it and serving is
    /// the held port. A bounded wait, and a tree-kill if it is exceeded: a server that took the port
    /// after all would otherwise serve for ever and hang the suite.
    /// </remarks>
    private async Task<(int Code, string Said)> RunServerAsync(string url)
    {
        var exe = Path.Combine(
            AppContext.BaseDirectory, "coai-server" + (OperatingSystem.IsWindows() ? ".exe" : ""));
        File.Exists(exe).Should().BeTrue($"the test needs the server binary beside it, at {exe}");

        var start = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("--urls");
        start.ArgumentList.Add(url);
        start.Environment["Coai__DataDir"] = _dir;
        start.Environment["Coai__AllowedDomains"] = TeamServer.Domain;
        start.Environment["Coai__AllowAnyDomain"] = "false";
        start.Environment["Coai__RequireForwardedHttps"] = "false";
        start.Environment["Coai__Admins"] = $"boss@{TeamServer.Domain}";
        start.Environment["Coai__SessionTtlDays"] = "7";
        start.Environment["Auth__Local__SigningKey"] = TeamServer.LocalSigningKey;
        start.Environment["Auth__Microsoft__Tenant"] = string.Empty;
        start.Environment["Auth__Microsoft__Audiences"] = string.Empty;
        start.Environment["Auth__Google__Enabled"] = "false";

        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));

        try
        {
            await process.WaitForExitAsync(deadline.Token);
        }
        catch (OperationCanceledException) when (!TestContext.Current.CancellationToken.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("the server did not exit within 60 s — it must have taken the port");
        }

        return (process.ExitCode, await stdout + await stderr);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // A log file still being released by the exiting process; the temp root is swept anyway.
        }
    }
}
