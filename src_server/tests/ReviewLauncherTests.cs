using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>
/// The REAL <see cref="ReviewLauncher"/>, against a process that behaved.
/// </summary>
/// <remarks>
/// <para>Every other test of this path drives <see cref="IReviewLauncher"/> through a fake that
/// hands the runner a <c>ReviewerOutcome.Ok</c> — a shape the real launcher has never once
/// produced. That is why 171 green tests sat on top of a Team server on which a review could not
/// succeed: `usage.jsonl` held two lines after weeks, both `NotStarted`, and the CLI transcript
/// beside them showed the model answering perfectly each time.</para>
/// <para>So this test starts one layer lower — at the launcher itself, with a fake PROCESS rather
/// than a fake launcher — because the defect lives in the translation between what the executor
/// says about a good run and what this server makes of it.</para>
/// </remarks>
public sealed class ReviewLauncherTests
{
    /// <summary>What `claude -p --output-format json` prints when it worked.</summary>
    private const string Envelope = """
        {"type":"result","subtype":"success","is_error":false,"result":"{\"findings\":[]}"}
        """;

    [Fact]
    public async Task AProcessThatExitedZeroWithAnAnswer_IsNotReportedAsNotStarted()
    {
        var attempt = await Launch(new ProcessResult(0, Envelope, string.Empty, TimedOut: false));

        attempt.Should().BeOfType<ReviewAttempt.Answered>(
            "the process ran, exited zero and answered — the executor's null verdict MEANS that");
    }

    [Fact]
    public async Task AProcessThatExitedZeroWithAnAnswer_CarriesTheVendorsRawText()
    {
        var attempt = await Launch(new ProcessResult(0, Envelope, string.Empty, TimedOut: false));

        attempt.Should().BeOfType<ReviewAttempt.Answered>()
            .Which.Raw.Should().Be("""{"findings":[]}""");
    }

    /// <summary>
    /// A run that said nothing is still a failure — but not the same one, and never a silent
    /// success. The distinction is what the person reading the round needs.
    /// </summary>
    [Fact]
    public async Task AProcessThatExitedZeroSayingNothing_FailsWithoutClaimingItNeverStarted()
    {
        var attempt = await Launch(new ProcessResult(0, string.Empty, string.Empty, TimedOut: false));

        attempt.Should().BeOfType<ReviewAttempt.Failed>()
            .Which.Outcome.Should().BeOfType<ReviewerOutcome.Unparseable>(
                "it started — it just produced no answer, and saying otherwise sends the reader to the wrong place");
    }

    [Fact]
    public async Task ANonZeroExit_IsStillTheExecutorsOwnVerdict()
    {
        var attempt = await Launch(new ProcessResult(3, string.Empty, "boom", TimedOut: false));

        attempt.Should().BeOfType<ReviewAttempt.Failed>()
            .Which.Outcome.Should().BeOfType<ReviewerOutcome.NonZeroExit>();
    }

    private static async Task<ReviewAttempt> Launch(ProcessResult result)
    {
        var slot = new AccountSlot(
            "claude", "a", Path.GetTempPath(), DateTimeOffset.UtcNow, null, false, string.Empty, 0);
        var job = new JobRecord(
            JobId.New(), "dev@example.com", "claude", "haiku", "PlanCritique", "review this",
            JobStatus.Running, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(10),
            TimeSpan.FromSeconds(60));

        return await new ReviewLauncher(new Fixed(result)).RunAsync(
            new VendorConfig("claude", "claude", ["haiku"], ["a"]),
            slot,
            job,
            new Dictionary<string, string?>(),
            CancellationToken.None);
    }

    private sealed class Fixed(ProcessResult result) : IProcessLauncher
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default) =>
            Task.FromResult(result);
    }
}
