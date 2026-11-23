using CoaiMcp.Core.Work;
using CoaiMcp.Roles;
using CoaiMcp.Runners.Files;
using CoaiMcp.Work;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>
/// The task a coder job works on, found from <c>start_role(role: "coder", repoPath, taskFile: &lt;task.yaml&gt;)</c> and judged BEFORE any folder or launch is made: what its file says, and the prompt's view of it.
/// </summary>
/// <param name="RepoRoot">The repository, resolved to its real path.</param>
/// <param name="TaskFile">The task file as a real path (it was not a link out of the repository).</param>
/// <param name="TaskRel">The task file from the repository's top, written with <c>/</c> — what a prompt calls it.</param>
/// <param name="Security">True only when the task says <c>security: true</c>; the router also treats kind <c>security</c> and complexity 10 as important (<see cref="ImportantRule"/>).</param>
internal sealed record CoderTaskSite(
    string RepoRoot, string TaskFile, string TaskRel, string Id, string Title, string Kind, int Complexity, bool Security, IReadOnlyList<string> Files, IReadOnlyList<WorkDod> Dod)
{
    /// <summary>The dod ids of the task: the ids the coder's report must answer, one entry each (<see cref="Core.Coder.CoderReportReader"/>).</summary>
    public IReadOnlyList<string> DodIds => [.. Dod.Select(item => item.Id)];

    /// <summary>What the prompt says of this task.</summary>
    public CoderPromptInputs PromptInputs => new(TaskRel, Id, Title, Kind, Files, Dod);
}

/// <summary>
/// Finds and judges the task of a coder job (<c>server.md</c> § 19: the coder is a role case of <c>start_role</c>). It must be a file called <c>task.yaml</c> inside the repository, in a work plan (<c>…/epoch-N/eN-x/stories/sN-x/tasks/tN-x/</c>
/// below a feature folder that has a <c>feature.yaml</c>), and the plan must pass the work-plan rules AS THEY APPLY TO THIS TASK. Nothing here writes.
/// </summary>
/// <remarks>
/// <para><b>The same rules, not a copy.</b> The feature folder is validated by <see cref="WorkPlanValidator"/> — the road <c>validate_work</c> and the BA take — and only the errors whose path is THIS task file count: a fault in another
/// task or epic is not a reason to refuse this one. The limits it is validated with are the shipped ones (the request check has no settings, and a limit is a fault of a story or an epic, never of one task file).</para>
/// <para>Complexity and security are read by the same loader, so the router gets what the validator saw; <see cref="CoderRoute"/> then narrows the route to claude.</para>
/// </remarks>
internal static class CoderTask
{
    private const string TaskFileName = "task.yaml";

    /// <summary>How many rule errors a refusal quotes; the rest are counted.</summary>
    private const int MostQuoted = 3;

    private static StringComparison PathComparison => OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    /// <summary>The start's look at a coder request: empty when it is fit, else the sentence that refuses it.</summary>
    public static string Refusal(RoleJobRequest request) => Locate(request.RepoPath, request.TaskFile).Refusal;

    /// <summary>The task <paramref name="taskFile"/> names in <paramref name="repoPath"/>, or the sentence that says why there is none.</summary>
    public static (CoderTaskSite? Site, string Refusal) Locate(string repoPath, string taskFile)
    {
        if (taskFile.Length == 0)
        {
            return (null, $"the coder works on one task: send taskFile, the path of its {TaskFileName} inside the repository (not a brief)");
        }

        if (taskFile != TaskFileName && !taskFile.EndsWith("/" + TaskFileName, StringComparison.Ordinal))
        {
            return (null, $"taskFile '{taskFile}' is not a file called {TaskFileName}: the coder works on the {TaskFileName} of one task of a work plan");
        }

        var root = RealPath.Resolve(repoPath);
        var file = root.Ok ? RealPath.ResolveBelow(root.Path, Path.Combine(root.Path, taskFile), PathComparison) : root;
        if (!file.Ok || !File.Exists(file.Path) || Path.GetFileName(file.Path) != TaskFileName)
        {
            return (null, $"taskFile '{taskFile}' is not a {TaskFileName} file inside the repository{(file.Ok ? string.Empty : $": {file.Why}")}");
        }

        return PlanFolder(file.Path) is { } feature ? Judged(root.Path, feature, file.Path) : (null, NotInAPlan(taskFile));
    }

    private static string NotInAPlan(string taskFile) =>
        $"taskFile '{taskFile}' is not a task of a work plan: its path must end <feature>/epoch-N/eN-<epic>/stories/sN-<story>/tasks/tN-<task>/{TaskFileName}, and the feature folder must hold a feature.yaml";

    /// <summary>The names of the folders between a task file and its feature folder, innermost first: a task, <c>tasks</c>, a story, <c>stories</c>, an epic, an epoch.</summary>
    private static readonly Func<string, bool>[] Levels =
    [
        WorkNames.IsTask, name => name == "tasks", WorkNames.IsStory, name => name == "stories", WorkNames.IsEpic, WorkNames.IsEpoch,
    ];

    /// <summary>The feature folder above a task file, when every folder on the way has the name of its level and the feature folder has a <c>feature.yaml</c>; null otherwise.</summary>
    private static string? PlanFolder(string taskFile)
    {
        var folder = Path.GetDirectoryName(taskFile);
        foreach (var isLevel in Levels)
        {
            if (folder is null || !isLevel(Path.GetFileName(folder)))
            {
                return null;
            }

            folder = Path.GetDirectoryName(folder);
        }

        return folder is not null && File.Exists(Path.Combine(folder, "feature.yaml")) ? folder : null;
    }

    private static (CoderTaskSite? Site, string Refusal) Judged(string root, string feature, string file)
    {
        var below = Path.GetRelativePath(feature, file).Replace('\\', '/');
        var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
        var disk = new DiskWorkFolder(feature);
        var limits = RolesAnswer.Compute(_ => null, string.Empty, string.Empty, claudeHook: false).WorkMethod.ToLimits();
        var errors = new WorkPlanValidator(new StrictYamlReader()).Validate(disk, limits).Errors.Where(error => error.Path == below).ToList();
        if (errors.Count > 0)
        {
            return (null, Broken(rel, errors));
        }

        var loaded = new List<WorkError>();
        var task = new WorkPlanLoader(disk, new StrictYamlReader(), loaded).Load()?.AllTasks.FirstOrDefault(candidate => candidate.Path == below);

        return task is { Loaded: true }
            ? (new CoderTaskSite(root, file, rel, task.Folder, task.Title, task.Kind, task.Complexity, task.Security == SecurityFlag.True, task.Files, task.Dod), string.Empty)
            : (null, $"{rel} could not be read as a task of the plan; run validate_work on the feature first");
    }

    private static string Broken(string rel, List<WorkError> errors)
    {
        var quoted = string.Join("; ", errors.Take(MostQuoted).Select(error => $"[{error.Code}] {error.Message}"));
        var more = errors.Count > MostQuoted ? $" (and {errors.Count - MostQuoted} more)" : string.Empty;

        return $"{rel} fails the work-plan task rules: {quoted}{more}; fix the task file (validate_work lists every error of the plan)";
    }
}
