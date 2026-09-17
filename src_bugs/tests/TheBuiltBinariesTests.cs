using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.RegularExpressions;
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

    /// <summary>Asserts the binary is there, rather than skipping when it is not.</summary>
    /// <remarks>
    /// <b>It used to skip, and a skip is indistinguishable from a pass in a summary line.</b> This
    /// project references BOTH binaries, so building the tests always builds them: there is no honest
    /// case where one is missing, and the one case that would produce it — a path computed wrongly for
    /// the platform — is exactly what would make this whole file quietly stop testing anything.
    /// </remarks>
    private static void MustExist(string exe) =>
        File.Exists(exe).Should().BeTrue(
            $"{exe} is referenced by this test project, so a build that produced the tests produced it "
            + "too — if it is not there, the path is wrong and this suite is testing nothing");

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
        MustExist(BugsExe);
        MustExist(McpExe);

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
        MustExist(BugsExe);

        Run(BugsExe, "--rotate-the-moon", _dir).Code.Should().Be(
            64, "a mode this binary has never heard of is how a caller detects an old one");

        Run(BugsExe, "--revoke", _dir).Code.Should().Be(
            65, "it KNOWS --revoke; the argument is what is wrong, and 64 would say otherwise");
    }

    /// <summary>
    /// The shipped server migrates the file the field has, stamps the month, refuses a flood with a
    /// number, and refuses a revoked key — over a real socket, with the real one-shots running beside it.
    /// </summary>
    /// <remarks>
    /// <para><b>Unit tests can all pass while the deployed `/ingest` never invokes the limiter</b>,
    /// because the middleware is wired wrong; and an in-process host cannot show a second PROCESS
    /// opening the database while the server holds it. So: the database is written first exactly as
    /// `bugs-v0.1.0` left it on the host (<c>user_version = 0</c>), the real binary migrates it on
    /// start, `--issue-key` and `--revoke` run as real concurrent openers, and every status this
    /// story added is read off a real socket. The one-server-per-directory refusal and the
    /// out-of-range rate refusal are exit codes, which only a process can show.</para>
    /// <para>The month is compared with the wall clock read on either side of the ingest, so a run
    /// that straddles a UTC month boundary is not a failure of the server.</para>
    /// </remarks>
    [Fact]
    public async Task TheRealServerMigratesLimitsAndStopsARevokedKey()
    {
        MustExist(BugsExe);
        var database = Path.Combine(_dir, "coai-bugs.db");
        TheMigrationTests.WriteAFileFromStepOneOnly(database);

        var port = FreePort();
        using var server = Start(BugsExe, $"--urls http://127.0.0.1:{port}", _dir, rate: "2");
        try
        {
            (await Listening(port)).Should().BeTrue("the server must migrate the field's file and come up");
            TestSql.Scalar(database, "PRAGMA user_version").Should().Be(
                CorpusSchema.Steps.Length.ToString(CultureInfo.InvariantCulture),
                "the REAL binary ran the migration on the shape the field has");

            var (key, id) = IssueKeyAndId();
            using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);

            var before = UtcMonth.Now(TimeProvider.System).Value;
            (await Ingest(http)).StatusCode.Should().Be(HttpStatusCode.OK);
            var after = UtcMonth.Now(TimeProvider.System).Value;
            var usage = UsageOf(database, id);
            usage.Month().Should().MatchRegex(UtcMonth.Shape().ToString()).And.BeOneOf(before, after);
            usage.Count().Should().Be(1);

            (await Ingest(http)).StatusCode.Should().Be(HttpStatusCode.OK);
            using var third = await Ingest(http);
            third.StatusCode.Should().Be(
                HttpStatusCode.TooManyRequests, "the third request inside a minute, at a limit of two");
            third.Headers.RetryAfter.Should().NotBeNull("a 429 with no number is a client that retries at once for ever");
            third.Headers.RetryAfter!.Delta.Should().BeGreaterThan(TimeSpan.Zero);
            (await third.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
                .Should().Contain("2 requests a minute", "the body names the limit");
            UsageOf(database, id).Count().Should().Be(2, "a 429 counts nothing");

            Run(BugsExe, $"--revoke --id {id}", _dir).Code.Should().Be(
                0, "the operator's stop button, run while the server serves — a real concurrent opener");
            (await Ingest(http)).StatusCode.Should().Be(
                HttpStatusCode.Unauthorized, "revoking must actually stop an ingest");

            Run(BugsExe, $"--urls http://127.0.0.1:{FreePort()}", _dir).Code.Should().Be(
                78, "one server per data directory: the limiter lives in the first one's memory");
        }
        finally
        {
            Stop(server);
        }

        Run(BugsExe, $"--urls http://127.0.0.1:{FreePort()}", _dir, rate: "1001").Code.Should().Be(
            78, "a rate past the cap is refused at startup rather than clamped");
    }

    private static readonly object OnePair = new
    {
        items = new[]
        {
            new
            {
                language = "CSharp",
                skeletonBefore = "method_1(var_1) { }",
                skeletonAfter = "method_1(var_1) { lock (var_2) { } }",
            },
        },
    };

    private static Task<HttpResponseMessage> Ingest(HttpClient http) =>
        http.PostAsJsonAsync("/ingest", OnePair, TestContext.Current.CancellationToken);

    /// <summary>What the key has done, read from the FILE by a third opener while the server serves.</summary>
    private static Usage UsageOf(string database, string id)
    {
        using var corpus = Corpus.Open(database);

        return corpus.UsageOf(new KeyId(id));
    }

    /// <summary>Mints a key through the real one-shot and reads its id off stderr, where the one-shot says it.</summary>
    private (string Key, string Id) IssueKeyAndId()
    {
        var issued = Run(BugsExe, "--issue-key --note a-scenario", _dir);
        issued.Code.Should().Be(0, $"--issue-key said: {issued.Err}");
        var id = Regex.Match(issued.Err, "issued ([0-9a-f]{16})").Groups[1].Value;
        id.Should().HaveLength(16, "the id is what --revoke needs, and stderr is where the one-shot says it");

        return (issued.Out.Trim(), id);
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
    private static Process Start(string exe, string args, string data, string key = "", string rate = "")
    {
        var how = Prepared(exe, args, data, key, rate);
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
        string exe, string args, string data, string key = "", string rate = "")
    {
        var how = Prepared(exe, args, data, key, rate);
        using var process = Process.Start(how)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit(60_000).Should().BeTrue($"{exe} {args} must finish");

        return (process.ExitCode, output.GetAwaiter().GetResult(), error);
    }

    /// <summary>The environment both launches share: the secret, the data directory, and what a scenario adds.</summary>
    /// <remarks>
    /// One builder for <see cref="Start"/> and <see cref="Run"/>, which used to carry two copies of
    /// it — a variable added to one and not the other is a scenario testing a differently
    /// configured server than it thinks. <paramref name="rate"/> is the limit setting as the unit
    /// would set it; empty leaves it unset, which is the default the server documents.
    /// </remarks>
    private static ProcessStartInfo Prepared(string exe, string args, string data, string key, string rate)
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
        // Never inherited from the machine: a developer's own setting must not decide a scenario.
        how.Environment[RatePerMinute.Variable] = rate.Length > 0 ? rate : null;
        if (key.Length > 0)
        {
            how.Environment["COAI_BUGS_KEY"] = key;
        }

        return how;
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
