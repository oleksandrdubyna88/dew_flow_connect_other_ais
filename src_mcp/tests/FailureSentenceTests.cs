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

    // ------------------------------------------------------------------------------------------
    // The LOCAL shim's half of the same defect (2026-09-08).
    //
    // Measured on a real gate at 17:15 UTC, branch feat/pin-drawn-by-default:
    //
    //   local/Conventions FAILED after 290.0s: exit 69: l Qwen3.5-35B-A3B-Q5_vk128:latest, pid 52068)
    //
    // The sentence begins mid-word. Nothing crashed: the reviewer waited its whole five-minute
    // deadline for the local engine and never got the card, which is exit 69 (EX_UNAVAILABLE), and
    // LocalAsk.QueuedOutMessage says what to do about it. That cure was written, printed, and lost
    // to forty-five characters of an unrelated line. Three links: the shim writes progress to the
    // same stderr as its verdict, the executor keeps the last 400 characters so the cut lands
    // mid-line, and the picker takes that fragment because it is neither progress it recognises nor
    // a runtime frame.
    // ------------------------------------------------------------------------------------------

    private const string Endpoint = "http://127.0.0.1:11434/v1";

    private const string Model = "Qwen3.5-35B-A3B-Q5_vk128:latest";

    /// <summary>What the local shim writes when the card was busy for the whole deadline.</summary>
    private static string LocalShimStdErr() =>
        "[coai-mcp] " + LocalAsk.WaitingMessage(Endpoint, 2, TimeSpan.FromSeconds(60), Model, 52068) + "\n"
        + "[coai-mcp] " + LocalAsk.WaitingMessage(Endpoint, 1, TimeSpan.FromSeconds(180), Model, 52068) + "\n"
        + "[coai-mcp] " + LocalAsk.QueuedOutMessage(Endpoint, TimeSpan.FromSeconds(290));

    /// <summary>The reported sentence carries the cure, not the note about waiting for it.</summary>
    [Fact]
    public void AQueuedOutLocalReviewer_ReportsWhatToDoAboutIt()
    {
        var sentence = ReviewerSummaryFactory.Describe(new ReviewerOutcome.NonZeroExit(69, LocalShimStdErr()));

        sentence.Should().Contain("was busy for the whole").And.NotContain("pid ");
    }

    /// <summary>
    /// The named-set exclusion, beside the remote one that already exists.
    /// </summary>
    /// <remarks>
    /// The recogniser and the sentence live together in <see cref="LocalAsk"/> for the reason the
    /// remote pair do: <c>Program.cs</c> composed this note inline, so nothing tied the words to
    /// whatever was supposed to recognise them, and nothing did.
    /// </remarks>
    [Fact]
    public void AProgressNoteIsNeverTheReason_ForTheLocalShimToo()
    {
        LocalAsk.IsProgress(LocalAsk.WaitingMessage(Endpoint, 3, TimeSpan.FromSeconds(9), "m", 1))
            .Should().BeTrue("the sentence is built where it is recognised");

        var sentence = ReviewerSummaryFactory.Describe(new ReviewerOutcome.NonZeroExit(69, LocalShimStdErr()));

        sentence.Should().NotContain("ahead,").And.NotContain("so far");
    }

    /// <summary>A reason never begins in the middle of a line.</summary>
    /// <remarks>
    /// The tail is cut by CHARACTER COUNT, so its first line is half of something whenever the
    /// stderr was longer than the budget. Raised on the plan round by all three reviewers, from
    /// three directions.
    /// </remarks>
    [Fact]
    public void AReasonIsNeverHalfALine()
    {
        // Deliberately ragged. Forty lines of one width put the 400-character cut exactly on a
        // newline by arithmetic accident, and the first draft of this test passed against the
        // unfixed code because of it.
        var written = string.Join(
            '\n',
            Enumerable.Range(0, 40).Select(i => "[coai-mcp] line " + i + " " + new string('x', 20 + (i % 7))));

        var tail = ReviewerExecutor.TailOf(written);

        tail.Should().StartWith("[coai-mcp] line ", "a kept tail begins where a line begins");
        written.Should().Contain(tail, "and it is a suffix of what was written, not a rewrite of it");
    }

    /// <summary>The guard on the other side: a short stderr keeps every line it has.</summary>
    [Fact]
    public void AShortStderrKeepsItsFirstLine()
    {
        const string wholeThing = "[coai-mcp] the only thing it said\nError: and the reason underneath";

        ReviewerExecutor.TailOf(wholeThing).Should().Be(wholeThing);
    }

    /// <summary>
    /// A cut that lands exactly on a newline loses nothing.
    /// </summary>
    /// <remarks>
    /// Dropping the first line whenever the stderr was longer than the budget would discard a whole,
    /// valid diagnostic here — a flag saying only "it was truncated" cannot tell this from a cut
    /// mid-word. (codex, the plan round.)
    /// </remarks>
    [Fact]
    public void ATailCutExactlyAtALineBoundaryKeepsEveryWholeLine()
    {
        const string above = "[coai-mcp] a line the tail has no room for";
        const string kept = "Error: the whole first line of the tail";

        // Sized so the 400-character cut lands EXACTLY on the newline before `kept` — which is the
        // case this test exists for, and which a first draft got wrong by guessing at the padding.
        var stdErr = above + "\n" + kept + "\n" + new string('x', 400 - kept.Length - 1);
        var tail = ReviewerExecutor.TailOf(stdErr);

        tail.Should().StartWith(kept, "a cut that lands on a boundary loses nothing")
            .And.NotContain("no room for");
    }

    /// <summary>
    /// One diagnostic longer than the whole budget still yields a reason.
    /// </summary>
    /// <remarks>
    /// A tail with no newline in it cannot be trimmed to a line boundary, and dropping "the first
    /// line" would leave nothing at all — a real failure reported as blank. Its OPENING is kept,
    /// which is also the half <c>Because</c> shows. (codex and gemini, the plan round.)
    /// </remarks>
    [Fact]
    public void AStderrOfOneOverlongLineStillYieldsAReason()
    {
        var oneLine = "Error: " + new string('y', 900);

        var tail = ReviewerExecutor.TailOf(oneLine);

        tail.Should().StartWith("Error: yyy").And.NotBeEmpty();
        ReviewerSummaryFactory.Describe(new ReviewerOutcome.NonZeroExit(1, tail)).Should().Contain("Error: yyy");
    }

    /// <summary>
    /// When the tail is nothing but progress notes, the sentence says that rather than showing one.
    /// </summary>
    /// <remarks>
    /// A reviewer killed while it was still waiting has no verdict underneath, and the picker's last
    /// fallback was the LAST line — a progress note again, which is the whole defect wearing the
    /// other shoe. (codex, the plan round.)
    /// </remarks>
    [Fact]
    public void WhenEveryLineIsAProgressNote_TheSentenceSaysSo()
    {
        var onlyWaiting = "[coai-mcp] " + LocalAsk.WaitingMessage(Endpoint, 2, TimeSpan.FromSeconds(60), "m", 7) + "\n"
            + "[coai-mcp] " + LocalAsk.WaitingMessage(Endpoint, 1, TimeSpan.FromSeconds(120), "m", 7);

        var sentence = ReviewerSummaryFactory.Describe(new ReviewerOutcome.NonZeroExit(69, onlyWaiting));

        sentence.Should().Contain("still waiting").And.NotContain("pid ");
    }
}
