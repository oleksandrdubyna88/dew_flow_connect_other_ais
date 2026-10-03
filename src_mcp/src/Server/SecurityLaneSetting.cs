using System.Text.Json;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Core.Security;

namespace CoaiMcp.Server;

public sealed record SecurityRun(string Vendor, string Prompt, string Context, int ContextTokens, IReadOnlyList<string> Stages)
{
    public string Refusal { get; init; } = string.Empty;
    public bool Serves(Stage stage) => Stages.Contains(SecurityStages.Of(stage));
}

/// <summary>One bounded configuration, with named refusals rather than guessed future semantics.</summary>
public sealed record SecurityLaneSetting
{
    public const string Key = "COAI_SECURITY_LANE";

    /// <summary>The member the extension preserves a malformed <c>coai.securityLane</c> under.</summary>
    private const string Preserved = "invalidConfiguration";
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

    /// <summary>What is wrong with a pairing's own configuration, or empty — a fault to repair, never "not due".</summary>
    public string ConfigurationRefusal(SecurityRun run) =>
        run.Refusal.Length > 0 ? run.Refusal : Prompts.First(p => p.Id == run.Prompt).Refusal;

    /// <summary>The pairings this stage's configuration can run: the lane on, the stage served, nothing broken.</summary>
    /// <remarks>The settings' half of the decision only; what a round adds — its budget, the reviewer rows — is
    /// <see cref="SecurityRoster.Due"/>'s.</remarks>
    public IReadOnlyList<SecurityRun> Configured(Stage stage) => Applies(stage)
        ? [.. Runs.Where(r => r.Serves(stage) && ConfigurationRefusal(r).Length == 0)]
        : [];

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

