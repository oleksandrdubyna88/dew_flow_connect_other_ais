namespace CoaiMcp.Core.Work;

/// <summary>Whether a task said <c>security</c> at all, and which way. Absent and false are different facts: only false contradicts kind <c>security</c>.</summary>
public enum SecurityFlag
{
    Absent,
    True,
    False,
}

/// <summary>One <c>dod</c> item.</summary>
public sealed record WorkDod(string Id, string Item);

/// <summary>
/// The plan as read: one record per folder. <see cref="Folder"/> is the folder's name and is the node's identity everywhere (a file whose id differs is already an error), so a node whose file
/// did not read still takes part by name. <see cref="Loaded"/> is false when the file was missing or had errors; the rules then skip what they would read from it.
/// </summary>
public sealed record WorkTask(
    string Folder, string Node, string Path, bool Loaded, string Title, string Kind, int Complexity, IReadOnlyList<string> DependsOn, bool Parallel,
    IReadOnlyList<string> Files, IReadOnlyList<WorkDod> Dod, SecurityFlag Security);

/// <summary>A story folder and its tasks.</summary>
public sealed record WorkStory(string Folder, string Node, string Path, bool Loaded, IReadOnlyList<string> DependsOn, IReadOnlyList<WorkTask> Tasks);

/// <summary>An epic folder and its stories.</summary>
public sealed record WorkEpic(string Folder, string Node, string Path, bool Loaded, bool Mvp, IReadOnlyList<string> DependsOn, IReadOnlyList<WorkStory> Stories);

/// <summary>An epoch folder, the epics its file lists, and the epic folders found.</summary>
public sealed record WorkEpoch(string Folder, string Node, string Path, bool Loaded, IReadOnlyList<string> DependsOn, IReadOnlyList<string> DeclaredEpics, IReadOnlyList<WorkEpic> Epics);

/// <summary>The feature: the epochs its file lists, the work-method numbers it records (for a note) and the epoch folders found.</summary>
public sealed record WorkFeature(bool Loaded, IReadOnlyList<string> DeclaredEpochs, IReadOnlyDictionary<string, long> MethodNumbers, IReadOnlyList<WorkEpoch> Epochs)
{
    public IEnumerable<WorkEpic> AllEpics => Epochs.SelectMany(epoch => epoch.Epics);

    public IEnumerable<WorkStory> AllStories => AllEpics.SelectMany(epic => epic.Stories);

    public IEnumerable<WorkTask> AllTasks => AllStories.SelectMany(story => story.Tasks);
}
