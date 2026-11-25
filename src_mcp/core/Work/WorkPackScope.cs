namespace CoaiMcp.Core.Work;

/// <summary>Which node of the plan a pack is for: the whole feature, or an epoch, an epic, a story or a task. <see cref="Folder"/> is the node's folder from the feature folder; empty is the whole feature.</summary>
internal sealed record WorkPackScope(string Folder, WorkEpoch? Epoch, WorkEpic? Epic, WorkStory? Story, WorkTask? Task)
{
    internal bool Whole => Epoch is null;

    /// <summary>The scope <paramref name="scopeFolder"/> (repository-relative) names in the plan, or null when it names no node of it.</summary>
    internal static WorkPackScope? Of(WorkFeature feature, string featureFolder, string scopeFolder)
    {
        if (scopeFolder == featureFolder)
        {
            return new WorkPackScope(string.Empty, null, null, null, null);
        }

        var prefix = featureFolder + "/";
        var relative = scopeFolder.StartsWith(prefix, StringComparison.Ordinal) ? scopeFolder[prefix.Length..] : string.Empty;

        return relative.Length == 0 ? null : Find(feature, relative);
    }

    private static WorkPackScope? Find(WorkFeature feature, string relative)
    {
        foreach (var epoch in feature.Epochs)
        {
            if (relative == epoch.Folder)
            {
                return new WorkPackScope(relative, epoch, null, null, null);
            }

            if (FindIn(epoch, relative) is { } inside)
            {
                return inside;
            }
        }

        return null;
    }

    private static WorkPackScope? FindIn(WorkEpoch epoch, string relative)
    {
        foreach (var epic in epoch.Epics)
        {
            if (relative == Dir(epic.Path))
            {
                return new WorkPackScope(relative, epoch, epic, null, null);
            }

            foreach (var story in epic.Stories)
            {
                if (relative == Dir(story.Path))
                {
                    return new WorkPackScope(relative, epoch, epic, story, null);
                }

                if (story.Tasks.FirstOrDefault(task => relative == Dir(task.Path)) is { } task)
                {
                    return new WorkPackScope(relative, epoch, epic, story, task);
                }
            }
        }

        return null;
    }

    private static string Dir(string path) => path[..path.LastIndexOf('/')];
}
