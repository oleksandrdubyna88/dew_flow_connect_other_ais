using System.Text.Json;

namespace CoaiMcp.Core.Coder;

/// <summary>What the coder says of one <c>dod</c> item of its task: whether it holds, and what shows it.</summary>
public sealed record CoderDodReport(string Id, bool Met, string Evidence);

/// <summary>The coder's report on a task it worked on: one entry for EVERY dod id of the task, and free notes (empty when it gave none).</summary>
public sealed record CoderReport(IReadOnlyList<CoderDodReport> Dod, string Notes);

/// <summary>What reading the coder's answer gave: a report, a declared refusal, or everything that is wrong with the shape. A closed set — a caller cannot forget a case.</summary>
public abstract record CoderRead
{
    private CoderRead()
    {
    }

    public sealed record Report(CoderReport Value) : CoderRead;

    public sealed record Refused(string Reason) : CoderRead;

    public sealed record Wrong(IReadOnlyList<string> Problems) : CoderRead;
}

/// <summary>
/// Reads the coder's answer against <c>shared/coder-report.schema.json</c> — strictly, and against the dod ids of the task it worked on: an unknown key, a missing key, a value of the wrong type, a dod id the task does not have,
/// a dod id used twice and a dod id with no entry are each a problem, and ALL are listed (so ONE repair turn can fix them all). Neither half of the product has a JSON Schema engine, so this is the engine for the one shape,
/// and <c>ACoderContractTests</c> holds it to the file.
/// </summary>
/// <remarks>Pure: JSON in, a value out. It never reads a file and never throws for an answer that is wrong. <c>met</c> is the coder's claim; nothing here — and nothing after it — takes it for a fact.</remarks>
public static class CoderReportReader
{
    /// <summary>The keys of the <c>ok</c> arm; the schema file's arm lists the same.</summary>
    public static readonly IReadOnlyList<string> OkKeys = ["status", "dod", "notes"];

    /// <summary>The keys of the <c>refused</c> arm.</summary>
    public static readonly IReadOnlyList<string> RefusedKeys = ["status", "reason"];

    /// <summary>The keys of one dod entry; every one is required.</summary>
    public static readonly IReadOnlyList<string> DodKeys = ["id", "met", "evidence"];

    public static CoderRead Read(JsonElement root, IReadOnlyList<string> taskDodIds)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return new CoderRead.Wrong(["the answer is not one JSON object"]);
        }

        var problems = new List<string>();
        var status = Text(root, "status", string.Empty, problems);
        if (status == "refused")
        {
            UnknownKeys(root, RefusedKeys, string.Empty, problems);
            var reason = Text(root, "reason", string.Empty, problems);

            return problems.Count > 0 ? new CoderRead.Wrong(problems) : new CoderRead.Refused(reason);
        }

        UnknownKeys(root, OkKeys.Concat(RefusedKeys).Distinct().ToList(), string.Empty, problems, status == "ok" ? OkKeys : null);
        if (status != "ok")
        {
            problems.Add($"status: must be \"ok\" or \"refused\", not '{status}'");
        }

        var dod = Dod(root, taskDodIds, problems);
        var notes = Notes(root, problems);

        return problems.Count > 0 ? new CoderRead.Wrong(problems) : new CoderRead.Report(new CoderReport(dod, notes));
    }

    private static List<CoderDodReport> Dod(JsonElement root, IReadOnlyList<string> taskDodIds, List<string> problems)
    {
        var entries = new List<CoderDodReport>();
        if (!root.TryGetProperty("dod", out var list) || list.ValueKind != JsonValueKind.Array || list.GetArrayLength() == 0)
        {
            problems.Add("dod: required, a list with one entry for every dod id of the task");

            return entries;
        }

        var index = 0;
        foreach (var item in list.EnumerateArray())
        {
            var where = $"dod[{index++}]";
            if (item.ValueKind != JsonValueKind.Object)
            {
                problems.Add($"{where}: must be an object with {string.Join(", ", DodKeys)}");

                continue;
            }

            UnknownKeys(item, DodKeys, where, problems);
            entries.Add(new CoderDodReport(Id(item, taskDodIds, entries, where, problems), Met(item, where, problems), Text(item, "evidence", where, problems)));
        }

        foreach (var missing in taskDodIds.Where(id => entries.All(entry => entry.Id != id)))
        {
            problems.Add($"dod: no entry for the dod id '{missing}' of the task; every dod id needs one, met true or false, with its evidence");
        }

        return entries;
    }

    /// <summary>The id of an entry: one of the task's dod ids, not used before; the empty string (and a problem) when it is not.</summary>
    private static string Id(JsonElement entry, IReadOnlyList<string> taskDodIds, List<CoderDodReport> before, string where, List<string> problems)
    {
        var id = Text(entry, "id", where, problems);
        if (id.Length == 0)
        {
            return id;
        }

        if (!taskDodIds.Contains(id, StringComparer.Ordinal))
        {
            problems.Add($"{where}.id: '{Printable(id)}' is not a dod id of this task (the task has: {string.Join(", ", taskDodIds)})");

            return string.Empty;
        }

        if (before.Any(other => other.Id == id))
        {
            problems.Add($"{where}.id: '{id}' is used twice; every dod id has ONE entry");
        }

        return id;
    }

    private static bool Met(JsonElement entry, string where, List<string> problems)
    {
        if (entry.TryGetProperty("met", out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return value.GetBoolean();
        }

        problems.Add($"{where}.met: required, true or false (not a string, not a number)");

        return false;
    }

    private static string Notes(JsonElement root, List<string> problems)
    {
        if (!root.TryGetProperty("notes", out var value))
        {
            return string.Empty;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() ?? string.Empty;
        }

        problems.Add("notes: must be a string");

        return string.Empty;
    }

    /// <summary>
    /// Every key of <paramref name="map"/> that is not in <paramref name="known"/> is a problem; with <paramref name="armKeys"/>, a key that belongs to the schema but not to this arm is one too (<c>reason</c> on an <c>ok</c> report).
    /// </summary>
    private static void UnknownKeys(JsonElement map, IReadOnlyList<string> known, string at, List<string> problems, IReadOnlyList<string>? armKeys = null)
    {
        var allowed = armKeys ?? known;
        foreach (var property in map.EnumerateObject().Where(property => !allowed.Contains(property.Name, StringComparer.Ordinal)))
        {
            problems.Add($"{Where(at, property.Name)}: unknown key (the keys here are {string.Join(", ", allowed)})");
        }
    }

    private static string Text(JsonElement map, string key, string at, List<string> problems)
    {
        if (!map.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        {
            problems.Add($"{Where(at, key)}: required, a non-empty string");

            return string.Empty;
        }

        return value.GetString()!;
    }

    private static string Where(string at, string key) => at.Length == 0 ? key : $"{at}.{key}";

    /// <summary>An id as a problem line may quote it: control characters shown as <c>?</c>, and not longer than a line should be.</summary>
    private static string Printable(string text)
    {
        var shown = new string([.. text.Select(c => c < ' ' ? '?' : c)]);

        return shown.Length <= 80 ? shown : shown[..80] + "…";
    }
}
