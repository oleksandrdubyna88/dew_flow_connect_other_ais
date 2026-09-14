using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A vendor that reports its failure on STDOUT is still asked why it failed.
/// </summary>
/// <remarks>
/// <para><b>Measured 2026-09-14, and it is not an edge case — it is every codex failure.</b> The
/// operator saw <c>codex/PlanCritique — failed (exit 1 (the CLI said nothing on stderr))</c> over
/// and over, on round after round, with nothing anywhere to say what was wrong. Reproducing the
/// gate's own invocation by hand gave the answer immediately:</para>
/// <code>
/// {"type":"error","message":"Selected model is at capacity. Please try a different model."}
/// {"type":"turn.failed","error":{"message":"Selected model is at capacity. Please try a different model."}}
/// </code>
/// <para>On STDOUT. Because the gate passes <c>--json</c>, and with <c>--json</c> codex writes its
/// whole event stream — including the error — to stdout and leaves stderr EMPTY. So
/// <c>BoundedScheduler.Because</c>, which reads stderr alone, was telling the exact truth ("the CLI
/// said nothing on stderr") and throwing away the only sentence that mattered.</para>
/// <para><b>Why this is worth a mechanism rather than a special case.</b> The capacity error is
/// TRANSIENT: the same model answers normally minutes later, which is what made it look like codex
/// "falling over" at random. A transient failure whose reason is unreadable is the worst kind to
/// diagnose, and this one had been running for at least a day.</para>
/// <para>Where the reason lives is vendor knowledge, exactly as where the ANSWER lives and how the
/// run is BILLED already are — <c>ReadAnswer</c> and <c>ReadUsage</c> sit on the adapter for that
/// reason, and this is the third of the same kind.</para>
/// </remarks>
public sealed class AReasonThatWentToStdoutTests
{
    private static ProcessResult Ran(string stdOut, string stdErr = "", int exitCode = 1) =>
        new(exitCode, stdOut, stdErr, TimedOut: false);

    private static ReviewerInvocation Invocation(IReviewerRuntime adapter) =>
        new("codex", "PlanCritique", new ProcessRequest("codex", [], "."), string.Empty, adapter);

    /// <summary>The stream codex actually produced, trimmed to what matters.</summary>
    private const string CapacityRun = """
        {"type":"thread.started","thread_id":"01a09f06-455d-77e1-985b-9e9bad489c8b"}
        {"type":"turn.started"}
        {"type":"error","message":"Selected model is at capacity. Please try a different model."}
        {"type":"turn.failed","error":{"message":"Selected model is at capacity. Please try a different model."}}
        """;

    [Fact]
    public void TheCapacityErrorCodexPutsOnStdout_ReachesTheSummarySentence()
    {
        var outcome = new ReviewerOutcome.NonZeroExit(1, string.Empty)
        {
            StdOutTail = new CodexRuntime().WhyItFailed(Ran(CapacityRun)) ?? string.Empty,
        };

        ReviewerSummaryFactory.Describe(outcome).Should().Contain("at capacity");
    }

    [Fact]
    public void AVendorThatSaidNothingAnywhere_StillSaysSoRatherThanInventingAReason()
    {
        // The sentence this replaced was not wrong, it was incomplete. When BOTH streams are silent
        // it is the only honest thing to say, and it must survive.
        var outcome = new ReviewerOutcome.NonZeroExit(1, string.Empty);

        ReviewerSummaryFactory.Describe(outcome).Should().Contain("said nothing");
    }

    [Fact]
    public void StderrIsPreferredWhenTheVendorUsedIt()
    {
        // Every other vendor here reports on stderr, and a stdout event stream is noisier than a
        // written error line. The new reading is a FALLBACK, not a replacement.
        var outcome = new ReviewerOutcome.NonZeroExit(1, "error: the door is closed")
        {
            StdOutTail = "something from the event stream",
        };

        ReviewerSummaryFactory.Describe(outcome).Should().Contain("the door is closed");
    }

    [Theory]
    [InlineData("""{"type":"error","message":"Selected model is at capacity."}""", "Selected model is at capacity.")]
    [InlineData("""{"type":"turn.failed","error":{"message":"stream disconnected before completion"}}""", "stream disconnected before completion")]
    public void CodexReportsItsOwnFailureFromTheEventStream(string line, string expected)
    {
        new CodexRuntime().WhyItFailed(Ran(line)).Should().Be(expected);
    }

    [Fact]
    public void ASuccessfulStreamHasNoFailureToReport()
    {
        var stream = """
            {"type":"thread.started","thread_id":"x"}
            {"type":"item.completed","item":{"id":"item_0","type":"agent_message","text":"{}"}}
            {"type":"turn.completed","usage":{"input_tokens":10,"output_tokens":2}}
            """;

        new CodexRuntime().WhyItFailed(Ran(stream, exitCode: 0)).Should().BeNull();
    }

    [Fact]
    public void AStreamThatIsNotJsonAtAll_IsNotAReason()
    {
        // stdout is somebody else's output: a shim's progress chatter, a banner, a half-written
        // line from a killed process. Only a line this vendor's own protocol defines counts.
        new CodexRuntime().WhyItFailed(Ran("Reading prompt from stdin...\nOpenAI Codex v0.153.4"))
            .Should().BeNull();
    }

