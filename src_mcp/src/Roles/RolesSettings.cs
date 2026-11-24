using System.Text.Encodings.Web;
using System.Text.Json;
using CoaiMcp.Core.Roles;

namespace CoaiMcp.Roles;

internal static class RolesJson
{
    internal static readonly JsonWriterOptions Options = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
}

/// <summary>
/// A person's <c>COAI_ROLES_*</c> settings read one KEY at a time: a key that reads replaces its default, a key that does not read leaves its default and says why. Each reader is pure.
/// </summary>
internal static class RolesSettings
{
    private const int TopBand = 10;

    internal static List<(string Role, IReadOnlyList<ModelRef> Models)> Models(
        string? text, List<(string Role, IReadOnlyList<ModelRef> Models)> defaults, List<RoleSettingProblem> problems)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return defaults;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                problems.Add(Problem(RolesKeys.Models, "it is not a JSON object of role name to a list of {model, vendor}"));

                return defaults;
            }

            var chosen = new Dictionary<string, IReadOnlyList<ModelRef>>(StringComparer.Ordinal);
            foreach (var role in document.RootElement.EnumerateObject())
            {
                if (!RoleDefaults.RoleNames.Contains(role.Name))
                {
                    problems.Add(Problem(RolesKeys.Models, $"'{role.Name}' is not a role (the roles are {string.Join(", ", RoleDefaults.RoleNames)}); its list is left out"));
                }
                else if (ModelList(role.Value) is { } list)
                {
                    if (list.Select(model => VendorRouting.Refusal(model.Model, model.Vendor)).FirstOrDefault(refusal => refusal.Length > 0) is { } refusal)
                    {
                        problems.Add(Problem(RolesKeys.Models, $"the list of '{role.Name}' breaks the vendor-routing rule — {refusal}; its default stands"));
                    }
                    else
                    {
                        chosen[role.Name] = list;
                    }
                }
                else
                {
                    problems.Add(Problem(RolesKeys.Models, $"the list of '{role.Name}' is not a non-empty list of {{\"model\":\"…\",\"vendor\":\"…\"}}; its default stands"));
                }
            }

            return [.. defaults.Select(role => chosen.TryGetValue(role.Role, out var models) ? (role.Role, models) : role)];
        }
        catch (JsonException e)
        {
            problems.Add(Problem(RolesKeys.Models, $"it is not valid JSON ({e.Message})"));

            return defaults;
        }
    }

    private static IReadOnlyList<ModelRef>? ModelList(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0)
        {
            return null;
        }

        var models = new List<ModelRef>();
        foreach (var entry in value.EnumerateArray())
        {
            if (ModelOf(entry) is not { } model)
            {
                return null;
            }

            models.Add(model);
        }

        return models;
    }

    private static ModelRef? ModelOf(JsonElement entry) =>
        entry.ValueKind == JsonValueKind.Object && Text(entry, "model") is { Length: > 0 } model && Text(entry, "vendor") is { Length: > 0 } vendor
            ? new ModelRef(model, vendor)
            : null;

    internal static IReadOnlyList<Band> Bands(string? text, IReadOnlyList<Band> defaults, List<RoleSettingProblem> problems)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return defaults;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                problems.Add(Problem(RolesKeys.Bands, "it is not a JSON list of {min, max, model, vendor}"));

                return defaults;
            }

            var bands = new List<Band>();
            foreach (var row in document.RootElement.EnumerateArray())
            {
                if (BandOf(row) is not { } band)
                {
                    problems.Add(Problem(RolesKeys.Bands, $"a row is not {{\"min\":0-{TopBand},\"max\":0-{TopBand},\"model\":\"…\",\"vendor\":\"…\"}} with min not above max; the default bands stand"));

                    return defaults;
                }

                if (VendorRouting.Refusal(band.Model, band.Vendor) is { Length: > 0 } refusal)
                {
                    problems.Add(Problem(RolesKeys.Bands, $"a band breaks the vendor-routing rule — {refusal}; the default bands stand"));

                    return defaults;
                }

                bands.Add(band);
            }

            var ordered = bands.OrderBy(band => band.Min).ToList();
            if (ordered.Zip(ordered.Skip(1)).Any(pair => pair.First.Max >= pair.Second.Min))
            {
                problems.Add(Problem(RolesKeys.Bands, "two bands overlap, so one complexity would have two models; the default bands stand"));

                return defaults;
            }

            return bands;
        }
        catch (JsonException e)
        {
            problems.Add(Problem(RolesKeys.Bands, $"it is not valid JSON ({e.Message})"));

            return defaults;
        }
    }

    private static Band? BandOf(JsonElement row)
    {
        if (row.ValueKind != JsonValueKind.Object || ModelOf(row) is not { } model || Whole(row, "min") is not { } min || Whole(row, "max") is not { } max)
        {
            return null;
        }

        return min is >= 0 and <= TopBand && max is >= 0 and <= TopBand && min <= max ? new Band(min, max, model.Model, model.Vendor) : null;
    }

    internal static IReadOnlyList<string> HaikuKinds(string? text, IReadOnlyList<string> defaults, List<RoleSettingProblem> problems)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return defaults;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            var kinds = document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.EnumerateArray().Select(kind => kind.ValueKind == JsonValueKind.String ? kind.GetString()!.Trim().ToLowerInvariant() : string.Empty).ToList()
                : null;
            if (kinds is null || kinds.Any(kind => kind.Length == 0))
            {
                problems.Add(Problem(RolesKeys.HaikuKinds, "it is not a JSON list of non-empty task kinds; the default kinds stand"));

                return defaults;
            }

            return [.. kinds.Distinct(StringComparer.Ordinal)];
        }
        catch (JsonException e)
        {
            problems.Add(Problem(RolesKeys.HaikuKinds, $"it is not valid JSON ({e.Message})"));

            return defaults;
        }
    }

    /// <summary>
    /// The work method, KEY BY KEY: a key that reads replaces its default, a key this server does not know or cannot read is named and leaves ONLY itself at its default — the
    /// other keys still apply (risk 3 of the epoch 3 E1 spec: one unknown key used to reset every number, so a newer settings file lost its limits on an older binary without a word).
    /// </summary>
    internal static WorkMethod WorkMethodOf(string? text, WorkMethod defaults, List<RoleSettingProblem> problems)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return defaults;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                problems.Add(Problem(RolesKeys.WorkMethod, "it is not a JSON object"));

                return defaults;
            }

            var method = defaults;
            foreach (var member in document.RootElement.EnumerateObject())
            {
                if (WorkNumber(member, method) is { } applied)
                {
                    method = applied;
                }
                else
                {
                    problems.Add(Problem(RolesKeys.WorkMethod, $"'{member.Name}' is not a work-method number this server reads (mvpEpics {{min,max}}, maxEpicsPerEpoch, maxStoriesPerEpic, maxTasksPerStory, minConsultsPerEpoch, minConsultsPerEpic — whole numbers); the default of that key stands"));
                }
            }

            return method;
        }
        catch (JsonException e)
        {
            problems.Add(Problem(RolesKeys.WorkMethod, $"it is not valid JSON ({e.Message})"));

            return defaults;
        }
    }

    /// <summary>The method with one member applied, or null when the member is not a key this server knows or its value does not read.</summary>
    private static WorkMethod? WorkNumber(JsonProperty member, WorkMethod method) => member.Name switch
    {
        "mvpEpics" => MvpRange(member.Value) is { } range ? method with { MvpMin = range.Min, MvpMax = range.Max } : null,
        "maxEpicsPerEpoch" => Positive(member.Value, 1) is { } epics ? method with { MaxEpicsPerEpoch = epics } : null,
        "maxStoriesPerEpic" => Positive(member.Value, 1) is { } stories ? method with { MaxStoriesPerEpic = stories } : null,
        "maxTasksPerStory" => Positive(member.Value, 1) is { } tasks ? method with { MaxTasksPerStory = tasks } : null,
        "minConsultsPerEpoch" => Positive(member.Value, 0) is { } perEpoch ? method with { MinConsultsPerEpoch = perEpoch } : null,
        "minConsultsPerEpic" => Positive(member.Value, 0) is { } perEpic ? method with { MinConsultsPerEpic = perEpic } : null,
        _ => null,
    };

    private static (int Min, int Max)? MvpRange(JsonElement value) =>
        value.ValueKind == JsonValueKind.Object && Whole(value, "min") is { } low && Whole(value, "max") is { } high && low >= 1 && low <= high ? (low, high) : null;

    private static int? Positive(JsonElement value, int least) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number >= least ? number : null;

    private static string? Text(JsonElement entry, string name) =>
        entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() : null;

    private static int? Whole(JsonElement entry, string name) =>
        entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : null;

    private static RoleSettingProblem Problem(string key, string sentence) => new(key, $"{key}: {sentence}.");
}
