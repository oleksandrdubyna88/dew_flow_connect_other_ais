using System.Text.Json;

namespace CoaiMcp.Core.Roles;

/// <summary>
/// The pieces of a strict reading that the scout's and the judge's readers share (epoch 3 E3 s1): an unknown key, a text with its length bounds, a list with its bounds, and the evidence items. Every problem names the member
/// it is about (<c>facts[2].evidence[0].text: …</c>) and ALL are collected, so ONE repair turn can fix them all. Pure: JSON in, problems out.
/// </summary>
internal static class RoleOutputShape
{
    /// <summary>Every key of <paramref name="map"/> that is not in <paramref name="allowed"/> is a problem.</summary>
    public static void UnknownKeys(JsonElement map, IReadOnlyList<string> allowed, string at, List<string> problems)
    {
        foreach (var property in map.EnumerateObject().Where(property => !allowed.Contains(property.Name, StringComparer.Ordinal)))
        {
            problems.Add($"{Where(at, property.Name)}: unknown key (the keys here are {string.Join(", ", allowed)})");
        }
    }

    /// <summary>A required text: a string with something in it, at most <paramref name="max"/> characters. The empty string (and a problem) when it is not.</summary>
    public static string Required(JsonElement map, string key, string at, int max, List<string> problems)
    {
        if (!map.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.String || IsEmpty(value.GetString()))
        {
            problems.Add($"{Where(at, key)}: required, a non-empty string{(max == int.MaxValue ? string.Empty : $" of at most {max} characters")} (not only white space)");

            return string.Empty;
        }

        var text = value.GetString()!;
        if (text.Length > max)
        {
            problems.Add($"{Where(at, key)}: {text.Length} characters, at most {max}");
        }

        return text;
    }

    /// <summary>An optional text: absent is empty; present it is a string of at most <paramref name="max"/> characters (empty is allowed).</summary>
    public static string Optional(JsonElement map, string key, string at, int max, List<string> problems)
    {
        if (!map.TryGetProperty(key, out var value))
        {
            return string.Empty;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            problems.Add($"{Where(at, key)}: must be a string of at most {max} characters");

            return string.Empty;
        }

        var text = value.GetString() ?? string.Empty;
        if (text.Length > max)
        {
            problems.Add($"{Where(at, key)}: {text.Length} characters, at most {max}");
        }

        return text;
    }

    /// <summary>A list of required texts, <paramref name="min"/> to <paramref name="max"/> items, each of at most <paramref name="maxText"/> characters.</summary>
    public static IReadOnlyList<string> Texts(JsonElement map, string key, string at, int min, int max, int maxText, List<string> problems)
    {
        var items = new List<string>();
        foreach (var (item, where) in Items(map, key, at, min, max, problems))
        {
            if (item.ValueKind != JsonValueKind.String || IsEmpty(item.GetString()))
            {
                problems.Add($"{where}: must be a non-empty string (not only white space)");
            }
            else if (item.GetString()!.Length > maxText)
            {
                problems.Add($"{where}: {item.GetString()!.Length} characters, at most {maxText}");
            }
            else
            {
                items.Add(item.GetString()!);
            }
        }

        return items;
    }

    /// <summary>
    /// The items of the array at <paramref name="key"/>, each with the name of its place. A missing or non-array member is a problem, and so is a count outside <paramref name="min"/> to <paramref name="max"/>; only the first
    /// <paramref name="max"/> items are read, so an answer with a million of them costs one problem.
    /// </summary>
    public static IEnumerable<(JsonElement Item, string Where)> Items(JsonElement map, string key, string at, int min, int max, List<string> problems)
    {
        if (!map.TryGetProperty(key, out var list) || list.ValueKind != JsonValueKind.Array)
        {
            problems.Add($"{Where(at, key)}: required, a list of {min} to {max} items");

            return [];
        }

        var count = list.GetArrayLength();
        if (count < min || count > max)
        {
            problems.Add($"{Where(at, key)}: {count} item{(count == 1 ? string.Empty : "s")}, from {min} to {max} are allowed");
        }

        return list.EnumerateArray().Take(max).Select((item, index) => (item, $"{Where(at, key)}[{index}]"));
    }

    /// <summary>The evidence items of <paramref name="key"/>: <c>{file, line, text}</c>, each judged as text — the blank text and the path that may not be opened are problems here, before any file is asked.</summary>
    public static IReadOnlyList<EvidenceItem> Evidence(JsonElement map, string key, string at, int min, int max, List<string> problems)
    {
        var items = new List<EvidenceItem>();
        foreach (var (item, where) in Items(map, key, at, min, max, problems))
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                problems.Add($"{where}: must be an object with {string.Join(", ", EvidenceKeys)}");

                continue;
            }

            UnknownKeys(item, EvidenceKeys, where, problems);
            items.Add(EvidenceOf(item, where, problems));
        }

        return items;
    }

    /// <summary>The keys of one evidence item; every one is required.</summary>
    public static readonly IReadOnlyList<string> EvidenceKeys = ["file", "line", "text"];

    private static EvidenceItem EvidenceOf(JsonElement item, string where, List<string> problems)
    {
        var file = item.TryGetProperty("file", out var path) && path.ValueKind == JsonValueKind.String ? path.GetString()! : string.Empty;
        var text = item.TryGetProperty("text", out var quoted) && quoted.ValueKind == JsonValueKind.String ? quoted.GetString()! : string.Empty;
        AddUnless(problems, ProblemOfPath(item, where, file));
        AddUnless(problems, RoleEvidence.BlankProblem($"{where}.text", text));
        if (text.Length > RoleEvidence.MaxTextLength)
        {
            problems.Add($"{where}.text: {text.Length} characters, at most {RoleEvidence.MaxTextLength}");
        }

        return new EvidenceItem(file, Line(item, where, problems), text);
    }

    private static string ProblemOfPath(JsonElement item, string where, string file) =>
        item.TryGetProperty("file", out var value) && value.ValueKind == JsonValueKind.String
            ? RoleEvidence.PathProblem($"{where}.file", file)
            : $"{where}.file: required, a path inside the repository written from its top";

    private static int Line(JsonElement item, string where, List<string> problems)
    {
        if (item.TryGetProperty("line", out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var line) && line >= 1)
        {
            return line;
        }

        problems.Add($"{where}.line: required, a whole number from 1 (not a string, not a fraction)");

        return 0;
    }

    private static void AddUnless(List<string> problems, string problem)
    {
        if (problem.Length > 0)
        {
            problems.Add(problem);
        }
    }

    /// <summary>The member's name with its place: <c>facts[0].claim</c>, or just the key at the top.</summary>
    public static string Where(string at, string key) => at.Length == 0 ? key : $"{at}.{key}";

    /// <summary>True for a string with nothing to read in it (white space, or characters that draw as nothing) — the one test every required text uses.</summary>
    public static bool IsEmpty(string? text) => RoleEvidence.IsBlank(text);

    /// <summary>A word the model wrote, as a problem line may quote it: control characters shown as <c>?</c>, and cut when long.</summary>
    public static string Printable(string text)
    {
        var shown = new string([.. text.Select(c => c < ' ' ? '?' : c)]);

        return shown.Length <= 80 ? shown : shown[..80] + "…";
    }
}
