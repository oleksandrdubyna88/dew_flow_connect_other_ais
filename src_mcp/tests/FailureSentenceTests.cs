using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The one sentence a person reads when a reviewer fails.
/// </summary>
/// <remarks>
/// <para>It is picked out of the CLI's stderr by CONTENT rather than position, because a vendor's
/// last line is usually its version banner. The rule works on vendor CLIs; it did not work on OUR
/// own shim, whose stderr carries PROGRESS notes as well as the reason — and a progress note is
/// the first line, so it won.</para>
/// <para>Measured on a real gate, 2026-09-07: a Team-server reviewer failed and the round said
/// <c>exit 70: [coai-mcp] claude: running on the Team server at https://coai.remsoft.dev</c> — the
/// reviewer reported as RUNNING in the sentence announcing that it had stopped. The line that said
/// what actually happened was sitting directly underneath, discarded, because the vocabulary of
/// "this line announces an error" had no word for a thing that failed.</para>
/// </remarks>
public sealed class FailureSentenceTests
{
    /// <summary>Exactly what the remote shim writes: progress first, verdict last.</summary>
    private const string ShimStdErr = """
        [coai-mcp] claude: queued on the Team server at https://coai.remsoft.dev, 2 ahead of it
        [coai-mcp] claude: running on the Team server at https://coai.remsoft.dev
        [coai-mcp] the Team server's claude reviewer failed (NotStarted): the executor returned no verdict
        """;

    [Fact]
    public void TheReasonAReviewerFailed_BeatsTheNoteSayingItWasStillRunning()
    {
        var sentence = ReviewerSummaryFactory.Describe(new ReviewerOutcome.NonZeroExit(70, ShimStdErr));

        sentence.Should().Contain("reviewer failed").And.NotContain("running on the Team server");
    }

    /// <summary>
    /// The queue note must not win either — it is the oldest line, and "ahead of it" reads like a
    /// diagnosis to somebody who never saw the other two.
    /// </summary>
    [Fact]
    public void TheReasonAReviewerFailed_BeatsTheNoteSayingItWasStillQueued()
    {
        var sentence = ReviewerSummaryFactory.Describe(new ReviewerOutcome.NonZeroExit(70, ShimStdErr));

        sentence.Should().NotContain("ahead of it");
    }

    /// <summary>
    /// A vendor's TALLY is not the reason anything failed.
    /// </summary>
    /// <remarks>
    /// The first attempt at this fix taught the picker the word "failed", and three reviewers broke
    /// it independently on the code round: <c>0 failed, 3 passed</c> and <c>3 tests failed</c> are
    /// tallies that announce nothing, while <c>Step 3 failed</c> and <c>Job 12 failed</c> are real
    /// verdicts with a digit in front — no rule over that one word can have it both ways. Excluding
    /// our OWN progress notes instead leaves the vocabulary untouched, so a tally is ordinary text
    /// again and the line that genuinely announces an error wins on its own merits.
    /// </remarks>
    [Fact]
    public void ACountOfFailures_IsNotTheReasonAnythingFailed()
    {
        const string tally = """
            Test run: 0 failed, 3 passed
            Error: the config file could not be read
            """;

        var sentence = ReviewerSummaryFactory.Describe(new ReviewerOutcome.NonZeroExit(1, tally));

        sentence.Should().Contain("config file").And.NotContain("3 passed");
    }

    /// <summary>
    /// The behaviour the picker was written for, still intact: a node CLI whose real error sits
    /// above its own version banner never reports the banner. Here the diagnosis goes one better
    /// and answers with the CURE, which is what <c>VendorDiagnosis</c> is for — the point of the
    /// guard is that the version line does not win, whichever of the two speaks.
    /// </summary>
    [Fact]
    public void AVendorsOwnErrorStillBeatsItsVersionBanner()
    {
        const string node = """
            file:///usr/lib/node_modules/x/cli.js:14
            throw new Error('boom');
            Error: Missing optional dependency @openai/codex-linux-x64
                at Module._compile (node:internal/modules/cjs/loader:1234:14)
            Node.js v20.20.2
            """;

        var sentence = ReviewerSummaryFactory.Describe(new ReviewerOutcome.NonZeroExit(1, node));

        sentence.Should().Contain("reinstall it").And.NotContain("Node.js v");
    }
}
