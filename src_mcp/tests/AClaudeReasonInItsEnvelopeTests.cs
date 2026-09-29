using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A Claude CLI reviewer that fails says WHY — the sentence is in its JSON envelope on stdout, not on stderr.
/// </summary>
/// <remarks>
/// <para><b>Measured 2026-09-29.</b> The benchmark's S7.3 campaigns lost 44 Fable 5.1 reviewer cells to
/// <c>exit 1 (the CLI said nothing on stderr)</c>, each in about three seconds. True, and useless: the adapter runs the
/// CLI with <c>--output-format json</c>, and the CLI writes its failure into that envelope — the same defect codex had
/// on 2026-09-14 (<see cref="AReasonThatWentToStdoutTests"/>), one vendor over. The envelopes below are the CLI's own,
/// captured on this machine: the monthly spend limit (2.1.284, HTTP 429) and an old CLI refusing a newer model (2.1.258,
/// HTTP 400). Both say <c>"subtype":"success"</c> beside <c>"is_error":true</c> — so <c>is_error</c> decides, never
/// <c>subtype</c>.</para>
/// </remarks>
public sealed class AClaudeReasonInItsEnvelopeTests
{
    private const string SpendLimit = """
        {"type":"result","subtype":"success","is_error":true,"api_error_status":429,"duration_ms":518,"duration_api_ms":824,"num_turns":1,"result":"You've hit your monthly spend limit. Switch to another model to continue.","stop_reason":"stop_sequence","session_id":"00000000-0000-0000-0000-000000000000","total_cost_usd":0.001282,"terminal_reason":"api_error","permission_denials":[],"uuid":"00000000-0000-0000-0000-000000000000"}
        """;

    private const string OldCliNewModel = """
        {"type":"result","subtype":"success","is_error":true,"api_error_status":400,"num_turns":1,"result":"API Error: 400 Claude Code 2.1.258 does not support this model; version 2.1.280 or newer is required. Run 'claude update', or update the Claude desktop app, then try again.","terminal_reason":"api_error","error":null}
        """;

    private static ProcessResult Ran(string stdOut, string stdErr = "", int exitCode = 1) =>
        new(exitCode, stdOut, stdErr, TimedOut: false);

    private static ReviewerInvocation Invocation() =>
        new("claude", "PlanCritique", new ProcessRequest("claude", [], "."), string.Empty, new ClaudeRuntime());

    /// <summary>Through the INTERFACE, as <c>ReviewerExecutor</c> calls it — so an adapter that never overrides the empty
    /// default is measured exactly as the executor meets it.</summary>
    private static string Why(string stdOut, int exitCode = 1) =>
        ((IReviewerRuntime)new ClaudeRuntime()).WhyItFailed(Invocation(), Ran(stdOut, exitCode: exitCode));

    [Fact]
    public void TheSpendLimitTheCliPutInItsEnvelope_IsTheReason()
    {
        Why(SpendLimit).Should().Be("You've hit your monthly spend limit. Switch to another model to continue. (HTTP 429)");
    }

    [Fact]
    public void AnOldCliRefusingANewerModel_IsTheReason()
    {
        Why(OldCliNewModel).Should().StartWith("API Error: 400 Claude Code 2.1.258 does not support this model").And.EndWith("(HTTP 400)");
    }

    /// <summary>The whole chain: the real executor, a process that exits 1 with the envelope and an empty stderr, and the
    /// finished sentence — the one that read "said nothing" 44 times.</summary>
    [Fact]
    public async Task TheExecutorAsksClaudeWhyItFailed_AndTheSentenceCarriesTheAnswer()
    {
        var executor = new ReviewerExecutor(new Answers(Ran(SpendLimit)));

        var launch = await executor.LaunchAsync(Invocation(), TestContext.Current.CancellationToken);

        launch.Terminal.Should().BeOfType<ReviewerOutcome.NonZeroExit>();
        ReviewerSummaryFactory.Describe(launch.Terminal!).Should().Contain("monthly spend limit").And.Contain("HTTP 429");
    }

