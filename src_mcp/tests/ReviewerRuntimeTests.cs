using Xunit;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;

namespace CoaiMcp.Tests;

/// <summary>Every safety flag asserted literally — these argvs were verified against the real
/// CLIs (codex 0.147.0, gemini 0.55.1) before being pinned here.</summary>
public sealed class ReviewerRuntimeTests
{
    private const string Worktree = "D:/storage/coai-wt-s1-r1";
    private const string Schema = "D:/storage/schema.json";
    private const string OutDir = "D:/storage/out";

    private static ReviewerInvocation Codex(ReviewerSettings? s = null) =>
        new CodexRuntime().Build(ReviewRole.Architecture, "review this", Worktree, Schema, OutDir, s ?? new("codex"));

    private static ReviewerInvocation Gemini(ReviewerSettings? s = null) =>
        new GeminiRuntime().Build(ReviewRole.SecurityReliability, "review this", Worktree, Schema, OutDir, s ?? new("gemini"));

    private static ReviewerInvocation Deepseek(ReviewerSettings? s = null) =>
        new DeepseekRuntime().Build(ReviewRole.UxDxPerformance, "review this", Worktree, Schema, OutDir, s ?? new("deepseek") { ApiKey = "sk-ds" });

    [Fact]
    public void CodexArgv_IsReadOnlyEphemeralAndSchemaBound()
    {
        var args = Codex().Request.Arguments;

        args.Should().ContainInOrder("exec", "-s", "read-only");
        args.Should().Contain("--ephemeral").And.Contain("--skip-git-repo-check");
        args.Should().ContainInOrder("-C", Worktree);
        args.Should().ContainInOrder("--output-schema", Schema);
        args.Should().Contain("-o");
        args[^1].Should().Be("review this", "the prompt rides last, after every flag");
        Codex().OutputFile.Should().StartWith(OutDir, "the answer is read from -o, not stdout");
    }

    [Fact]
    public void GeminiArgv_IsHeadlessJsonAndPlanMode()
    {
        var invocation = Gemini();

        invocation.Request.Arguments.Should().ContainInOrder("-p", "review this");
        invocation.Request.Arguments.Should().ContainInOrder("-o", "json");
        invocation.Request.Arguments.Should().ContainInOrder("--approval-mode", "plan");
        // A round's worktree is always a fresh directory and so never a trusted folder; without
        // this Gemini exits 55 headless AND overrides plan mode away — every reviewer of the
        // first real run died on it.
        invocation.Request.Arguments.Should().Contain("--skip-trust");
        invocation.OutputFile.Should().BeEmpty("gemini answers on stdout, inside its envelope");
    }

    [Fact]
    public void DeepseekArgv_OverridesProviderAndBaseUrl_OnTheCodexRuntime()
    {
        var invocation = Deepseek();

        invocation.Request.Arguments.Should().Contain("model_provider=deepseek");
        invocation.Request.Arguments.Should().Contain("model_providers.deepseek.base_url=https://api.deepseek.com/v1");
        invocation.Request.Arguments.Should().Contain("model_providers.deepseek.env_key=DEEPSEEK_API_KEY");
        invocation.Request.Arguments.Should().Contain("--ephemeral", "it inherits every codex safety flag");
        invocation.Request.Environment.Should().ContainKey("DEEPSEEK_API_KEY");
    }

    [Fact]
    public void Key_LandsInChildEnv_NeverInArgv()
    {
        foreach (var invocation in (ReviewerInvocation[])
                 [
                     Codex(new("codex") { ApiKey = "sk-secret-1" }),
                     Gemini(new("gemini") { ApiKey = "sk-secret-2" }),
                     Deepseek(new("deepseek") { ApiKey = "sk-secret-3" }),
                 ])
        {
            string.Join(' ', invocation.Request.Arguments).Should().NotContain("sk-secret");
            invocation.Request.Environment.Values.Should().Contain(v => v!.StartsWith("sk-secret"));
        }
    }

    [Fact]
    public void NoKey_MeansNoKeyVariable_TheCliAuthenticatesItself()
    {
        Codex().Request.Environment.Should().BeEmpty();
        Gemini().Request.Environment.Should().BeEmpty();
    }

    [Fact]
    public void WorkingDirectory_IsTheWorktree_NeverTheLiveCheckout()
    {
        foreach (var invocation in (ReviewerInvocation[])[Codex(), Gemini(), Deepseek()])
        {
            invocation.Request.WorkingDirectory.Should().Be(Worktree);
        }
    }

    [Fact]
    public void ModelFlag_AppearsOnlyWhenConfigured()
    {
        Codex(new("codex") { Model = "gpt-5.3-codex" }).Request.Arguments.Should().ContainInOrder("-m", "gpt-5.3-codex");
        Codex().Request.Arguments.Should().NotContain("-m");
    }

    [Fact]
    public void UnknownProvider_Refuses_NamingTheCatalog()
    {
        var selector = ReviewerRuntimeSelector.Default;

        selector.Find("mistral").Should().BeNull();
        selector.RefusalFor("mistral").Should().Contain("'mistral'")
            .And.Contain("codex").And.Contain("deepseek").And.Contain("gemini");
    }
}
