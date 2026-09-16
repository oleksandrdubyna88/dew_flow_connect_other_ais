using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using Xunit;

namespace CoaiBugs.Tests;

/// <summary>
/// Two real processes, a real socket: the shipped `coai-bugs` and the shipped `coai-mcp`.
/// </summary>
/// <remarks>
/// <para><b>What an in-process test cannot see.</b> `BothHalvesTests` hosts the server's ASSEMBLIES
/// through `WebApplicationFactory` and talks to it through a handler, which is the right shape for
/// checking that the two halves agree about ids and words. A code round pointed out what it still
/// cannot reach: a trimming, source-generation, embedded-resource or publish-layout defect leaves
/// the released binaries unable to ingest a single pair while every one of those tests stays
/// green — and this story's whole reason for existing is a keyword file that was read from a
/// directory no release has.</para>
/// <para><b>The seam is `COAI_CONTRACT_EXE`</b>, which `McpContractTests` and `LogCliScenarioTests`
/// already use and the release workflow already sets: unset, this runs the binaries this build
/// produced; set, it runs the PUBLISHED, AOT-compiled ones. So the same scenario is a fast check
/// here and the release smoke there, rather than two tests that drift.</para>
/// <para>It starts a server on a real port, mints a key through the real `--issue-key` one-shot,
/// uploads through the real `--upload-pairs` one-shot, and reads the result back through the real
/// `--waiting` one-shot. Nothing in the path is a fixture.</para>
/// </remarks>
[Collection("the-server")]
public sealed class TheBuiltBinariesTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "coai-built-" + Guid.NewGuid().ToString("N")[..8]);

    private const string Secret = "a-secret-for-this-scenario-only";

    public TheBuiltBinariesTests() => Directory.CreateDirectory(_dir);

    /// <summary>The server binary: the published one when the release smoke points at it.</summary>
    private static string BugsExe =>
        Environment.GetEnvironmentVariable("COAI_BUGS_CONTRACT_EXE") is { Length: > 0 } published
            ? published
            : Built("src_bugs", "src", "coai-bugs");

    /// <summary>The client binary, on the same seam the other contract suites use.</summary>
    private static string McpExe =>
        Environment.GetEnvironmentVariable("COAI_CONTRACT_EXE") is { Length: > 0 } published
            ? published
            : Built("src_mcp", "src", "coai-mcp");

    private static string Built(params string[] parts)
    {
        var configuration = AppContext.BaseDirectory.Contains("Release", StringComparison.Ordinal)
            ? "Release"
            : "Debug";
        var name = parts[^1] + (OperatingSystem.IsWindows() ? ".exe" : string.Empty);

        return Path.GetFullPath(Path.Combine(
            [AppContext.BaseDirectory, "..", "..", "..", "..", "..", .. parts[..^1],
             "bin", configuration, "net10.0", name]));
    }

    /// <summary>
    /// A pair goes from a real client process to a real server process and is really stored.
    /// </summary>
    /// <remarks>
    /// The embedded keyword list is what this proves hardest: the server reads it during startup, so
    /// a binary that cannot find it never answers `/health` and this test times out rather than
    /// passing quietly.
    /// </remarks>
    [Fact]
    public async Task ARealUploadCrossesTwoRealProcesses()
    {
        Assert.SkipUnless(File.Exists(BugsExe), $"{BugsExe} is not built");
        Assert.SkipUnless(File.Exists(McpExe), $"{McpExe} is not built");

        var port = FreePort();
        var key = IssueKey();
        key.Should().NotBeEmpty("--issue-key prints the key once, on stdout");

        using var server = Start(BugsExe, $"--urls http://127.0.0.1:{port}", _dir);
        try
        {
            (await Listening(port)).Should().BeTrue(
                $"{BugsExe} must reach the point of listening — if it cannot find its embedded "
                + "keyword list it throws during startup instead, which is the defect this exists for");

            SeedOneKeptPair();
            var upload = Run(
                McpExe,
                $"--upload-pairs --server http://127.0.0.1:{port}",
                Client(),
                key);

            upload.Code.Should().Be(0, $"the client said: {upload.Err}");
            upload.Out.Should().Contain("\"accepted\": 1", "the summary is this mode's whole answer")
                .And.Contain("\"refused\": 0")
                .And.Contain("\"trouble\": \"\"");
        }
        finally
        {
            Stop(server);
        }

        var waiting = Run(BugsExe, "--waiting", _dir);
        waiting.Code.Should().Be(0);
        waiting.Err.Should().Contain("1 waiting", "the pair really landed in the real database");
        waiting.Out.Should().Contain("method_1", "and `--waiting` shows a person the skeleton");
    }

    /// <summary>An unknown mode exits 64, which is how a caller detects an old binary.</summary>
    /// <remarks>
    /// The other half of the rule — a mode this binary KNOWS never exits 64 — is `--revoke` with no
    /// `--id`, which the round found answering 64 and which answers 65 now. Both directions in one
    /// test, over the real executable, because an exit code is not observable any other way.
    /// </remarks>
    [Fact]
    public void TheExitCodesTellAnOldBinaryFromABadArgument()
    {
        Assert.SkipUnless(File.Exists(BugsExe), $"{BugsExe} is not built");

        Run(BugsExe, "--rotate-the-moon", _dir).Code.Should().Be(
            64, "a mode this binary has never heard of is how a caller detects an old one");

        Run(BugsExe, "--revoke", _dir).Code.Should().Be(
            65, "it KNOWS --revoke; the argument is what is wrong, and 64 would say otherwise");
    }

    /// <summary>Seeds one kept pair into a client-side database the real CLI will read.</summary>
    private void SeedOneKeptPair() => Seed.KeptPairs(
        Client(),
        new CoaiMcp.Core.Collecting.CollectedPair(
            "GetOrAdd", "CSharp", "method_1(var_1) { }", "method_1(var_1) { lock (var_2) { } }"));

    /// <summary>Where the CLIENT's database lives, which is not the server's.</summary>
    private string Client()
    {
        var client = Path.Combine(_dir, "client");
        Directory.CreateDirectory(client);

        return client;
    }

    /// <summary>Mints a key through the real one-shot, which prints it once on stdout.</summary>
    private string IssueKey()
    {
        var issued = Run(BugsExe, "--issue-key --note a-scenario", _dir);
        issued.Code.Should().Be(0, $"--issue-key said: {issued.Err}");

        return issued.Out.Trim();
    }

    private static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        return port;
    }

    /// <summary>Waits for the server to actually answer, rather than assuming it started.</summary>
    private static async Task<bool> Listening(int port)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        for (var attempt = 0; attempt < 60; attempt++)
        {
            try
            {
                using var reply = await http.GetAsync(
                    new Uri($"http://127.0.0.1:{port}/health"), TestContext.Current.CancellationToken);
                if (reply.IsSuccessStatusCode)
                {
                    return true;
                }
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
            {
                // Not up yet, which is the ordinary case for the first few attempts.
            }

            await Task.Delay(250, TestContext.Current.CancellationToken);
        }

        return false;
    }

    /// <summary>
    /// Starts one, with its streams DRAINED rather than merely redirected.
    /// </summary>
    /// <remarks>
    /// A redirected stream nobody reads is a pipe that fills, and a process whose pipe is full stops
    /// writing and then stops running. The server logs to its console sink on every request, so it
    /// would reach that buffer and hang there — looking exactly like a server that never came up.
    /// </remarks>
    private static Process Start(string exe, string args, string data, string key = "")
    {
        var how = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
        };
        foreach (var argument in args.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            how.ArgumentList.Add(argument);
        }

        how.Environment["COAI_BUGS_SECRET"] = Secret;
        how.Environment["COAI_BUGS_DATA"] = data;
        how.Environment["COAI_DATA_DIR"] = data;
        if (key.Length > 0)
        {
            how.Environment["COAI_BUGS_KEY"] = key;
        }

        var started = Process.Start(how)!;

        // Drained on their own threads, because the SERVER is left running: nobody calls
        // `ReadToEnd` on a process that never exits, and an undrained pipe stops it dead.
        started.OutputDataReceived += (_, _) => { };
        started.ErrorDataReceived += (_, _) => { };
        started.BeginOutputReadLine();
        started.BeginErrorReadLine();

        return started;
    }

    /// <summary>Runs one to completion and collects what it said.</summary>
    /// <remarks>
    /// Its own start, without the asynchronous draining: `ReadToEnd` on stdout while stderr fills is
    /// the other half of the same deadlock, so this reads stdout asynchronously and stderr on this
    /// thread — which is the documented pair that cannot block.
    /// </remarks>
    private static (int Code, string Out, string Err) Run(
        string exe, string args, string data, string key = "")
    {
        var how = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
        };
        foreach (var argument in args.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            how.ArgumentList.Add(argument);
        }

        how.Environment["COAI_BUGS_SECRET"] = Secret;
        how.Environment["COAI_BUGS_DATA"] = data;
        how.Environment["COAI_DATA_DIR"] = data;
        if (key.Length > 0)
        {
            how.Environment["COAI_BUGS_KEY"] = key;
        }

        using var process = Process.Start(how)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit(60_000).Should().BeTrue($"{exe} {args} must finish");

        return (process.ExitCode, output.GetAwaiter().GetResult(), error);
    }

    private static void Stop(Process server)
    {
        try
        {
            if (!server.HasExited)
            {
                server.Kill(entireProcessTree: true);
                server.WaitForExit(10_000);
            }
        }
        catch (InvalidOperationException)
        {
            // Already gone, which is the outcome this was asking for.
        }

        server.Dispose();
    }

    public void Dispose() => Scratch.Delete(_dir);
}
