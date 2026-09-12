using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Every adapter records the model it launched, and the effort it actually applied.
/// </summary>
/// <remarks>
/// <para><b>The defect (issue #129).</b> <c>ReviewerInvocation.Model</c> is a trailing parameter
/// defaulted to the empty string, and only <c>LocalRuntime</c> and <c>RemoteRuntime</c> ever passed
/// it. Codex, Gemini, Claude and Antigravity each put the model on their CLI's command line and
/// then built the invocation without it — so <c>ReviewerState.Model</c> was empty for four vendors
/// of six, and the rounds log named no model for exactly the vendors whose model people ask
/// about.</para>
///
/// <para><b>Why nothing caught it.</b> The test that covers the model builds the invocation BY HAND
/// — <c>Work("codex", …).Invocation with { Model = "gpt-5-codex" }</c> — and its own docstring
/// claimed "the invocation has carried the model since the adapters were written". A test that
/// constructs the value it then asserts can only ever confirm itself. So this file goes through
/// each adapter's own <c>Build</c>, which is the only way to see the difference.</para>
///
/// <para><b>The effort.</b> An adapter records the effort it APPLIED, not one its vendor class might
/// support. No hosted adapter here puts a reasoning flag on its argv, so recording one for them
/// would claim something that did not happen; <c>LocalRuntime</c> does, and records it. Antigravity
/// bakes its effort into the model id (<c>gemini-3.7-flash-high</c>), which is why it records none
/// and why no line can read <c>gemini-3.7-flash-high (effort: high)</c>.</para>
/// </remarks>
public sealed class EveryAdapterRecordsWhatItLaunchedTests
{
    private const string Worktree = "D:/storage/coai-wt-s1-r1";
    private const string Schema = "D:/storage/schema.json";
    private const string OutDir = "D:/storage/out";

    /// <summary>Every hosted adapter, with the settings that name a model and an effort.</summary>
    public static TheoryData<string, IReviewerRuntime, ReviewerSettings> Hosted()
    {
        var data = new TheoryData<string, IReviewerRuntime, ReviewerSettings>();
        data.Add("codex", new CodexRuntime(), new("codex") { Model = "gpt-5.6-sol", ReasoningEffort = "high" });
        data.Add("gemini", new GeminiRuntime(), new("gemini") { Model = "gemini-3.7-pro", ReasoningEffort = "high" });
        data.Add("claude", new ClaudeRuntime(), new("claude") { Model = "claude-opus-5", ReasoningEffort = "high" });
        data.Add("antigravity", new AntigravityRuntime(), new("antigravity") { Model = "gemini-3.7-flash-high", ReasoningEffort = "high" });
        data.Add("deepseek", new DeepseekRuntime(), new("deepseek") { Model = "deepseek-reasoner", ApiKey = "sk-ds", ReasoningEffort = "high" });
        return data;
    }

    [Theory]
    [MemberData(nameof(Hosted))]
    public void AHostedAdapterRecordsTheModelItLaunched(string name, IReviewerRuntime adapter, ReviewerSettings settings)
    {
        var invocation = adapter.Build(RoleCatalog.ArchitectureRole, "review this", Worktree, Schema, OutDir, settings);

        invocation.Model.Should().Be(settings.Model,
            $"{name} passes the model to its CLI, so the record of the run must name it too");
    }

    [Theory]
    [MemberData(nameof(Hosted))]
    public void AHostedAdapterRecordsNoEffort_BecauseItAppliesNone(string name, IReviewerRuntime adapter, ReviewerSettings settings)
    {
        // Table-driven across all four rather than asserted for codex alone: a copy-paste into any
        // one of them would otherwise show an invented effort with the suite still green. Raised on
        // the plan round.
        var invocation = adapter.Build(RoleCatalog.ArchitectureRole, "review this", Worktree, Schema, OutDir, settings);

        invocation.Effort.Should().BeEmpty(
            $"{name} puts no reasoning flag on its command line, so claiming an effort would be a lie");
        invocation.Request.Arguments.Should().NotContain("--reasoning-effort",
            $"and if {name} ever gains the flag, whoever adds it adds the record with it");
    }

    [Fact]
    public void ALocalAdapterRecordsBothTheModelAndTheEffortItApplied()
    {
        var settings = new ReviewerSettings("local") { Model = "qwen3.5:latest", ReasoningEffort = "high" };

        var invocation = new LocalRuntime("local", "http://127.0.0.1:11434/v1").Build(RoleCatalog.ArchitectureRole, "review this", Worktree, Schema, OutDir, settings);

        invocation.Model.Should().Be("qwen3.5:latest");
        invocation.Effort.Should().Be("high", "local is the one runtime that puts --reasoning-effort on the argv");
    }

    [Fact]
    public void ALocalAdapterWithNoEffortConfiguredRecordsNone()
    {
        var settings = new ReviewerSettings("local") { Model = "qwen3.5:latest" };

        var invocation = new LocalRuntime("local", "http://127.0.0.1:11434/v1").Build(RoleCatalog.ArchitectureRole, "review this", Worktree, Schema, OutDir, settings);

        invocation.Effort.Should().BeEmpty("no flag was passed, so there is nothing to record");
    }

    /// <summary>
    /// An empty model is a supported choice — the panel offers "the CLI's default" — so it is
    /// recorded as empty rather than guessed at or refused.
    /// </summary>
    [Fact]
    public void AnAdapterGivenNoModelRecordsNone_RatherThanInventingOne()
    {
        var invocation = new CodexRuntime().Build(RoleCatalog.ArchitectureRole, "review this", Worktree, Schema, OutDir, new("codex"));

        invocation.Model.Should().BeEmpty();
    }
}
