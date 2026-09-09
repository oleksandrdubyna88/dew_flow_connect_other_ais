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

        using var shim = StartShim(info);
        // Waits for a READABLE claim, not for the file to appear. Waiting on File.Exists killed the
        // shim in the window between creating the file and writing it, which is a real window and a
        // real defect — it left a claim naming no job, and the parent could not cancel. It is fixed
        // (the write goes to a sibling and moves over), and this waits for the thing it actually
        // needs so the test cannot depend on how fast the machine is. It failed on a win-x64 release
        // runner and nowhere else: `Expected string to be "job-77" ... but "" has a length of 0`.
        await WaitForAsync(
            () => RemoteRuntime.ReadClaim(invocation.JobFile).JobId.Length > 0,
            "the claim file to name its job",
            shim);
        shim.Process.Kill(entireProcessTree: true);
        await shim.Process.WaitForExitAsync();

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

            using var shim = StartShim(info);
            // As early as this can be arranged: the instant ANYTHING appears under the claim's name,
            // including the sibling being written. On a slow machine that lands inside the write.
            await WaitForAsync(
                () => File.Exists(invocation.JobFile) || File.Exists(invocation.JobFile + ".writing"),
                "the claim file or its .writing sibling to appear",
                shim);
            shim.Process.Kill(entireProcessTree: true);
            await shim.Process.WaitForExitAsync();

            if (File.Exists(invocation.JobFile))
            {
                RemoteRuntime.ReadClaim(invocation.JobFile).JobId.Should().Be("job-77",
                    "a claim file that exists must name its job — a parent holding half of one cannot "
                    + "cancel, and the review keeps costing money until the server's own deadline");
            }
        }
    }

    /// <summary>
    /// How long a PREREQUISITE may take before the interesting part can begin.
    /// </summary>
    /// <remarks>
    /// <para>It was thirty seconds, chosen once against the machine the test was written on, and it
    /// cost `mcp-v0.18.13` its Windows ARM build: five of six release legs passed the same test and
    /// the sixth ran out at 30s 359ms, so no `coai-mcp-0.18.13-win-arm64.zip` was ever published and
    /// every install of that version on that platform answers 404.</para>
    /// <para>This wait measures nothing. It is spent on a cold .NET start, a sign-in, an HTTP round
    /// trip and a first write, immediately after a Release build of the whole solution, on whichever
    /// of six machines the matrix picked. A generous bound costs a fast runner nothing — the wait
    /// returns the instant the condition holds — and costs a slow one a passing build. What a
    /// prerequisite must never do is give up quietly and leave a sentence nobody can act on.</para>
    /// </remarks>
    private static readonly TimeSpan PrerequisiteWait = TimeSpan.FromSeconds(120);

    /// <summary>
    /// The shim, running, with its stderr being READ.
    /// </summary>
    /// <remarks>
    /// <para><b>Reading it is not for the diagnostic; it is so the child can run at all.</b> Both
    /// scenarios set <c>RedirectStandardError</c> and neither drained the pipe, and a child that
    /// fills a redirected pipe nobody is reading BLOCKS on its next write — for ever, since the
    /// parent's next act is to wait for it. The symptom of that is a prerequisite wait running out
    /// on one machine and not another, which is exactly the failure being fixed here; whether it was
    /// this failure cannot be proved after the fact, but a redirected stream with no reader is a
    /// latent hang either way.</para>
    /// <para>Disposal kills the tree. A wait that throws leaves no <c>finally</c> of its own, and a
    /// leaked <c>coai-mcp</c> holding a claim file is how one failing test makes the next three
    /// fail for a reason that has nothing to do with them. (codex and local, the plan round.)</para>
    /// </remarks>
    /// <remarks>
    /// A CLASS, not a record: it owns a process and a growing buffer, which is a stateful service
    /// rather than a data container, and the repository's own rule keeps those apart. (gemini.)
    /// </remarks>
    private sealed class RunningShim(Process process) : IDisposable
    {
        /// <summary>
        /// What the child has said, kept to a bound.
        /// </summary>
        /// <remarks>
        /// A shim can talk for the whole 120 seconds, and an unbounded buffer would be copied again
        /// into the failure message — a hundred megabytes of progress notes is not a diagnostic.
        /// The LAST lines are kept, because what a process said just before it stopped is the part
        /// that explains why. (codex.)
        /// </remarks>
        private const int SaidCap = 8_000;

        private readonly StringBuilder _heard = new();

        public Process Process { get; } = process;

        internal void Heard(string line)
        {
            lock (_heard)
            {
                _heard.AppendLine(line);
                if (_heard.Length > SaidCap)
                {
                    _heard.Remove(0, _heard.Length - SaidCap);
                }
            }
        }

        /// <summary>Everything the child has said so far, safe to read while it is still saying it.</summary>
        public string Said
        {
            get
            {
                lock (_heard)
                {
                    return _heard.ToString().Trim();
                }
            }
        }

        public void Dispose()
        {
            try
            {
                if (!Process.HasExited)
                {
                    Process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Already gone, or gone between the question and the answer, or a tree member the OS
                // would not let us touch. Nothing left to kill and nothing a test can do about it.
                // (gemini: Kill throws Win32Exception on Windows, not only InvalidOperationException.)
            }

            Process.Dispose();
        }
    }

    /// <summary>Starts the shim with its stderr redirected AND drained.</summary>
    private static RunningShim StartShim(ProcessStartInfo info)
    {
        info.RedirectStandardError = true;
        info.UseShellExecute = false;

        var process = Process.Start(info)!;
        var shim = new RunningShim(process);
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                shim.Heard(e.Data);
            }
        };
        process.BeginErrorReadLine();

        return shim;
    }

    /// <summary>
    /// Waits for a prerequisite, and gives up EARLY when the child that must satisfy it has died.
    /// </summary>
    /// <remarks>
    /// <para>The description and the child are required rather than optional, so a call site cannot
    /// go back to a bare condition and a hand-picked number: the whole defect was one constant,
    /// chosen once, with a failure sentence that named neither what was awaited nor who was meant to
    /// deliver it. (codex, the plan round: making tests 1–2 pass while the scenarios still pass 30s
    /// would fix nothing.)</para>
    /// <para>A dead child cannot satisfy the condition, so waiting out the rest of the budget only
    /// delays the report by two minutes. The condition is read ONCE more after an exit is seen,
    /// because a child can satisfy it on its way out — and the state is reported as an OBSERVATION
    /// with the moment it was taken, since a process can change state between the check and the
    /// sentence. (codex again, and the same point from local twice.)</para>
    /// </remarks>
    private static Task WaitForAsync(Func<bool> condition, string what, RunningShim shim) =>
        WaitUntilAsync(condition, what, shim, PrerequisiteWait);

    /// <summary>
    /// The same wait on a deliberately small budget — for the tests OF the wait, and nothing else.
    /// </summary>
    /// <remarks>
    /// A separate method rather than an optional parameter, because an optional parameter is exactly
    /// how the thirty seconds got there: a scenario could pass its own number again and the compiler
    /// would say nothing. Raised three times by codex on the code round, and by gemini from the other
    /// side — a diagnostic test inheriting the two-minute budget blocks the suite for two minutes the
    /// day it regresses.
    /// </remarks>
    private static Task WaitBrieflyForAsync(
        Func<bool> condition, string what, RunningShim shim, TimeSpan limit) =>
        WaitUntilAsync(condition, what, shim, limit);

    private static async Task WaitUntilAsync(
        Func<bool> condition, string what, RunningShim shim, TimeSpan deadline)
    {
        var clock = Stopwatch.StartNew();
        // A DO, so the condition is read at least once. A budget small enough to be already spent
        // would otherwise report a failure for something nobody ever looked at. (gemini.)
        do
        {
            if (condition())
            {
                return;
            }

            if (Gone(shim.Process) is { } code && !condition())
            {
                throw Failure(what, clock.Elapsed, deadline, $"had exited with {code}", Drained(shim));
            }

            await Task.Delay(50);
        }
        while (clock.Elapsed < deadline);

        throw Failure(what, clock.Elapsed, deadline, StateOf(shim.Process), shim.Said);
    }

    /// <summary>
    /// Everything the child said, including whatever was still in flight when it exited.
    /// </summary>
    /// <remarks>
    /// <c>ErrorDataReceived</c> is asynchronous, so a child that writes its reason and exits can have
    /// that reason still queued at the moment the exit is noticed — and the reason is the whole point
    /// of reporting the exit. <c>WaitForExit</c> with no timeout is documented to wait for the
    /// asynchronous handlers to finish, which <c>WaitForExitAsync</c> does not promise. (codex.)
    /// </remarks>
    private static string Drained(RunningShim shim)
    {
        try
        {
            shim.Process.WaitForExit();
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // The handle is gone; whatever was already buffered is what there is to report.
        }

        return shim.Said;
    }

    /// <summary>The exit code if the child is gone, or nothing while it lives.</summary>
    /// <remarks>
    /// Guarded, because both properties throw once the handle is gone — and a test that dies with
    /// <c>InvalidOperationException</c> instead of its own timeout has thrown away the evidence it
    /// exists to produce. (gemini, the plan round.)
    /// </remarks>
    private static int? Gone(Process process)
    {
        try
        {
            return process.HasExited ? process.ExitCode : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static string StateOf(Process process) =>
        Gone(process) is { } code ? $"had exited with {code}" : "was still running";

    /// <summary>
    /// What ran out, how long it had, what the child was doing, and what the child said.
    /// </summary>
    /// <remarks>
    /// The BUDGET is named as well as the elapsed time, so a reader can tell the prerequisite's two
    /// minutes from a diagnostic test's fraction of a second without opening the file. (gemini.)
    /// </remarks>
    private static TimeoutException Failure(
        string what, TimeSpan waited, TimeSpan budget, string state, string said) =>
        new($"waiting for {what}: it was still not true after {waited.TotalSeconds:0.0}s "
            + $"of a {budget.TotalSeconds:0.0}s budget. The child {state} when that was checked. "
            + (said.Length == 0 ? "It had said nothing on stderr." : $"It had said:{Environment.NewLine}{said}"));

    // ------------------------------------------------------------------------------------------
    // What a prerequisite wait has to say when it runs out (2026-09-09).
    //
    // mcp-v0.18.13, 2026-09-08 17:35 UTC. The win-arm64 leg of the release matrix failed:
    //
    //   failed …AShimKilledMidClaim_LeavesEitherNothingOrAWholeClaim_NeverHalf (30s 359ms)
    //     System.TimeoutException : the condition was still false after 30s
    //
    // Five of six legs passed the same test, and the build before it was clean — so this was a WAIT
    // that ran out, not a binary that was wrong. It cost the release a platform: no
    // coai-mcp-0.18.13-win-arm64.zip was ever published and every Windows ARM install of that
    // version answers 404. The sentence above is every word of evidence there was, and it cannot
    // tell a slow machine from a child that died on the way.
    // ------------------------------------------------------------------------------------------

    [Fact]
    public async Task AFailedWait_NamesWhatItWaitedForAndWhatTheChildSaid()
    {
        // A child that says something and stops: both halves of the diagnostic, in one run.
        using var shim = StartShim(new ProcessStartInfo(ShimExe) { ArgumentList = { "--not-a-real-flag" } });
        await shim.Process.WaitForExitAsync();

        var failure = await Record.ExceptionAsync(
            () => WaitBrieflyForAsync(() => false, "the claim file to appear", shim, TimeSpan.FromMilliseconds(200)));

        failure.Should().BeOfType<TimeoutException>().Which.Message
            .Should().Contain("the claim file to appear", "the wait must say what it was waiting FOR")
            .And.Contain("exited", "and that the child was gone, which is a different cure from a slow one")
            .And.Contain("64", "naming the code it exited with")
            .And.Contain("unknown argument", "and quoting what the child said on its way out");
    }

    /// <summary>
    /// A child that stays: no arguments, so it serves MCP and waits on a stdin nobody closes.
    /// </summary>
    private static RunningShim StartLivingShim() =>
        StartShim(new ProcessStartInfo(ShimExe) { RedirectStandardInput = true });

    [Fact]
    public async Task AFailedWait_SaysTheChildWasStillRunning()
    {
        using var shim = StartLivingShim();

        var failure = await Record.ExceptionAsync(
            () => WaitBrieflyForAsync(() => false, "something that never happens", shim, TimeSpan.FromMilliseconds(200)));

        failure.Should().BeOfType<TimeoutException>().Which.Message
            .Should().Contain("still running", "a live child and a dead one send a reader to different places")
            .And.Contain("0.2s", "and the wait says how long it actually waited");
    }

    /// <summary>
    /// The other side: a condition that comes true late still returns, rather than being outrun.
    /// </summary>
    /// <remarks>
    /// The whole point of the change is that the deadline is generous. A test that only ever proves
    /// the failure path would pass just as well against a wait that gave up immediately. (gemini,
    /// the plan round, asked for a local stand-in for the slow runner.)
    /// </remarks>
    [Fact]
    public async Task AWaitReturnsWhenTheConditionComesTrueLate()
    {
        using var shim = StartLivingShim();
        var clock = Stopwatch.StartNew();

        await WaitBrieflyForAsync(
            () => clock.Elapsed > TimeSpan.FromMilliseconds(300), "a late condition", shim, TimeSpan.FromSeconds(5));

        clock.Elapsed.Should().BeGreaterThan(TimeSpan.FromMilliseconds(300));
    }
}
