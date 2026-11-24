using System.Text.Json;

namespace CoaiMcp.Core.Work;

/// <summary>One <c>dod</c> item of a task the BA wrote.</summary>
public sealed record BaDod(string Id, string Item);

/// <summary>One task of the BA's output — every key of a <c>task.yaml</c> but <c>status</c>, which a new task has no use for. <see cref="Security"/> is null when the BA did not say.</summary>
public sealed record BaTask(
    string Id, string Title, string Kind, int Complexity, IReadOnlyList<string> DependsOn, bool Parallel, IReadOnlyList<string> Files, IReadOnlyList<BaDod> Dod, bool? Security = null);

/// <summary>One story of the BA's output and its tasks. <see cref="Cornerstone"/> is null when the BA did not say.</summary>
public sealed record BaStory(string Id, string Title, IReadOnlyList<string> DependsOn, IReadOnlyList<BaTask> Tasks, bool? Cornerstone = null);

/// <summary>The BA's plan of an epic: the epic's id (an echo coai checks against the folder) and its stories.</summary>
public sealed record BaPlan(string Epic, IReadOnlyList<BaStory> Stories, IReadOnlyList<string> Notes);

/// <summary>
/// What reading the BA's answer gave: a plan, a declared refusal, or the list of everything wrong with the shape (so ONE repair turn can fix them all). A closed set — a caller cannot forget a case.
/// </summary>
public abstract record BaRead
{
    private BaRead()
    {
    }

    /// <summary>The answer has the shape of <c>shared/ba-output.schema.json</c>, arm <c>ok</c>. Every id is well formed and cannot name a place outside the epic folder.</summary>
    public sealed record Plan(BaPlan Value) : BaRead;

    /// <summary>The model declared it would not do the work (arm <c>refused</c>).</summary>
    public sealed record Refused(string Reason) : BaRead;

    /// <summary>The answer is not that shape; each entry says where and what (<c>stories[1].tasks[0].complexity: …</c>).</summary>
    public sealed record Wrong(IReadOnlyList<string> Problems) : BaRead;
}

/// <summary>
/// Reads the BA's answer against <c>shared/ba-output.schema.json</c> — strictly: an unknown key, a missing key, a value of the wrong type and an id that does not match its pattern IN FULL are each a problem, and
/// ALL are listed. Neither half of the product has a JSON Schema engine, so this is the engine for the one shape, and <c>ABaOutputSchemaAgreesWithTheWorkPlanTests</c> holds it to the file.
/// </summary>
/// <remarks>
/// <para><b>Ids cannot escape a folder</b> (spec E.1). A story id must match <c>^s\d+-[a-z0-9-]+$</c> and a task id <c>^t\d+-[a-z0-9-]+$</c> in full — printable ASCII, no <c>/</c>, no <c>..</c> — so a path derived from an id
/// stays inside the epic folder; the publisher checks the resolved path as well, and this check is what lets it hold.</para>
/// <para>Pure: JSON in, a value out. It never reads a file and never throws for an answer that is wrong.</para>
/// </remarks>
public static class BaOutputReader
{
    private static readonly string[] RootKeys = ["status", "reason", "epic", "stories", "notes"];
    private static readonly string[] StoryKeys = ["id", "title", "dependsOn", "cornerstone", "tasks"];
    private static readonly string[] TaskKeys = ["id", "title", "kind", "complexity", "dependsOn", "parallel", "files", "dod", "security"];
    private static readonly string[] DodKeys = ["id", "item"];

