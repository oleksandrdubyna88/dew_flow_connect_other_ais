using System.Text.Json;
using CoaiMcp.Core.Catalog;
using CoaiMcp.Runners.Consultation;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// coai-mcp reads <c>shared/feature-availability.json</c> itself — the file the extension generates its pickers from
/// (research/PLAN_one_model_catalog.md D4; epic 1 story 2 made the file, epic 2 story 1 makes this half read it).
/// </summary>
/// <remarks>
/// Until epic 2, <see cref="ConsultantResolution.Consulting"/> was a literal held level with the file by this test. Now it
/// IS the file's list: the server and the panel answer "which runtime can consult" from one source, so a runtime added to
/// the file is offered by one and run by the other on the same day.
/// </remarks>
public sealed class FeatureAvailabilityTests
{
    private static JsonElement Shared()
    {
        var path = Path.Combine(ProductionSources.RepositoryRoot(), "shared", "feature-availability.json");
        using var parsed = JsonDocument.Parse(File.ReadAllBytes(path));

        return parsed.RootElement.Clone();
    }

    private static IReadOnlyList<string> Words(JsonElement list) => [.. list.EnumerateArray().Select(one => one.GetString()!)];

    [Fact]
    public void TheEmbeddedFile_IsTheSharedFile_FeatureForFeature()
    {
        var features = Shared().GetProperty("features");

        FeatureAvailability.Builtin.Consultant.Should().Equal(Words(features.GetProperty("consultant")));
        FeatureAvailability.Builtin.Chat.Should().Equal(Words(features.GetProperty("chat")));
        FeatureAvailability.Builtin.Effort.Select(row => row.Runtime)
            .Should().Equal(Shared().GetProperty("effort").EnumerateArray().Select(row => row.GetProperty("runtime").GetString()));
        FeatureAvailability.Builtin.Thinking.Select(row => (row.Runtime, row.Source))
            .Should().Equal(Shared().GetProperty("thinking").EnumerateArray()
                .Select(row => (row.GetProperty("runtime").GetString()!, row.GetProperty("source").GetString()!)));
    }

    private static FeatureAvailabilitySeed SeedWith(IReadOnlyList<ThinkingRowSeed>? thinking) => new(
        ["codex", "api"],
        new FeatureListsSeed(["codex"], ["codex"]),
        [new EffortRowSeed("codex", "unmeasured", [], "", "n"), new EffortRowSeed("api", "probe", [], "", "n")],
        thinking);

    [Fact]
    public void AThinkingListMissingARuntime_OrNamingOneTwice_IsRefused_LikeTheGeneratorRefusesIt()
    {
        var missing = () => FeatureAvailability.FromSeed(SeedWith([new ThinkingRowSeed("codex", "unmeasured", "n")]));
        var twice = () => FeatureAvailability.FromSeed(SeedWith(
            [new ThinkingRowSeed("codex", "unmeasured", "n"), new ThinkingRowSeed("codex", "none", "n"), new ThinkingRowSeed("api", "probe", "n")]));

        missing.Should().Throw<InvalidOperationException>().WithMessage("*api*thinking*");
        twice.Should().Throw<InvalidOperationException>().WithMessage("*codex*thinking*");
        FeatureAvailability.FromSeed(SeedWith(null)).Thinking.Should().BeEmpty("a seed from before D12 carries no thinking list");
    }

    [Fact]
    public void ThinkingIsSwitchable_OnlyWhereTheModelSaysSo_AndAnUnknownRuntimeHasNone()
    {
        FeatureAvailability.Builtin.ThinkingOf("api").Source.Should().Be("probe");
        FeatureAvailability.Builtin.ThinkingOf("claude").Source.Should().Be("none", "claude's depth is its effort (D12)");
        FeatureAvailability.Builtin.ThinkingOf("llama.cpp").Source.Should().Be("none");
    }

    [Fact]
    public void TheConsultingRuntimes_AreTheLoadedList_NotACopyOfIt()
    {
        ConsultantResolution.Consulting.Should().BeSameAs(FeatureAvailability.Builtin.Consultant,
            "a second list is a second place to forget a runtime");
        ConsultantResolution.Consulting.Should().Equal(Words(Shared().GetProperty("features").GetProperty("consultant")));
    }

    [Fact]
    public void TheEffortLevels_ComeFromTheFile_ClaudeListedAndAntigravityNone()
    {
        FeatureAvailability.Builtin.EffortOf("claude").Levels.Should().Equal("low", "medium", "high", "xhigh", "max");
        FeatureAvailability.Builtin.EffortOf("antigravity").Source.Should().Be("none");
        FeatureAvailability.Builtin.EffortOf("codex").Source.Should().Be("unmeasured");
    }

    [Fact]
    public void AFileThatNamesNoConsultingRuntime_IsRefusedByName()
    {
        var broken = new FeatureAvailabilitySeed(["codex"], new FeatureListsSeed([], ["codex"]), []);

        var act = () => FeatureAvailability.FromSeed(broken);

        act.Should().Throw<InvalidOperationException>().WithMessage("*consultant*");
    }

    [Fact]
    public void AFeatureRuntimeOutsideTheRuntimes_IsRefusedByName()
    {
        var broken = new FeatureAvailabilitySeed(["codex"], new FeatureListsSeed(["codex", "emacs"], ["codex"]), []);

        var act = () => FeatureAvailability.FromSeed(broken);

        act.Should().Throw<InvalidOperationException>().WithMessage("*emacs*");
    }
}
