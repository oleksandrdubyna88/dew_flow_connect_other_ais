using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What ONE launch produced, before anyone reads it as findings.
/// </summary>
/// <remarks>
/// <para>The seam exists because a second binary needs the first half of a reviewer run and not the
/// second: the planned Team server launches the same vendor CLIs through the same adapters and hands
/// the vendor's RAW answer back over HTTP, while parsing, the repair launch and de-duplication stay
/// on the client that asked for the review.</para>
/// <para>So <c>Terminal == null</c> means exactly one thing — the process ran and exited zero. It is
/// NOT a promise that there is an answer to read: an adapter whose output file never appeared says
/// so with a null <c>Answer</c>, and deciding what that means is the caller's job, which is the
/// whole point of the split.</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class ReviewerLaunchTests
{
    private readonly ReviewerExecutor _executor = new(new ProcessLauncher());
    private readonly string _dir = Directory.CreateTempSubdirectory("coai-launch-").FullName;

    [Fact]
    public async Task AnAnswerComesBackRaw_NotParsed()
    {
        var launch = await _executor.LaunchAsync(
            FakeCliInvocations.Invoke("gemini", ["emit", FakeCliInvocations.CleanReview]),
            TestContext.Current.CancellationToken);

        launch.Terminal.Should().BeNull("the process ran and exited zero");
        // Trimmed, and only for the line ending the capture adds: the launcher joins the child's
        // stdout lines, so a one-line answer arrives with a newline the vendor did not write.
        launch.Answer!.Trim().Should().Be(FakeCliInvocations.CleanReview,
            "the launch hands over what the vendor said, not what it means");
    }

    /// <summary>
    /// The distinction the whole seam is for: nonsense is still an ANSWER at this level.
    /// </summary>
    /// <remarks>
    /// "This is not findings" is a judgement the caller makes — and the Team server must not make it,
    /// because it does not hold the schema and its client already does this work for local reviewers.
    /// </remarks>
    [Fact]
    public async Task GarbageIsAnAnswerToo_AndTheLaunchDoesNotJudgeIt()
    {
        var launch = await _executor.LaunchAsync(
            FakeCliInvocations.Invoke("gemini", ["emit", "this is not JSON at all"]),
            TestContext.Current.CancellationToken);

        launch.Terminal.Should().BeNull("the process was fine; its answer is somebody else's problem");
        launch.Answer!.Trim().Should().Be("this is not JSON at all");
    }

    [Fact]
    public async Task ANonZeroExit_CarriesTheCodeAndWhatWasSaid()
    {
        var launch = await _executor.LaunchAsync(
            FakeCliInvocations.Invoke("codex", ["stderr-exit", "boom", "3"]),
            TestContext.Current.CancellationToken);

        launch.Terminal.Should().BeOfType<ReviewerOutcome.NonZeroExit>()
            .Which.Should().Match<ReviewerOutcome.NonZeroExit>(e => e.ExitCode == 3 && e.StdErrTail.Contains("boom"));
    }

    [Fact]
    public async Task ARateLimit_IsItsOwnOutcome_WithTheVendorsOwnLine()
    {
        var launch = await _executor.LaunchAsync(
            FakeCliInvocations.Invoke("codex", ["stderr-exit", "429 Too Many Requests", "1"]),
            TestContext.Current.CancellationToken);

        launch.Terminal.Should().BeOfType<ReviewerOutcome.RateLimited>()
            .Which.Reason.Should().Contain("429");
    }

    [Fact]
    public async Task AnExecutableThatIsNotThere_IsNotStarted_AndNamesIt()
    {
        var missing = new ReviewerInvocation(
            "codex",
            ReviewRole.Architecture,
            new ProcessRequest(Path.Combine(_dir, "no-such-cli.exe"), ["--version"], _dir));

        var launch = await _executor.LaunchAsync(missing, TestContext.Current.CancellationToken);

        launch.Terminal.Should().BeOfType<ReviewerOutcome.NotStarted>()
            .Which.Reason.Should().Contain("no-such-cli");
    }

    /// <summary>
    /// An envelope that came back empty leaves the diagnosis on the streams, and that is what the
    /// evidence must carry — a kept file of zero bytes is what the alternative looked like.
    /// </summary>
    [Fact]
    public async Task AnAnswerFileThatNeverAppeared_LeavesTheTranscriptAsEvidence()
    {
        var launch = await _executor.LaunchAsync(
            FakeCliInvocations.Invoke(
                "codex",
                ["stderr-emit", "the vendor complained here", ""],
                outputFile: Path.Combine(_dir, "answer-that-was-never-written.json")),
            TestContext.Current.CancellationToken);

        launch.Terminal.Should().BeNull("exit zero is exit zero, whatever the file says");
        launch.Answer.Should().BeNull("there is nothing where this vendor puts its answer");
        launch.Evidence.Should().Contain("the vendor complained here",
            "the streams are the only diagnosis left");
    }

    /// <summary>
    /// The other half of the seam: nothing, and nonsense, are both "not findings".
    /// </summary>
    /// <remarks>
    /// Pure, and asked for on this change's code round: the evidence rule treats an empty answer
    /// like a missing one, and the PARSE has to agree — an empty string is not a review, and it must
    /// not reach <c>ReviewParser</c> as though it might be.
    /// </remarks>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("prose, not findings")]
    public void ParseAnswer_AnswersNothing_ForAnythingThatIsNotAReview(string? raw)
    {
        ReviewerExecutor.ParseAnswer(raw, "codex").Should().BeNull();
    }

    [Fact]
    public void ParseAnswer_ReadsARealReview_AndStampsWhoSaidIt()
    {
        var review = ReviewerExecutor.ParseAnswer(FakeCliInvocations.CleanReview, "codex");

        review.Should().NotBeNull();
    }

    /// <summary>
    /// An EMPTY answer takes the same path as a missing one — an adapter that returns "" rather
    /// than null must not cost the transcript. Raised on this story's plan round.
    /// </summary>
    [Fact]
    public async Task AnEmptyAnswer_KeepsTheTranscriptToo()
    {
        var launch = await _executor.LaunchAsync(
            FakeCliInvocations.Invoke("gemini", ["stderr-emit", "nothing to say, and here is why", ""]),
            TestContext.Current.CancellationToken);

        launch.Answer.Should().BeEmpty();
        launch.Evidence.Should().Contain("nothing to say, and here is why");
    }
}