    public static BaRead Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return new BaRead.Wrong(["the answer is not one JSON object"]);
        }

        var problems = new List<string>();
        UnknownKeys(root, RootKeys, string.Empty, problems);
        var status = Text(root, "status", string.Empty, problems);
        if (status == "refused")
        {
            var reason = Text(root, "reason", string.Empty, problems);

            return problems.Count > 0 ? new BaRead.Wrong(problems) : new BaRead.Refused(reason);
        }

        if (status != "ok")
        {
            problems.Add($"status: must be \"ok\" or \"refused\", not '{status}'");
        }

        var epic = Text(root, "epic", string.Empty, problems);
        if (epic.Length > 0 && !WorkTables.IsId(WorkTables.EpicIdPattern, epic))
        {
            problems.Add($"epic: '{epic}' does not match {WorkTables.EpicIdPattern}");
        }

        var stories = Stories(root, problems);
        var notes = Strings(root, "notes", string.Empty, problems, required: false);

        return problems.Count > 0 ? new BaRead.Wrong(problems) : new BaRead.Plan(new BaPlan(epic, stories, notes));
    }

    private static List<BaStory> Stories(JsonElement root, List<string> problems)
    {
        var stories = new List<BaStory>();
        if (!root.TryGetProperty("stories", out var list) || list.ValueKind != JsonValueKind.Array || list.GetArrayLength() == 0)
        {
            problems.Add("stories: required, a list with at least one story");

            return stories;
        }

        var index = 0;
        foreach (var story in list.EnumerateArray())
        {
            stories.Add(Story(story, $"stories[{index++}]", problems));
        }

        return stories;
    }

    private static BaStory Story(JsonElement story, string at, List<string> problems)
    {
        if (story.ValueKind != JsonValueKind.Object)
        {
            problems.Add($"{at}: must be an object");

            return new BaStory(string.Empty, string.Empty, [], []);
        }

        UnknownKeys(story, StoryKeys, at, problems);
        var id = Id(story, WorkTables.StoryIdPattern, at, problems);
        var title = Text(story, "title", at, problems);
        var depends = Strings(story, "dependsOn", at, problems, required: true);
        var cornerstone = Flag(story, "cornerstone", at, problems);
        var tasks = new List<BaTask>();
        if (!story.TryGetProperty("tasks", out var list) || list.ValueKind != JsonValueKind.Array || list.GetArrayLength() == 0)
        {
            problems.Add($"{at}.tasks: required, a list with at least one task");
        }
        else
        {
            var index = 0;
            foreach (var task in list.EnumerateArray())
            {
                tasks.Add(Task(task, $"{at}.tasks[{index++}]", problems));
            }
        }

        return new BaStory(id, title, depends, tasks, cornerstone);
    }

    private static BaTask Task(JsonElement task, string at, List<string> problems)
    {
        if (task.ValueKind != JsonValueKind.Object)
        {
            problems.Add($"{at}: must be an object");

            return new BaTask(string.Empty, string.Empty, string.Empty, 0, [], false, [], []);
        }

        UnknownKeys(task, TaskKeys, at, problems);
        var id = Id(task, WorkTables.TaskIdPattern, at, problems);
        var title = Text(task, "title", at, problems);
        var kind = Text(task, "kind", at, problems);
        if (kind.Length > 0 && !WorkTables.Kinds.Contains(kind))
        {
            problems.Add($"{at}.kind: '{kind}' is not one of {string.Join(", ", WorkTables.Kinds)}");
        }

        var complexity = Complexity(task, at, problems);
        var depends = Strings(task, "dependsOn", at, problems, required: true);
        var parallel = Flag(task, "parallel", at, problems);
        if (parallel is null)
        {
            problems.Add($"{at}.parallel: required, true or false");
        }

        var files = Strings(task, "files", at, problems, required: true);

        return new BaTask(id, title, kind, complexity, depends, parallel ?? false, files, Dod(task, at, problems), Flag(task, "security", at, problems));
    }

    private static List<BaDod> Dod(JsonElement task, string at, List<string> problems)
    {
        var items = new List<BaDod>();
        if (!task.TryGetProperty("dod", out var list) || list.ValueKind != JsonValueKind.Array || list.GetArrayLength() == 0)
        {
            problems.Add($"{at}.dod: required, a list with at least one item");

            return items;
        }

        var index = 0;
        foreach (var item in list.EnumerateArray())
        {
            var where = $"{at}.dod[{index++}]";
            if (item.ValueKind != JsonValueKind.Object)
            {
                problems.Add($"{where}: must be an object with id and item");

                continue;
            }

            UnknownKeys(item, DodKeys, where, problems);
            items.Add(new BaDod(Id(item, WorkTables.DodIdPattern, where, problems), Text(item, "item", where, problems)));
        }

        return items;
    }

    private static int Complexity(JsonElement task, string at, List<string> problems)
    {
        if (!task.TryGetProperty("complexity", out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number))
        {
            problems.Add($"{at}.complexity: required, a whole number from {WorkTables.ComplexityMin} to {WorkTables.ComplexityMax} (not a string, not a fraction)");

            return 0;
        }

        if (number < WorkTables.ComplexityMin || number > WorkTables.ComplexityMax)
        {
            problems.Add($"{at}.complexity: {number} is outside {WorkTables.ComplexityMin} to {WorkTables.ComplexityMax}");
        }

        return number;
    }

    private static void UnknownKeys(JsonElement map, string[] known, string at, List<string> problems)
    {
        foreach (var property in map.EnumerateObject().Where(property => !known.Contains(property.Name, StringComparer.Ordinal)))
        {
            problems.Add($"{Where(at, property.Name)}: unknown key (the keys here are {string.Join(", ", known)})");
        }
    }

    /// <summary>An id of the level whose pattern is <paramref name="pattern"/>, matched IN FULL; the empty string when it is absent or wrong (the problem is recorded).</summary>
    private static string Id(JsonElement map, string pattern, string at, List<string> problems)
    {
        var id = Text(map, "id", at, problems);
        if (id.Length > 0 && !WorkTables.IsId(pattern, id))
        {
            problems.Add($"{Where(at, "id")}: '{Printable(id)}' does not match {pattern} in full; an id is the folder name and cannot hold a path");

            return string.Empty;
        }

        return id;
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

    private static bool? Flag(JsonElement map, string key, string at, List<string> problems)
    {
        if (!map.TryGetProperty(key, out var value))
        {
            return null;
        }

        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return value.GetBoolean();
        }

        problems.Add($"{Where(at, key)}: must be true or false, not {value.ValueKind.ToString().ToLowerInvariant()}");

        return null;
    }

    private static List<string> Strings(JsonElement map, string key, string at, List<string> problems, bool required)
    {
        var items = new List<string>();
        if (!map.TryGetProperty(key, out var list))
        {
            if (required)
            {
                problems.Add($"{Where(at, key)}: required, a list of strings (an empty list is [])");
            }

            return items;
        }

        if (list.ValueKind != JsonValueKind.Array)
        {
            problems.Add($"{Where(at, key)}: must be a list of strings");

            return items;
        }

        var index = 0;
        foreach (var item in list.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(item.GetString()))
            {
                problems.Add($"{Where(at, key)}[{index}]: must be a non-empty string");
            }
            else
            {
                items.Add(item.GetString()!);
            }

            index++;
        }

        return items;
    }

    private static string Where(string at, string key) => at.Length == 0 ? key : $"{at}.{key}";

    /// <summary>An id as a problem line may quote it: control characters shown as <c>?</c>, and not longer than a line should be.</summary>
    private static string Printable(string text)
    {
        var shown = new string([.. text.Select(c => c < ' ' ? '?' : c)]);

        return shown.Length <= 80 ? shown : shown[..80] + "…";
    }
}
