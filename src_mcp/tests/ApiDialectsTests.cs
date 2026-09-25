using System.Text.Json;
using CoaiMcp.Core.Api;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The dialect table this build embeds is <c>shared/api-dialects.json</c>, and its two required rows
/// say what the measurements said.
/// </summary>
/// <remarks>
/// The extension keeps the NAMES of the same file (<c>apiDialects.ts</c>, <c>apiDialects.test.ts</c>);
/// neither half owns the file, and each is held to it here — the two-implementations rule in
/// <c>testing.md</c>. The values of the <c>local</c> row are pinned again through the body they produce
/// in <c>LocalRequestBodyIsPinnedTests</c>; this file pins the ROWS, so a value edited in the JSON is
/// caught by name.
/// </remarks>
public sealed class ApiDialectsTests
{
    [Fact]
    public void TheEmbeddedTable_IsTheSharedFile_ByteForByte()
    {
        using var embedded = typeof(ApiDialects).Assembly.GetManifestResourceStream(ApiDialects.Resource);
        embedded.Should().NotBeNull("a build without the table cannot spell a completion request");
        using var reader = new StreamReader(embedded!);

        reader.ReadToEnd().Should().Be(SharedFixtures.Text("api-dialects.json"));
    }

    [Fact]
    public void EveryRowOfTheSharedFile_IsADialectThisBuildKnows_AndNothingElse()
    {
        using var document = JsonDocument.Parse(SharedFixtures.Text("api-dialects.json"));
        var rows = document.RootElement.GetProperty("dialects").EnumerateObject().Select(p => p.Name).ToList();

        ApiDialects.Names.Should().BeEquivalentTo(rows);
        rows.Should().Contain([ApiDialects.LocalName, ApiDialects.OpenAiName]);
    }

    [Fact]
    public void OnlyTheGenericDialectShipsBesideLocal_UntilAVendorRowIsMeasured()
    {
        // PLAN_feature_review.md §4.10: `xai` and `qwen` are written FROM `--probe-api` rows, never
        // from documentation. The day one lands, this assertion is edited in the same commit as the
        // §6 table — which is the point of pinning the list rather than its size.
        ApiDialects.Names.Should().BeEquivalentTo(["local", "openai"]);
    }

    [Fact]
    public void TheLocalRow_CarriesTheMeasuredValues()
    {
        var local = ApiDialects.Local;

        local.Temperature.Should().Be(0);
        local.Seed.Should().BeTrue();
        local.FrequencyPenalty.Should().Be(0.2);
        local.MaxTokensField.Should().Be("max_tokens");
        local.ResponseFormat.Should().Be("json_schema");
        local.Strict.Should().BeTrue();
        local.BoundedSchema.Should().BeTrue();
        local.ReasoningEffortField.Should().Be("reasoning_effort");
        local.EffortToSend("none").Should().Be("none", "a local engine takes the word as it was typed");
    }

    [Fact]
    public void TheGenericRow_SendsNothingAReasoningModelIsDocumentedToRefuse()
    {
        var openai = ApiDialects.OpenAi;

        openai.Temperature.Should().BeNull();
        openai.Seed.Should().BeFalse();
        openai.FrequencyPenalty.Should().BeNull();
        openai.MaxTokensField.Should().Be("max_completion_tokens");
        openai.BoundedSchema.Should().BeFalse("OpenAI's strict mode rejects maxLength with a 400");
        openai.EffortToSend("none").Should().BeEmpty("the panel's `none` is a local word; a hosted model gets no field");
        openai.EffortToSend("high").Should().Be("high");
    }

    [Theory]
    [InlineData("")]
    [InlineData("engine")]
    [InlineData("  Engine ")]
    public void EngineOrNothing_SendsNoEffortField_InEveryDialect(string configured)
    {
        foreach (var name in ApiDialects.Names)
        {
            ApiDialects.Named(name)!.EffortToSend(configured).Should().BeEmpty(name);
        }
    }

    [Fact]
    public void ANameIsLookedUpWithoutCase_AndAnUnknownOneIsNull()
    {
        ApiDialects.Named(" OpenAI ").Should().NotBeNull();
        ApiDialects.Named("grokish").Should().BeNull();
    }

    [Fact]
    public void ARowMissingAField_IsRefusedByName()
    {
        using var row = JsonDocument.Parse("""{"temperature":0}""");

        var act = () => ApiDialect.From("broken", row.RootElement);

        act.Should().Throw<JsonException>().WithMessage("*'broken'*'seed'*");
    }

    [Fact]
    public void AJsonObjectDialect_OnlyAsksForJson_AndBoundsNothing()
    {
        var asks = ApiDialects.OpenAi with { ResponseFormat = "json_object" };

        var body = ChatRequest.Body(asks, "m", "p", """{"type":"object"}""", 1);

        body.Should().Contain("\"response_format\":{\"type\":\"json_object\"}").And.NotContain("json_schema");
    }
}
