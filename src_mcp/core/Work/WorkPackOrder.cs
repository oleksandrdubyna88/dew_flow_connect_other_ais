namespace CoaiMcp.Core.Work;

/// <summary>
/// The ORDER of a pack: which files, in which order, which nodes the scope holds and which outside nodes it depends on. Pure over the plan as read (epoch 3 E3 s3 t3). A node comes after the nodes it depends on; a tie is broken by the
/// numbers in the ids (so <c>s2</c> is before <c>s10</c>), then by the node's name; a plan that has a loop of dependencies (the validator refuses it) still gets one fixed order rather than none.
/// </summary>
internal static class WorkPackOrder
{
    /// <summary>One file of the pack: its path from the feature folder and, for a file the plan NAMES (<c>plan</c>, <c>spec</c>), the key, the name as written and the file that wrote it.</summary>
    internal sealed record PackFile(string Path, string Key, string Name, string Where);

    /// <summary>A node outside the scope that a scope node depends on, as the pack shows it.</summary>
    internal sealed record Dependency(string Node, string Title);

    /// <summary>The files in pack order, the outside dependencies in pack order, and how many nodes have no file in the pack.</summary>
    internal sealed record Layout(IReadOnlyList<PackFile> Files, IReadOnlyList<Dependency> Dependencies, int NodesNotSent);

    private sealed class Walk
    {
        private readonly HashSet<string> _seen = new(StringComparer.Ordinal);

        internal List<PackFile> Files { get; } = [];

        internal List<string> ScopeNodes { get; } = [];

        internal bool Has(string path) => _seen.Contains(path);

        internal void Add(string path, string key = "", string name = "", string where = "")
        {
            if (_seen.Add(path))
            {
                Files.Add(new PackFile(path, key, name, where));
            }
        }
    }

    internal static Layout Of(WorkFeature feature, IReadOnlyDictionary<string, IReadOnlyList<string>> edges, WorkPackScope scope)
    {
        var walk = new Walk();
        walk.Add("feature.yaml");
        if (feature.Plan.Length > 0)
        {
            walk.Add(feature.Plan, "plan", feature.Plan, "feature.yaml");
        }

        Context(feature, edges, scope, walk);
        Stories(feature, edges, scope, walk);

        return new Layout(walk.Files, Dependencies(feature, edges, walk.ScopeNodes), Nodes(feature).Count(node => !walk.Has(node.Path)));
    }

    // ---------- context: the epochs and epics, then the ancestors of a story or a task ----------

    private static void Context(WorkFeature feature, IReadOnlyDictionary<string, IReadOnlyList<string>> edges, WorkPackScope scope, Walk walk)
    {
        if (scope.Whole || scope.Epic is null)
        {
            foreach (var epoch in scope.Whole ? Epochs(feature, edges) : [scope.Epoch!])
            {
                EpochFiles(epoch, walk, inScope: true);
                Epics(epoch, edges).ForEach(epic => EpicFiles(epic, walk, inScope: true));
            }

            return;
        }

        EpochFiles(scope.Epoch!, walk, inScope: false);
        EpicFiles(scope.Epic, walk, inScope: scope.Story is null);
        if (scope.Story is not null)
        {
            walk.Add(scope.Story.Path);
            if (scope.Task is null)
            {
                walk.ScopeNodes.Add(scope.Story.Node);
            }
        }
    }

    private static void EpochFiles(WorkEpoch epoch, Walk walk, bool inScope)
    {
        walk.Add(epoch.Path);
        if (inScope)
        {
            walk.ScopeNodes.Add(epoch.Node);
        }
    }

    private static void EpicFiles(WorkEpic epic, Walk walk, bool inScope)
    {
        walk.Add(epic.Path);
        if (epic.Spec.Length > 0)
        {
            walk.Add($"{Dir(epic.Path)}/{epic.Spec}", "spec", epic.Spec, epic.Path);
        }

        if (inScope)
        {
            walk.ScopeNodes.Add(epic.Node);
        }
    }

    // ---------- stories and tasks ----------

    private static void Stories(WorkFeature feature, IReadOnlyDictionary<string, IReadOnlyList<string>> edges, WorkPackScope scope, Walk walk)
    {
        foreach (var story in StoriesOf(feature, edges, scope))
        {
            walk.Add(story.Path);
            if (scope.Story is null)
            {
                walk.ScopeNodes.Add(story.Node);
            }

            foreach (var task in Tasks(story, edges).Where(task => scope.Task is null || task.Node == scope.Task.Node))
            {
                walk.Add(task.Path);
                walk.ScopeNodes.Add(task.Node);
            }
        }
    }

