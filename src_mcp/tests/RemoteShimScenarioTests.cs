using System.Diagnostics;
using System.Net;
using System.Text;
using CoaiMcp.Core.Findings;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The remote reviewer flow as it actually runs: the REAL <c>coai-mcp --ask-remote</c> binary, in its
/// own process, against a real HTTP server on a real socket.
/// </summary>
/// <remarks>
/// <para>Every other test here calls the shim's pieces in-process. That proves the pieces and not the
/// wiring — and the wiring is where this product has twice shipped a break that every unit test
/// missed: a released server binary that could not serve one request because body binding had no JSON
/// resolver under AOT, and a page whose minified binding was renamed. Both were caught only by
/// something that ran the real artefact.</para>
/// <para>So this test builds the command line with the real adapter, launches what it names, and reads
/// what comes back: the answer file, the usage line on stdout, and the exit code. Nothing is
/// simulated except the Team server itself.</para>
/// </remarks>
public sealed class RemoteShimScenarioTests : IAsyncLifetime
{
    private HttpListener _server = null!;
    private string _dataDir = null!;
    private string _outputDir = null!;
    private string _prefix = null!;

    /// <summary>What the stub answers, set per test.</summary>
    private Func<HttpListenerContext, (int Status, string Body)> _answer =
        _ => (200, """{"id":"1","status":"done","position":0,"answer":"{}","tokensIn":0,"tokensOut":0}""");

    private readonly List<string> _paths = [];

    public ValueTask InitializeAsync()
    {
        _dataDir = Directory.CreateTempSubdirectory("coai-shim-data-").FullName;
        _outputDir = Directory.CreateTempSubdirectory("coai-shim-out-").FullName;

        // A free port, taken by asking the OS rather than by guessing one.
        var port = FreePort();
        _prefix = $"http://127.0.0.1:{port}/";
        _server = new HttpListener();
        _server.Prefixes.Add(_prefix);
        _server.Start();
        _ = Task.Run(ServeAsync);

        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            _server.Stop();
            _server.Close();
        }
        catch (Exception e) when (e is ObjectDisposedException or HttpListenerException) { }

