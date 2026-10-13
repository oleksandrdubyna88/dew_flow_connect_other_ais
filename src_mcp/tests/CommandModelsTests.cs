using Xunit;
using FluentAssertions;
using CoaiMcp.Core.Commands;
using CoaiMcp.Server;

namespace CoaiMcp.Tests;

/// <summary>
/// Which three models the split order names for a caller kind, and how the setting that chooses them
/// is read (issue #117; the third tier, <c>coder</c>, 2026-10-13).
/// </summary>
public sealed class CommandModelsTests
{
    private static readonly IReadOnlyDictionary<string, ModelPair> Nothing = new Dictionary<string, ModelPair>();

    [Fact]
    public void TheShippedMap_HasARowForEveryCallerKindTheServerCanIdentify()
    {
        // Enumerated from the kinds the server resolves, not retyped: a kind added to CallerIdentity
        // without a row here would silently borrow `other`'s pair.
        CommandModels.Shipped.Keys.Should().BeEquivalentTo(CallerIdentity.Kinds);
    }

    [Fact]
    public void TheShippedMap_IsTheOneInTheSharedFile()
    {
        // `shared/command-models.json` is owned by neither half; the extension asserts its own
        // constant against it too, so a shipped pair changed on one side goes red on that side.
        using var document = System.Text.Json.JsonDocument.Parse(SharedFixtures.Text("command-models.json"));
        var shared = document.RootElement.GetProperty("shipped").EnumerateObject().ToDictionary(
            row => row.Name,
            row => new ModelPair(
                row.Value.GetProperty("strongest").GetString()!,
                row.Value.GetProperty("implementation").GetString()!,
                row.Value.GetProperty("coder").GetString()!));

        CommandModels.Shipped.Should().BeEquivalentTo(shared);
    }

    [Fact]
    public void AClaudeCodeCaller_WithNothingConfigured_IsFableOpusAndSonnet()
    {
        CommandModels.For(Nothing, CallerIdentity.Claude).Should().Be(new ModelPair("Fable", "Opus", "Sonnet"));
    }

