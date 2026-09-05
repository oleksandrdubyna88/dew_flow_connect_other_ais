using System.Text.Json;
using Xunit;
using FluentAssertions;
using CoaiMcp.Core.Findings;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Tests;

/// <summary>
/// A local review cannot list findings until the token ceiling cuts it off mid-string.
/// </summary>
/// <remarks>
/// <para>The third way the local model fails, found by the five-window campaign of 2026-09-05 after
/// the first two were fixed: with every string bounded (0.17.2), a reviewer produced <b>forty-three</b>
/// findings in 385 lines and hit <c>max_tokens</c> inside the forty-third's <c>why</c> — an
/// unterminated string at character 32,066 of 32,112, one repair launch, the same again. Every
/// string was finite; the ARRAY was not.</para>
/// <para>The grammar bounds arrays too. And the bound is not only a guard: a review with forty
/// findings is a review nobody reads. Ten is the count at which a review with every string at its
/// bound still fits the token ceiling — the point being that a schema-valid answer can always
/// finish — and more than any local reviewer here has returned and been worth resolving.</para>
/// </remarks>
public sealed class LocalReviewIsBoundedInCountTests
{
    private static JsonElement SchemaSent() =>
        JsonDocument.Parse(LocalAsk.RequestBody("qwen", "review this", FindingSchema.Json, 42, "none", 8192))
            .RootElement.GetProperty("response_format").GetProperty("json_schema").GetProperty("schema");

    [Fact]
    public void TheFindingsArray_IsBounded_InTheSchemaTheLocalEngineReceives()
    {
        var findings = SchemaSent().GetProperty("properties").GetProperty("findings");

        findings.TryGetProperty("maxItems", out var max).Should().BeTrue(
            "an unbounded array is where a model lists findings until the token ceiling");
        max.GetInt32().Should().BeLessThanOrEqualTo(25).And.BeGreaterThanOrEqualTo(10);
    }

    [Fact]
    public void TheBoundFitsTheTokenCeiling()
    {
        // Every finding at its string bounds (200 + 1000 + 1000 characters plus the fixed fields) must
        // fit under the 8192-token ceiling the request carries — the point is that a schema-valid
        // answer can always finish, even the longest one the grammar allows.
        var schema = SchemaSent();
        var items = schema.GetProperty("properties").GetProperty("findings").GetProperty("maxItems").GetInt32();
        var perFinding = 200 + 1000 + 1000 + 200; // title, why, fix, and the enum/path/line fields
        var roughTokens = items * perFinding / 3;   // ~3 characters per token for English prose

        roughTokens.Should().BeLessThan(8192, "otherwise the bound does not bound anything");
    }

    [Fact]
    public void TheSharedSchema_StaysUnbounded_BecauseOpenAIStrictModeRejectsMaxItems() =>
        FindingSchema.Json.Should().NotContain("maxItems");
}
