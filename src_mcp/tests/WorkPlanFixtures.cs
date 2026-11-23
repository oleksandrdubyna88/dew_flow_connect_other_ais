using System.Text.Json;
using CoaiMcp.Core.Work;

namespace CoaiMcp.Tests;

/// <summary>A feature folder held in memory: path to text, folders from the paths. What the vectors and the unit scenarios run the validator over.</summary>
internal sealed class InMemoryWorkFolder(IReadOnlyDictionary<string, string> files) : IWorkFolder
{
    public IReadOnlyList<string> Folders(string relative)
    {
        var prefix = relative.Length == 0 ? string.Empty : relative + "/";

        return [.. files.Keys.Where(path => path.StartsWith(prefix, StringComparison.Ordinal))
            .Select(path => path[prefix.Length..].Split('/'))
            .Where(parts => parts.Length > 1)
            .Select(parts => parts[0])
            .Distinct(StringComparer.Ordinal)];
    }

    public WorkFile Read(string relative, int maxBytes) =>
        !files.TryGetValue(relative, out var text) ? new WorkFile.Missing()
        : System.Text.Encoding.UTF8.GetByteCount(text) > maxBytes ? new WorkFile.TooLarge(System.Text.Encoding.UTF8.GetByteCount(text))
        : new WorkFile.Text(text);
}

/// <summary>The valid base plan of the vectors (one epoch, one MVP epic, one story, one task), and the helpers that change it.</summary>
internal static class WorkPlan
{
    internal static readonly WorkLimits Defaults = new(MaxEpicsPerEpoch: 5, MvpMin: 1, MvpMax: 2, MaxStoriesPerEpic: 8, MaxTasksPerStory: 6);

    internal const string Epic = "epoch-1/e1-core";
    internal const string Story = Epic + "/stories/s1-first";
    internal const string TaskFile = Story + "/tasks/t1-one/task.yaml";

    internal static Dictionary<string, string> Base() => new()
    {
        ["feature.yaml"] = "id: demo\ntitle: \"Demo plan\"\nepochs:\n  - epoch-1\n",
        ["epoch-1/epoch.yaml"] = "id: epoch-1\ntitle: Foundation\ndependsOn: []\nepics:\n  - e1-core\n",
        [Epic + "/epic.yaml"] = "id: e1-core\ntitle: Core\nmvp: true\ndependsOn: []\n",
        [Story + "/story.yaml"] = "id: s1-first\ntitle: First\ndependsOn: []\n",
        [TaskFile] = Task("t1-one"),
    };

    internal static string Task(string id, string kind = "code", int complexity = 3, string dependsOn = "[]", bool parallel = false, string files = "\n  - src/x.txt", string extra = "") =>
        $"id: {id}\ntitle: Task {id}\nkind: {kind}\ncomplexity: {complexity}\ndependsOn: {dependsOn}\nparallel: {(parallel ? "true" : "false")}\nfiles:{files}\ndod:\n  - id: d1\n    item: it works\n{extra}";

    internal static string TaskPath(string story, string id) => $"{story}/tasks/{id}/task.yaml";

    internal static WorkReport Validate(IReadOnlyDictionary<string, string> files, WorkLimits? limits = null) =>
        new WorkPlanValidator(new StrictYamlReader()).Validate(new InMemoryWorkFolder(files), limits ?? Defaults);

    internal static string[] Codes(WorkReport report) => [.. report.Errors.Select(error => error.Code).Distinct().OrderBy(code => code, StringComparer.Ordinal)];

    /// <summary>One vector of <c>shared/work-plan-vectors.json</c>.</summary>
    internal sealed record Vector(string Name, string Covers, IReadOnlyDictionary<string, string> Files, WorkLimits Limits, IReadOnlyList<string> Expect);

    internal static IReadOnlyList<Vector> Vectors()
    {
        var path = Path.Combine(RepoPaths.Root(), "shared", "work-plan-vectors.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));

        return [.. document.RootElement.GetProperty("vectors").EnumerateArray().Select(vector => new Vector(
            vector.GetProperty("name").GetString()!,
            vector.GetProperty("covers").GetString()!,
            vector.GetProperty("files").EnumerateObject().ToDictionary(file => file.Name, file => FileText(file.Value)),
            LimitsOf(vector),
            [.. vector.GetProperty("expect").EnumerateArray().Select(code => code.GetString()!)]))];
    }

    /// <summary>A file value is a string, or {prefix, line, times}: the prefix and then the line, that many times.</summary>
    private static string FileText(JsonElement value) =>
        value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : value.GetProperty("prefix").GetString() + string.Concat(Enumerable.Repeat(value.GetProperty("line").GetString()!, value.GetProperty("times").GetInt32()));

    private static WorkLimits LimitsOf(JsonElement vector)
    {
        if (!vector.TryGetProperty("limits", out var limits))
        {
            return Defaults;
        }

        int Read(string name, int fallback) => limits.TryGetProperty(name, out var value) ? value.GetInt32() : fallback;
        var mvp = limits.TryGetProperty("mvpEpics", out var range) ? range : default;

        return new WorkLimits(
            Read("maxEpicsPerEpoch", Defaults.MaxEpicsPerEpoch),
            mvp.ValueKind == JsonValueKind.Object ? mvp.GetProperty("min").GetInt32() : Defaults.MvpMin,
            mvp.ValueKind == JsonValueKind.Object ? mvp.GetProperty("max").GetInt32() : Defaults.MvpMax,
            Read("maxStoriesPerEpic", Defaults.MaxStoriesPerEpic),
            Read("maxTasksPerStory", Defaults.MaxTasksPerStory));
    }
}
