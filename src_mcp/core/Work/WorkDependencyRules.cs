namespace CoaiMcp.Core.Work;

/// <summary>
/// The <c>dependsOn</c> rules. An entry resolves to a node of its own level: a sibling; or, for an epic, story or task, the same level inside an epoch or epic this one
/// depends on (decision 0.4 of the epoch 3 E1 spec: v2 has a story that depends on a story of another epic); or <c>&lt;epic&gt;/&lt;story&gt;</c> for a story. Anything unnamed is
/// <c>unknown-dependency</c>, two matches are <c>ambiguous-dependency</c>, and a loop is <c>dependency-cycle</c> with its path. Only nodes whose file read clean give their entries.
/// </summary>
internal sealed class WorkDependencyRules(WorkFeature feature, List<WorkError> errors)
{
    private readonly Dictionary<string, List<string>> _edges = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Path, string Folder)> _where = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<WorkEpic>> _dependedEpics = new(StringComparer.Ordinal);

    /// <summary>Checks the whole plan and answers the resolved edges (node to the nodes it depends on), which the parallel rule reads.</summary>
    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> Check(WorkFeature feature, List<WorkError> errors)
    {
        var rules = new WorkDependencyRules(feature, errors);
        rules.Resolve();
        rules.Cycles();

        return rules._edges.ToDictionary(edge => edge.Key, edge => (IReadOnlyList<string>)edge.Value, StringComparer.Ordinal);
    }

    // ---------- resolving ----------

    private void Resolve()
    {
        foreach (var epoch in feature.Epochs)
        {
            Register(epoch.Node, epoch.Path, epoch.Folder);
            foreach (var epic in epoch.Epics)
            {
                Register(epic.Node, epic.Path, epic.Folder);
                foreach (var story in epic.Stories)
                {
                    Register(story.Node, story.Path, story.Folder);
                    story.Tasks.ToList().ForEach(task => Register(task.Node, task.Path, task.Folder));
                }
            }
        }

        foreach (var epoch in feature.Epochs.Where(epoch => epoch.Loaded))
        {
            ResolveAll(epoch.Node, epoch.Path, "epoch", epoch.DependsOn, name => new Named(feature.Epochs.Where(other => other.Folder == name).Select(other => other.Node).ToList(), "a sibling epoch"));
        }

        foreach (var epoch in feature.Epochs)
        {
            foreach (var epic in epoch.Epics.Where(epic => epic.Loaded))
            {
                ResolveAll(epic.Node, epic.Path, "epic", epic.DependsOn, name => new Named(EpicCandidates(epoch, name), "an epic of this epoch or of an epoch this epoch depends on"));
            }
        }

        foreach (var epic in feature.AllEpics)
        {
            foreach (var story in epic.Stories.Where(story => story.Loaded))
            {
                ResolveAll(story.Node, story.Path, "story", story.DependsOn, name => new Named(StoryCandidates(epic, name), "a story of this epic or of an epic this epic depends on, or <epic>/<story>"));
            }

            foreach (var task in epic.Stories.SelectMany(story => story.Tasks.Where(task => task.Loaded).Select(task => (story, task))))
            {
                ResolveAll(task.task.Node, task.task.Path, "task", task.task.DependsOn, name => new Named(TaskCandidates(epic, task.story, name), "a task of this story or of an epic this epic depends on"));
            }
        }
    }

    private void Register(string node, string path, string folder)
    {
        _where[node] = (path, folder);
        _edges[node] = [];
    }

    private void ResolveAll(string node, string path, string level, IReadOnlyList<string> entries, Func<string, Named> pick)
    {
        foreach (var entry in entries)
        {
            var found = pick(entry);
            if (found.Targets.Count == 1)
            {
                _edges[node].Add(found.Targets[0]);
            }
            else if (found.Targets.Count == 0)
            {
                errors.Add(new WorkError(WorkErrorCodes.UnknownDependency, path, node, $"dependsOn '{entry}' names no {level}: it must be {found.Allowed}"));
            }
            else
            {
                errors.Add(new WorkError(WorkErrorCodes.AmbiguousDependency, path, node, $"dependsOn '{entry}' names {found.Targets.Count} {level}s ({string.Join(", ", found.Targets)}); write <epic>/<story> or depend on one"));
            }
        }
    }

    /// <summary>The nodes an entry names, and what would have been legal.</summary>
    private readonly record struct Named(IReadOnlyList<string> Targets, string Allowed);

    private List<string> EpicCandidates(WorkEpoch epoch, string name)
    {
        var siblings = epoch.Epics.Where(other => other.Folder == name).Select(other => other.Node).ToList();
        if (siblings.Count > 0)
        {
            return siblings;
        }

        var dependedEpochs = feature.Epochs.Where(other => epoch.DependsOn.Contains(other.Folder) && other != epoch);

        return [.. dependedEpochs.SelectMany(other => other.Epics).Where(other => other.Folder == name).Select(other => other.Node)];
    }

    /// <summary>The epics an epic depends on, by the same resolution as an epic's own entries (one match each; an unresolved entry is already an error).</summary>
    private IReadOnlyList<WorkEpic> DependedEpics(WorkEpic epic)
    {
        if (_dependedEpics.TryGetValue(epic.Node, out var known))
        {
            return known;
        }

        var epoch = feature.Epochs.First(candidate => candidate.Epics.Contains(epic));
        var resolved = new List<WorkEpic>();
        if (epic.Loaded)
        {
            foreach (var name in epic.DependsOn)
            {
                var nodes = EpicCandidates(epoch, name);
                if (nodes.Count == 1)
                {
                    resolved.Add(feature.AllEpics.First(other => other.Node == nodes[0]));
                }
            }
        }

        _dependedEpics[epic.Node] = resolved;

        return resolved;
    }

    private List<string> StoryCandidates(WorkEpic epic, string name)
    {
        if (name.Contains('/'))
        {
            return Qualified(name);
        }

        var siblings = epic.Stories.Where(story => story.Folder == name).Select(story => story.Node).ToList();

        return siblings.Count > 0
            ? siblings
            : [.. DependedEpics(epic).SelectMany(other => other.Stories).Where(story => story.Folder == name).Select(story => story.Node)];
    }

    /// <summary><c>&lt;epic&gt;/&lt;story&gt;</c>: the story of that name in the epic of that name; an epic named twice in the feature gives two matches, so it is ambiguous.</summary>
    private List<string> Qualified(string name)
    {
        var parts = name.Split('/');

        return parts.Length == 2 && parts.All(part => part.Length > 0)
            ? [.. feature.AllEpics.Where(epic => epic.Folder == parts[0]).SelectMany(epic => epic.Stories).Where(story => story.Folder == parts[1]).Select(story => story.Node)]
            : [];
    }

    private List<string> TaskCandidates(WorkEpic epic, WorkStory story, string name)
    {
        var siblings = story.Tasks.Where(task => task.Folder == name).Select(task => task.Node).ToList();

        return siblings.Count > 0
            ? siblings
            : [.. DependedEpics(epic).SelectMany(other => other.Stories).SelectMany(other => other.Tasks).Where(task => task.Folder == name).Select(task => task.Node)];
    }

    // ---------- cycles ----------

    private void Cycles()
    {
        var state = new Dictionary<string, int>(StringComparer.Ordinal);
        var stack = new List<string>();
        foreach (var node in _edges.Keys.OrderBy(key => key, StringComparer.Ordinal))
        {
            if (!state.ContainsKey(node))
            {
                Visit(node, state, stack);
            }
        }
    }

    /// <summary>Depth-first; state 1 is on the stack, 2 is done. A neighbour on the stack closes a loop, reported once, from the node the loop was entered at.</summary>
    private void Visit(string node, Dictionary<string, int> state, List<string> stack)
    {
        state[node] = 1;
        stack.Add(node);
        foreach (var next in _edges[node].OrderBy(key => key, StringComparer.Ordinal))
        {
            if (!state.TryGetValue(next, out var seen))
            {
                Visit(next, state, stack);
            }
            else if (seen == 1)
            {
                Report(stack.Skip(stack.IndexOf(next)).Append(next).ToList());
            }
        }

        stack.RemoveAt(stack.Count - 1);
        state[node] = 2;
    }

    private void Report(List<string> loop)
    {
        var (path, _) = _where[loop[0]];
        errors.Add(new WorkError(WorkErrorCodes.DependencyCycle, path, loop[0], $"the dependencies go round: {string.Join(" -> ", loop.Select(node => _where[node].Folder))}"));
    }
}
