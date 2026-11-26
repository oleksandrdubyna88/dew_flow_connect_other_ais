using System.Globalization;

namespace CoaiMcp.Core.Toolchain;

/// <summary>One line of <c>git diff --name-status -z -M</c>: the status letter, the path in the head, and for a rename or a copy the path in the base.</summary>
public sealed record PathChange(char Status, string Path, string? OldPath);

/// <summary>A project (or a test project) the selection did not run, and why.</summary>
public sealed record LeftOut(string Project, string Reason);

/// <summary>
/// What level one of the selection decided (epoch 3 E4 s3 t6): which projects run, which test projects of a .NET unit run, and what was left out with its reason. <see cref="Everything"/> is the answer when the impact is unknown.
/// </summary>
/// <param name="Everything">True when nothing was left out because coai could not show that the diff does not reach it; <see cref="Why"/> says why.</param>
/// <param name="Why">The sentence that says why everything runs; empty when a selection was made.</param>
/// <param name="Projects">The ids of the projects that run (every project when <see cref="Everything"/>).</param>
/// <param name="TestProjects">For a .NET unit that runs: the test projects whose test step runs; a unit that is not in the map runs ALL its test steps.</param>
/// <param name="Left">Every project and test project that was left out, with the reason; empty when everything runs.</param>
public sealed record AffectedSelection(bool Everything, string Why, IReadOnlySet<string> Projects, IReadOnlyDictionary<string, IReadOnlySet<string>> TestProjects, IReadOnlyList<LeftOut> Left)
{
    /// <summary>Whether the project runs.</summary>
    public bool Runs(string projectId) => Everything || Projects.Contains(projectId);

    /// <summary>Whether the test step of <paramref name="testProject"/> in the unit <paramref name="unitId"/> runs.</summary>
    public bool RunsTest(string unitId, string testProject) => Everything || !TestProjects.TryGetValue(unitId, out var tests) || tests.Contains(testProject);
}

/// <summary>
/// <b>Selection, level one: a project is left out only when coai can show that the diff does not reach it</b> (epoch 3 E4 s3 t6; SPEC §E). Pure: the changes between the base and the head and the resolved toolchain in, a selection out.
/// </summary>
/// <remarks>
/// <para><b>Always run:</b> a project of the file (coai does not know what its commands read), and every node project — a workspace, a <c>file:</c> or <c>link:</c> dependency, a relative import or a script can join a package to anything in the tree, and coai reads manifests, not source files.</para>
/// <para><b>A .NET unit is left out</b> when no changed path is in its folder, none of its projects references (directly or through other projects, across units) a project of a unit that has a changed path, every <c>ProjectReference</c> resolves to a project file of the toolchain, and no project, <c>.props</c> or
/// <c>.targets</c> file of the unit holds a path that leaves the unit's folder other than a resolved <c>ProjectReference</c>. Inside a unit that runs, a test step runs only for the test projects that reach a changed project through the reverse reference graph of the head, a project the head removed counting as changed;
/// a changed solution file, <c>global.json</c>, <c>nuget.config</c> or any <c>.props</c> or <c>.targets</c> file makes every test project of the unit run.</para>
/// <para><b>Impact unknown runs EVERYTHING:</b> a changed path outside every project folder (a root configuration, a shared folder, documents), a reference that cannot be resolved, a project file that cannot be read, a diff that names nothing. A selection never selects nothing, and each unit left out says why.
/// <b>Stated limit:</b> a test of one unit that reads a file of another unit at run time by a path built in code is in no manifest; a change of that file alone leaves the reading unit out.</para>
/// </remarks>
public static class AffectedProjects
{
    /// <summary>Reads <c>git diff --name-status -z -M</c> output: <c>STATUS NUL path NUL</c>, and for <c>R</c> and <c>C</c> two paths. A record that cannot be read gives null: the caller then runs everything.</summary>
    public static IReadOnlyList<PathChange>? ParseNameStatus(string output)
    {
        var parts = output.Split('\0');
        var changes = new List<PathChange>();
        var index = 0;
        while (index < parts.Length && parts[index].Length > 0)
        {
            var status = parts[index][0];
            if (status is 'R' or 'C')
            {
                if (index + 2 >= parts.Length || parts[index + 1].Length == 0 || parts[index + 2].Length == 0)
                {
                    return null;
                }

                changes.Add(new PathChange(status, parts[index + 2], parts[index + 1]));
                index += 3;
            }
            else
            {
                if (!"AMDTUXB".Contains(status) || index + 1 >= parts.Length || parts[index + 1].Length == 0)
                {
                    return null;
                }

                changes.Add(new PathChange(status, parts[index + 1], null));
                index += 2;
            }
        }

        return changes;
    }