    // The extension keeps a setting it could not read under this member and sends the lane off: a
    // fault in the operator's settings, which "update this server" would send them to cure in vain.
    private static string RootProblem(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(Preserved, out _)
            ? "the extension found the security lane configuration malformed; the lane is off until coai.securityLane is corrected"
            : Members(root, ["enabled", "threshold", "maxRounds", "prompts", "runs"]);

    private static SecurityLaneSetting Read(JsonElement root, IReadOnlyList<ProviderSettings> providers)
    {
        var shell = Shell(root);
        // A refused root carries its one complaint and nothing else; an accepted one carries none yet.
        if (shell.Complaints.Count > 0) return shell;
        var complaints = new List<string>();
        var prompts = ReadPrompts(root, complaints);
        var runs = ReadRuns(root, providers, prompts, complaints);
        return shell with
        {
            Prompts = prompts,
            Runs = runs,
            Complaints = [.. complaints.Select(c => $"{Key}: {c}")],
        };
    }

    /// <summary>The root's own members (switch, threshold, rounds), or the refusal that turns the lane off.</summary>
    private static SecurityLaneSetting Shell(JsonElement root)
    {
        var problem = RootRefusal(root);
        if (problem.Length > 0) return Refused(problem);
        if (!Number(root, "threshold", 0, 0, 100, out var threshold) || !Number(root, "maxRounds", 2, 1, 10, out var rounds))
            return Refused("threshold must be 0..100 and maxRounds 1..10");
        return new() { Enabled = IsTrue(root, "enabled"), Threshold = threshold, MaxRounds = rounds };
    }

    private static string RootRefusal(JsonElement root)
    {
        var problem = RootProblem(root);
        if (problem.Length > 0) return problem;
        var list = ListProblem(root);
        if (list.Length > 0) return list;
        return IsBooleanOrAbsent(root, "enabled") ? string.Empty : "enabled must be true or false";
    }

    /// <summary>The members that must be arrays when present, in the order a refusal names them.</summary>
    private static readonly string[] ListMembers = ["prompts", "runs"];

    private static string ListProblem(JsonElement root) =>
        ListMembers.Where(name => IsPresentButNotArray(root, name))
            .Select(name => $"{name} must be an array; the lane is off").FirstOrDefault() ?? string.Empty;

    private static bool IsPresentButNotArray(JsonElement row, string name) =>
        row.TryGetProperty(name, out var list) && list.ValueKind != JsonValueKind.Array;

    private static bool IsBooleanOrAbsent(JsonElement row, string name) =>
        !row.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.True or JsonValueKind.False;

    private static bool IsTrue(JsonElement row, string name) =>
        row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static IReadOnlyList<SecurityPrompt> ReadPrompts(JsonElement root, List<string> complaints)
    {
        var library = SecurityCatalog.Prompts.ToDictionary(p => p.Id, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in Entries(root, "prompts", SecurityCatalog.MostPrompts, complaints))
            AddPrompt(entry, library, seen, complaints);
        return [.. library.Values];
    }

    private static void AddPrompt(JsonElement entry, Dictionary<string, SecurityPrompt> library, HashSet<string> seen,
        List<string> complaints)
    {
        var id = Text(entry, "id");
        var refusal = PromptEntryRefusal(id, library, seen);
        if (refusal.Length > 0)
        {
            complaints.Add(refusal);
            return;
        }
        library[id] = ReadPrompt(entry, library.GetValueOrDefault(id, new(id, [], [])), complaints);
    }

    /// <summary>Why an entry cannot join the library at all; a valid slug is claimed even when the library is full.</summary>
    private static string PromptEntryRefusal(string id, Dictionary<string, SecurityPrompt> library, HashSet<string> seen)
    {
        if (!IsNewSlug(id, seen)) return $"prompt '{id}' must have a unique redteam- slug";
        return IsLibraryFull(id, library) ? $"prompt library is limited to {SecurityCatalog.MostPrompts}" : string.Empty;
    }

    private static bool IsNewSlug(string id, HashSet<string> seen) => SecurityCatalog.IsPromptId(id) && seen.Add(id);

    private static bool IsLibraryFull(string id, Dictionary<string, SecurityPrompt> library) =>
        !library.ContainsKey(id) && library.Count >= SecurityCatalog.MostPrompts;

    private static SecurityPrompt ReadPrompt(JsonElement entry, SecurityPrompt seed, List<string> complaints)
    {
        var triggers = Tags(entry, "triggers", seed.Triggers, out var badTriggers);
        var focus = Tags(entry, "focus", seed.Focus, out var badFocus);
        var refusal = PromptRefusal(entry, seed.Id, triggers, badTriggers);
        if (badFocus || focus.Any(t => !IsSignal(t))) complaints.Add($"{seed.Id}: unknown focus tags ignored");
        if (refusal.Length > 0) complaints.Add($"{seed.Id}: {refusal}");
        return seed with { Triggers = triggers, Focus = [.. focus.Where(IsSignal)], Refusal = refusal };
    }

    /// <summary>
    /// The one refusal a prompt entry carries when several apply: an invalid trigger outranks a preset left
    /// without one, which outranks a malformed member list.
    /// </summary>
    private static string PromptRefusal(JsonElement entry, string id, IReadOnlyList<string> triggers, bool badTriggers)
    {
        if (HasInvalidTrigger(triggers, badTriggers)) return "unknown or invalid trigger; update this server or correct the trigger";
        if (IsPresetWithoutTrigger(id, triggers)) return "a preset requires at least one trigger; select a condition before enabling it";
        return Members(entry, ["id", "triggers", "focus"]);
    }

    private static bool HasInvalidTrigger(IReadOnlyList<string> triggers, bool invalid) => invalid || triggers.Any(t => !IsTrigger(t));

    private static bool IsPresetWithoutTrigger(string id, IReadOnlyList<string> triggers) =>
        SecurityCatalog.IsPreset(id) && triggers.Count == 0;

    private static bool IsTrigger(string tag) => SecurityCatalog.Signals.Any(s => s.Id == tag && s.Trigger);

    private static bool IsSignal(string tag) => SecurityCatalog.Signals.Any(s => s.Id == tag);

    private static List<SecurityRun> ReadRuns(JsonElement root, IReadOnlyList<ProviderSettings> providers,
        IReadOnlyList<SecurityPrompt> prompts, List<string> complaints)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return [.. Entries(root, "runs", SecurityCatalog.MostRuns, complaints)
            .SelectMany(entry => Pair(entry, providers, prompts, seen, complaints))];
    }

