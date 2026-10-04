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

    /// <summary>
    /// A shipped prompt that is only on or off: paired with a reviewer, it runs on every change and carries no
    /// conditions (research/PLAN_the_security_tab_reads_at_a_glance.md, D1). A catalogue fact, looked up by id — it
    /// is never a settings member, because a 0.41/0.42 server refuses a prompt entry carrying one.
    /// </summary>
    public static bool IsAlways(string id) => AlwaysIds.Contains(id);

    private static readonly HashSet<string> AlwaysIds = [.. Seed.GetProperty("prompts").EnumerateArray()
        .Where(p => p.TryGetProperty("always", out var always) && always.ValueKind == JsonValueKind.True)
        .Select(p => p.GetProperty("id").GetString()!)];

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