    /// <summary>Decides which projects run. <paramref name="atBase"/> and <paramref name="atHead"/> are the manifests of the two commits.</summary>
    public static AffectedSelection Select(ResolvedToolchain toolchain, IReadOnlyList<PathChange>? changes, ManifestSet atBase, ManifestSet atHead)
    {
        var all = toolchain.Projects.Select(project => project.Id).ToHashSet(StringComparer.Ordinal);
        AffectedSelection Everything(string why) => new(true, why, all, new Dictionary<string, IReadOnlySet<string>>(), []);

        if (changes is null || changes.Count == 0)
        {
            return Everything("the diff between the base and the head names no path coai could read, so the impact is unknown");
        }

        var changed = changes.SelectMany(change => change.OldPath is null ? new[] { change.Path } : [change.Path, change.OldPath]).Distinct(StringComparer.Ordinal).ToList();
        var folders = toolchain.Projects.Select(FolderOf).Distinct(StringComparer.Ordinal).ToList();
        var stray = changed.FirstOrDefault(path => !folders.Any(folder => ProjectGraph.Under(folder, path)));
        if (stray is not null)
        {
            return Everything($"a changed path is outside every project folder ({PathSpelling.Spell(stray)}{(changed.Count(path => !folders.Any(folder => ProjectGraph.Under(folder, path))) > 1 ? " and others" : string.Empty)}): a root configuration, a shared folder or documents can reach every project");
        }

        var graph = Graph(atHead);
        if (graph.Refusal.Length > 0)
        {
            return Everything(graph.Refusal);
        }

        var baseGraph = Graph(atBase);
        var run = new HashSet<string>(StringComparer.Ordinal);
        var tests = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);
        var left = new List<LeftOut>();
        var dotnet = toolchain.Projects.Where(project => project.Detected is { Stack: "dotnet" }).ToList();

        // The projects of the whole repository that the diff touched (a project the head removed counts too): a test project of one unit reaches a changed project of another.
        var changedEverywhere = graph.Projects.Keys.Where(path => changed.Any(changedPath => ProjectGraph.Under(ProjectGraph.Folder(path), changedPath))).ToHashSet(StringComparer.Ordinal);
        foreach (var removed in baseGraph.Projects.Keys.Where(path => !graph.Projects.ContainsKey(path)))
        {
            changedEverywhere.Add(removed);
        }

        var unitsWithChanges = dotnet.Where(unit => changed.Any(path => ProjectGraph.Under(FolderOf(unit), path))).Select(unit => unit.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var project in toolchain.Projects)
        {
            if (project.Detected is not { Stack: "dotnet" } detected)
            {
                run.Add(project.Id);

                continue;
            }

            var owned = detected.OwnedPaths;
            var folder = FolderOf(project);
            var reachesAChangedUnit = unitsWithChanges.Contains(project.Id) || owned.Any(path => Reach(path, graph).Any(other => dotnet.Any(unit => unit.Id != project.Id && unitsWithChanges.Contains(unit.Id) && OwnedBy(unit, other))));
            var leaving = owned.Select(path => (Path: path, File: atHead.Files.FirstOrDefault(file => file.Path == path))).FirstOrDefault(item => item.File is not null && ProjectGraph.PathsLeaving(item.File.Text, item.Path, folder) > 0);
            var leavingDefinition = atHead.Files.Where(file => file.Stack == "dotnet" && file.Kind == ManifestKind.Definition && ProjectGraph.Under(folder, file.Path) && (file.Path.EndsWith(".props", StringComparison.OrdinalIgnoreCase) || file.Path.EndsWith(".targets", StringComparison.OrdinalIgnoreCase)))
                .FirstOrDefault(file => ProjectGraph.PathsLeaving(file.Text, file.Path, folder) > 0);
            if (!reachesAChangedUnit && leaving.File is null && leavingDefinition is null)
            {
                left.Add(new LeftOut(project.Id, "no changed path is in its folder, and none of its projects references a project of a unit that has one; every reference resolves, and no file of it holds a path that leaves its folder"));

                continue;
            }

            run.Add(project.Id);
            var unitInfo = TestSelection(detected, owned, changed, folder, graph, baseGraph, changedEverywhere);
            if (unitInfo.Unknown.Length > 0)
            {
                return Everything(unitInfo.Unknown);
            }

            if (unitInfo.Tests is { } selected)
            {
                tests[project.Id] = selected;
                left.AddRange(detected.TestPaths.Where(test => !selected.Contains(test)).Select(test => new LeftOut(test, "it reaches no changed project through the reverse ProjectReference graph of the head")));
            }
        }

