using CoaiMcp.Core.Catalog;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Which runtime and model has a fast tier is DATA in <c>shared/feature-availability.json</c> (research/PLAN_fast_mode.md,
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

    [Theory]
    [InlineData("models", new string[0])]
    [InlineData("every-model", new[] { "opus" })]
    [InlineData("none", new[] { "opus" })]
    public void ARowWhoseModelsDoNotMatchItsSource_IsRefused_AsTheGeneratorRefusesIt(string source, string[] models)
    {
        // CodeRabbit on #693: `models` with no models would read as "no model has the tier", and a list beside any other
        // source would say nothing — the extension's generator refuses both, so the server must too.
        var seed = new FeatureAvailabilitySeed(
            ["codex"], new FeatureListsSeed(["codex"], ["codex"]),
            [new("codex", "none", [], "", "x")],
            [new("codex", "none", "x")],
            [new("codex", source, models, "", "x")]);

        var read = () => FeatureAvailability.FromSeed(seed);

        read.Should().Throw<InvalidOperationException>().WithMessage("*'models' source lists its models*");
    }

    /// <summary>
    /// codex 0.110.0–0.130.0 refuse <c>service_tier=default</c> at config load (todo/PLAN_codex_tier_floor.md, change 2) —
    /// and the range is the file's, beside the measurement, compared as numbers: <c>0.12.0</c> is NOT in it, which a
    /// string comparison ("0.110" &lt; "0.12" &lt; "0.130") would say it is.
    /// </summary>
    [Theory]
    [InlineData("0.110.0", true)]
    [InlineData("0.120.0", true)]
    [InlineData("0.130.0", true)]
    [InlineData("0.107.0", false)]
    [InlineData("0.131.0", false)]
    [InlineData("0.160.0", false)]
    [InlineData("0.12.0", false)]
    public void TheCodexRow_NamesTheReleasesThatRefuseTheStandardTier(string release, bool refuses)
    {
        File.FastModeOf("codex").RefusesStandard.Contains(Version.Parse(release)).Should().Be(refuses);
    }

    [Fact]
    public void NoOtherRow_NamesAReleaseRange()
    {
        File.FastMode.Where(row => row.Runtime != "codex").Should().OnlyContain(row => row.RefusesStandard == ReleaseRange.None);
    }

    [Theory]
    [InlineData("codex", "0.110", "0.130.0", "is not a release")]
    [InlineData("codex", "0.110.0", "latest", "is not a release")]
    [InlineData("codex", "0.130.0", "0.110.0", "ends before it starts")]
    [InlineData("codex", "0.110.0", null, "is not a release")]
    [InlineData("claude", "0.110.0", "0.130.0", "only codex")]
    public void AMalformedRange_RefusesTheSeed_AsTheGeneratorRefusesIt(string runtime, string from, string? through, string said)
    {
        var seed = new FeatureAvailabilitySeed(
            [runtime], new FeatureListsSeed([runtime], [runtime]),
            [new(runtime, "none", [], "", "x")],
            [new(runtime, "none", "x")],
            [new(runtime, "every-model", [], "", "x", new ReleaseRangeSeed(from, through))]);

        var read = () => FeatureAvailability.FromSeed(seed);

        read.Should().Throw<InvalidOperationException>().WithMessage($"*refusesStandard*{said}*");
    }
}
