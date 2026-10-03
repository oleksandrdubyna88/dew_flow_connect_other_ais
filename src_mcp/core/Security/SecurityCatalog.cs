using System.Text.Json;

namespace CoaiMcp.Core.Security;

public sealed record SecuritySignal(string Id, string Label, bool Trigger);
public sealed record SecurityPrompt(string Id, IReadOnlyList<string> Triggers, IReadOnlyList<string> Focus)
{
    public string Refusal { get; init; } = string.Empty;
}

/// <summary>The seed shared by settings and detection; prompt bodies belong to the operator.</summary>
public static class SecurityCatalog
{
    public const string Gate = "lane:security";
    public const int MostPrompts = 32;
    public const int MostRuns = 16;
    private static readonly JsonElement Seed = ReadSeed();
    public static IReadOnlyList<SecuritySignal> Signals { get; } = [.. Seed.GetProperty("signals").EnumerateArray()
        .Select(s => new SecuritySignal(s.GetProperty("id").GetString()!, s.GetProperty("label").GetString()!, s.GetProperty("trigger").GetBoolean()))];
    public static IReadOnlyList<SecurityPrompt> Prompts { get; } = [.. Seed.GetProperty("prompts").EnumerateArray()
        .Select(p => new SecurityPrompt(p.GetProperty("id").GetString()!, Strings(p, "triggers"), Strings(p, "focus")))];

    public static bool IsPromptId(string id) => id.Length is > 8 and <= 80
        && id.StartsWith("redteam-", StringComparison.Ordinal)
        && id.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

    public static bool IsPreset(string id) => Prompts.Any(p => p.Id == id);

    private static IReadOnlyList<string> Strings(JsonElement row, string field) =>
        [.. row.GetProperty(field).EnumerateArray().Select(x => x.GetString()!)];

    private static JsonElement ReadSeed()
    {
        using var stream = typeof(SecurityCatalog).Assembly.GetManifestResourceStream("CoaiMcp.Core.security-lane.json")
            ?? throw new InvalidOperationException("The security lane catalog is not embedded in this build.");
        using var doc = JsonDocument.Parse(stream);
        return doc.RootElement.Clone();
    }
}
