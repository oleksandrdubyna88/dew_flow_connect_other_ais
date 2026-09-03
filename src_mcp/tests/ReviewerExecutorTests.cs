using System.Diagnostics;
using Xunit;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;

namespace CoaiMcp.Tests;

/// <summary>Shared plumbing for driving the fake CLI as if it were a vendor.</summary>
internal static class FakeCliInvocations
{
    /// <summary>
    /// The apphost, not <c>dotnet FakeCli.dll</c>: one process instead of two.
    /// </summary>
    /// <remarks>
    /// The extra <c>dotnet</c> host is what a killed timeout leaves behind — a CI job ended with
    /// three "Terminate orphan process: dotnet" lines, and every launch paid a second host start
    /// on a two-core runner.
    /// </remarks>
    internal static readonly string Exe = Path.Combine(
        AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli");

    internal const string CleanReview = """{"findings": []}""";

    internal static ReviewerInvocation Invoke(
        string provider,
        string[] verbArgs,
        TimeSpan? timeout = null,
        string outputFile = "")
        => new(
            provider,
            ReviewRole.Architecture,
            new ProcessRequest(Exe, verbArgs, AppContext.BaseDirectory)
            {
                Timeout = timeout ?? TimeSpan.FromMinutes(1),
            },
            outputFile);
}

[Collection("fakecli-env")]
public sealed class ReviewerExecutorTests
{
    private readonly ReviewerExecutor _executor = new(new ProcessLauncher());
    private readonly string _dir = Directory.CreateTempSubdirectory("coai-exec-").FullName;

    [Fact]
    public async Task Ok_FromStdout_TheGeminiPath()
    {
        var outcome = await _executor.RunAsync(
            FakeCliInvocations.Invoke("gemini", ["emit", FakeCliInvocations.CleanReview]),
            ct: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ReviewerOutcome.Ok>().Which.Repaired.Should().BeFalse();
    }

    [Fact]
    public async Task Ok_FromTheOutputFile_TheCodexPath()
    {
        var outputFile = Path.Combine(_dir, "answer.json");
        var outcome = await _executor.RunAsync(
            FakeCliInvocations.Invoke("codex", ["emit-to", outputFile, FakeCliInvocations.CleanReview], outputFile: outputFile),
            ct: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ReviewerOutcome.Ok>();
    }

    [Fact]
    public async Task Timeout_IsItsOwnOutcome_AndComesBackQuickly()
    {
        var watch = Stopwatch.StartNew();
        var outcome = await _executor.RunAsync(
            FakeCliInvocations.Invoke("codex", ["sleep", "20000"], timeout: TimeSpan.FromMilliseconds(700)),
            ct: TestContext.Current.CancellationToken);
        watch.Stop();

        outcome.Should().BeOfType<ReviewerOutcome.TimedOut>("a timeout is not an exit code");
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10), "the process tree is killed, not waited out");
    }

    [Fact]
    public async Task NonZeroExit_CarriesTheStderrTail()
    {
        var outcome = await _executor.RunAsync(
            FakeCliInvocations.Invoke("codex", ["stderr-exit", "the model refused politely", "3"]),
            ct: TestContext.Current.CancellationToken);

        var exit = outcome.Should().BeOfType<ReviewerOutcome.NonZeroExit>().Subject;
        exit.ExitCode.Should().Be(3);
        exit.StdErrTail.Should().Contain("refused politely");
    }

