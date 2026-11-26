using System.Text.Json;
using CoaiMcp.Core.Toolchain;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// <c>shared/toolchain.schema.json</c> and the reader's tables say the same (epoch 3 E2 s1 t1). Neither half of the product has a JSON Schema engine, so this test is the only guard against the two
/// drifting: the maps, their keys, which keys are required, the types, the version, the adapters, the skippable steps and the limits.
/// </summary>
public sealed class AToolchainSchemaAgreesWithTheReaderTests
{
    private static JsonElement Defs()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoPaths.Root(), "shared", "toolchain.schema.json")));

        return document.RootElement.GetProperty("$defs").Clone();
    }

    public static TheoryData<string> LevelNames => [.. ToolchainTables.Levels.Select(level => level.Name)];

    [Fact]
    public void TheSchemaHasOneDefPerMap_AndNoOther_ButTheArgvList()
    {
        Defs().EnumerateObject().Select(def => def.Name).Should().BeEquivalentTo([.. ToolchainTables.Levels.Select(level => level.Name), "argv"]);
    }

    [Theory]
    [MemberData(nameof(LevelNames))]
    public void AMap_HasTheSameKeys_RequiredKeys_AndIsClosed(string name)
    {
        var level = ToolchainTables.Levels.Single(one => one.Name == name);
        var def = Defs().GetProperty(name);

        def.GetProperty("properties").EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(level.Fields.Keys);
        RequiredOf(def).Should().BeEquivalentTo(level.Required);
        def.GetProperty("additionalProperties").GetBoolean().Should().BeFalse("an unknown key is refused: a typo must not pass");
    }

    private static IEnumerable<string> RequiredOf(JsonElement def) =>
        def.TryGetProperty("required", out var required) ? required.EnumerateArray().Select(key => key.GetString()!) : [];

    [Theory]
    [MemberData(nameof(LevelNames))]
    public void EveryKey_HasTheSameType(string name)
    {
        var level = ToolchainTables.Levels.Single(one => one.Name == name);
        var properties = Defs().GetProperty(name).GetProperty("properties");

        foreach (var (key, field) in level.Fields)
        {
            TypeOf(properties.GetProperty(key)).Should().Be(
                field switch
                {
                    ToolchainField.Integer => "integer",
                    ToolchainField.Text => "string",
                    ToolchainField.Argv or ToolchainField.SkipList or ToolchainField.TextList or ToolchainField.Projects => "array",
                    ToolchainField.Timeouts => "object",
                    _ => throw new InvalidOperationException(),
                },
                $"{name}.{key}");
        }
    }

    private static string TypeOf(JsonElement property)
    {
        if (property.TryGetProperty("$ref", out var reference))
        {
            return reference.GetString() switch
            {
                "#/$defs/argv" => "array",
                "#/$defs/timeouts" => "object",
                var other => throw new InvalidOperationException($"unexpected reference {other}"),
            };
        }

        return property.TryGetProperty("const", out _) ? "integer" : property.GetProperty("type").GetString()!;
    }

    [Fact]
    public void TestFor_NeedsTestFiles_InTheSchemaAsInTheReader()
    {
        Defs().GetProperty("project").GetProperty("dependentRequired").GetProperty("testFor").EnumerateArray().Select(name => name.GetString()).Should().Equal("testFiles");
    }

    [Fact]
    public void TheComment_SaysTheRuleOfSpecA2()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoPaths.Root(), "shared", "toolchain.schema.json")));
        var comment = document.RootElement.GetProperty("$comment").GetString()!;

        comment.Should().Contain("states no command at all").And.Contain("run exactly as written").And.Contain("`skip` of `restore` or `format` takes that step out of a defaulted project");
    }

    [Fact]
    public void TheVersion_TheAdapters_AndTheSkippableSteps_AreTheTables()
    {
        var root = Defs().GetProperty("toolchain").GetProperty("properties");

        root.GetProperty("version").GetProperty("const").GetInt32().Should().Be(ToolchainTables.Version);
        root.GetProperty("skip").GetProperty("items").GetProperty("enum").EnumerateArray().Select(step => step.GetString()).Should().Equal(ToolchainTables.SkippableSteps);
        Defs().GetProperty("project").GetProperty("properties").GetProperty("adapter").GetProperty("enum").EnumerateArray().Select(adapter => adapter.GetString()).Should().Equal(ToolchainTables.Adapters);
    }

    [Fact]
    public void TheMemoryRange_AndTheDurationPattern_AreTheTables()
    {
        var memory = Defs().GetProperty("project").GetProperty("properties").GetProperty("memoryMb");
        memory.GetProperty("minimum").GetInt32().Should().Be(ToolchainTables.MemoryMbMin);
        memory.GetProperty("maximum").GetInt32().Should().Be(ToolchainTables.MemoryMbMax);

        var timeouts = Defs().GetProperty("timeouts").GetProperty("properties");
        timeouts.GetProperty("build").GetProperty("pattern").GetString().Should().Be(ToolchainTables.DurationPattern);
        timeouts.GetProperty("test").GetProperty("pattern").GetString().Should().Be(ToolchainTables.DurationPattern);
    }
}
