using System.Text.Json;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Core.Security;

namespace CoaiMcp.Server;

public sealed record SecurityRun(string Vendor, string Prompt, string Context, int ContextTokens, IReadOnlyList<string> Stages)
{
    public string Refusal { get; init; } = string.Empty;
    public bool Serves(Stage stage) => Stages.Contains(stage == Stage.CodeReview ? "code" : "feature");
}

/// <summary>One bounded configuration, with named refusals rather than guessed future semantics.</summary>
public sealed record SecurityLaneSetting
{
    public const string Key = "COAI_SECURITY_LANE";
    public bool Enabled { get; init; }
    public int Threshold { get; init; }
    public int MaxRounds { get; init; } = 2;
    public IReadOnlyList<SecurityPrompt> Prompts { get; init; } = SecurityCatalog.Prompts;
    public IReadOnlyList<SecurityRun> Runs { get; init; } = [];
    public IReadOnlyList<string> Complaints { get; init; } = [];
    public bool Applies(Stage stage) => Enabled && stage is Stage.CodeReview or Stage.FeatureReview;

    public static PanelSettings Apply(PanelSettings settings, string? json)
    {
        var lane = Parse(json, settings.Providers);
        return settings with
        {
            SecurityLane = lane,
            Rounds = settings.Rounds with { SecurityLane = lane.Gate },
            UnrecognisedSettings = [.. settings.UnrecognisedSettings,
                .. lane.Complaints.Select(c => new UnrecognisedSetting(Key, c))],
        };
    }

    public RoleGate Gate => new(MaxRounds, Threshold, Enabled);