    /// <summary>One run entry: the pairing it names, or nothing, with the complaint that says why.</summary>
    private static SecurityRun[] Pair(JsonElement entry, IReadOnlyList<ProviderSettings> providers,
        IReadOnlyList<SecurityPrompt> prompts, HashSet<string> seen, List<string> complaints)
    {
        var vendor = Text(entry, "vendor");
        var prompt = Text(entry, "prompt");
        var provider = providers.FirstOrDefault(p => p.Provider.Equals(vendor, StringComparison.OrdinalIgnoreCase));
        if (provider is null || !prompts.Any(p => p.Id == prompt))
        {
            complaints.Add($"pair '{vendor}/{prompt}' names an unknown reviewer or prompt and was dropped");
            return [];
        }
        return UniquePair(entry, provider, vendor, prompt, seen, complaints);
    }

    private static SecurityRun[] UniquePair(JsonElement entry, ProviderSettings provider, string vendor, string prompt,
        HashSet<string> seen, List<string> complaints)
    {
        if (!seen.Add($"{vendor}/{prompt}"))
        {
            complaints.Add($"duplicate pair '{vendor}/{prompt}' dropped; use different prompts for different runs");
            return [];
        }
        var run = ReadRun(entry, provider, prompt);
        if (run.Refusal.Length > 0) complaints.Add($"{vendor}/{prompt}: {run.Refusal}");
        return [run];
    }

    private static SecurityRun ReadRun(JsonElement entry, ProviderSettings provider, string prompt)
    {
        var context = ContextOf(entry, provider);
        var tokensValid = Number(entry, "contextTokens", DefaultTokens(context), 1024, 200000, out var tokens);
        var stages = Tags(entry, "stages", [SecurityStages.Code, SecurityStages.Feature], out var badStages);
        var refusal = RunRefusal(entry, context, tokensValid, AreStages(stages, badStages));
        return new(provider.Provider, prompt, context, tokens, stages) { Refusal = refusal };
    }

    private static string ContextOf(JsonElement entry, ProviderSettings provider)
    {
        if (entry.TryGetProperty("context", out _)) return Text(entry, "context");
        return provider.Runtime == "local" ? SecurityContextModes.Slice : SecurityContextModes.Diff;
    }

    private static int DefaultTokens(string context) => context == SecurityContextModes.Slice ? 24000 : 200000;

    private static bool AreStages(IReadOnlyList<string> stages, bool invalid) =>
        !invalid && stages.All(s => s is SecurityStages.Code or SecurityStages.Feature);

    /// <summary>
    /// The one refusal a run carries when several apply: stages outrank the token budget, which outranks the
    /// context mode, which outranks a malformed member list.
    /// </summary>
    private static string RunRefusal(JsonElement entry, string context, bool tokensValid, bool stagesValid)
    {
        if (!stagesValid) return $"stages must contain only {SecurityStages.Code} and {SecurityStages.Feature}";
        if (!tokensValid) return "contextTokens must be 1024..200000";
        if (context is not (SecurityContextModes.Slice or SecurityContextModes.Diff))
            return $"context must be {SecurityContextModes.Slice} or {SecurityContextModes.Diff}";
        return Members(entry, ["vendor", "prompt", "context", "contextTokens", "stages"]);
    }

    private static JsonElement[] Entries(JsonElement root, string name, int limit, List<string> complaints)
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
        return IsInt32(value, ref number) && number >= min && number <= max;
    }

    /// <summary>A value that is not a number keeps the fallback; a number that is no Int32 reads as zero, as TryGetInt32 leaves it.</summary>
    private static bool IsInt32(JsonElement value, ref int number) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out number);

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
