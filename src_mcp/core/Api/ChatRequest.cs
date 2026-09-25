using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CoaiMcp.Core.Api;

/// <summary>
/// One <c>chat/completions</c> request body, spelled the way a <see cref="ApiDialect"/> says.
/// </summary>
/// <remarks>
/// <para>Extracted from <c>LocalAsk.RequestBody</c> (story S1.2 of <c>PLAN_feature_review.md</c>) so
/// the local reviewer and the <c>api</c> reviewer spell one request from one writer, and the
/// differences between engines are ROWS of a file rather than a second writer. The local body is
/// pinned byte for byte by <c>LocalRequestBodyIsPinnedTests</c>; the field ORDER below is that
/// body's order and is part of the pin.</para>
/// <para>What the local body decided — sampling in the request, the schema demanded rather than
/// requested, a ceiling on tokens, a small penalty against a degenerate loop, bounded free text —
/// is recorded at each field in <c>LocalAsk</c>'s history; this writer only carries the decisions.</para>
/// </remarks>
public static class ChatRequest
{
    /// <summary>How many findings one bounded review may return. See <see cref="Bounded"/>.</summary>
    private const int MaxFindings = 10;

    private static readonly (string Field, int MaxLength)[] FreeTextBounds =
    [
        ("title", 200),
        ("why", 1000),
        ("fix", 1000),
    ];

    /// <summary>The completion request, as the wire wants it for this dialect.</summary>
    /// <param name="dialect">Which endpoint family is being spoken to.</param>
    /// <param name="model">Empty is legal — the endpoint picks.</param>
    /// <param name="prompt">The whole review prompt: role, rules, plan or diff.</param>
    /// <param name="schemaJson">The finding schema, verbatim.</param>
    /// <param name="seed">Per-prompt, sent only when the dialect says so.</param>
    /// <param name="reasoningEffort">What the panel configured; the dialect decides what is sent.</param>
    /// <param name="maxTokens">The answer ceiling; zero or less sends none.</param>
    /// <exception cref="JsonException">The schema does not parse — refused before any request is
    /// sent, because an unconstrained request buys a full generation and an unusable answer.</exception>
    public static string Body(
        ApiDialect dialect,
        string model,
        string prompt,
        string schemaJson,
        int seed,
        string reasoningEffort = "",
        int maxTokens = 8192)
    {
        using var validate = JsonDocument.Parse(schemaJson);

        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream))
        {
            json.WriteStartObject();
            json.WriteString("model", model);
            json.WriteBoolean("stream", false);
            WriteSampling(json, dialect, seed);
            if (maxTokens > 0 && dialect.MaxTokensField.Length > 0)
            {
                json.WriteNumber(dialect.MaxTokensField, maxTokens);
            }

            var effort = dialect.EffortToSend(reasoningEffort);
            if (effort.Length > 0)
            {
                json.WriteString(dialect.ReasoningEffortField, effort);
            }

            json.WriteStartArray("messages");
            json.WriteStartObject();
            json.WriteString("role", "user");
            json.WriteString("content", prompt);
            json.WriteEndObject();
            json.WriteEndArray();

            WriteResponseFormat(json, dialect, schemaJson);
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>The three sampling fields, each only when the dialect sends it.</summary>
    private static void WriteSampling(Utf8JsonWriter json, ApiDialect dialect, int seed)
    {
        if (dialect.Temperature is { } temperature)
        {
            json.WriteNumber("temperature", temperature);
        }

        if (dialect.Seed)
        {
            json.WriteNumber("seed", seed);
        }

        if (dialect.FrequencyPenalty is { } penalty)
        {
            json.WriteNumber("frequency_penalty", penalty);
        }
    }

    /// <summary>
    /// <c>json_schema</c> demands the shape; <c>json_object</c> only asks for JSON. The local
    /// measurement chose the first — <c>json_object</c> was answered with an invented shape — and a
    /// hosted dialect chooses per what its probe measured.
    /// </summary>
    private static void WriteResponseFormat(Utf8JsonWriter json, ApiDialect dialect, string schemaJson)
    {
        json.WriteStartObject("response_format");
        if (dialect.ResponseFormat == "json_object")
        {
            json.WriteString("type", "json_object");
            json.WriteEndObject();

            return;
        }

        json.WriteString("type", "json_schema");
        json.WriteStartObject("json_schema");
        json.WriteString("name", "findings");
        json.WriteBoolean("strict", dialect.Strict);
        json.WritePropertyName("schema");
        (dialect.BoundedSchema ? Bounded(schemaJson) : JsonNode.Parse(schemaJson)!).WriteTo(json);
        json.WriteEndObject();
        json.WriteEndObject();
    }

    /// <summary>
    /// The finding schema with its free-text fields bounded — or any other schema exactly as given.
    /// </summary>
    /// <remarks>
    /// <para>Moved here from <c>LocalAsk</c> with its behaviour intact. A reasoning model can reason in
    /// the wrong place: observed twice on 2026-09-05, an answer opened as good JSON and then the
    /// <c>why</c> field became the model's chain of thought for thirty kilobytes until the token
    /// ceiling cut it mid-string. The frequency penalty cannot touch that; the GRAMMAR can — a string
    /// with a <c>maxLength</c> is a string the engine forces the model to close. And the array was the
    /// third leak: forty-three findings in 385 lines, the ceiling inside the forty-third's <c>why</c>.
    /// Ten is the count at which a full-length review still fits the 8,192-token ceiling.</para>
    /// <para>Only a dialect that says <c>boundedSchema</c> gets this: OpenAI's strict structured
    /// outputs reject <c>maxLength</c> as an unsupported keyword with a 400, so the bound is added to
    /// the copy a local engine gets and never to <c>FindingSchema.Json</c>.</para>
    /// <para>The walker looks for the finding schema's own shape and touches nothing else: a probe or a
    /// test handing this route a schema of another shape must get it back unchanged.</para>
    /// </remarks>
    public static JsonNode Bounded(string schemaJson)
    {
        var root = JsonNode.Parse(schemaJson) ?? throw new JsonException("the schema parsed to nothing");
        if (root["properties"]?["findings"] is not JsonObject findings
            || findings["items"]?["properties"] is not JsonObject fields)
        {
            return root;
        }

        findings["maxItems"] = MaxFindings;

        foreach (var (field, maxLength) in FreeTextBounds)
        {
            if (fields[field] is JsonObject property && property["type"]?.GetValue<string>() == "string")
            {
                property["maxLength"] = maxLength;
                var description = property["description"]?.GetValue<string>() ?? string.Empty;
                property["description"] = $"{description} (at most {maxLength} characters)".Trim();
            }
        }

        return root;
    }
}
