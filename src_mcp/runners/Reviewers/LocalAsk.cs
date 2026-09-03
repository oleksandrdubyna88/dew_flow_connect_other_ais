using System.Text;
using System.Text.Json;
using CoaiMcp.Core.Findings;

namespace CoaiMcp.Runners.Reviewers;

/// <summary>
/// The `--ask-local` shim: one completion against a local OpenAI-compatible endpoint.
/// </summary>
/// <remarks>
/// <para>Split from the process that runs it so the two decisions that matter are pure and tested:
/// what the request body says, and what an answer is taken to be.</para>
///
/// <para><b>Two things are pinned and neither is negotiable.</b></para>
///
/// <para><i>Sampling travels in the request.</i> Ollama's `/v1` route substitutes its own defaults
/// over anything a Modelfile sets — learned in `dew_flow_rag_qln`, at the cost of a measurement
/// series nobody could reproduce. So `temperature` and `seed` are always sent, and temperature is
/// zero: a review is not a place for variety.</para>
///
/// <para><i>The schema is demanded, not requested.</i> `response_format: {type: "json_object"}` was
/// tried against the real endpoint and answered with an invented shape — a reviewer that cannot
/// answer IN the schema is reported unparseable, which is a wasted round. Only
/// `{type: "json_schema"}` binds the shape, and it was verified to return well-formed findings.</para>
/// </remarks>
public static class LocalAsk
{
    /// <summary>
    /// How long the shim itself waits, given the deadline the executor will enforce.
    /// </summary>
    /// <remarks>
    /// <para>Shorter than the executor's, deliberately. Two deadlines that disagree mean the shorter
    /// always wins and the longer is decoration — and the shim had a fixed thirty minutes, longer
    /// than any round, so the only real deadline was being killed. A shim that reaches its OWN
    /// deadline exits with a reason the round can print; one that is killed leaves the round
    /// guessing.</para>
    /// <para>The floor matters as much as the margin: subtracting it from a very short reviewer
    /// timeout would produce a negative deadline and fail every local round before it began.</para>
    /// </remarks>
    public static int ShimDeadlineSeconds(TimeSpan reviewerTimeout)
    {
        const int marginSeconds = 10;
        var whole = (int)Math.Round(reviewerTimeout.TotalSeconds);

        return Math.Max(5, whole - marginSeconds);
    }

    /// <summary>The sampling seed for a prompt: the same prompt is the same request, in any process.</summary>
    /// <remarks>
    /// <para>FNV-1a over the UTF-8 bytes, and the choice of algorithm is not the point — being a
    /// function of the BYTES is. This was <c>prompt.GetHashCode()</c>, which .NET randomises per
    /// process, so the seed changed on every run underneath a comment promising that it did not.
    /// Three of the five models in the 2026-09-02 campaign named it.</para>
    /// <para>Unsigned arithmetic throughout, so there is no <c>Math.Abs(int.MinValue)</c> to throw
    /// and no negative seed for an engine to refuse.</para>
    /// </remarks>
    public static int SeedFor(string prompt)
    {
        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;
        var hash = offsetBasis;
        foreach (var b in Encoding.UTF8.GetBytes(prompt))
        {
            hash = (hash ^ b) * prime;
        }

        return (int)(hash % 100_000u);
    }

    /// <summary>The completion request, as the wire wants it.</summary>
    /// <param name="model">Empty is legal — the endpoint picks, which is what "whatever the engine
    /// answers with" means in the panel.</param>
    /// <param name="prompt">The whole review prompt: role, rules, plan or diff.</param>
    /// <param name="schemaJson">The finding schema, verbatim, as the server already writes it to
    /// disk for the CLI reviewers.</param>
    /// <param name="seed">Pinned per round so a re-run of the same round is the same request.</param>
    /// <exception cref="System.Text.Json.JsonException">
    /// The schema does not parse. Thrown rather than worked around, and that is a correction: this
    /// method first fell back to an empty <c>{}</c> schema, which is the same defect as the
    /// <c>json_object</c> fallback refused two paragraphs above — an unconstrained request that a
    /// local model answers with an invented shape, after a full generation has been paid for. The
    /// gate's own reviewers caught the contradiction. A schema that cannot be parsed is a bug on
    /// this side, and failing before the request costs nothing and says so.
    /// </exception>
    public static string RequestBody(string model, string prompt, string schemaJson, int seed, string reasoningEffort = "")
    {
        using var validate = JsonDocument.Parse(schemaJson);

        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream))
        {
            json.WriteStartObject();
            json.WriteString("model", model);
            json.WriteBoolean("stream", false);
            json.WriteNumber("temperature", 0);
            json.WriteNumber("seed", seed);

            // Only when somebody said something. `engine` is the explicit way to send nothing: the
            // field is ABSENT rather than set to a value this build guessed would be neutral, so the
            // engine's own default applies, whatever it is on that version.
            if (reasoningEffort.Length > 0 && !string.Equals(reasoningEffort, "engine", StringComparison.OrdinalIgnoreCase))
            {
                json.WriteString("reasoning_effort", reasoningEffort);
            }

            json.WriteStartArray("messages");
            json.WriteStartObject();
            json.WriteString("role", "user");
            json.WriteString("content", prompt);
            json.WriteEndObject();
            json.WriteEndArray();

            json.WriteStartObject("response_format");
            json.WriteString("type", "json_schema");
            json.WriteStartObject("json_schema");
            json.WriteString("name", "findings");
            json.WriteBoolean("strict", true);
            json.WritePropertyName("schema");
            validate.RootElement.WriteTo(json);
            json.WriteEndObject();
            json.WriteEndObject();

            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// The answer text and what it consumed, or null when the endpoint said nothing usable.
    /// </summary>
    /// <remarks>
    /// Null rather than an empty string: the round already distinguishes "unparseable" from "no
    /// findings", and an engine that returned prose instead of the schema is the first, with its raw
    /// text kept on disk.
    /// </remarks>
    public static (string? Answer, Usage Usage) ReadResponse(string response)
    {
        try
        {
            using var parsed = JsonDocument.Parse(response);
            var root = parsed.RootElement;
            // Valid JSON is not the same as an answer. `[]`, `42`, `null` and a bare string all
            // parse, and `TryGetProperty` on a root that is not an object THROWS
            // `InvalidOperationException` — which the `catch` below, written for `JsonException`,
            // does not catch. An engine answering an array took the round down with it.
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (null, new Usage(0, 0, null));
            }

            var usage = ReadUsage(root);

            if (!root.TryGetProperty("choices", out var choices)
                || choices.ValueKind != JsonValueKind.Array
                || choices.GetArrayLength() == 0)
            {
                return (null, usage);
            }

            var content = choices[0].TryGetProperty("message", out var message)
                          && message.TryGetProperty("content", out var text)
                          && text.ValueKind == JsonValueKind.String
                ? text.GetString()
                : null;

            return (content is { Length: > 0 } ? content : null, usage);
        }
        catch (JsonException)
        {
            return (null, new Usage(0, 0, null));
        }
    }

    private static Usage ReadUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
        {
            return new Usage(0, 0, null);
        }

        // Money is null on purpose: a local run has no bill, and 0 would read as free.
        return new Usage(
            usage.TryGetProperty("prompt_tokens", out var input) && input.TryGetInt64(out var tin) ? tin : 0,
            usage.TryGetProperty("completion_tokens", out var output) && output.TryGetInt64(out var tout) ? tout : 0,
            null);
    }
}
