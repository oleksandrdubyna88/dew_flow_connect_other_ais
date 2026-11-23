namespace CoaiMcp.Core.Work;

/// <summary>
/// The rules about how many and in what order: the <c>epochs</c> and <c>epics</c> lists equal the folders, the limits of the device (epics per epoch, stories per epic, tasks
/// per story), the MVP epics first, and a story with a task. The numbers arrive in <see cref="WorkLimits"/>; none is typed here.
/// </summary>
internal static class WorkStructureRules
{
    internal static void Check(WorkFeature feature, WorkLimits limits, List<WorkError> errors)
    {
        if (feature.Loaded)
        {
            Lists("feature.yaml", string.Empty, "epochs", feature.DeclaredEpochs, feature.Epochs.Select(epoch => epoch.Folder), errors);
        }

        foreach (var epoch in feature.Epochs)
        {
            Epoch(epoch, limits, errors);
        }
    }

    private static void Epoch(WorkEpoch epoch, WorkLimits limits, List<WorkError> errors)
    {
        if (epoch.Loaded)
        {
            Lists(epoch.Path, epoch.Node, "epics", epoch.DeclaredEpics, epoch.Epics.Select(epic => epic.Folder), errors);
        }

        if (epoch.Epics.Count > limits.MaxEpicsPerEpoch)
        {
            errors.Add(new WorkError(WorkErrorCodes.TooManyEpics, epoch.Path, epoch.Node, $"{epoch.Epics.Count} epics; this device allows at most {limits.MaxEpicsPerEpoch} per epoch (more is a new epoch)"));
        }

        if (epoch.Epics.Count > 0 && epoch.Epics.All(epic => epic.Loaded))
        {
            Mvp(epoch, limits, errors);
        }

        foreach (var epic in epoch.Epics)
        {
            Epic(epic, limits, errors);
        }
    }

    /// <summary>The first epics (in the order a person numbers them) are the MVP: their count is within the device's range, and none follows an epic that is not.</summary>
    private static void Mvp(WorkEpoch epoch, WorkLimits limits, List<WorkError> errors)
    {
        var leading = epoch.Epics.TakeWhile(epic => epic.Mvp).Count();
        var least = Math.Min(limits.MvpMin, epoch.Epics.Count);
        if (leading < least || leading > limits.MvpMax)
        {
            errors.Add(new WorkError(WorkErrorCodes.MvpCount, epoch.Path, epoch.Node, $"{leading} leading MVP epics (mvp: true); this device wants {limits.MvpMin} to {limits.MvpMax}"));
        }

        foreach (var late in epoch.Epics.Skip(leading).Where(epic => epic.Mvp))
        {
            errors.Add(new WorkError(WorkErrorCodes.MvpOrder, late.Path, late.Node, "this epic is mvp: true but an earlier epic is not; the MVP epics come first"));
        }
    }

    private static void Epic(WorkEpic epic, WorkLimits limits, List<WorkError> errors)
    {
        if (epic.Stories.Count > limits.MaxStoriesPerEpic)
        {
            errors.Add(new WorkError(WorkErrorCodes.TooManyStories, epic.Path, epic.Node, $"{epic.Stories.Count} stories; this device allows at most {limits.MaxStoriesPerEpic} per epic"));
        }

        foreach (var story in epic.Stories)
        {
            if (story.Tasks.Count > limits.MaxTasksPerStory)
            {
                errors.Add(new WorkError(WorkErrorCodes.TooManyTasks, story.Path, story.Node, $"{story.Tasks.Count} tasks; this device allows at most {limits.MaxTasksPerStory} per story"));
            }

            if (story.Tasks.Count == 0)
            {
                errors.Add(new WorkError(WorkErrorCodes.StoryWithoutTasks, story.Path, story.Node, "a story must have at least one task folder"));
            }
        }
    }

    private static void Lists(string path, string node, string key, IReadOnlyList<string> declared, IEnumerable<string> folders, List<WorkError> errors)
    {
        var onDisk = folders.ToList();
        var missing = declared.Except(onDisk, StringComparer.Ordinal).ToList();
        var unlisted = onDisk.Except(declared, StringComparer.Ordinal).ToList();
        var twice = declared.GroupBy(name => name, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key).ToList();
        if (missing.Count + unlisted.Count + twice.Count == 0)
        {
            return;
        }

        var parts = new List<string>();
        AddPart(parts, "listed but has no folder", missing);
        AddPart(parts, "has a folder but is not listed", unlisted);
        AddPart(parts, "listed twice", twice);
        errors.Add(new WorkError(WorkErrorCodes.ListFolderMismatch, path, node, $"'{key}' does not equal the folders: {string.Join("; ", parts)}"));
    }

    private static void AddPart(List<string> parts, string what, List<string> names)
    {
        if (names.Count > 0)
        {
            parts.Add($"{string.Join(", ", names)} {what}");
        }
    }
}
