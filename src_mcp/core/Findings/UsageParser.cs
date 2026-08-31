using System.Text.Json;

namespace CoaiMcp.Core.Findings;

/// <summary>What one reviewer run consumed. Zeroes mean "the CLI did not say", never "free".</summary>
/// <param name="CostUsd">Only when the CLI itself reported money (claude does); estimating prices
/// for vendors that do not would mean shipping a price table that is wrong within a month.</param>
public sealed record Usage(long TokensIn, long TokensOut, double? CostUsd)
{
    public static readonly Usage None = new(0, 0, null);

    public Usage Add(Usage other) => new(
        TokensIn + other.TokensIn,
        TokensOut + other.TokensOut,
        CostUsd is null && other.CostUsd is null ? null : (CostUsd ?? 0) + (other.CostUsd ?? 0));
}

/// <summary>
/// Pulls token counts and cost out of whatever a vendor CLI printed — one JSON object, or a JSONL
/// event stream — without knowing any vendor's exact schema.
/// </summary>
/// <remarks>
/// <para>Deliberately schema-less: codex streams events, gemini wraps an envelope, claude returns
/// one object, and each of them has renamed these fields at least once. The parser walks every
/// JSON object it can find and takes the MAXIMUM per category, because streamed totals are
/// cumulative — the last "input_tokens" is the biggest one, and summing repeats would double-count.</para>
/// <para>Bare key names like <c>prompt</c> or <c>output</c> only count under a parent named
/// <c>tokens</c> or <c>usage</c> — anywhere else they are far too common to trust.</para>
/// </remarks>
public static class UsageParser
{
    // Chosen against the three envelopes actually verified (2026-08-31): claude's result JSON,
    // codex's --json event stream, gemini's -o json stats. Subset keys the vendor already folds
    // into a total (codex `cached_input_tokens`, OpenAI `reasoning`) are deliberately absent —
    // counting them again would double-bill; claude's cache_* are NOT inside input_tokens, so
    // they are counted.
    private static readonly string[] InputKeys =
        ["input_tokens", "prompt_tokens", "prompttokencount", "cache_creation_input_tokens", "cache_read_input_tokens"];
    private static readonly string[] OutputKeys = ["output_tokens", "completion_tokens", "candidatestokencount"];
    private static readonly string[] CostKeys = ["total_cost_usd", "cost_usd", "costusd"];
    private static readonly string[] ScopedInput = ["prompt", "input"];
    private static readonly string[] ScopedOutput = ["candidates", "output", "completion", "thoughts"];
    private static readonly string[] Scopes = ["tokens", "usage"];

    public static Usage Parse(string text)
    {
        // The MAXIMUM per key name, then the sum of those maxima per category: a streamed total
        // (codex repeats a growing input_tokens per event) must not be summed with itself, while
        // distinct kinds (claude's input_tokens + cache_creation_input_tokens) must both count.
        var maxima = new Dictionary<string, long>();
        double? cost = null;

        foreach (var chunk in JsonChunks(text))
        {
            Walk(chunk.RootElement, parent: "", maxima, ref cost);
            chunk.Dispose();
        }

        return new Usage(SumOf(maxima, InputKeys, ScopedInput), SumOf(maxima, OutputKeys, ScopedOutput), cost);
    }

    private static long SumOf(Dictionary<string, long> maxima, string[] keys, string[] scoped) =>
        maxima.Where(m => keys.Contains(m.Key) || scoped.Any(s => m.Key == $"@{s}")).Sum(m => m.Value);

    private static IEnumerable<JsonDocument> JsonChunks(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            yield break;
        }

        // One document, or one document per line — both shapes exist in the wild.
        if (TryParse(trimmed) is { } whole)
        {
            yield return whole;
            yield break;
        }

        foreach (var line in trimmed.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (TryParse(line) is { } doc)
            {
                yield return doc;
            }
        }
    }

    private static JsonDocument? TryParse(string text)
    {
        if (!text.StartsWith('{') && !text.StartsWith('['))
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void Walk(JsonElement element, string parent, Dictionary<string, long> maxima, ref double? cost)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var key = property.Name.ToLowerInvariant();
                    if (property.Value.ValueKind == JsonValueKind.Number)
                    {
                        Classify(key, parent, property.Value, maxima, ref cost);
                    }
                    else
                    {
                        Walk(property.Value, key, maxima, ref cost);
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Walk(item, parent, maxima, ref cost);
                }

                break;
        }
    }

    private static void Classify(string key, string parent, JsonElement value, Dictionary<string, long> maxima, ref double? cost)
    {
        var scoped = Scopes.Contains(parent);
        if (InputKeys.Contains(key) || OutputKeys.Contains(key))
        {
            Record(maxima, key, value);
        }
        else if (scoped && (ScopedInput.Contains(key) || ScopedOutput.Contains(key)))
        {
            // Scoped bare words are kept under a marker so `usage.input` can never collide with
            // an explicit `input_tokens` counted elsewhere in the same document.
            Record(maxima, $"@{key}", value);
        }
        else if (CostKeys.Contains(key))
        {
            cost = Math.Max(cost ?? 0, value.GetDouble());
        }
    }

    private static void Record(Dictionary<string, long> maxima, string key, JsonElement value)
    {
        if (value.TryGetInt64(out var count))
        {
            maxima[key] = Math.Max(maxima.GetValueOrDefault(key), count);
        }
    }
}
