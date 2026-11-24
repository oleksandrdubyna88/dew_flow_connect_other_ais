using System.Text.Json;
using CoaiMcp.Core.Work;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// B13 — <c>shared/work-plan.schema.json</c> and the validator's tables say the same. Neither half of the product has a JSON Schema engine, so this is the only guard against the two
/// drifting (risk 4 of the spec): the levels, their keys and which are required, the types, the enums, the complexity range and every id pattern.
/// </summary>
public sealed class AWorkPlanSchemaAgreesWithTheValidatorTests
{
    private static JsonElement Defs()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoPaths.Root(), "shared", "work-plan.schema.json")));

        return document.RootElement.GetProperty("$defs").Clone();
    }

    public static TheoryData<string> LevelNames => [.. WorkTables.Levels.Select(level => level.Name)];

    [Fact]
    public void TheSchemaHasOneDefPerLevel_AndNoOther_ButTheSharedList()
    {
        Defs().EnumerateObject().Select(def => def.Name).Should().BeEquivalentTo([.. WorkTables.Levels.Select(level => level.Name), "idList"]);
    }

    [Theory]
    [MemberData(nameof(LevelNames))]
    public void ALevel_HasTheSameKeys_RequiredKeys_AndOpenness(string name)
    {
        var level = WorkTables.Levels.Single(one => one.Name == name);
        var def = Defs().GetProperty(name);

        def.GetProperty("properties").EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(level.Fields.Keys);
        def.GetProperty("required").EnumerateArray().Select(key => key.GetString()).Should().BeEquivalentTo(level.Required);
        def.GetProperty("additionalProperties").GetBoolean().Should().Be(level.AllowUnknown, "an unknown key below the feature level is an error");
    }

    [Theory]
    [MemberData(nameof(LevelNames))]
    public void EveryKey_HasTheSameType(string name)
    {
        var level = WorkTables.Levels.Single(one => one.Name == name);
        var properties = Defs().GetProperty(name).GetProperty("properties");

        foreach (var (key, field) in level.Fields)
        {
            var property = properties.GetProperty(key);
            TypeOf(property).Should().Be(field switch
            {
                WorkField.Text => "string",
                WorkField.Boolean => "boolean",
                WorkField.Integer => "integer",
                WorkField.IdList or WorkField.Dod => "array",
                WorkField.Map => "object",
                _ => throw new InvalidOperationException(),
            }, $"{name}.{key}");
        }
    }

    private static string TypeOf(JsonElement property)
    {
        if (property.TryGetProperty("$ref", out var reference))
        {
            reference.GetString().Should().Be("#/$defs/idList");

            return "array";
        }

        return property.TryGetProperty("enum", out _) ? "string" : property.GetProperty("type").GetString()!;
    }

    [Fact]
    public void TheKindAndStatusLists_AndTheComplexityRange_AreTheSame()
    {
        var task = Defs().GetProperty("task").GetProperty("properties");

        task.GetProperty("kind").GetProperty("enum").EnumerateArray().Select(kind => kind.GetString()).Should().Equal(WorkTables.Kinds);
        task.GetProperty("status").GetProperty("enum").EnumerateArray().Select(status => status.GetString()).Should().Equal(WorkTables.Statuses);
        task.GetProperty("complexity").GetProperty("minimum").GetInt32().Should().Be(WorkTables.ComplexityMin);
        task.GetProperty("complexity").GetProperty("maximum").GetInt32().Should().Be(WorkTables.ComplexityMax);
    }

    [Fact]
    public void EveryIdPattern_IsTheValidatorsPatternVerbatim()
    {
        var defs = Defs();

        foreach (var level in WorkTables.Levels.Where(level => level.IdPattern.Length > 0))
        {
            defs.GetProperty(level.Name).GetProperty("properties").GetProperty("id").GetProperty("pattern").GetString().Should().Be(level.IdPattern, level.Name);
        }

        var dod = defs.GetProperty("task").GetProperty("properties").GetProperty("dod");
        dod.GetProperty("items").GetProperty("properties").GetProperty("id").GetProperty("pattern").GetString().Should().Be(WorkTables.DodIdPattern);
        dod.GetProperty("minItems").GetInt32().Should().Be(1);
        dod.GetProperty("items").GetProperty("required").EnumerateArray().Select(key => key.GetString()).Should().BeEquivalentTo("id", "item");
        dod.GetProperty("items").GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void ThePatterns_RefuseWhatTheSchemaPatternsRefuse_InFull()
    {
        WorkTables.IsId(WorkTables.StoryIdPattern, "s1-first").Should().BeTrue();
        WorkTables.IsId(WorkTables.StoryIdPattern, "s1-../../../outside").Should().BeFalse("an id cannot climb out of a folder");
        WorkTables.IsId(WorkTables.TaskIdPattern, "t1-a/b").Should().BeFalse("an id with a slash");
        WorkTables.IsId(WorkTables.TaskIdPattern, "t1-ok\n").Should().BeFalse("a trailing newline is not part of an id (.NET's $ would allow it)");
        WorkTables.IsId(WorkTables.TaskIdPattern, "t١-ok").Should().BeFalse("only ASCII digits (.NET's \\d would take other scripts')");
        WorkTables.IsId(WorkTables.EpicIdPattern, "e1-Core").Should().BeFalse();
        WorkTables.IsId(WorkTables.EpochIdPattern, "epoch-12").Should().BeTrue();
        WorkTables.IsId(WorkTables.DodIdPattern, "d10").Should().BeTrue();
        WorkTables.IsId(WorkTables.DodIdPattern, "d").Should().BeFalse();
    }

    [Fact]
    public void TheRoleDefaultsFile_AndTheSchemasKinds_AgreeOnTheHaikuKinds()
    {
        using var defaults = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoPaths.Root(), "shared", "role-defaults.json")));
        var haiku = defaults.RootElement.GetProperty("haikuKinds").EnumerateArray().Select(kind => kind.GetString()!);

        haiku.Should().OnlyContain(kind => WorkTables.Kinds.Contains(kind), "a kind a small model may take must be a kind a task can have");
    }
}
