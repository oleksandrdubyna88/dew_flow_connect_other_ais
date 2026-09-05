using System.Text.Json;
using Xunit;
using FluentAssertions;
using CoaiMcp.Core.Findings;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Tests;

/// <summary>
/// A local model must not be able to think inside a JSON string until the token ceiling.
/// </summary>
/// <remarks>
/// <para>Observed twice on 2026-09-05 (10:33 and 10:43, both Architecture, both kept under
/// <c>unparseable/</c>): the answer opened as perfectly good JSON — severity, file, line, a real
/// title — and then the <c>why</c> field turned into the model's reasoning: <i>"The plan *is* the
/// instruction. The plan says … The rule says … Is there a violation? Maybe … No. Wait"</i>, for
/// thirty kilobytes, until <c>max_tokens</c> stopped it mid-string. Unterminated string at character
/// 482; one repair launch; the same again; a reviewer lost and three minutes of the one GPU with it.</para>
/// <para>The frequency penalty of 0.17.1 cannot touch this — it is not a repeated sentence, it is a
/// reasoning model reasoning in the wrong place. What can touch it is the grammar: the local
/// engine constrains generation to the schema, and a schema that BOUNDS its strings forces the
/// string to close. So the schema the local engine receives carries <c>maxLength</c> on every
/// free-text field — and ONLY that schema: OpenAI's strict structured outputs, which codex's
/// <c>--output-schema</c> feeds, reject <c>maxLength</c> as an unsupported keyword, so the shared
/// schema must stay exactly as it is.</para>
/// </remarks>
public sealed class LocalReasoningStaysOutOfTheJsonTests
{
    private static JsonElement SchemaSent() =>
        JsonDocument.Parse(LocalAsk.RequestBody("qwen", "review this", FindingSchema.Json, 42, "none", 8192))
            .RootElement.GetProperty("response_format").GetProperty("json_schema").GetProperty("schema");

    private static JsonElement Field(JsonElement schema, string name) =>
        schema.GetProperty("properties").GetProperty("findings").GetProperty("items").GetProperty("properties").GetProperty(name);

    [Theory]
    [InlineData("title", 200)]
    [InlineData("why", 1000)]
    [InlineData("fix", 1000)]
    public void EveryFreeTextField_IsBounded_InTheSchemaTheLocalEngineReceives(string field, int atMost)
    {
        var bounded = Field(SchemaSent(), field);

        bounded.TryGetProperty("maxLength", out var max).Should().BeTrue(
            $"an unbounded '{field}' is where a reasoning model thinks until the token ceiling");
        max.GetInt32().Should().BeLessThanOrEqualTo(atMost).And.BeGreaterThan(50);
    }

    [Fact]
    public void TheBoundIsInTheDescriptionToo_SoAModelThatReadsTheSchemaSeesIt()
    {
        var why = Field(SchemaSent(), "why").GetProperty("description").GetString() ?? string.Empty;

        why.Should().Contain("characters", "the grammar enforces it, the description explains it");
    }

    [Fact]
    public void TheSharedSchema_StaysUnbounded_BecauseOpenAIStrictModeRejectsMaxLength() =>
        FindingSchema.Json.Should().NotContain("maxLength",
            "codex feeds this to OpenAI structured outputs, which answer 400 to unsupported keywords");

    [Fact]
    public void EverythingElseInTheSchema_IsUntouched()
    {
        var sent = SchemaSent();

        Field(sent, "severity").GetProperty("enum").GetArrayLength().Should().Be(4);
        Field(sent, "file").GetProperty("type").GetArrayLength().Should().Be(2, "still nullable");
        sent.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void ASchemaOfAnotherShape_IsPassedThroughUnchanged()
    {
        // The bound is for the finding schema. Anything else handed to the local route — a probe, a
        // test — must arrive exactly as given rather than be rewritten by a walker that assumed a shape.
        var body = JsonDocument.Parse(LocalAsk.RequestBody("qwen", "p", """{"type":"object","properties":{"ok":{"type":"boolean"}}}""", 1, "none", 64));
        var schema = body.RootElement.GetProperty("response_format").GetProperty("json_schema").GetProperty("schema");

        schema.GetProperty("properties").GetProperty("ok").GetProperty("type").GetString().Should().Be("boolean");
        schema.ToString().Should().NotContain("maxLength");
    }
}