    public static SecurityLaneSetting Parse(string? json, IReadOnlyList<ProviderSettings> providers)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        if (System.Text.Encoding.UTF8.GetByteCount(json) > 131072) return Refused("configuration exceeds 128 KiB; the lane is off");
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            return Read(doc.RootElement, providers);
        }
        catch (JsonException)
        {
            return Refused("invalid JSON; the lane is off");
        }
    }

    private static SecurityLaneSetting Refused(string reason) => new() { Complaints = [$"{Key}: {reason}"] };

    private static SecurityLaneSetting Read(JsonElement root, IReadOnlyList<ProviderSettings> providers)
    {
        var problem = Members(root, ["enabled", "threshold", "maxRounds", "prompts", "runs"]);
        if (problem.Length > 0) return Refused(problem);
        foreach (var name in new[] { "prompts", "runs" })
            if (root.TryGetProperty(name, out var list) && list.ValueKind != JsonValueKind.Array)
                return Refused($"{name} must be an array; the lane is off");
        if (root.TryGetProperty("enabled", out var enabled) && enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            return Refused("enabled must be true or false");
        if (!Number(root, "threshold", 0, 0, 100, out var threshold) || !Number(root, "maxRounds", 2, 1, 10, out var rounds))
            return Refused("threshold must be 0..100 and maxRounds 1..10");
        var complaints = new List<string>();
        var prompts = ReadPrompts(root, complaints);
        var runs = ReadRuns(root, providers, prompts, complaints);
        return new()
        {
            Enabled = enabled.ValueKind == JsonValueKind.True,
            Threshold = threshold,
            MaxRounds = rounds,
            Prompts = prompts,
            Runs = runs,
            Complaints = [.. complaints.Select(c => $"{Key}: {c}")],
        };
    }

    private static IReadOnlyList<SecurityPrompt> ReadPrompts(JsonElement root, List<string> complaints)
    {
        var library = SecurityCatalog.Prompts.ToDictionary(p => p.Id, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in Entries(root, "prompts", SecurityCatalog.MostPrompts, complaints))
        {
            var id = Text(entry, "id");
            if (!SecurityCatalog.IsPromptId(id) || !seen.Add(id))
            {
                complaints.Add($"prompt '{id}' must have a unique redteam- slug");
                continue;
            }
            if (!library.ContainsKey(id) && library.Count >= SecurityCatalog.MostPrompts)
            {
                complaints.Add($"prompt library is limited to {SecurityCatalog.MostPrompts}");
                continue;
            }
            library[id] = ReadPrompt(entry, library.GetValueOrDefault(id, new(id, [], [])), complaints);
        }
        return [.. library.Values];
    }

    private static SecurityPrompt ReadPrompt(JsonElement entry, SecurityPrompt seed, List<string> complaints)
    {
        var refusal = Members(entry, ["id", "triggers", "focus"]);
        var triggers = Tags(entry, "triggers", seed.Triggers, out var badTriggers);
        var focus = Tags(entry, "focus", seed.Focus, out var badFocus);
        var unknown = triggers.Where(t => !SecurityCatalog.Signals.Any(s => s.Id == t && s.Trigger)).ToArray();
        if (SecurityCatalog.IsPreset(seed.Id) && triggers.Count == 0) refusal = "a preset requires at least one trigger; select a condition before enabling it";
        if (badTriggers || unknown.Length > 0) refusal = "unknown or invalid trigger; update this server or correct the trigger";
        if (badFocus || focus.Any(t => !SecurityCatalog.Signals.Any(s => s.Id == t)))
            complaints.Add($"{seed.Id}: unknown focus tags ignored");
        if (refusal.Length > 0) complaints.Add($"{seed.Id}: {refusal}");
        return seed with { Triggers = triggers, Focus = [.. focus.Where(t => SecurityCatalog.Signals.Any(s => s.Id == t))], Refusal = refusal };
    }

    private static IReadOnlyList<SecurityRun> ReadRuns(JsonElement root, IReadOnlyList<ProviderSettings> providers,
        IReadOnlyList<SecurityPrompt> prompts, List<string> complaints)
    {
        var result = new List<SecurityRun>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Entries(root, "runs", SecurityCatalog.MostRuns, complaints))
        {
            var vendor = Text(entry, "vendor");
            var prompt = Text(entry, "prompt");
            var provider = providers.FirstOrDefault(p => p.Provider.Equals(vendor, StringComparison.OrdinalIgnoreCase));
            if (provider is null || !prompts.Any(p => p.Id == prompt))
            {
                complaints.Add($"pair '{vendor}/{prompt}' names an unknown reviewer or prompt and was dropped");
                continue;
            }
            if (!seen.Add($"{vendor}/{prompt}"))
            {
                complaints.Add($"duplicate pair '{vendor}/{prompt}' dropped; use different prompts for different runs");
                continue;
            }
            var run = ReadRun(entry, provider, prompt);
            if (run.Refusal.Length > 0) complaints.Add($"{vendor}/{prompt}: {run.Refusal}");
            result.Add(run);
        }
        return result;
    }

    private static SecurityRun ReadRun(JsonElement entry, ProviderSettings provider, string prompt)
    {
        var refusal = Members(entry, ["vendor", "prompt", "context", "contextTokens", "stages"]);
        var context = Text(entry, "context");
        if (!entry.TryGetProperty("context", out _)) context = provider.Runtime == "local" ? "slice" : "diff";
        if (context is not ("slice" or "diff")) refusal = "context must be slice or diff";
        if (!Number(entry, "contextTokens", context == "slice" ? 24000 : 200000, 1024, 200000, out var tokens))
            refusal = "contextTokens must be 1024..200000";
        var stages = Tags(entry, "stages", ["code", "feature"], out var badStages);
        if (badStages || stages.Any(s => s is not ("code" or "feature"))) refusal = "stages must contain only code and feature";
        return new(provider.Provider, prompt, context, tokens, stages) { Refusal = refusal };
    }

    private static IEnumerable<JsonElement> Entries(JsonElement root, string name, int limit, List<string> complaints)
    {
        if (!root.TryGetProperty(name, out var entries)) return [];
        if (entries.ValueKind != JsonValueKind.Array)
        {
            complaints.Add($"{name} must be an array; no entries read");
            return [];
        }
        if (entries.GetArrayLength() > limit) complaints.Add($"{name} is limited to {limit}; the tail was dropped");
        return entries.EnumerateArray().Take(limit).ToArray();
    }

    private static string Members(JsonElement row, IReadOnlyList<string> allowed)
    {
        if (row.ValueKind != JsonValueKind.Object) return "expected an object";
        var names = row.EnumerateObject().Select(p => p.Name).ToArray();
        if (names.Distinct(StringComparer.Ordinal).Count() != names.Length) return "duplicate JSON members are not allowed";
        var unknown = names.Except(allowed, StringComparer.Ordinal).ToArray();
        return unknown.Length == 0 ? string.Empty : $"unknown members {string.Join(", ", unknown)}; update this server";
    }

    private static string Text(JsonElement row, string name) => row.ValueKind == JsonValueKind.Object
        && row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : string.Empty;

    private static bool Number(JsonElement row, string name, int fallback, int min, int max, out int number)
    {
        number = fallback;
        if (!row.TryGetProperty(name, out var value)) return true;
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out number) && number >= min && number <= max;
    }

    private static IReadOnlyList<string> Tags(JsonElement row, string name, IReadOnlyList<string> fallback, out bool invalid)
    {
        invalid = false;
        if (!row.TryGetProperty(name, out var values)) return fallback;
        if (values.ValueKind != JsonValueKind.Array) { invalid = true; return []; }
        var items = values.EnumerateArray().ToArray();
        invalid = items.Any(v => v.ValueKind != JsonValueKind.String);
        return [.. items.Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!).Distinct(StringComparer.Ordinal)];
    }
}
