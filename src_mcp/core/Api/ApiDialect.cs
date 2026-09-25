using System.Text.Json;

namespace CoaiMcp.Core.Api;

/// <summary>
/// How one OpenAI-compatible endpoint family wants a completion request spelled — one row of
/// <c>shared/api-dialects.json</c>.
/// </summary>
/// <remarks>
/// <para><b>Dialects are data</b> (PLAN_feature_review.md §4.10). The local body's every value was
/// measured against a real engine, and a hosted vendor's will be measured through
/// <c>coai-mcp --probe-api</c> before it is written down; a dialect that lives in a JSON file is one
/// the probe's rows can be copied into without a release deciding anything on its own. The
/// precedent is <c>ReviewerExecutor</c>'s rate-limit phrases: nothing goes in that has not been read
/// off a real vendor answer.</para>
/// <para>A <c>null</c> number means the field is not sent at all — never sent as JSON <c>null</c>,
/// which a strict endpoint refuses as a type error.</para>
/// </remarks>
/// <param name="Name">The row's key: <c>local</c>, <c>openai</c>, later a measured vendor.</param>
/// <param name="Temperature">Sent as <c>temperature</c> when present.</param>
/// <param name="Seed">Whether the per-prompt seed travels as <c>seed</c>.</param>
/// <param name="FrequencyPenalty">Sent as <c>frequency_penalty</c> when present.</param>
/// <param name="MaxTokensField">The ceiling's field name — <c>max_tokens</c> or
/// <c>max_completion_tokens</c> — or empty to send no ceiling.</param>
/// <param name="ResponseFormat"><c>json_schema</c> demands the shape; <c>json_object</c> only asks.</param>
/// <param name="Strict">The <c>strict</c> flag inside a <c>json_schema</c> response format.</param>
/// <param name="BoundedSchema">Whether the finding schema's free-text fields are bounded
/// (<c>maxLength</c>, <c>maxItems</c>) before they are sent. Local engines enforce the bound through
/// grammar-constrained decoding; OpenAI's strict mode rejects <c>maxLength</c> with a 400.</param>
/// <param name="ReasoningEffortField">Where the effort travels, or empty to send none.</param>
/// <param name="ReasoningEffortMap">A translation applied before the effort is sent; an empty
/// translation omits the field. A value not in the map is sent verbatim.</param>
public sealed record ApiDialect(
    string Name,
    double? Temperature,
    bool Seed,
    double? FrequencyPenalty,
    string MaxTokensField,
    string ResponseFormat,
    bool Strict,
    bool BoundedSchema,
    string ReasoningEffortField,
    IReadOnlyDictionary<string, string> ReasoningEffortMap)
{
    /// <summary>The panel's word for "send nothing about effort": the endpoint's own default applies.</summary>
    public const string EngineDecides = "engine";

    /// <summary>
    /// The effort value to send for what the panel configured — or empty, meaning the field is left out.
    /// </summary>
    /// <remarks>
    /// Three ways to send nothing, and they are all the same decision: the panel said nothing, the panel
    /// said <c>engine</c>, or this dialect maps the word to an empty translation. The field is then
    /// ABSENT rather than set to a value this build guessed would be neutral — the same rule the local
    /// body has always followed.
    /// </remarks>
    public string EffortToSend(string configured)
    {
        var effort = configured.Trim();
        if (effort.Length == 0
            || ReasoningEffortField.Length == 0
            || string.Equals(effort, EngineDecides, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return ReasoningEffortMap.TryGetValue(effort, out var translated) ? translated : effort;
    }

    /// <summary>One row, read out of the shared file's <c>dialects</c> object.</summary>
    /// <exception cref="JsonException">A field is missing or of the wrong kind — a broken build, said loudly.</exception>
    internal static ApiDialect From(string name, JsonElement row)
    {
        if (row.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException($"dialect '{name}' is not an object");
        }

        return new ApiDialect(
            name,
            OptionalNumber(row, name, "temperature"),
            RequiredBool(row, name, "seed"),
            OptionalNumber(row, name, "frequencyPenalty"),
            RequiredString(row, name, "maxTokensField"),
            RequiredString(row, name, "responseFormat"),
            RequiredBool(row, name, "strict"),
            RequiredBool(row, name, "boundedSchema"),
            RequiredString(row, name, "reasoningEffortField"),
            Map(row, name, "reasoningEffortMap"));
    }

    private static double? OptionalNumber(JsonElement row, string name, string field) =>
        Field(row, name, field) switch
        {
            { ValueKind: JsonValueKind.Null } => null,
            { ValueKind: JsonValueKind.Number } number => number.GetDouble(),
            _ => throw new JsonException($"dialect '{name}': '{field}' must be a number or null"),
        };

    private static bool RequiredBool(JsonElement row, string name, string field) =>
        Field(row, name, field) switch
        {
            { ValueKind: JsonValueKind.True } => true,
            { ValueKind: JsonValueKind.False } => false,
            _ => throw new JsonException($"dialect '{name}': '{field}' must be true or false"),
        };

    private static string RequiredString(JsonElement row, string name, string field) =>
        Field(row, name, field) is { ValueKind: JsonValueKind.String } text
            ? text.GetString() ?? string.Empty
            : throw new JsonException($"dialect '{name}': '{field}' must be a string");

    private static IReadOnlyDictionary<string, string> Map(JsonElement row, string name, string field)
    {
        var element = Field(row, name, field);
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException($"dialect '{name}': '{field}' must be an object");
        }

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in element.EnumerateObject())
        {
            map[entry.Name] = entry.Value.ValueKind == JsonValueKind.String
                ? entry.Value.GetString() ?? string.Empty
                : throw new JsonException($"dialect '{name}': '{field}.{entry.Name}' must be a string");
        }

        return map;
    }

    private static JsonElement Field(JsonElement row, string name, string field) =>
        row.TryGetProperty(field, out var value)
            ? value
            : throw new JsonException($"dialect '{name}' has no '{field}'");
}
