using System.Globalization;

namespace CoaiMcp.Core.Toolchain;

/// <summary>
/// <b>The one place that decides where the checks of a repository come from</b> (epoch 3 E4 s2 t5; SPEC §A). Pure: the file at the base, a way to read the manifests of the base and of the head, and the setting go in; checks, nothing to check or a refusal come out. Nothing is read from a working tree and no process is started here.
/// </summary>
/// <remarks>
/// <para><b>The order.</b> (1) A file at the base commit wins; detection decides nothing for a project that states a command. A project of the file that states NO command (<c>path</c> and <c>adapter</c> only) gets the adapter's defaults for its own folder, read at the base, and a refusal of the detector there refuses the file
/// (source <c>file+defaults</c>). (2) No file at the base: detection at the base AND at the head. <c>COAI_TOOLCHAIN_DETECT=off</c> gives E2's answer, no file is <c>not_configured</c>; anything but <c>on</c> and <c>off</c> is <c>on</c>, with a note.</para>
/// <para><b>Both trees.</b> The head is the commit coai made, so it is the coder's. A project only the head has is added with the defaults and marked <c>DefinedByHead</c>. <b>A step (project and step name) that the base had and the head has not is <c>toolchain_refused</c></b>: <c>the worktree removed &lt;step&gt; of &lt;project&gt;</c>.
/// A manifest of a stack that is known and not built (php, rust, python, java), at the base or at the head, is refused with the stack, the path and the cure. The check DEFINITION of a project the base has (the scripts of a package, the test projects of a solution, its targets and imports: <see cref="CheckDefinition"/>) must be the same at the head, or the job is refused; the other differences of facts are reported and not judged.</para>
/// <para><b>Never green, never partial.</b> <c>not_configured</c> and <c>toolchain_refused</c> plan no step. One refused project refuses the repository: a repository that is half checked would be shown green.</para>
/// </remarks>
public static class ToolchainResolve
{
    /// <summary>The setting that turns detection off. Default on.</summary>
    public const string DetectVariable = "COAI_TOOLCHAIN_DETECT";

    private const string Cure = "name the commands in " + ToolchainTables.FilePath;

    /// <summary>Resolves the checks. <paramref name="atBase"/> and <paramref name="atHead"/> are asked only when the answer needs them (a file that states every command needs neither).</summary>
    /// <param name="file">The toolchain file read at the BASE commit.</param>
    /// <param name="atBase">Reads the manifests of the base commit.</param>
    /// <param name="atHead">Reads the manifests of the head commit.</param>
    /// <param name="detectSetting">The value of <see cref="DetectVariable"/>, or null.</param>
    public static ToolchainResolution Resolve(ToolchainRead file, Func<ManifestRead> atBase, Func<ManifestRead> atHead, string? detectSetting, ToolchainDefaults table)
    {
        var notes = new List<string>();
        var detect = DetectOn(detectSetting, notes);

        return file switch
        {
            ToolchainRead.Refused refused => new ToolchainResolution.Refused(refused.Sentence),
            ToolchainRead.Configured configured => FromFile(configured.Toolchain, atBase, atHead, table, notes, detect),
            _ => detect ? FromManifests(atBase, atHead, table, notes) : new ToolchainResolution.NotConfigured($"there is no {ToolchainTables.FilePath}, and detection is off ({DetectVariable}=off), so nothing was run; checks that did not run are never green"),
        };
    }

