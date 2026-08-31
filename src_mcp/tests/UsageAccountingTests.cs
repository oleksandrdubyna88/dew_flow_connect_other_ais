using CoaiMcp.Core.Findings;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What a round says it consumed, pinned to what the vendors ACTUALLY printed.
/// </summary>
/// <remarks>
/// Every envelope below was captured from a real CLI call on 2026-08-31, not written from
/// documentation. Cache accounting is the reason this file exists: codex folds cached tokens
/// INTO its input count and claude reports them BESIDE it, so one shared rule would be wrong for
/// one of them by a factor of two — silently, in the direction nobody checks.
/// </remarks>
public sealed class UsageAccountingTests
{
    private static ProcessResult Out(string stdout) => new(0, stdout, string.Empty, false);

    private static ReviewerInvocation Invocation(IReviewerRuntime runtime, string outputFile = "") =>
        new("v", ReviewRole.PlanCritique, new ProcessRequest("x", [], "."), outputFile, runtime);

    // Captured: `codex exec --json` after a multi-turn run (list files, read one, answer).
    private const string CodexStream = """
        {"type":"thread.started","thread_id":"01a0594e"}
        {"type":"turn.started"}
        {"type":"item.completed","item":{"id":"item_0","type":"agent_message","text":"{\"findings\":[]}"}}
        {"type":"turn.completed","usage":{"input_tokens":29138,"cached_input_tokens":14080,"cache_write_input_tokens":0,"output_tokens":261,"reasoning_output_tokens":62}}
        """;

    // Captured: `claude -p --output-format json`. Note the two disagreeing reports of one run.
    private const string ClaudeEnvelope = """
        {"type":"result","result":"pong","total_cost_usd":0.048924999999999996,
         "usage":{"input_tokens":10,"cache_creation_input_tokens":24054,"cache_read_input_tokens":0,"output_tokens":44},
         "modelUsage":{"claude-haiku-4-5-20251001":{"inputTokens":532,"outputTokens":57,"cacheReadInputTokens":0,"cacheCreationInputTokens":24054,"costUSD":0.048924999999999996,"maxOutputTokens":32000}}}
        """;

    [Fact]
    public void Codex_CountsCachedInputOnce_BecauseItIsAlreadyInsideTheInputTotal()
    {
        var usage = ((IReviewerRuntime)new CodexRuntime()).ReadUsage(Invocation(new CodexRuntime()), Out(CodexStream));

        usage.TokensIn.Should().Be(29138, "cached_input_tokens is a SUBSET of input_tokens, not an addition");
        usage.TokensOut.Should().Be(261, "reasoning_output_tokens is already inside output_tokens");
        usage.CostUsd.Should().BeNull("codex prices nothing in its own output, and inventing a price table would be worse");
    }

    [Fact]
    public void Claude_CountsTheWholeSession_NotJustItsLastMessage()
    {
        var usage = ((IReviewerRuntime)new ClaudeRuntime()).ReadUsage(Invocation(new ClaudeRuntime()), Out(ClaudeEnvelope));

        // 532 fresh + 24054 cache-creation + 0 cache-read. The `usage` block would have said 10.
        usage.TokensIn.Should().Be(24586);
        usage.TokensOut.Should().Be(57, "`usage` says 44 — that is the last message, not the run");
        usage.CostUsd.Should().BeApproximately(0.048925, 1e-9, "claude prices its own run, so we never guess");
    }

    [Fact]
    public void ARoundsTotal_IsTheSumOfItsReviewers_AndKeepsCostOnlyWhereItWasReported()
    {
        var codex = ((IReviewerRuntime)new CodexRuntime()).ReadUsage(Invocation(new CodexRuntime()), Out(CodexStream));
        var claude = ((IReviewerRuntime)new ClaudeRuntime()).ReadUsage(Invocation(new ClaudeRuntime()), Out(ClaudeEnvelope));

        var round = Usage.None.Add(codex).Add(claude);

        round.TokensIn.Should().Be(29138 + 24586);
        round.TokensOut.Should().Be(261 + 57);
        round.CostUsd.Should().BeApproximately(0.048925, 1e-9,
            "a vendor that reports no price contributes tokens without inventing money");
    }

    [Fact]
    public void AVendorThatSaidNothing_ReportsZero_NeverAGuess()
    {
        var usage = ((IReviewerRuntime)new CodexRuntime()).ReadUsage(Invocation(new CodexRuntime()), Out("no json here at all"));

        usage.Should().Be(Usage.None);
        usage.CostUsd.Should().BeNull("zero and unknown must not read the same as free");
    }

    [Fact]
    public void ARepairedReviewer_IsBilledForBothLaunches()
    {
        // Two launches happened, so two launches are counted; reporting only the successful one
        // would under-report every reviewer that needed a repair.
        var first = new Usage(1000, 50, 0.01);
        var second = new Usage(1200, 60, 0.02);

        first.Add(second).Should().Be(new Usage(2200, 110, 0.03));
    }
}