    [Fact]
    public async Task UnparseableThenRepaired_IsOk_AndSaysItWasRepaired()
    {
        var outcome = await _executor.RunAsync(
            FakeCliInvocations.Invoke("gemini", ["emit", "sorry, I panicked and wrote prose"]),
            repair: FakeCliInvocations.Invoke("gemini", ["emit", FakeCliInvocations.CleanReview]),
            ct: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ReviewerOutcome.Ok>().Which.Repaired.Should().BeTrue();
    }

    [Fact]
    public async Task UnparseableAfterTheOneRepair_IsItsOwnOutcome()
    {
        var counter = Path.Combine(_dir, "launches.txt");
        var garbage = FakeCliInvocations.Invoke("gemini", ["count", counter, "emit", "still prose"]);

        var outcome = await _executor.RunAsync(garbage, repair: garbage, ct: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ReviewerOutcome.Unparseable>().Which.Reason.Should().Contain("repair");
        (await File.ReadAllLinesAsync(counter, TestContext.Current.CancellationToken))
            .Should().HaveCount(2, "one launch plus exactly one repair, never a loop");
    }

    [Fact]
    public async Task ExitZeroButNoOutputFile_IsUnparseable_NotOk()
    {
        var outcome = await _executor.RunAsync(
            FakeCliInvocations.Invoke("codex", ["emit", "went to stdout instead"], outputFile: Path.Combine(_dir, "never-written.json")),
            ct: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ReviewerOutcome.Unparseable>();
    }

    [Theory]
    [InlineData(1, "", "429 Too Many Requests", true)]
    [InlineData(1, "openai rate limit reached", "", true)]
    // Codex's real words, from the first real run: it says neither "429" nor "rate limit", so a
    // quota exhaustion was misreported as a plain non-zero exit and never retried.
    [InlineData(1, "", "ERROR: You've hit your usage limit. Upgrade to Plus to continue using Codex", true)]
    [InlineData(1, "", "quota exceeded for this project", true)]
    [InlineData(1, "", "some other failure", false)]
    [InlineData(0, "", "429 in ordinary output of a fine run", false)]
    // A code is a code, not three digits. Measured 2026-09-03: this exact line was reported to a
    // person as "rate limited (after one retry)" — the digits were somewhere in a Cloudflare ray id
    // or the rest of the payload, and the vendor had answered 404. They were told to wait for a
    // quota that was never the problem, on a route no retry can fix.
    [InlineData(
        1,
        "",
        "{\"type\":\"error\",\"message\":\"Reconnecting... 2/5 (unexpected status 404 Not Found: "
            + "Unknown error, url: https://chatgpt.com/backend-api/codex/responses, "
            + "cf-ray: a3f4291e8b2c7d01-FRA)\"}",
        false)]
    [InlineData(1, "", "cf-ray: 9d5031aa77c0-AMS", false)]
    [InlineData(1, "", "prompt_tokens: 429, completion_tokens: 503", false)]
    [InlineData(1, "", "finished in 4290ms", false)]
    // And the shapes a vendor actually prints a code in, which must all still be caught.
    [InlineData(1, "", "HTTP 429", true)]
    [InlineData(1, "", "status: 503", true)]
    [InlineData(1, "", "503 UNAVAILABLE: This model is currently experiencing high demand", true)]
    [InlineData(1, "", "503 Service Unavailable", true)]
    public void RateLimit_Detection_IsATable(int exitCode, string stdout, string stderr, bool hit) =>
        RateLimit.Hit(new ProcessResult(exitCode, stdout, stderr, TimedOut: false)).Should().Be(hit);

    /// <summary>
    /// What the person is told when the vendor's own CLI fails for a reason no retry clears: the
    /// vendor's words, once, and no wording about a limit that was never hit.
    /// </summary>
    [Fact]
    public async Task ATransientVendorFailure_IsReportedAsItselfAndNotRetried()
    {
        var codex404 = "{\"type\":\"error\",\"message\":\"Reconnecting... 2/5 (unexpected status 404 "
            + "Not Found: Unknown error, url: https://chatgpt.com/backend-api/codex/responses, "
            + "cf-ray: a3f4291e8b2c7d01-FRA)\"}";
        var result = new ProcessResult(1, string.Empty, codex404, TimedOut: false);

        RateLimit.Hit(result).Should().BeFalse("a 404 is not a rate limit");

        var scheduler = new BoundedScheduler(globalCap: 1, perProviderCap: 1);
        var launcher = new CountingLauncher(result);
        var outcomes = await scheduler.RunAllAsync(
            [new ReviewerWork(FakeCliInvocations.Invoke("codex", ["noop"]))],
            new ReviewerExecutor(launcher),
            TestContext.Current.CancellationToken);

        launcher.Calls.Should().Be(1, "the route answered 404; a second attempt asks the same route");
        var outcome = outcomes[0].Outcome;
        outcome.Should().BeOfType<ReviewerOutcome.NonZeroExit>();
        ((ReviewerOutcome.NonZeroExit)outcome).StdErrTail.Should().Contain("404 Not Found");
    }

    private sealed class CountingLauncher(ProcessResult answer) : IProcessLauncher
    {
        public int Calls { get; private set; }

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(answer);
        }
    }
}

/// <summary>
/// The launcher against a child that does not want our input.
/// </summary>
/// <remarks>
/// Found by the suite itself, on both platforms, as a one-in-many flake: a git command exited
/// before the prompt finished writing and <c>StandardInput.WriteAsync</c> threw a broken pipe
/// straight out of the launcher. Every process in this product goes through here, so the failure
/// was not "a flaky test" — it was a reviewer that could die as an exception instead of as one of
/// the five named outcomes, and a round that failed as a whole because one CLI exited early.
/// </remarks>
[Collection("fakecli-env")]
public sealed class ProcessLauncherStdInTests
{
    private readonly ProcessLauncher _launcher = new();

    [Fact]
    public async Task AChildThatExitsWithoutReadingStdIn_IsNotAnException()
    {
        // A megabyte is past any pipe buffer, so the write cannot complete before `emit` exits —
        // the race the flake lost is the deterministic case here.
        var request = new ProcessRequest(FakeCliInvocations.Exe, ["emit", "done"], AppContext.BaseDirectory)
        {
            StdIn = new string('x', 1024 * 1024),
            Timeout = TimeSpan.FromSeconds(30),
        };

        var result = await _launcher.RunAsync(request, TestContext.Current.CancellationToken);

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("done", "the child's own answer still arrives");
        result.TimedOut.Should().BeFalse();
    }

    [Fact]
    public async Task TheChildStillReceivesStdIn_WhenItActuallyReadsIt()
    {
        // The guard must not turn into "stdin is optional": the prompt travels this way.
        var record = Directory.CreateTempSubdirectory("coai-stdin-").FullName;
        Environment.SetEnvironmentVariable("FAKECLI_MODE", "vendor");
        Environment.SetEnvironmentVariable("FAKECLI_RECORD_DIR", record);
        try
        {
            var request = new ProcessRequest(FakeCliInvocations.Exe, ["exec", "-"], AppContext.BaseDirectory)
            {
                StdIn = "the whole review prompt",
                Timeout = TimeSpan.FromSeconds(30),
            };

            await _launcher.RunAsync(request, TestContext.Current.CancellationToken);

            var recorded = await File.ReadAllTextAsync(
                Directory.EnumerateFiles(record, "*.argv").Single(), TestContext.Current.CancellationToken);
            recorded.Should().Contain("the whole review prompt");
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAKECLI_MODE", null);
            Environment.SetEnvironmentVariable("FAKECLI_RECORD_DIR", null);
        }
    }
}

/// <summary>
/// What the child receives on stdin, byte for byte.
/// </summary>
/// <remarks>
/// The prompt travels on stdin for every vendor, so what precedes it matters. `Encoding.UTF8` has
/// a byte-order mark, and .NET flushes it into the child from inside `Process.Start()` — three
/// stray bytes in front of every prompt, and a broken pipe out of Start itself when the child had
/// already exited. WSL loses that race reliably: five tests, one cause.
/// <para>The bytes are recorded UNDECODED on purpose. The first version of this test read the
/// prompt back through the fake CLI's own `Console.In`, which strips a BOM while decoding — so it
/// passed against the unfixed launcher and proved nothing. A decoder cannot be the witness to a
/// question about bytes.</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class StdInBytesTests
{
    [Fact]
    public async Task ThePromptArrivesWithNoByteOrderMarkInFrontOfIt()
    {
        var file = Path.Combine(Directory.CreateTempSubdirectory("coai-bom-").FullName, "stdin.bin");
        Environment.SetEnvironmentVariable("FAKECLI_MODE", "vendor");
        Environment.SetEnvironmentVariable("FAKECLI_RECORD_STDIN_BYTES", file);
        try
        {
            await new ProcessLauncher().RunAsync(
                new ProcessRequest(FakeCliInvocations.Exe, ["exec", "-"], AppContext.BaseDirectory)
                {
                    StdIn = "## The plan under review",
                    Timeout = TimeSpan.FromSeconds(30),
                },
                TestContext.Current.CancellationToken);

            var bytes = await File.ReadAllBytesAsync(file, TestContext.Current.CancellationToken);

            bytes.Should().StartWith([(byte)'#', (byte)'#'], "the prompt is the first thing the child reads");
            bytes.Take(3).Should().NotEqual([(byte)0xEF, (byte)0xBB, (byte)0xBF], "no byte-order mark");
            System.Text.Encoding.UTF8.GetString(bytes).Should().Be("## The plan under review");
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAKECLI_MODE", null);
            Environment.SetEnvironmentVariable("FAKECLI_RECORD_STDIN_BYTES", null);
        }
    }
}