    [Theory]
    [InlineData(CallerIdentity.Codex)]
    [InlineData(CallerIdentity.Gemini)]
    [InlineData(CallerIdentity.Other)]
    public void EveryOtherCaller_WithNothingConfigured_IsNamedNoModel(string kind)
    {
        CommandModels.For(Nothing, kind).Should().Be(new ModelPair(string.Empty, string.Empty, string.Empty));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a-client-from-next-year")]
    public void AKindWithNoRow_ReadsAsOther_SoItIsNeverToldClaudeModels(string kind)
    {
        var configured = new Dictionary<string, ModelPair> { [CallerIdentity.Other] = new("x-strong", "x-usual", "x-fast") };

        CommandModels.For(configured, kind).Should().Be(new ModelPair("x-strong", "x-usual", "x-fast"));
        CommandModels.For(Nothing, kind).Should().Be(new ModelPair(string.Empty, string.Empty));
    }

    [Fact]
    public void AConfiguredName_WinsPerField_AndABlankFieldKeepsTheShippedOne()
    {
        // A person who changed only the implementation model for Claude Code keeps Fable for the
        // split and Sonnet for the code — clearing one box must not blank another slot.
        var configured = new Dictionary<string, ModelPair> { [CallerIdentity.Claude] = new(string.Empty, "x-usual") };

        CommandModels.For(configured, CallerIdentity.Claude).Should().Be(new ModelPair("Fable", "x-usual", "Sonnet"));
    }

    [Fact]
    public void OnlyTheCoderConfigured_KeepsTheShippedStrongestAndImplementation()
    {
        var configured = new Dictionary<string, ModelPair> { [CallerIdentity.Claude] = new(Coder: "x-fast") };

        CommandModels.For(configured, CallerIdentity.Claude).Should().Be(new ModelPair("Fable", "Opus", "x-fast"));
    }

    [Fact]
    public void ARowWithACoder_IsRead_AndARowWithoutOne_LeavesItBlankForTheShippedOne()
    {
        // Every extension released before the third tier writes rows with two fields; this server must
        // read those as "coder not named", which For() then resolves to the shipped coder.
        var setting = CommandModelsSetting.Parse(
            """{ "codex": { "strongest": "a", "implementation": "b", "coder": " c " }, "claude": { "implementation": "x-usual" } }""");

        setting.Complaints.Should().BeEmpty();
        setting.Map["codex"].Should().Be(new ModelPair("a", "b", "c"));
        setting.Map["claude"].Coder.Should().BeEmpty();
        CommandModels.For(setting.Map, CallerIdentity.Claude).Coder.Should().Be("Sonnet");
    }

    [Fact]
    public void AnAbsentSetting_IsTheShippedMapAndNoComplaint()
    {
        var setting = CommandModelsSetting.Parse(null);

        setting.Map.Should().BeEmpty();
        setting.Complaints.Should().BeEmpty();
    }

    [Fact]
    public void AWellFormedSetting_IsReadTrimmedAndKeyedByLowerCaseKind()
    {
        var setting = CommandModelsSetting.Parse(
            """{ " Codex ": { "strongest": " gpt-6-astra ", "implementation": "gpt-6-luna" } }""");

        setting.Complaints.Should().BeEmpty();
        setting.Map.Should().ContainKey("codex").WhoseValue.Should().Be(new ModelPair("gpt-6-astra", "gpt-6-luna"));
    }

    [Fact]
    public void TwoSpellingsOfOneKind_AreOneRow_AndNeverAnException()
    {
        // JSON object keys are case-sensitive, so both survive deserialisation — and they are one
        // kind once trimmed and lower-cased. `ToDictionary` threw ArgumentException here, which the
        // JsonException catch did not see, and it would have left through every settings read.
        // The last one wins, which is what ConsultantRouting's indexer has always done.
        var setting = CommandModelsSetting.Parse(
            """{ "Codex": { "strongest": "first" }, "codex ": { "strongest": "second" } }""");

        setting.Complaints.Should().BeEmpty();
        setting.Map.Should().ContainSingle().Which.Value.Strongest.Should().Be("second");
    }

    [Fact]
    public void AKindThisBuildDoesNotKnow_IsKept()
    {
        // A newer panel may know a caller kind this server does not; dropping it would lose the
        // person's choice the day this server is updated.
        CommandModelsSetting.Parse("""{ "cursor": { "strongest": "c-max" } }""")
            .Map.Should().ContainKey("cursor");
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("""{ "codex": 5 }""")]
    [InlineData("""{ "codex": { "strongest": 5 } }""")]
    public void AnUnreadableSetting_IsTheShippedMapAndOneSentence_NeverAnException(string json)
    {
        // Advice, not a gate: an unreadable value must not refuse the round, and must not pass in
        // silence either — the panel shows every unrecognised setting.
        var setting = CommandModelsSetting.Parse(json);

        setting.Map.Should().BeEmpty();
        setting.Complaints.Should().ContainSingle().Which.Should().Contain("COAI_COMMAND_MODELS");
    }

    [Fact]
    public void TheSettingIsReadFromTheEnvironmentBlock_AndItsComplaintIsUnrecognised()
    {
        var good = PanelSettings.FromEnvironment(key => key == "COAI_COMMAND_MODELS"
            ? """{ "codex": { "strongest": "gpt-6-astra" } }"""
            : null);
        var bad = PanelSettings.FromEnvironment(key => key == "COAI_COMMAND_MODELS" ? "{ not json" : null);

        good.CommandModels.Should().ContainKey("codex");
        bad.UnrecognisedSettings.Should().Contain(u => u.Key == "COAI_COMMAND_MODELS");
    }
}
