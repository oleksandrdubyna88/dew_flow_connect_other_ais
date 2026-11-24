using System.Text.RegularExpressions;

namespace CoaiMcp.Core.Work;

/// <summary>The type of a key's value.</summary>
public enum WorkField
{
    Text,
    Boolean,
    Integer,
    IdList,
    Map,
    Dod,
}

/// <summary>One level of the plan (feature, epoch, epic, story, task): its file, its keys and what each holds.</summary>
public sealed record WorkLevel(string Name, string File, IReadOnlyList<string> Required, IReadOnlyDictionary<string, WorkField> Fields, bool AllowUnknown, string IdPattern);

/// <summary>
/// The tables of the work-plan contract: the levels, their keys, the kinds, the statuses, the complexity range and the id patterns. ONE source for the validator;
/// <c>shared/work-plan.schema.json</c> says the same in JSON Schema and <c>AWorkPlanSchemaAgreesWithTheValidatorTests</c> holds the two equal — neither half has a
/// schema engine, so that test is the only guard against drift (risk 4 of the spec).
/// </summary>
public static partial class WorkTables
{
    public const string EpochIdPattern = @"^epoch-\d+$";
    public const string EpicIdPattern = @"^e\d+-[a-z0-9-]+$";
    public const string StoryIdPattern = @"^s\d+-[a-z0-9-]+$";
    public const string TaskIdPattern = @"^t\d+-[a-z0-9-]+$";
    public const string DodIdPattern = @"^d\d+$";

    public const int ComplexityMin = 0;
    public const int ComplexityMax = 10;

    public static readonly IReadOnlyList<string> Kinds =
        ["code", "test", "docs", "measure", "config", "rename", "translate", "summarize", "changelog", "simple-docs", "security"];

    public static readonly IReadOnlyList<string> Statuses = ["open", "in-progress", "done"];

    public static readonly WorkLevel Feature = new(
        "feature", "feature.yaml", ["id", "title", "epochs"],
        new Dictionary<string, WorkField> { ["id"] = WorkField.Text, ["title"] = WorkField.Text, ["epochs"] = WorkField.IdList, ["workMethod"] = WorkField.Map },
        AllowUnknown: true, string.Empty);

    public static readonly WorkLevel Epoch = new(
        "epoch", "epoch.yaml", ["id", "title", "dependsOn", "epics"],
        new Dictionary<string, WorkField>
        {
            ["id"] = WorkField.Text,
            ["title"] = WorkField.Text,
            ["proves"] = WorkField.Text,
            ["dependsOn"] = WorkField.IdList,
            ["epics"] = WorkField.IdList,
        },
        AllowUnknown: false, EpochIdPattern);

    public static readonly WorkLevel Epic = new(
        "epic", "epic.yaml", ["id", "title", "dependsOn"],
        new Dictionary<string, WorkField>
        {
            ["id"] = WorkField.Text,
            ["title"] = WorkField.Text,
            ["mvp"] = WorkField.Boolean,
            ["half"] = WorkField.Text,
            ["dependsOn"] = WorkField.IdList,
            ["parallelWith"] = WorkField.IdList,
            ["doneWhen"] = WorkField.IdList,
            ["spec"] = WorkField.Text,
        },
        AllowUnknown: false, EpicIdPattern);

    public static readonly WorkLevel Story = new(
        "story", "story.yaml", ["id", "title", "dependsOn"],
        new Dictionary<string, WorkField>
        {
            ["id"] = WorkField.Text,
            ["title"] = WorkField.Text,
            ["dependsOn"] = WorkField.IdList,
            ["cornerstone"] = WorkField.Boolean,
        },
        AllowUnknown: false, StoryIdPattern);

    public static readonly WorkLevel Task = new(
        "task", "task.yaml", ["id", "title", "kind", "complexity", "dependsOn", "parallel", "files", "dod"],
        new Dictionary<string, WorkField>
        {
            ["id"] = WorkField.Text,
            ["title"] = WorkField.Text,
            ["kind"] = WorkField.Text,
            ["complexity"] = WorkField.Integer,
            ["dependsOn"] = WorkField.IdList,
            ["parallel"] = WorkField.Boolean,
            ["files"] = WorkField.IdList,
            ["dod"] = WorkField.Dod,
            ["security"] = WorkField.Boolean,
            ["status"] = WorkField.Text,
        },
        AllowUnknown: false, TaskIdPattern);

    public static readonly IReadOnlyList<WorkLevel> Levels = [Feature, Epoch, Epic, Story, Task];

    [GeneratedRegex(EpochIdPattern, RegexOptions.CultureInvariant)]
    private static partial Regex EpochId();

    [GeneratedRegex(EpicIdPattern, RegexOptions.CultureInvariant)]
    private static partial Regex EpicId();

    [GeneratedRegex(StoryIdPattern, RegexOptions.CultureInvariant)]
    private static partial Regex StoryId();

    [GeneratedRegex(TaskIdPattern, RegexOptions.CultureInvariant)]
    private static partial Regex TaskId();

    [GeneratedRegex(DodIdPattern, RegexOptions.CultureInvariant)]
    private static partial Regex DodId();

    /// <summary>
    /// Whether <paramref name="id"/> matches <paramref name="pattern"/> IN FULL. The patterns are written for ECMAScript (the schema file); .NET's <c>\d</c> also takes other scripts'
    /// digits and its <c>$</c> allows a trailing newline, so an id must first be printable ASCII — which is also what keeps <c>s1-../../outside</c> and a newline out of a folder name.
    /// </summary>
    public static bool IsId(string pattern, string id) =>
        id.All(c => c is >= ' ' and <= '~')
        && pattern switch
        {
            EpochIdPattern => EpochId().IsMatch(id),
            EpicIdPattern => EpicId().IsMatch(id),
            StoryIdPattern => StoryId().IsMatch(id),
            TaskIdPattern => TaskId().IsMatch(id),
            DodIdPattern => DodId().IsMatch(id),
            _ => false,
        };
}
