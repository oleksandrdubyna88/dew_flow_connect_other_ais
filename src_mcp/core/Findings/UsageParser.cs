using System.Text.Json;

namespace CoaiMcp.Core.Findings;

/// <summary>What one reviewer run consumed. Zeroes mean "the CLI did not say", never "free".</summary>
/// <param name="CostUsd">Only when the CLI itself reported money (claude does); estimating prices
/// for vendors that do not would mean shipping a price table that is wrong within a month.</param>
/// <param name="TokensCached">
/// How many of <paramref name="TokensIn"/> the vendor said it served from its prompt cache — a SUBSET
/// of the input count, never added to it, and zero when the vendor did not say.
/// <para>Recorded per turn of a feature reviewer's conversation (plan D25, S3.2): the follow-up turns
/// resend the base prompt byte for byte so a vendor that caches a prefix can reuse it, and whether one
/// does is what the first live run measures — from this number, not from a belief about the vendor.
/// Trailing and defaulted, so every existing construction and every session already on disk keeps
/// meaning what it meant.</para>
/// </param>
/// <param name="NoPriceSet">
/// A METERED run — an <c>api</c> reviewer, billed per token by its key's vendor — whose row carried no
/// rate, so its money is unknown rather than nothing (PLAN_feature_review.md S3.7). What the rounds log,
/// the audit line and <c>status</c> then say is "no price set", never <c>$0</c>. False for a CLI on a
/// subscription, which was never going to report money in the first place. Trailing and defaulted, so
/// every existing construction and every session on disk keeps meaning what it meant.
/// </param>
/// <param name="TokensReasoning">
/// How many of <paramref name="TokensOut"/> the vendor reported as reasoning
/// (<c>completion_tokens_details.reasoning_tokens</c>), or zero when it did not say. For the record, never
/// for the bill — the bill is <paramref name="TokensOut"/>, which already holds them wherever the vendor
/// filed them (xAI outside <c>completion_tokens</c>, the Alibaba route inside; measured 2026-09-26).
/// Trailing and defaulted, like the two before it.
/// </param>
public sealed record Usage(long TokensIn, long TokensOut, double? CostUsd, long TokensCached = 0, bool NoPriceSet = false, long TokensReasoning = 0)
{
    public static readonly Usage None = new(0, 0, null);

    public Usage Add(Usage other) => new(
        TokensIn + other.TokensIn,
        TokensOut + other.TokensOut,
        CostUsd is null && other.CostUsd is null ? null : (CostUsd ?? 0) + (other.CostUsd ?? 0),
        TokensCached + other.TokensCached,
        NoPriceSet || other.NoPriceSet,
        TokensReasoning + other.TokensReasoning);
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

    // The cached SUBSET, read for its own number and never added to the input: codex's
    // `cached_input_tokens`, the OpenAI-compatible `prompt_tokens_details.cached_tokens`, and
    // claude's `cache_read_input_tokens` — which IS counted as input above, because claude bills it
    // beside `input_tokens`, and is counted here again as the part that was a cache hit.
    private static readonly string[] CachedKeys = ["cached_input_tokens", "cached_tokens", "cache_read_input_tokens"];
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

        return new Usage(
            SumOf(maxima, InputKeys, ScopedInput),
            SumOf(maxima, OutputKeys, ScopedOutput),
            cost,
            SumOf(maxima, CachedKeys, []));
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
        if (InputKeys.Contains(key) || OutputKeys.Contains(key) || CachedKeys.Contains(key))
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
