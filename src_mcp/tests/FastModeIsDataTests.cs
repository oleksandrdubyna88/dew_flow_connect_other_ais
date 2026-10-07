using CoaiMcp.Core.Catalog;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Which runtime and model has a fast tier is DATA in <c>shared/feature-availability.json</c> (todo/PLAN_fast_mode.md,
/// decision 2) — one row per runtime, read by this server and by the extension's generator alike, so the card never
/// offers a state the launch does not send.
/// </summary>
public sealed class FastModeIsDataTests
{
    private static readonly FeatureAvailability File = FeatureAvailability.Builtin;

    [Theory]
    [InlineData("codex", "gpt-6.1-sol", true)]
    [InlineData("codex", "", true)]
    [InlineData("claude", "opus", true)]
    [InlineData("claude", "claude-opus-5-5[1m]", true)]
    [InlineData("claude", "claude-sonnet-5", false)]
    [InlineData("claude", "", false)]
    [InlineData("antigravity", "gemini-3.7-flash-high", false)]
    [InlineData("local", "qwen", false)]
    [InlineData("api", "grok-4", false)]
    public void TheFileSaysWhichModelHasTheTier(string runtime, string model, bool has)
    {
        File.HasFastTier(runtime, model).Should().Be(has);
    }

    [Fact]
    public void EveryRuntimeHasExactlyOneRow_SayingWhy()
    {
        File.FastMode.Select(row => row.Runtime).Should().OnlyHaveUniqueItems();
        File.FastMode.Should().OnlyContain(row => row.Note.Length > 0);
    }

    [Fact]
    public void ASeedMissingARuntimesRow_IsRefusedWhole()
    {
        var seed = new FeatureAvailabilitySeed(
            ["codex", "claude"], new FeatureListsSeed(["codex"], ["claude"]),
            [new("codex", "none", [], "", "x"), new("claude", "none", [], "", "x")],
            [new("codex", "none", "x"), new("claude", "none", "x")],
            [new("codex", "every-model", [], "codex-cli 0.160.0", "x")]);

        var read = () => FeatureAvailability.FromSeed(seed);

        read.Should().Throw<InvalidOperationException>().WithMessage("*'claude' has 0 fast-mode rows*");
    }
}