    [Fact]
    public void ATornLastLine_DoesNotThrow()
    {
        // A process killed mid-write leaves half a JSON object, and this runs on the failure path
        // of a reviewer that has already gone wrong — it must never add an exception to that.
        var reading = () => new CodexRuntime().WhyItFailed(Ran("""{"type":"error","mess"""));

        reading.Should().NotThrow();
    }

    /// <summary>
    /// A vendor that has not been taught this reports nothing, and nothing changes for it.
    /// </summary>
    /// <remarks>
    /// The default is null, so an adapter that says where its answer lives but not where its
    /// reasons do behaves exactly as it did — which is what makes this safe to add to the
    /// interface rather than to every implementation.
    /// </remarks>
    [Fact]
    public void AnAdapterThatKnowsNothingAboutItsOwnFailures_SaysNothing()
    {
        // Through the INTERFACE, because that is where the default lives — an adapter that has
        // not been taught this does not gain a member, it inherits a silence.
        ((IReviewerRuntime)new GeminiRuntime()).WhyItFailed(Ran(CapacityRun)).Should().BeNull();
    }

    // ---------- the caller path, end to end (the finding two vendors raised) ----------

    private sealed class Answers(ProcessResult result) : IProcessLauncher
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default) =>
            Task.FromResult(result);
    }

    /// <summary>
    /// The whole path: a codex process exits 1 with its reason on stdout, and the SENTENCE says it.
    /// </summary>
    /// <remarks>
    /// <para>codex and gemini both raised the same gap on the plan round, and they were right that
    /// the tests above could not see it: each asserts one link of the chain. An adapter that reads
    /// the stream perfectly proves nothing if <c>ReviewerExecutor</c> never calls it, and the whole
    /// defect being fixed is a value that existed and was not carried.</para>
    /// <para>So this drives the real executor with a fake launcher and asserts the finished sentence
    /// — the one that appeared in the operator's panel for a day saying nothing.</para>
    /// </remarks>
    [Fact]
    public async Task TheExecutorAsksTheAdapterWhyItFailed_AndTheSentenceCarriesTheAnswer()
    {
        var executor = new ReviewerExecutor(new Answers(Ran(CapacityRun)));

        var launch = await executor.LaunchAsync(Invocation(new CodexRuntime()), TestContext.Current.CancellationToken);

        launch.Terminal.Should().BeOfType<ReviewerOutcome.NonZeroExit>();
        ReviewerSummaryFactory.Describe(launch.Terminal!).Should().Contain("at capacity");
    }

    /// <summary>
    /// And a vendor that has not been taught this is unchanged — stdout is still not a reason.
    /// </summary>
    /// <remarks>
    /// The same stream, through an adapter that never claimed it. gemini asked for exactly this
    /// guarantee: a reviewer's own output must never become its failure reason.
    /// </remarks>
    [Fact]
    public async Task AnUntaughtVendorsStdout_IsStillNotAReason()
    {
        var executor = new ReviewerExecutor(new Answers(Ran(CapacityRun)));

        var launch = await executor.LaunchAsync(Invocation(new GeminiRuntime()), TestContext.Current.CancellationToken);

        ReviewerSummaryFactory.Describe(launch.Terminal!).Should().Contain("said nothing");
    }

    /// <summary>
    /// A reason survives a record torn off after it — a killed process writes half a line.
    /// </summary>
    /// <remarks>
    /// codex, plan round: an implementation that read only the LAST record, or the tail as one
    /// document, would return nothing here and recreate the reported symptom exactly. Every
    /// complete record is scanned; only the torn one is skipped.
    /// </remarks>
    [Fact]
    public void AnErrorFollowedByATornRecord_IsStillTheReason()
    {
        var stream = """
            {"type":"error","message":"Selected model is at capacity. Please try a different model."}
            {"type":"turn.fail
            """;

        new CodexRuntime().WhyItFailed(Ran(stream)).Should().Be("Selected model is at capacity. Please try a different model.");
    }

    /// <summary>
    /// Noise on stderr does not outrank the reason the vendor put where it puts reasons.
    /// </summary>
    /// <remarks>
    /// <para>gemini, plan round, and it is this change's own defect one branch further in: a node
    /// deprecation warning or a locale notice is not why a reviewer failed, and preferring it over
    /// the capacity error would put the useless sentence back on the page.</para>
    /// <para>The predicate is the one the summary already had — a line that is scaffolding is not a
    /// reason — so this adds a fallback rather than a second opinion about what stderr means.</para>
    /// </remarks>
    [Fact]
    public void StderrThatIsNothingButScaffolding_DoesNotOutrankTheVendorsOwnReason()
    {
        var outcome = new ReviewerOutcome.NonZeroExit(1, "(node:12) [DEP0040] DeprecationWarning: punycode is deprecated")
        {
            StdOutTail = "Selected model is at capacity. Please try a different model.",
        };

        ReviewerSummaryFactory.Describe(outcome).Should().Contain("at capacity");
    }
}
