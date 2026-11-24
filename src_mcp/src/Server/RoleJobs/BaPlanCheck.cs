using CoaiMcp.Core.Work;
using CoaiMcp.Work;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>One file the BA's output would make: where it goes below the epic folder, what it is, and its LF text.</summary>
/// <param name="EpicRelative">The path below the epic folder, written with <c>/</c> — <c>stories/s1-x/story.yaml</c>.</param>
/// <param name="Kind"><c>story</c> or <c>task</c>.</param>
/// <param name="Text">The file's text, LF, one final newline (<see cref="WorkPlanYaml"/>).</param>
internal sealed record BaFile(string EpicRelative, string Kind, string Text);

/// <summary>What checking the BA's plan found: the sentences for a repair turn (empty when the plan is acceptable) and the files the plan makes.</summary>
internal sealed record BaChecked(IReadOnlyList<string> Errors, IReadOnlyList<BaFile> Files);

/// <summary>
/// Checks the BA's plan the way <c>validate_work</c> checks a plan folder, over an OVERLAY — the feature folder as it is on disk plus the story and task files the plan would make (spec C) — before anything is written.
/// </summary>
/// <remarks>
/// <para><b>Only what the BA caused.</b> The folder is validated twice, with and without the plan's files, and only the errors that are NEW are the BA's: a fault elsewhere in the feature (another epic's file) is not a reason for
/// the BA to be sent back, and a limit the plan breaks (<c>too-many-tasks</c>) is. Errors are told in ids the BA knows — <c>s1-x/t2-y</c> — never in paths it did not write.</para>
/// <para><b>The same rules, the same limits.</b> The validator and its tables are the ones <c>validate_work</c> uses and the limits are the device's, so a plan accepted here is a plan <c>validate_work</c> accepts on this device.</para>
/// </remarks>
internal static class BaPlanCheck
{
    public static BaChecked Check(BaEpicSite site, BaPlan plan, WorkLimits limits)
    {
        var errors = new List<string>();
        if (plan.Epic != site.EpicId)
        {
            errors.Add($"epic: the answer names '{plan.Epic}' but the epic to plan is '{site.EpicId}'");
        }

        var files = FilesOf(plan, errors);
        if (errors.Count > 0)
        {
            return new BaChecked(errors, files);
        }

        var epicBelowFeature = BaEpic.Rel(site.FeatureFolder, site.EpicFolder);
        var disk = new DiskWorkFolder(site.FeatureFolder);
        var overlay = new OverlayWorkFolder(disk, files.ToDictionary(file => $"{epicBelowFeature}/{file.EpicRelative}", file => file.Text, StringComparer.Ordinal));
        var validator = new WorkPlanValidator(new StrictYamlReader());
        var before = validator.Validate(disk, limits).Errors.Select(Key).ToHashSet(StringComparer.Ordinal);
        var after = validator.Validate(overlay, limits);
        errors.AddRange(after.Errors.Where(error => !before.Contains(Key(error))).Select(error => Sentence(error, epicBelowFeature)));

        return new BaChecked(errors, files);
    }

    /// <summary>The files of the plan, in the order the output gives them. A story or task id used twice is an error: two files cannot take one path.</summary>
    private static List<BaFile> FilesOf(BaPlan plan, List<string> errors)
    {
        var files = new List<BaFile>();
        var stories = new HashSet<string>(StringComparer.Ordinal);
        foreach (var story in plan.Stories)
        {
            if (!stories.Add(story.Id))
            {
                errors.Add($"stories: the id '{story.Id}' is used twice; every story has its own id");

                continue;
            }

            files.Add(new BaFile($"stories/{story.Id}/story.yaml", "story", WorkPlanYaml.Story(story)));
            var tasks = new HashSet<string>(StringComparer.Ordinal);
            foreach (var task in story.Tasks)
            {
                if (tasks.Add(task.Id))
                {
                    files.Add(new BaFile($"stories/{story.Id}/tasks/{task.Id}/task.yaml", "task", WorkPlanYaml.Task(task)));
                }
                else
                {
                    errors.Add($"{story.Id}: the task id '{task.Id}' is used twice in this story; every task has its own id");
                }
            }
        }

        return files;
    }

    private static string Key(WorkError error) => $"{error.Code}\n{error.Path}\n{error.Node}\n{error.Message}";

    /// <summary>One error as the BA reads it: the rule, where in its own ids, what is wrong.</summary>
    private static string Sentence(WorkError error, string epicBelowFeature)
    {
        var node = error.Node.StartsWith(epicBelowFeature, StringComparison.Ordinal) ? error.Node[epicBelowFeature.Length..].TrimStart('/') : error.Node;
        var where = node.Length == 0 ? "the epic" : node;

        return $"[{error.Code}] {where}: {error.Message}";
    }
}
