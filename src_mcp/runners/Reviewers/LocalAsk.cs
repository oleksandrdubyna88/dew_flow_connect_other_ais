using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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

    /// <summary>What the shim says when the endpoint refused the connection.</summary>
    /// <remarks>
    /// <para>A formatter rather than an interpolated string at the call site, because
    /// <see cref="VendorDiagnosis"/> reads this sentence back to turn it into a cure — and a message
    /// that is WRITTEN in one file and MATCHED in another by two copies of the same literal is one
    /// rewording away from silently losing the cure. Raised in the code round: "if the server changes
    /// its error message format, the diagnosis will silently fail to match". Now the writer and the
    /// reader share the two constants, so a rename moves both or neither.</para>
    /// </remarks>
    public const string UnreachableOpening = "the local engine at ";

    /// <inheritdoc cref="UnreachableOpening"/>
    public const string UnreachableClosing = " could not be reached";

    /// <inheritdoc cref="UnreachableOpening"/>
    public static string UnreachableMessage(string endpoint, string detail) =>
        $"{UnreachableOpening}{endpoint}{UnreachableClosing}: {detail}";

    /// <summary>
    /// An engine that IS there and did not finish in time — a different sentence, and a different
    /// cure, from one that could not be reached.
    /// </summary>
    /// <remarks>
    /// <para>What it replaced said "did not answer within the round's deadline: The request was
    /// canceled due to the configured HttpClient.Timeout", which reads as a broken engine and sent
    /// the reader to check a port that was healthy. Measured 2026-09-03: two reviewers were reported
    /// that way while the engine was up, loaded and answering — to three requests of the same round
    /// at once, on one card.</para>
    /// <para>So it says the two numbers that matter, how long it waited and what it was allowed, and
    /// then the cures in the order they are worth trying. It names the .NET exception no more: that
    /// text was the one part of the old sentence nobody could act on.</para>
    /// </remarks>
    public static string TooSlowMessage(string endpoint, TimeSpan waited, TimeSpan deadline) =>
        $"{TooSlowOpening}{endpoint}{TooSlowClosing} — it was still working after "
            + $"{waited.TotalSeconds:F0}s of the {deadline.TotalSeconds:F0}s this reviewer was given. "
            + "The engine is up; it is slower than the deadline. Give it more time "
            + "(COAI_REVIEWER_TIMEOUT_MINUTES), a smaller prompt (the Fast context), or a smaller "
            + "model — and check nothing else is on the card: reviewers of one round are serialised "
            + "per engine, other programs are not.";

    /// <summary>
    /// The deadline went entirely on the QUEUE: this reviewer never reached the engine.
    /// </summary>
    /// <remarks>
    /// A different sentence from "the engine did not finish in time" on purpose, because it is a
    /// different problem with a different cure: the card was busy with somebody else's round for
    /// longer than this reviewer was allowed to wait. Reporting it as a slow engine would send the
    /// reader to shrink a prompt that was never sent.
    /// </remarks>
    public static string QueuedOutMessage(string endpoint, TimeSpan deadline) =>
        $"the local engine at {endpoint} was busy for the whole {deadline.TotalSeconds:F0}s this "
            + "reviewer was given, so its question was never asked. One caller uses the card at a "
            + "time, across every window on this machine. Give the reviewers more time "
            + "(COAI_REVIEWER_TIMEOUT_MINUTES), run fewer local roles per round, or point this "
            + "vendor at a second engine.";

    /// <inheritdoc cref="TooSlowMessage"/>
    public const string TooSlowOpening = "the local engine at ";

    /// <inheritdoc cref="TooSlowMessage"/>
    public const string TooSlowClosing = " did not finish in time";

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
    public static string RequestBody(string model, string prompt, string schemaJson, int seed, string reasoningEffort = "", int maxTokens = 8192)
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
            // Greedy decoding loops, and a schema does not save it: a sentence repeated inside a
            // string value stays schema-valid right up to the token that runs out. Measured here on
            // 2026-09-04 — a local reviewer opened with a good finding, collapsed into "The client
            // retries again." for forty kilobytes, and spent 6.7 minutes of the one GPU while every
            // other window queued behind it. The round then reported "not the schema's JSON", which
            // was true and nothing like the story.
            //
            // Small on purpose. A review repeats words legitimately — the file names it is talking
            // about — and a large penalty is an opinion about content rather than a guard against a
            // degenerate loop. Deterministic, so `temperature: 0` and the seed still mean what they
            // meant: the same prompt is still the same request.
            json.WriteNumber("frequency_penalty", 0.2);
            // The ceiling that was missing. Measured 2026-09-03: uncapped, this engine did not
            // finish a one-line question in 90 seconds, and the same question capped at twenty
            // tokens came back in 8.5 — the model does not stop, and nothing else here bounds it.
            // Every local reviewer of a round therefore spent its whole deadline and was reported as
            // a slow engine, which was true and useless.
            if (maxTokens > 0)
            {
                json.WriteNumber("max_tokens", maxTokens);
            }

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
            Bounded(schemaJson).WriteTo(json);
            json.WriteEndObject();
            json.WriteEndObject();

            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>How long a finding's free text may be, in characters, on the local route only.</summary>
    /// <remarks>
    /// <para>A reasoning model can reason in the wrong place. Observed twice on 2026-09-05: the answer
    /// opened as good JSON and then the <c>why</c> field became the model's chain of thought — <i>"The
    /// plan *is* the instruction. The plan says … Is there a violation? Maybe … No. Wait"</i> — for
    /// thirty kilobytes until <c>max_tokens</c> cut it mid-string. The frequency penalty above cannot
    /// touch that; it is not a repeated sentence. The GRAMMAR can: the engine constrains generation to
    /// the schema, and a string with a <c>maxLength</c> is a string the model is forced to close.</para>
    /// <para>A thousand characters is about a hundred and fifty words — more than any finding that
    /// has ever been worth having here used, and a fifth of what the leak spent before the first
    /// sentence ended. The title gets two hundred, because it is one sentence by definition.</para>
    /// <para>Local ONLY. Codex feeds the shared schema to OpenAI's strict structured outputs, which
    /// reject <c>maxLength</c> as an unsupported keyword with a 400 — so the bound is added to the copy
    /// this route sends, never to <c>FindingSchema.Json</c>.</para>
    /// </remarks>
    private static readonly (string Field, int MaxLength)[] FreeTextBounds =
    [
        ("title", 200),
        ("why", 1000),
        ("fix", 1000),
    ];

    /// <summary>
    /// The finding schema with its free-text fields bounded — or any other schema exactly as given.
    /// </summary>
    /// <remarks>
    /// The walker looks for the finding schema's own shape and touches nothing else: a probe or a test
    /// handing this route a schema of another shape must get it back unchanged rather than rewritten
    /// by code that assumed what it was looking at.
    /// </remarks>
    internal static JsonNode Bounded(string schemaJson)
    {
        var root = JsonNode.Parse(schemaJson) ?? throw new JsonException("the schema parsed to nothing");
        if (root["properties"]?["findings"]?["items"]?["properties"] is not JsonObject fields)
        {
            return root;
        }

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
