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
    public void OnlyMeasuredVendorRowsShipBesideLocalAndTheGenericOne()
    {
        // PLAN_feature_review.md §4.10: a vendor row is written FROM measured answers, never from
        // documentation, and this assertion is edited in the same commit as the §6 table — which is the
        // point of pinning the list rather than its size. `xai` landed on 2026-09-26 from the S0.5 probe
        // and the first live feature review (RESULTS_feature_reviewer_models.md, phase 1); `dashscope` the
        // same day from the probe and the first trial's raw answers on the Alibaba route.
        ApiDialects.Names.Should().BeEquivalentTo(["local", "openai", "xai", "dashscope"]);
    }

    /// <summary>
    /// The Alibaba Model Studio (DashScope compatible-mode) row, value by value, each traced to an answer of
    /// 2026-09-26: the probe accepted every field (200 to `frequency_penalty`, `seed`, `temperature`, strict
    /// `json_schema` and each `reasoning_effort`) — but the trial's raw answers showed the schema is NOT
    /// enforced there (a GLM-5.3 answer without the required `fix`; the vendor's guide: json_schema only
    /// on the Qwen3.7/3.8 families, and "when thinking mode is enabled … the schema constraints will not
    /// take effect"), so the row asks for JSON and lets the parser and the repair hold the shape; and the
    /// ceiling bounds reasoning PLUS the answer (`completion_tokens` includes `reasoning_tokens`: at 16,384
    /// every GLM-5.3 call spent all of it thinking, at 65,536 four of four finished with `stop` using
    /// 21–58K), so the row floors it at 65,536. No thinking switch or budget: each model runs at its
    /// vendor's default depth (the measurement's frozen rule), and GLM-5.3 cannot be switched anyway.
    /// </summary>
    [Fact]
    public void TheDashscopeRow_IsWrittenFromTheMeasuredAnswers()
    {
        var dashscope = ApiDialects.Named("dashscope")!;

        dashscope.Temperature.Should().BeNull();
        dashscope.Seed.Should().BeFalse();
        dashscope.FrequencyPenalty.Should().BeNull();
        // Measured 2026-09-27 on qwen3.8-max, one prompt, ceiling 64 on both: `max_tokens: 64` → 2,410 completion tokens
        // (2,344 of them reasoning — the field bounds the ANSWER only there), `max_completion_tokens: 64` → 64 tokens,
        // `finish_reason: length` — the documented total. A ceiling that bounds reasoning plus the answer is the whole
        // point of the floor, so the row names the field that does.
        dashscope.MaxTokensField.Should().Be("max_completion_tokens", "the field the vendor documents as reasoning plus answer; max_tokens bounded the answer only");
        dashscope.MaxTokensFloor.Should().Be(65536);
        dashscope.CeilingFor(8192).Should().Be(65536, "the local engine's 8,192 cut every reasoning answer before a character of it");
        dashscope.ResponseFormat.Should().Be("json_object", "a schema is accepted but not enforced in thinking mode");
        dashscope.BoundedSchema.Should().BeFalse();
        dashscope.ReasoningEffortField.Should().Be("reasoning_effort");
        dashscope.EffortToSend("none").Should().BeEmpty();
        dashscope.CacheKeyHeader.Should().BeEmpty("the implicit cache is content-addressed: a 1,024-token common prefix, no key");
        dashscope.ExtraBody.Should().BeEmpty("no thinking switch: the vendor's default depth is the rule");
    }

    /// <summary>
    /// The xAI row, value by value, each traced to an answer of 2026-09-26: the S0.5 probe (`frequency_penalty`
    /// refused with 400 "does not support parameter frequencyPenalty"; `json_schema` strict, `seed`,
    /// `temperature` and every `reasoning_effort` value answered 200) and the first product-path review
    /// (three turns with a byte-identical prefix cached 1,152 tokens each — the docs' `x-grok-conv-id`
    /// routing header is what a hosted cache needs to be hit).
    /// </summary>
    [Fact]
    public void TheXaiRow_IsWrittenFromTheMeasuredAnswers()
    {
        var xai = ApiDialects.Named("xai")!;

        xai.Temperature.Should().BeNull("a review is not a place for variety, and sending nothing leaves the vendor's default");
        xai.Seed.Should().BeFalse();
        xai.FrequencyPenalty.Should().BeNull("xAI answered 400 'Model grok-4.7 does not support parameter frequencyPenalty'");
        xai.MaxTokensField.Should().Be("max_completion_tokens");
        xai.ResponseFormat.Should().Be("json_schema");
        xai.Strict.Should().BeTrue();
        xai.BoundedSchema.Should().BeFalse();
        xai.ReasoningEffortField.Should().Be("reasoning_effort");
        xai.EffortToSend("none").Should().BeEmpty();
        xai.CacheKeyHeader.Should().Be("x-grok-conv-id");
    }

    [Fact]
    public void EveryRow_NamesItsCacheRoutingHeader_OrNone()
    {
        ApiDialects.Local.CacheKeyHeader.Should().BeEmpty("a local engine has one server and needs no routing");
        ApiDialects.OpenAi.CacheKeyHeader.Should().BeEmpty("the generic row sends nothing a vendor is not documented to read");
        foreach (var name in ApiDialects.Names)
        {
            ApiDialects.Named(name)!.CacheKeyHeader.Should().NotContain(" ", name);
        }
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

    // ---------- vendor fields (the Alibaba route's enable_thinking / thinking_budget, measured 2026-09-26) ----------

    /// <summary>
    /// A row's <c>extraBody</c> fields go into the request verbatim, at the top level — a bool, a number, an
    /// object — because no standard field spells them, and without <c>thinking_budget</c> a DashScope
    /// reasoning model thinks until the token ceiling.
    /// </summary>
    [Fact]
    public void ARowsExtraBodyFields_AreSentVerbatim_AtTheTopLevel()
    {
        var dialect = ApiDialect.From("probe", Row("""{"enable_thinking":true,"thinking_budget":8000,"nested":{"a":[1,2]}}"""));

        var body = ChatRequest.Body(dialect, "m", "p", """{"type":"object"}""", 1, maxTokens: 30000);

        using var sent = JsonDocument.Parse(body);
        sent.RootElement.GetProperty("enable_thinking").GetBoolean().Should().BeTrue();
        sent.RootElement.GetProperty("thinking_budget").GetInt32().Should().Be(8000);
        sent.RootElement.GetProperty("nested").GetProperty("a").GetArrayLength().Should().Be(2);
        sent.RootElement.GetProperty("max_tokens").GetInt32().Should().Be(30000);
    }

    [Fact]
    public void EveryShippedRow_SendsNoVendorFieldItWasNotMeasuredWith()
    {
        // `local` is pinned byte for byte and `openai`/`xai` were measured without any: a field added here
        // is a field added to that row's measurement, in the same commit.
        foreach (var name in (string[])["local", "openai", "xai"])
        {
            ApiDialects.Named(name)!.ExtraBody.Should().BeEmpty(name);
            ApiDialects.Named(name)!.MaxTokensFloor.Should().Be(0, name);
        }
    }

    [Fact]
    public void AnExtraBodyThatIsNotAnObject_IsRefusedByName()
    {
        var act = () => ApiDialect.From("broken", Row("[1]"));

        act.Should().Throw<JsonException>().WithMessage("*'broken'*'extraBody'*");
    }

    // ---------- the ceiling floor (the Alibaba route bounds reasoning + answer with one number, measured 2026-09-26) ----------

    /// <summary>
    /// A configured ceiling below the row's floor is sent AS the floor; one above it is sent as configured.
    /// The panel's 8,192 is a local engine's number that every api row inherits, and at 8,192 (and 16,384)
    /// every GLM-5.3 and Qwen3.8-max answer of the first trial was cut before a character of it was written.
    /// </summary>
    [Fact]
    public void ARowsFloor_RaisesACeilingBelowIt_AndLeavesAHigherOneAlone()
    {
        var dialect = ApiDialect.From("probe", Row("{}", floor: 32768));

        dialect.CeilingFor(8192).Should().Be(32768);
        dialect.CeilingFor(65536).Should().Be(65536);
        using var low = JsonDocument.Parse(ChatRequest.Body(dialect, "m", "p", """{"type":"object"}""", 1, maxTokens: 8192));
        low.RootElement.GetProperty("max_tokens").GetInt32().Should().Be(32768);
        ApiDialects.OpenAi.CeilingFor(8192).Should().Be(8192, "a row without a floor sends what was configured");
    }

    [Fact]
    public void ANegativeFloor_IsRefusedByName()
    {
        var act = () => ApiDialect.From("broken", Row("{}", floor: -1));

        act.Should().Throw<JsonException>().WithMessage("*'broken'*'maxTokensFloor'*");
    }

    /// <summary>A probe row with every required field, the vendor fields and the floor as given.</summary>
    private static JsonElement Row(string extraBody, int floor = 0) =>
        JsonDocument.Parse(
            "{\"temperature\":null,\"seed\":false,\"frequencyPenalty\":null,\"maxTokensField\":\"max_tokens\",\"responseFormat\":\"json_object\","
            + "\"strict\":false,\"boundedSchema\":false,\"reasoningEffortField\":\"\",\"reasoningEffortMap\":{},\"cacheKeyHeader\":\"\","
            + "\"extraBody\":" + extraBody + ",\"maxTokensFloor\":" + floor + "}").RootElement.Clone();
}