    /// <summary>A review's own text is never its failure: a successful envelope, whatever the exit code, is no reason.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ASuccessfulEnvelope_IsNeverAReason(int exitCode)
    {
        Why("""{"type":"result","subtype":"success","is_error":false,"result":"{\"findings\":[{\"title\":\"a spend limit is not handled\"}]}"}""", exitCode)
            .Should().BeEmpty();
    }

    /// <summary>The line in the round summary is ONE line; a reason spanning several is collapsed (plan round, finding 1).</summary>
    [Fact]
    public void AMultiLineReason_IsCollapsedToOneLine()
    {
        Why("""{"type":"result","is_error":true,"result":"Authentication failed.\n\n  Run  claude login\tand retry."}""")
            .Should().Be("Authentication failed. Run claude login and retry.");
    }

    [Fact]
    public void AStatusThatIsNotAnInteger_LeavesTheReasonWithoutTheHttpSuffix()
    {
        Why("""{"type":"result","is_error":true,"api_error_status":"429","result":"You've hit your monthly spend limit."}""")
            .Should().Be("You've hit your monthly spend limit.");
    }

    /// <summary>Every shape that is not the measured failure envelope is not a reason — and never an exception, because
    /// this runs on the path of a reviewer that has ALREADY failed (the codex adapter's lesson: <c>GetString</c> and
    /// <c>TryGetProperty</c> throw <c>InvalidOperationException</c>, not <c>JsonException</c>, on the wrong kind).</summary>
    [Theory]
    [InlineData("""{"type":"result","is_error":true}""")]
    [InlineData("""{"type":"result","is_error":true,"result":42}""")]
    [InlineData("""{"type":"result","is_error":true,"result":""}""")]
    [InlineData("""{"type":"result","is_error":true,"result":"   "}""")]
    [InlineData("""{"type":"result","is_error":"true","result":"a string that says true is not a boolean"}""")]
    [InlineData("""{"type":"result","result":"no is_error at all"}""")]
    [InlineData("""["is_error",true]""")]
    [InlineData("""{"type":"result","is_error":true,"resu""")]
    [InlineData("Error: something went wrong before any JSON")]
    [InlineData("")]
    public void AnythingButTheFailureEnvelope_IsNotAReasonAndDoesNotThrow(string stdOut)
    {
        var reading = () => Why(stdOut);

        reading.Should().NotThrow();
        reading().Should().BeEmpty();
    }

    /// <summary>A stderr line that ANNOUNCES a failure keeps precedence: the envelope is the fallback, as codex's stream is.</summary>
    [Fact]
    public void StderrThatAnnouncesAFailure_StillWins()
    {
        var outcome = new ReviewerOutcome.NonZeroExit(1, "Error: authentication failed — run claude login")
        {
            FailureReason = Why(SpendLimit),
        };

        ReviewerSummaryFactory.Describe(outcome).Should().Contain("authentication failed");
    }

    /// <summary>The old CLI's measured stderr is a diagnostic TAG, not a sentence — it announces nothing, so the envelope's
    /// own sentence is what the round line says (the summary's existing rule, from the codex work).</summary>
    [Fact]
    public void TheOldClisDiagnosticTagOnStderr_GivesWayToTheEnvelopesSentence()
    {
        var outcome = new ReviewerOutcome.NonZeroExit(1, "[claude-code:unrecognized_model] {\"model\":\"claude-opus-5-5\",\"query_source\":\"sdk\"}")
        {
            FailureReason = Why(OldCliNewModel),
        };

        ReviewerSummaryFactory.Describe(outcome).Should().Contain("does not support this model").And.Contain("2.1.280 or newer");
    }

    private sealed class Answers(ProcessResult result) : IProcessLauncher
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default) => Task.FromResult(result);
    }
}
