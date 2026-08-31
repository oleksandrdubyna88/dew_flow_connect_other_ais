using System.Diagnostics;
using Xunit;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;

namespace CoaiMcp.Tests;

/// <summary>Shared plumbing for driving the fake CLI as if it were a vendor.</summary>
internal static class FakeCliInvocations
{
    internal static readonly string Dll = Path.Combine(AppContext.BaseDirectory, "FakeCli.dll");

    internal const string CleanReview = """{"findings": []}""";

    internal static ReviewerInvocation Invoke(
        string provider,
        string[] verbArgs,
        TimeSpan? timeout = null,
        string outputFile = "")
        => new(
            provider,
            ReviewRole.Architecture,
            new ProcessRequest("dotnet", [Dll, .. verbArgs], AppContext.BaseDirectory)
            {
                Timeout = timeout ?? TimeSpan.FromMinutes(1),
            },
            outputFile);
}

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
    [InlineData(1, "", "some other failure", false)]
    [InlineData(0, "", "429 in ordinary output of a fine run", false)]
    public void RateLimit_Detection_IsATable(int exitCode, string stdout, string stderr, bool hit) =>
        RateLimit.Hit(new ProcessResult(exitCode, stdout, stderr, TimedOut: false)).Should().Be(hit);
}