        return run.Count == 0 ? Everything("the selection would have selected nothing, so everything runs") : new AffectedSelection(false, string.Empty, run, tests, left);
    }

    private static string FolderOf(ResolvedProject project) =>
        project.Detected is { } detected ? (detected.Folder == "." ? string.Empty : detected.Folder) : project.FileProject is { } file ? (file.Path == "." ? string.Empty : file.Path) : string.Empty;

    private static bool OwnedBy(ResolvedProject unit, string projectPath) => unit.Detected!.OwnedPaths.Contains(projectPath, StringComparer.Ordinal);

    private sealed record GraphRead(IReadOnlyDictionary<string, ProjectFacts> Projects, string Refusal);

    /// <summary>Every .NET project file of a commit read as facts. A project that cannot be read, or a reference that cannot be resolved to a project file of the set, is a refusal (impact unknown).</summary>
    private static GraphRead Graph(ManifestSet set)
    {
        var props = set.Files.Where(file => file.Stack == "dotnet" && file.Kind == ManifestKind.Definition && file.Path.Split('/')[^1] == "Directory.Build.props").ToDictionary(file => ProjectGraph.Folder(file.Path), file => file.Text, StringComparer.Ordinal);
        var projects = new Dictionary<string, ProjectFacts>(StringComparer.Ordinal);
        foreach (var file in set.Files.Where(file => file.Stack == "dotnet" && file.Kind == ManifestKind.Manifest && !file.Path.EndsWith(".sln", StringComparison.Ordinal) && !file.Path.EndsWith(".slnx", StringComparison.Ordinal)))
        {
            var (facts, fault) = ProjectGraph.Read(file.Path, file.Text, DotnetDetector.NearestProps(props, ProjectGraph.Folder(file.Path)));
            if (fault is not null)
            {
                return new GraphRead(projects, $"the project file {PathSpelling.Spell(file.Path)} cannot be read, so the impact of the diff on the .NET units is unknown");
            }

            projects[file.Path] = facts!;
        }

        foreach (var facts in projects.Values)
        {
            if (facts.Unresolved.Count > 0 || facts.Outside.Count > 0)
            {
                return new GraphRead(projects, $"the project {PathSpelling.Spell(facts.Path)} has a ProjectReference that cannot be resolved ({(facts.Unresolved.Count + facts.Outside.Count).ToString(CultureInfo.InvariantCulture)}), so the impact of the diff is unknown");
            }

            var missing = facts.References.FirstOrDefault(reference => !projects.ContainsKey(reference));
            if (missing is not null)
            {
                return new GraphRead(projects, $"the project {PathSpelling.Spell(facts.Path)} references a project file that is not in the repository's manifests, so the impact of the diff is unknown");
            }
        }

        return new GraphRead(projects, string.Empty);
    }

    /// <summary>Every project a project references, directly or through others.</summary>
    private static HashSet<string> Reach(string path, GraphRead graph)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>([path]);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (graph.Projects.TryGetValue(current, out var facts))
            {
                foreach (var reference in facts.References.Where(seen.Add))
                {
                    queue.Enqueue(reference);
                }
            }
        }

        return seen;
    }

    private sealed record TestInfo(IReadOnlySet<string>? Tests, string Unknown);

    private static TestInfo TestSelection(DetectedProject unit, IReadOnlyList<string> owned, List<string> changedPaths, string folder, GraphRead graph, GraphRead baseGraph, HashSet<string> changedProjects)
    {
        var testProjects = unit.TestPaths;
        var inUnit = changedPaths.Where(path => ProjectGraph.Under(folder, path)).ToList();
        // The folders of the unit's projects at the head AND at the base: a project the head removed had a folder, and a change in it is shown to reach what referenced it.
        var projectFolders = owned.Concat(baseGraph.Projects.Keys.Where(path => ProjectGraph.Under(folder, path))).Select(path => ProjectGraph.Folder(path)).Distinct(StringComparer.Ordinal).ToList();

        // A changed file that configures every project of the unit: the solution file, global.json, nuget.config, a .props or .targets file.
        if (inUnit.Any(path => IsUnitWide(path)) || changedPaths.Any(path => IsUnitWide(path) && ProjectGraph.Under(ProjectGraph.Folder(path), folder + "/x")))
        {
            return new TestInfo(null, string.Empty);
        }

        // A path of the unit that is in no project folder (documents, a loose file): not shown to reach a project.
        var stray = inUnit.FirstOrDefault(path => !projectFolders.Any(projectFolder => ProjectGraph.Under(projectFolder, path)));
        if (stray is not null)
        {
            return new TestInfo(null, $"a changed path of the unit {PathSpelling.Spell(unit.Id)} is in no project folder ({PathSpelling.Spell(stray)}), so what it reaches is unknown");
        }

        var selected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var test in testProjects)
        {
            var reached = Reach(test, graph);
            reached.Add(test);
            var baseReached = baseGraph.Projects.ContainsKey(test) ? Reach(test, baseGraph) : [];
            if (reached.Any(changedProjects.Contains) || baseReached.Any(path => changedProjects.Contains(path) && !graph.Projects.ContainsKey(path)))
            {
                selected.Add(test);
            }
        }

        return new TestInfo(selected, string.Empty);
    }

    private static bool IsUnitWide(string path)
    {
        var name = path.Split('/')[^1];

        return path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "global.json", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "nuget.config", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".props", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".targets", StringComparison.OrdinalIgnoreCase);
    }
}