        foreach (var dir in new[] { _dataDir, _outputDir })
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException) { }
        }

        return ValueTask.CompletedTask;
    }

    private static int FreePort()
    {
        using var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        return port;
    }

    private async Task ServeAsync()
    {
        while (_server.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _server.GetContextAsync();
            }
            catch (Exception e) when (e is HttpListenerException or ObjectDisposedException)
            {
                return;
            }

            lock (_paths)
            {
                _paths.Add($"{context.Request.HttpMethod} {context.Request.Url!.AbsolutePath}");
            }

            var (status, body) = _answer(context);
            var bytes = Encoding.UTF8.GetBytes(body);
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
        }
    }

    private static string ShimExe
    {
        get
        {
            var configuration = AppContext.BaseDirectory.Contains("Release") ? "Release" : "Debug";

            return Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "src", "bin", configuration, "net10.0",
                OperatingSystem.IsWindows() ? "coai-mcp.exe" : "coai-mcp"));
        }
    }

    private void SignIn(string token = "a-token") =>
        TeamServerAuth.WriteToken(TeamServerAuth.TokenPath(_dataDir, _prefix), token);

    /// <summary>Run what the ADAPTER says to run — argv included, so the command line is under test too.</summary>
    private async Task<(int Exit, string StdOut, string StdErr, ReviewerInvocation Invocation)> RunAsync(
        TimeSpan timeout)
    {
        var invocation = new RemoteRuntime("codex", _prefix).Build(
            ReviewRole.Architecture,
            "review this",
            _outputDir,
            Path.Combine(_outputDir, "schema.json"),
            _outputDir,
            new ReviewerSettings("codex") { Model = "m", DataDir = _dataDir, Timeout = timeout });

        // The adapter names this binary through the dotnet host when it is running from a dll; the
        // test runs the published exe directly, so only the arguments are taken from it.
        var info = new ProcessStartInfo(ShimExe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in invocation.Request.Arguments.SkipWhile(a => a != "--ask-remote"))
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode, await stdout, await stderr, invocation);
    }

    [Fact]
    public async Task AReviewGoesOutAndItsAnswerComesBack()
    {
        SignIn();
        _answer = context => context.Request.HttpMethod == "POST"
            ? (202, """{"id":"job-1","position":0}""")
            : (200, """{"id":"job-1","status":"done","position":0,"answer":"THE VENDOR'S TEXT","tokensIn":11,"tokensOut":22}""");

        var (exit, stdout, stderr, invocation) = await RunAsync(TimeSpan.FromMinutes(2));

        exit.Should().Be(RemoteAsk.Ok, $"stderr said: {stderr}");
        // The answer lands where the adapter told the round it would — the half a unit test cannot check.
        File.ReadAllText(invocation.OutputFile).Should().Be("THE VENDOR'S TEXT");
        // And the usage the adapter reads back off stdout is the usage the server reported.
        var usage = new RemoteRuntime("codex", _prefix).ReadUsage(
            invocation, new Runners.Processes.ProcessResult(exit, stdout, stderr, false));
        usage.TokensIn.Should().Be(11);
        usage.TokensOut.Should().Be(22);
    }

    [Fact]
    public async Task TheReviewIsSUBMITTEDToTheRouteTheServerActuallyServes()
    {
        SignIn();
        _answer = context => context.Request.HttpMethod == "POST"
            ? (202, """{"id":"job-1","position":0}""")
            : (200, """{"id":"job-1","status":"done","position":0,"answer":"{}","tokensIn":0,"tokensOut":0}""");

        await RunAsync(TimeSpan.FromMinutes(2));

        lock (_paths)
        {
            // `…//api/reviews` would 404 on a real server while matching the token perfectly — the
            // failure the URL normalisation exists to prevent, and only a real socket can observe it.
            _paths.Should().Contain("POST /api/reviews");
            _paths.Should().NotContain(p => p.Contains("//api"));
        }
    }

    [Fact]
    public async Task WithoutATokenItRefusesBeforeItAsksAnybody()
    {
        var (exit, _, stderr, _) = await RunAsync(TimeSpan.FromMinutes(2));

        exit.Should().Be(RemoteAsk.NotSignedIn);
        stderr.Should().Contain("Team servers section");
        lock (_paths)
        {
            _paths.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task AVendorFailureCarriesTheServersOwnReasonAndItsOwnExitCode()
    {
        SignIn();
        _answer = context => context.Request.HttpMethod == "POST"
            ? (202, """{"id":"job-1","position":0}""")
            : (200, """{"id":"job-1","status":"failed","position":0,"failure":"rate_limited","reason":"resets 9:30pm"}""");

        var (exit, _, stderr, _) = await RunAsync(TimeSpan.FromMinutes(2));

        exit.Should().Be(RemoteAsk.VendorFailed);
        stderr.Should().Contain("rate_limited").And.Contain("9:30pm");
    }

    [Fact]
    public async Task ARejectedTokenSaysToSignInAgain()
    {
        SignIn("a-stale-token");
        _answer = _ => (401, """{"error":"token expired"}""");

        var (exit, _, stderr, _) = await RunAsync(TimeSpan.FromMinutes(2));

        exit.Should().Be(RemoteAsk.NotSignedIn);
        stderr.Should().Contain("sign in again");
    }

    [Fact]
    public async Task AReviewThatNEVERFinishesIsCancelledAndSaidSoWithinItsOwnDeadline()
    {
        // The two clocks, observed rather than asserted about: the shim's deadline is the shorter one,
        // so reaching it must produce a SENTENCE well before the executor's kill — and the job must be
        // cancelled on the way out, or it keeps running on the company's subscription.
        SignIn();
        _answer = context => context.Request.HttpMethod == "POST"
            ? (202, """{"id":"job-1","position":3}""")
            : (200, """{"id":"job-1","status":"queued","position":3}""");

        // 25 s of reviewer timeout is a ~15 s shim deadline (the margin is 10) — long enough that
        // process start-up cannot be mistaken for the deadline, short enough to keep the suite honest
        // about what it costs.
        var clock = Stopwatch.StartNew();
        var (exit, _, stderr, invocation) = await RunAsync(TimeSpan.FromSeconds(25));
        clock.Stop();

        exit.Should().Be(RemoteAsk.TooSlow, $"stderr said: {stderr}");
        stderr.Should().Contain("cancelled");
        stderr.Should().Contain("3 in the queue");
        // The shim's deadline is derived from the reviewer timeout and is deliberately shorter.
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(25));
        clock.Elapsed.Should().BeGreaterThan(TimeSpan.FromSeconds(10), "it really waited, rather than failing fast");
        lock (_paths)
        {
            _paths.Should().Contain("DELETE /api/reviews/job-1", "an abandoned review must stop costing money");
        }

        // And nothing is left claiming to be outstanding, because this one was actually cancelled.
        File.Exists(invocation.JobFile).Should().BeFalse();
    }

    [Fact]
    public async Task AKilledShimLEAVESAClaimTheParentCanActOn()
    {
        // The other half of the same guarantee, and the one that needed the file: when the process is
        // killed it runs no cleanup at all, so what it wrote down beforehand is all that survives.
        SignIn();
        _answer = context => context.Request.HttpMethod == "POST"
            ? (202, """{"id":"job-77","position":1}""")
            : (200, """{"id":"job-77","status":"queued","position":1}""");

        var invocation = new RemoteRuntime("codex", _prefix).Build(
            ReviewRole.Architecture, "review this", _outputDir,
            Path.Combine(_outputDir, "schema.json"), _outputDir,
            new ReviewerSettings("codex") { Model = "m", DataDir = _dataDir, Timeout = TimeSpan.FromMinutes(5) });

        var info = new ProcessStartInfo(ShimExe) { RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in invocation.Request.Arguments.SkipWhile(a => a != "--ask-remote"))
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info)!;
        // Waits for a READABLE claim, not for the file to appear. Waiting on File.Exists killed the
        // shim in the window between creating the file and writing it, which is a real window and a
        // real defect — it left a claim naming no job, and the parent could not cancel. It is fixed
        // (the write goes to a sibling and moves over), and this waits for the thing it actually
        // needs so the test cannot depend on how fast the machine is. It failed on a win-x64 release
        // runner and nowhere else: `Expected string to be "job-77" ... but "" has a length of 0`.
        await WaitForAsync(
            () => RemoteRuntime.ReadClaim(invocation.JobFile).JobId.Length > 0, TimeSpan.FromSeconds(30));
        process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync();

        var claim = RemoteRuntime.ReadClaim(invocation.JobFile);
        claim.JobId.Should().Be("job-77");
        claim.TokenFile.Should().NotBeEmpty("the parent has no data directory to resolve it from");

        // And the parent, holding only that file, can stop the job.
        var cancelled = await RemoteRuntime.CancelAbandonedAsync(invocation.JobFile, new HttpClient());
        cancelled.Should().BeTrue();
        lock (_paths)
        {
            _paths.Should().Contain("DELETE /api/reviews/job-77");
        }
    }

    /// <summary>
    /// Killed as early as the kill can be arranged: whatever the parent finds, it is never HALF a
    /// claim.
    /// </summary>
    /// <remarks>
    /// <para><b>This is the regression test, and the test above is not.</b> That one waits for a
    /// readable claim before killing, which is what makes it deterministic — and a reviewer pointed
    /// out that determinism removed the exact window that found the defect. Both are needed: one
    /// asserts the parent can cancel a claim that exists, and this one asserts nothing can be left in
    /// between.</para>
    /// <para>The assertion is what makes it stable rather than a race dressed up as a test. Two
    /// outcomes are correct — the shim died before publishing anything, or a whole claim is there —
    /// and only the third is a failure: a file that exists and names nothing, which is what a
    /// truncate-then-write leaves and what `CancelAbandonedAsync` cannot act on. Reintroducing a
    /// direct `File.WriteAllText` brings that third state back and this goes red; every other test in
    /// the suite would stay green, which was the finding.</para>
    /// <para><b>What this test cannot claim.</b> It was NOT observed to go red against the old
    /// truncate-then-write on this machine: reverting the fix and running it twice left it green,
    /// because the window between creating the file and filling it is microseconds here and killing a
    /// process takes longer than that. That is not a weakness of the test — it is the reason the
    /// defect survived every local run and was found by a loaded CI runner instead. The test
    /// reproduces the CI failure by construction (kill on the first sign of the file, assert the
    /// parent never sees half a claim) and cannot fail falsely, because two of the three outcomes are
    /// accepted.</para>
    /// <para>The `.writing` sibling may exist after a kill — no `finally` runs for one — which is why
    /// its name is derived from the job file rather than random: the next attempt writes over it
    /// instead of leaving an orphan nobody can name.</para>
    /// </remarks>
    [Fact]
    public async Task AShimKilledMidClaim_LeavesEitherNothingOrAWholeClaim_NeverHalf()
    {
        SignIn();
        _answer = context => context.Request.HttpMethod == "POST"
            ? (202, """{"id":"job-77","position":1}""")
            : (200, """{"id":"job-77","status":"queued","position":1}""");

        for (var attempt = 0; attempt < 6; attempt++)
        {
            var invocation = new RemoteRuntime("codex", _prefix).Build(
                ReviewRole.Architecture, "review this", _outputDir,
                Path.Combine(_outputDir, "schema.json"), _outputDir,
                new ReviewerSettings("codex") { Model = "m", DataDir = _dataDir, Timeout = TimeSpan.FromMinutes(5) });

            var info = new ProcessStartInfo(ShimExe) { RedirectStandardError = true, UseShellExecute = false };
            foreach (var argument in invocation.Request.Arguments.SkipWhile(a => a != "--ask-remote"))
            {
                info.ArgumentList.Add(argument);
            }

            using var process = Process.Start(info)!;
            // As early as this can be arranged: the instant ANYTHING appears under the claim's name,
            // including the sibling being written. On a slow machine that lands inside the write.
            await WaitForAsync(
                () => File.Exists(invocation.JobFile) || File.Exists(invocation.JobFile + ".writing"),
                TimeSpan.FromSeconds(30));
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();

            if (File.Exists(invocation.JobFile))
            {
                RemoteRuntime.ReadClaim(invocation.JobFile).JobId.Should().Be("job-77",
                    "a claim file that exists must name its job — a parent holding half of one cannot "
                    + "cancel, and the review keeps costing money until the server's own deadline");
            }
        }
    }

    private static async Task WaitForAsync(Func<bool> condition, TimeSpan limit)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < limit)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"the condition was still false after {limit.TotalSeconds:0}s");
    }
}