    /// <summary>The setting as a switch: <c>off</c> is off; <c>on</c>, nothing, and anything else is on — the last with a note, because a typo must not turn detection off silently.</summary>
    public static bool DetectOn(string? setting, List<string> notes)
    {
        var value = setting?.Trim();
        if (string.IsNullOrEmpty(value) || string.Equals(value, "on", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(value, "off", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        notes.Add($"{DetectVariable} is '{(value.Length > 20 ? value[..20] + "..." : value)}', which is neither on nor off, so detection stays on");

        return true;
    }

    private static bool Stated(ToolchainProject project) => project.Build.Stated || project.Test.Stated;

    // ---- (1) the file at the base ----

    private static ToolchainResolution FromFile(RepoToolchain file, Func<ManifestRead> atBase, Func<ManifestRead> atHead, ToolchainDefaults table, List<string> notes, bool detect)
    {
        // Detection off is E2's answer: a project that names no command runs nothing, as it did.
        var bare = detect ? file.Projects.Where(project => !Stated(project)).ToList() : new List<ToolchainProject>();
        if (bare.Count == 0)
        {
            return new ToolchainResolution.Resolved(new ResolvedToolchain(ToolchainSource.File, [.. file.Projects.Select(project => new ResolvedProject(project.Path, "file", false, project, null))], file, notes, []));
        }

        var baseRead = atBase();
        if (baseRead is ManifestRead.Refused baseRefused)
        {
            return new ToolchainResolution.Refused($"the project {PathSpelling.Spell(bare[0].Path)} of {ToolchainTables.FilePath} names no command, so its defaults were looked for, and: {baseRefused.Sentence}");
        }

        var headRead = atHead();
        if (headRead is ManifestRead.Refused headRefused)
        {
            return new ToolchainResolution.Refused($"the project {PathSpelling.Spell(bare[0].Path)} of {ToolchainTables.FilePath} names no command, so its defaults were looked for at the head too, and: {headRefused.Sentence}");
        }

        var baseSet = ((ManifestRead.Found)baseRead).Set;
        var headSet = ((ManifestRead.Found)headRead).Set;
        notes.AddRange(baseSet.Notes);
        var projects = new List<ResolvedProject>();
        var differences = new List<string>();
        foreach (var project in file.Projects)
        {
            if (Stated(project) || !detect)
            {
                projects.Add(new ResolvedProject(project.Path, "file", false, project, null));

                continue;
            }

            if (!table.Stacks.TryGetValue(project.Adapter, out var stack) || !stack.Built)
            {
                return new ToolchainResolution.Refused($"the project {PathSpelling.Spell(project.Path)} of {ToolchainTables.FilePath} names no command and its adapter '{project.Adapter}' has no defaults yet; name its build and test commands");
            }

            var folder = project.Path == "." ? string.Empty : project.Path;
            var baseDetected = DetectIn(project.Adapter, Scope(baseSet, folder), table);
            if (baseDetected is Detection.Refused baseFault)
            {
                return new ToolchainResolution.Refused($"the project {PathSpelling.Spell(project.Path)} of {ToolchainTables.FilePath} names no command, and its defaults were refused: {baseFault.Sentence}");
            }

            var headDetected = DetectIn(project.Adapter, Scope(headSet, folder), table);
            if (headDetected is Detection.Refused headFault)
            {
                return new ToolchainResolution.Refused($"the worktree of the project {PathSpelling.Spell(project.Path)} of {ToolchainTables.FilePath} is refused at the head: {headFault.Sentence}");
            }

            var baseProjects = ((Detection.Detected)baseDetected).Projects;
            if (baseProjects.Count == 0)
            {
                return new ToolchainResolution.Refused($"the project {PathSpelling.Spell(project.Path)} of {ToolchainTables.FilePath} names no command, and no {project.Adapter} manifest was found in {PathSpelling.Spell(project.Path)} at the base commit; name its build and test commands");
            }

            var headProjects = ((Detection.Detected)headDetected).Projects;
            var merged = Merge(baseProjects, headProjects, differences);
            if (merged.Refusal.Length > 0)
            {
                return new ToolchainResolution.Refused(merged.Refusal);
            }

            if (projects.Count + merged.Projects.Count > ManifestLimits.MaxProjects)
            {
                return new ToolchainResolution.Refused($"the projects of {ToolchainTables.FilePath} and their defaults are more than the limit of {ManifestLimits.MaxProjects.ToString(CultureInfo.InvariantCulture)}");
            }

            if (Frozen(baseProjects, headProjects, baseSet, headSet) is { Length: > 0 } changedDefinition)
            {
                return new ToolchainResolution.Refused(changedDefinition);
            }

            projects.AddRange(merged.Projects.Select(item => new ResolvedProject(item.Detected.Id, "defaults", item.DefinedByHead, null, item.Detected)));
            notes.AddRange(baseProjects.SelectMany(item => item.Notes));
        }

        return new ToolchainResolution.Resolved(new ResolvedToolchain(ToolchainSource.FileAndDefaults, projects, file, notes, differences));
    }

    /// <summary>The manifests a project folder owns, plus the definition and presence files of its ancestors (a root <c>global.json</c> or <c>.editorconfig</c> counts for a folder below it).</summary>
    private static ManifestSet Scope(ManifestSet set, string folder) =>
        set with
        {
            Files = [.. set.Files.Where(file => folder.Length == 0 || ProjectGraph.Under(folder, file.Path) || (file.Kind != ManifestKind.Manifest && ProjectGraph.Under(ProjectGraph.Folder(file.Path), folder + "/x")))],
        };

    private static Detection DetectIn(string adapter, ManifestSet set, ToolchainDefaults table) =>
        adapter == "dotnet" ? DotnetDetector.Detect(set, table) : NodeDetector.Detect(set, table);

    // ---- (2) no file at the base ----

    private static ToolchainResolution FromManifests(Func<ManifestRead> atBase, Func<ManifestRead> atHead, ToolchainDefaults table, List<string> notes)
    {
        var baseRead = atBase();
        if (baseRead is ManifestRead.Refused baseRefused)
        {
            return new ToolchainResolution.Refused($"there is no {ToolchainTables.FilePath} at the base commit, so the manifests were read, and: {baseRefused.Sentence}");
        }

        var headRead = atHead();
        if (headRead is ManifestRead.Refused headRefused)
        {
            return new ToolchainResolution.Refused($"there is no {ToolchainTables.FilePath} at the base commit, so the manifests of the head commit were read, and: {headRefused.Sentence}");
        }

        var baseSet = ((ManifestRead.Found)baseRead).Set;
        var headSet = ((ManifestRead.Found)headRead).Set;
        notes.AddRange(baseSet.Notes);
        foreach (var (set, where) in new[] { (baseSet, "base"), (headSet, "head") })
        {
            if (Unbuilt(set, table) is { } unbuilt)
            {
                return new ToolchainResolution.Refused($"{PathSpelling.Spell(unbuilt.Path)} is a {unbuilt.Stack} manifest at the {where} commit; {unbuilt.Stack} is a known stack with no adapter yet, so a repository that holds one is not checked by guessing; {Cure}");
            }
        }

        var any = baseSet.Of(ManifestKind.Manifest).Any() || headSet.Of(ManifestKind.Manifest).Any();
        if (!any)
        {
            return new ToolchainResolution.NotConfigured($"there is no {ToolchainTables.FilePath} and no manifest of a known stack at the base or the head commit, so nothing was run; checks that did not run are never green");
        }

        var baseDetected = DetectAll(baseSet, table);
        if (baseDetected is Detection.Refused baseFault)
        {
            return new ToolchainResolution.Refused(baseFault.Sentence);
        }

        var headDetected = DetectAll(headSet, table);
        if (headDetected is Detection.Refused headFault)
        {
            return new ToolchainResolution.Refused($"at the head commit: {headFault.Sentence}");
        }

        var differences = new List<string>();
        var baseProjects = ((Detection.Detected)baseDetected).Projects;
        var headProjects = ((Detection.Detected)headDetected).Projects;
        var merged = Merge(baseProjects, headProjects, differences);
        if (merged.Refusal.Length > 0)
        {
            return new ToolchainResolution.Refused(merged.Refusal);
        }

        if (merged.Projects.Count > ManifestLimits.MaxProjects)
        {
            return new ToolchainResolution.Refused($"{merged.Projects.Count.ToString(CultureInfo.InvariantCulture)} projects were found in all (the head added some), over the limit of {ManifestLimits.MaxProjects.ToString(CultureInfo.InvariantCulture)}; {Cure}");
        }

        if (Frozen(baseProjects, headProjects, baseSet, headSet) is { Length: > 0 } changedDefinition)
        {
            return new ToolchainResolution.Refused(changedDefinition);
        }

        if (merged.Projects.All(item => item.Detected.Steps.Count == 0))
        {
            return new ToolchainResolution.NotConfigured($"every project found is unchecked (no build, typecheck or test step: {PathSpelling.SpellMany(merged.Projects.Select(item => item.Detected.Id))}), so nothing was run; checks that did not run are never green");
        }

        notes.AddRange(baseProjects.SelectMany(item => item.Notes));

        return new ToolchainResolution.Resolved(new ResolvedToolchain(ToolchainSource.Detected, [.. merged.Projects.Select(item => new ResolvedProject(item.Detected.Id, "detected", item.DefinedByHead, null, item.Detected))], null, notes, differences));
    }

    private static ManifestFile? Unbuilt(ManifestSet set, ToolchainDefaults table) =>
        set.Of(ManifestKind.Manifest).FirstOrDefault(file => table.Stacks.TryGetValue(file.Stack, out var stack) && !stack.Built);

    private static Detection DetectAll(ManifestSet set, ToolchainDefaults table)
    {
        var dotnet = DotnetDetector.Detect(set, table);
        if (dotnet is Detection.Refused)
        {
            return dotnet;
        }

        var node = NodeDetector.Detect(set, table);
        if (node is Detection.Refused)
        {
            return node;
        }

        var all = ((Detection.Detected)dotnet).Projects.Concat(((Detection.Detected)node).Projects).OrderBy(project => project.Id, StringComparer.Ordinal).ToList();

        return all.Count > ManifestLimits.MaxProjects
            ? new Detection.Refused($"{all.Count.ToString(CultureInfo.InvariantCulture)} projects were found in all, over the limit of {ManifestLimits.MaxProjects.ToString(CultureInfo.InvariantCulture)}; {Cure}")
            : new Detection.Detected(all);
    }

    // ---- both trees ----

    private sealed record MergedProject(DetectedProject Detected, bool DefinedByHead);

    private sealed record Merged(IReadOnlyList<MergedProject> Projects, string Refusal);

    /// <summary>
    /// The base's projects (their definition is the base's own), plus the projects only the head has (marked), and the refusal for a step the base had and the head has not. A difference of a fact is listed and is not judged here.
    /// </summary>
    private static Merged Merge(IReadOnlyList<DetectedProject> atBase, IReadOnlyList<DetectedProject> atHead, List<string> differences)
    {
        var merged = new List<MergedProject>();
        foreach (var project in atBase)
        {
            var head = atHead.FirstOrDefault(candidate => candidate.Id == project.Id);
            foreach (var step in project.Steps)
            {
                if (head is null || !head.Steps.Any(candidate => candidate.Name == step.Name && candidate.Subject == step.Subject))
                {
                    var label = step.Name == "test" && step.Subject != project.Id ? $"test ({PathSpelling.Bare(step.Subject)})" : step.Name;

                    return new Merged([], $"the worktree removed {label} of {PathSpelling.Bare(project.Id)}; a step the base commit had is never lost at the head, so nothing was run. Land the change in a commit of its own, or {Cure}");
                }
            }

            if (head is not null)
            {
                differences.AddRange(Differences(project, head));
            }

            merged.Add(new MergedProject(project, false));
        }

        merged.AddRange(atHead.Where(project => atBase.All(candidate => candidate.Id != project.Id)).Select(project => new MergedProject(project, true)));

        return new Merged([.. merged.OrderBy(item => item.Detected.Id, StringComparer.Ordinal)], string.Empty);
    }

    /// <summary>A fact value for a sentence: the value of a short word fact as it is, a list of paths spelled and capped.</summary>
    private static string Shown(string name, string value) => name is "projects" or "testProjects" ? PathSpelling.SpellFact(value) : $"'{(value.Length > 40 ? value[..40] + "..." : value)}'";

    /// <summary>
    /// <b>A detected check cannot be changed by the job it checks</b> (SPEC §A4, decision 11): for every project the base has, the check definition (<see cref="CheckDefinition"/>) at the head must be the base's. The first project whose definition differs gives the refusal, which names the member and the cure; empty when every definition is the same. A project only the head has is not compared.
    /// </summary>
    private static string Frozen(IReadOnlyList<DetectedProject> atBase, IReadOnlyList<DetectedProject> atHead, ManifestSet baseSet, ManifestSet headSet)
    {
        foreach (var project in atBase)
        {
            var head = atHead.FirstOrDefault(candidate => candidate.Id == project.Id);
            if (head is null)
            {
                continue;
            }

            var changed = CheckDefinition.Changed(CheckDefinition.Of(project, baseSet), CheckDefinition.Of(head, headSet));
            if (changed.Count > 0)
            {
                return CheckDefinition.Refusal(project.Id, changed);
            }
        }

        return string.Empty;
    }

    private static IEnumerable<string> Differences(DetectedProject baseProject, DetectedProject head)
    {
        foreach (var (name, value) in baseProject.Facts)
        {
            var headValue = head.Fact(name);
            if (!string.Equals(value, headValue, StringComparison.Ordinal))
            {
                yield return string.Create(CultureInfo.InvariantCulture, $"{PathSpelling.Bare(baseProject.Id)}: {name} was {Shown(name, value)} at the base and is {Shown(name, headValue)} at the head");
            }
        }
    }
}
