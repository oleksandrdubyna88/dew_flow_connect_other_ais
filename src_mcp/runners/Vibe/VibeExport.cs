using System.Text.Json;
using CoaiMcp.Core.Findings;

namespace CoaiMcp.Runners.Vibe;

/// <summary>
/// What one Vibe run said about itself in <c>--output-dir</c>'s <c>export.json</c> (D7, D8 of
/// <c>todo/PLAN_vibe_and_kimi_runtimes.md</c>) — read, never trusted to be there.
/// </summary>
/// <param name="Exists">Whether an export was written and could be read as a JSON object.</param>
/// <param name="Outcome"><c>finished</c>, <c>deadline</c>, <c>config_error</c> … — what coai reads, never <c>stop_reason</c>, which is null on a deadline (§18).</param>
/// <param name="Error">The export's <c>error.message</c>, or empty.</param>
/// <param name="ActiveModel">The <c>config.active_model</c> Vibe ran with — empty when absent (a deadline's export has no config, §18).</param>
/// <param name="Usage">Its <c>usage</c>; <see cref="Core.Findings.Usage.Unknown"/> when null or absent, never zero (§18: null in 2 of 3).</param>
public sealed record VibeExport(bool Exists, string Outcome, string Error, string ActiveModel, Usage Usage)
{
    /// <summary>The export file's name inside <c>--output-dir</c>.</summary>
    public const string FileName = "export.json";

    /// <summary>No export at all.</summary>
    public static VibeExport Missing { get; } = new(false, string.Empty, string.Empty, string.Empty, Usage.Unknown);

    /// <summary>The export in <paramref name="folder"/>, or <see cref="Missing"/> — never an exception.</summary>
    public static VibeExport In(string folder)
    {
        try
        {
            var path = Path.Combine(folder, FileName);

            return File.Exists(path) ? Parse(File.ReadAllText(path)) : Missing;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Missing;
        }
    }

    /// <summary>An export's text, read — or <see cref="Missing"/> for anything that is not a JSON object.</summary>
    public static VibeExport Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            return root.ValueKind == JsonValueKind.Object
                ? new VibeExport(true, Text(root, "outcome"), Message(root), Model(root), UsageOf(root))
                : Missing;
        }
        catch (JsonException)
        {
            return Missing;
        }
    }

    /// <summary>
    /// The run's tokens as Vibe reports them. Cost is never carried (D9): <c>cost_usd</c> is Vibe's list price even on a
    /// subscription, and a numeric cost is read downstream as a bill. Whether <c>cached_input_tokens</c> sits inside
    /// <c>input_tokens</c> is NOT settled (§9: it was 0 in every export measured), so the input is taken as reported and
    /// the cached count is carried beside it, as the cached subset it is named as — never added.
    /// </summary>
    private static Usage UsageOf(JsonElement root) =>
        root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object
            ? new Usage(Number(usage, "input_tokens"), Number(usage, "output_tokens"), CostUsd: null, Number(usage, "cached_input_tokens"))
            : Usage.Unknown;

    private static string Model(JsonElement root) =>
        root.TryGetProperty("config", out var config) && config.ValueKind == JsonValueKind.Object ? Text(config, "active_model") : string.Empty;

    private static string Message(JsonElement root) =>
        root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object ? Text(error, "message") : string.Empty;

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    private static long Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number : 0;
}

/// <summary>
/// The answer out of <c>--output json</c>'s stdout: an array of session entries, the answer being the LAST
/// <c>role: assistant</c> message's text (D7; the shape in research/RESULTS_vibe_confinement.md's probe runs).
/// </summary>
public static class VibeStdout
{
    /// <summary>The last assistant message's text, or null when the array holds none or is not an array.</summary>
    public static string? Answer(string stdout)
    {
        try
        {
            using var document = JsonDocument.Parse(stdout);

            return document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.EnumerateArray().Select(AssistantText).LastOrDefault(text => text.Length > 0)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The text of one entry when it is an assistant message; empty for anything else.</summary>
    private static string AssistantText(JsonElement entry) =>
        entry.ValueKind == JsonValueKind.Object && Is(entry, "role", "assistant") && entry.TryGetProperty("content", out var content)
            ? Joined(content)
            : string.Empty;

    private static bool Is(JsonElement entry, string name, string value) =>
        entry.TryGetProperty(name, out var found) && found.ValueKind == JsonValueKind.String && found.ValueEquals(value);

    /// <summary>A content that is a string, or an array of parts whose <c>text</c> are joined.</summary>
    private static string Joined(JsonElement content) => content.ValueKind switch
    {
        JsonValueKind.String => content.GetString() ?? string.Empty,
        JsonValueKind.Array => string.Concat(content.EnumerateArray().Select(PartText)),
        _ => string.Empty,
    };

    private static string PartText(JsonElement part) =>
        part.ValueKind == JsonValueKind.Object && part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String
            ? text.GetString() ?? string.Empty
            : string.Empty;
}
