using System.Globalization;
using System.Text.Json;
using CoaiMcp.Store;

namespace CoaiMcp.Tests;

/// <summary>One assistant line of a fixture as the TEST reads it, straight from the JSON — never through the reader under test.</summary>
internal sealed record RawAssistant(string FileKey, int Line, string MessageId, DateTime Stamp, TokenUsage Tokens);

/// <summary>
/// What a fixture holds, computed by a second, deliberately naive reader, so a count is checked against the bytes and not against the
/// code that made it. It has no gate, no budget and no rule of its own beyond "a synthetic line is not a message".
/// </summary>
internal static class TranscriptTruth
{
    public static IReadOnlyList<RawAssistant> Assistants(CallerTranscriptFixture fixture) =>
        [.. FilesOf(fixture).SelectMany(file => Assistants(fixture, file))];

    /// <summary>The per-term MAXIMUM over every line of one message id — the figure the reader must give.</summary>
    public static TokenUsage MaxOf(IEnumerable<RawAssistant> lines) => new(
        lines.Max(l => l.Tokens.Input),
        lines.Max(l => l.Tokens.CacheCreation),
        lines.Max(l => l.Tokens.CacheRead),
        lines.Max(l => l.Tokens.Output));

    /// <summary>Every parseable stamp of every line of every file of the case.</summary>
    public static IReadOnlyList<DateTime> AllStamps(CallerTranscriptFixture fixture) =>
        [.. FilesOf(fixture).SelectMany(fixture.Lines).Select(StampOrMin).Where(stamp => stamp > DateTime.MinValue)];

    public static IEnumerable<TranscriptFile> FilesOf(CallerTranscriptFixture fixture) =>
        [TranscriptFile.Main, .. fixture.AgentKeys.Select(TranscriptFile.Agent)];

    private static IEnumerable<RawAssistant> Assistants(CallerTranscriptFixture fixture, TranscriptFile file) =>
        fixture.Lines(file).Select((text, index) => Parse(file, index + 1, text)).OfType<RawAssistant>();

    private static RawAssistant? Parse(TranscriptFile file, int number, string text)
    {
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;

        return IsAssistant(root, out var message) && IsCounted(message, out var usage)
            ? new RawAssistant(file.Key, number, message.GetProperty("id").GetString() ?? string.Empty, StampOf(root), UsageOf(usage))
            : null;
    }

    /// <summary>An <c>assistant</c> line that carries a message.</summary>
    private static bool IsAssistant(JsonElement root, out JsonElement message)
    {
        message = default;

        return root.TryGetProperty("type", out var type) && type.GetString() == "assistant" && root.TryGetProperty("message", out message);
    }

    /// <summary>A message with a real model (a synthetic one is named <c>&lt;synthetic&gt;</c>) and a usage block.</summary>
    private static bool IsCounted(JsonElement message, out JsonElement usage)
    {
        usage = default;
        var model = ModelOf(message);

        return model.Length > 0 && model[0] != '<' && message.TryGetProperty("usage", out usage);
    }

    private static string ModelOf(JsonElement message) =>
        message.TryGetProperty("model", out var model) ? model.GetString() ?? string.Empty : string.Empty;

    private static DateTime StampOrMin(string line)
    {
        using var document = JsonDocument.Parse(line);

        return document.RootElement.TryGetProperty("timestamp", out _) ? StampOf(document.RootElement) : DateTime.MinValue;
    }

    private static DateTime StampOf(JsonElement root) =>
        DateTime.Parse(root.GetProperty("timestamp").GetString() ?? string.Empty, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

    private static TokenUsage UsageOf(JsonElement usage) => new(
        usage.GetProperty("input_tokens").GetInt64(),
        usage.GetProperty("cache_creation_input_tokens").GetInt64(),
        usage.GetProperty("cache_read_input_tokens").GetInt64(),
        usage.GetProperty("output_tokens").GetInt64());
}