    private static List<WorkStory> StoriesOf(WorkFeature feature, IReadOnlyDictionary<string, IReadOnlyList<string>> edges, WorkPackScope scope)
    {
        if (scope.Story is not null)
        {
            return [scope.Story];
        }

        var epics = scope.Epic is not null ? [scope.Epic] : scope.Epoch is not null ? scope.Epoch.Epics : feature.AllEpics.ToList();
        var stories = epics.SelectMany(epic => epic.Stories).ToList();
        var storyOf = stories.SelectMany(story => story.Tasks.Select(task => (task.Node, Story: story.Node))).ToDictionary(pair => pair.Node, pair => pair.Story, StringComparer.Ordinal);
        var order = new Dictionary<string, (int, int, int)>(StringComparer.Ordinal);
        foreach (var epic in epics)
        {
            epic.Stories.ToList().ForEach(story => order[story.Node] = (WorkNames.Number(feature.Epochs.First(epoch => epoch.Epics.Contains(epic)).Folder), WorkNames.Number(epic.Folder), WorkNames.Number(story.Folder)));
        }

        return Sorted(stories, story => story.Node, story => order[story.Node], story => StoryDependencies(story, edges, storyOf));
    }

    /// <summary>The stories a story must come after: the ones it names, and the stories of the tasks its tasks name.</summary>
    private static IEnumerable<string> StoryDependencies(WorkStory story, IReadOnlyDictionary<string, IReadOnlyList<string>> edges, Dictionary<string, string> storyOf) =>
        Edges(edges, story.Node).Concat(story.Tasks.SelectMany(task => Edges(edges, task.Node)).Select(target => storyOf.GetValueOrDefault(target, string.Empty)));

    private static List<WorkTask> Tasks(WorkStory story, IReadOnlyDictionary<string, IReadOnlyList<string>> edges) =>
        Sorted(story.Tasks, task => task.Node, task => (WorkNames.Number(task.Folder), 0, 0), task => Edges(edges, task.Node));

    private static List<WorkEpoch> Epochs(WorkFeature feature, IReadOnlyDictionary<string, IReadOnlyList<string>> edges) =>
        Sorted(feature.Epochs, epoch => epoch.Node, epoch => (WorkNames.Number(epoch.Folder), 0, 0), epoch => Edges(edges, epoch.Node));

    private static List<WorkEpic> Epics(WorkEpoch epoch, IReadOnlyDictionary<string, IReadOnlyList<string>> edges) =>
        Sorted(epoch.Epics, epic => epic.Node, epic => (WorkNames.Number(epic.Folder), 0, 0), epic => Edges(edges, epic.Node));

    // ---------- dependencies outside the scope ----------

    private static List<Dependency> Dependencies(WorkFeature feature, IReadOnlyDictionary<string, IReadOnlyList<string>> edges, List<string> scopeNodes)
    {
        var titles = Nodes(feature).ToDictionary(node => node.Node, node => node.Title, StringComparer.Ordinal);
        var inside = scopeNodes.ToHashSet(StringComparer.Ordinal);
        var named = new HashSet<string>(StringComparer.Ordinal);

        return [.. scopeNodes.SelectMany(node => Edges(edges, node)).Where(target => !inside.Contains(target) && named.Add(target)).Select(target => new Dependency(target, titles.GetValueOrDefault(target, string.Empty)))];
    }

    private static IEnumerable<(string Node, string Title, string Path)> Nodes(WorkFeature feature) =>
        feature.Epochs.Select(epoch => (epoch.Node, epoch.Title, epoch.Path))
            .Concat(feature.AllEpics.Select(epic => (epic.Node, epic.Title, epic.Path)))
            .Concat(feature.AllStories.Select(story => (story.Node, story.Title, story.Path)))
            .Concat(feature.AllTasks.Select(task => (task.Node, task.Title, task.Path)));

    private static IReadOnlyList<string> Edges(IReadOnlyDictionary<string, IReadOnlyList<string>> edges, string node) =>
        edges.TryGetValue(node, out var targets) ? targets : [];

    private static string Dir(string path) => path[..path.LastIndexOf('/')];

    /// <summary>
    /// A topological order by repeated choice: of the items whose dependencies are all done, the one with the smallest key, then the smallest name. When none is ready (a loop) the smallest of what is left goes, so the order is always whole and always the same.
    /// </summary>
    private static List<T> Sorted<T>(IEnumerable<T> items, Func<T, string> node, Func<T, (int, int, int)> key, Func<T, IEnumerable<string>> depends)
    {
        var left = items.ToList();
        var done = new List<T>();
        while (left.Count > 0)
        {
            var names = left.Select(node).ToHashSet(StringComparer.Ordinal);
            var ready = left.Where(item => !depends(item).Any(target => target != node(item) && names.Contains(target))).ToList();
            var next = (ready.Count > 0 ? ready : left).OrderBy(key).ThenBy(node, StringComparer.Ordinal).First();
            done.Add(next);
            left.Remove(next);
        }

        return done;
    }
}
