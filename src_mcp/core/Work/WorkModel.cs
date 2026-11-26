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

/// <summary>A story folder and its tasks. <c>Title</c> (and the same member of the epoch and the epic) is read for the work pack of a plan round (epoch 3 E3 s3).</summary>
public sealed record WorkStory(string Folder, string Node, string Path, bool Loaded, IReadOnlyList<string> DependsOn, IReadOnlyList<WorkTask> Tasks, string Title = "");

/// <summary>An epic folder and its stories. <c>Spec</c> is the file name its <c>spec</c> key gives (relative to the epic folder), or empty.</summary>
public sealed record WorkEpic(string Folder, string Node, string Path, bool Loaded, bool Mvp, IReadOnlyList<string> DependsOn, IReadOnlyList<WorkStory> Stories, string Title = "", string Spec = "");

/// <summary>An epoch folder, the epics its file lists, and the epic folders found.</summary>
public sealed record WorkEpoch(string Folder, string Node, string Path, bool Loaded, IReadOnlyList<string> DependsOn, IReadOnlyList<string> DeclaredEpics, IReadOnlyList<WorkEpic> Epics, string Title = "");

/// <summary>The feature: the epochs its file lists, the work-method numbers it records (for a note) and the epoch folders found. The files named by its <c>plan</c> and <c>architecture</c> keys are members below.</summary>
public sealed record WorkFeature(bool Loaded, IReadOnlyList<string> DeclaredEpochs, IReadOnlyDictionary<string, long> MethodNumbers, IReadOnlyList<WorkEpoch> Epochs)
{
    /// <summary>The file the <c>plan</c> key names, relative to the feature folder; empty when there is none.</summary>
    public string Plan { get; init; } = string.Empty;

    /// <summary>The files of the <c>architecture</c> list, relative to the feature folder.</summary>
    public IReadOnlyList<string> Architecture { get; init; } = [];

    public IEnumerable<WorkEpic> AllEpics => Epochs.SelectMany(epoch => epoch.Epics);

    public IEnumerable<WorkStory> AllStories => AllEpics.SelectMany(epic => epic.Stories);

    public IEnumerable<WorkTask> AllTasks => AllStories.SelectMany(story => story.Tasks);
}
