using System.Text.Encodings.Web;
using System.Text.Json;

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
                    chosen[role.Name] = list;
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

            var (mvpMin, mvpMax, epics, perEpoch, perEpic) = (defaults.MvpMin, defaults.MvpMax, defaults.MaxEpicsPerEpoch, defaults.MinConsultsPerEpoch, defaults.MinConsultsPerEpic);
            foreach (var member in document.RootElement.EnumerateObject())
            {
                var ok = member.Name switch
                {
                    "mvpEpics" => MvpRange(member.Value, ref mvpMin, ref mvpMax),
                    "maxEpicsPerEpoch" => Positive(member.Value, 1, ref epics),
                    "minConsultsPerEpoch" => Positive(member.Value, 0, ref perEpoch),
                    "minConsultsPerEpic" => Positive(member.Value, 0, ref perEpic),
                    _ => false,
                };
                if (!ok)
                {
                    problems.Add(Problem(RolesKeys.WorkMethod, $"'{member.Name}' is not a work-method number this server knows (mvpEpics {{min,max}}, maxEpicsPerEpoch, minConsultsPerEpoch, minConsultsPerEpic — whole numbers); the default method stands"));

                    return defaults;
                }
            }

            return new WorkMethod(mvpMin, mvpMax, epics, perEpoch, perEpic);
        }
        catch (JsonException e)
        {
            problems.Add(Problem(RolesKeys.WorkMethod, $"it is not valid JSON ({e.Message})"));

            return defaults;
        }
    }

    private static bool MvpRange(JsonElement value, ref int min, ref int max)
    {
        if (value.ValueKind != JsonValueKind.Object || Whole(value, "min") is not { } low || Whole(value, "max") is not { } high || low < 1 || low > high)
        {
            return false;
        }

        (min, max) = (low, high);

        return true;
    }

    private static bool Positive(JsonElement value, int least, ref int target)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number) || number < least)
        {
            return false;
        }

        target = number;

        return true;
    }

    private static string? Text(JsonElement entry, string name) =>
        entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() : null;

    private static int? Whole(JsonElement entry, string name) =>
        entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : null;

    private static RoleSettingProblem Problem(string key, string sentence) => new(key, $"{key}: {sentence}.");
}
