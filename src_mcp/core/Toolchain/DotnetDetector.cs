using System.Text.Json;

namespace CoaiMcp.Core.Toolchain;

/// <summary>
/// The .NET detector (epoch 3 E4 s2 t3): from the manifests of ONE commit it makes the units a repository has and the steps of each, or refuses. <b>Pure</b>: a <see cref="ManifestSet"/> and the defaults in, a <see cref="Detection"/> out; no file, no process, no MSBuild. What it does not
/// understand it refuses by name and with the cure — it never guesses and it never checks a part (SPEC decision 2).
/// </summary>
/// <remarks>
/// <para><b>Units.</b> A folder with ONE solution file (<c>.slnx</c> or <c>.sln</c>) is one unit and owns every project file below it; a project file outside every unit that no other project file references is a unit of its own. Two solution files in one folder, and a solution inside another unit's
/// folder, are refusals that name both files. More than 20 units is a refusal.</para>
/// <para><b>Test projects and their runner</b> are read from the facts story s1 measured (<c>RESULTS_s1_dotnet.md</c>): a project that references <c>Microsoft.NET.Test.Sdk</c> is a VSTest project; one that sets <c>UseMicrosoftTestingPlatformRunner</c> or <c>TestingPlatformDotnetTestSupport</c> is an MTP
/// project; <c>IsTestProject</c> set to false takes a project out. MTP's <c>dotnet test</c> refuses to run unless <c>global.json</c> at the repository root opts in (<c>test.runner</c> = <c>Microsoft.Testing.Platform</c>), and refuses a VSTest project when it does: so a unit whose test projects have two
/// runners, an MTP project with no opt-in and a VSTest project under an opt-in are refusals that name the cure. The <c>global.json</c> that counts is the ROOT one: every .NET step runs from the repository root, and the SDK, with the runner it opts in to, follows the working folder (s1).</para>
/// <para><b>The steps</b> are the table's argv with <c>{unit}</c> filled: the solution or project file for restore and build, each test project for its test step. <c>{jobs}</c> and <c>{file}</c> stay for the runner. A unit with no test project has no test step and says <c>tests: none</c>.</para>
/// </remarks>
public static class DotnetDetector
{
    /// <summary>The most units a repository may have.</summary>
    public const int MaxUnits = ManifestLimits.MaxProjects;

    private const string Cure = "name the commands in " + ToolchainTables.FilePath;

    private const string MtpRunner = "Microsoft.Testing.Platform";

    /// <summary>Detects the .NET units of <paramref name="set"/>. No .NET manifest at all is an empty answer.</summary>
    public static Detection Detect(ManifestSet set, ToolchainDefaults table)
    {
        var stack = table.Stacks["dotnet"];
        var dotnet = set.Files.Where(file => file.Stack == "dotnet" && file.Kind == ManifestKind.Manifest).ToList();
        var solutions = dotnet.Where(file => IsSolution(file.Path)).ToList();
        var projectFiles = dotnet.Where(file => !IsSolution(file.Path)).ToList();
        if (dotnet.Count == 0)
        {
            return new Detection.Detected([]);
        }

        if (SolutionRefusal(solutions) is { Length: > 0 } clash)
        {
            return new Detection.Refused(clash);
        }

        var props = set.Files.Where(file => file.Stack == "dotnet" && file.Kind == ManifestKind.Definition && file.Path.Split('/')[^1] == "Directory.Build.props").ToDictionary(file => ProjectGraph.Folder(file.Path), file => file.Text, StringComparer.Ordinal);
        foreach (var definition in set.Files.Where(file => file.Stack == "dotnet" && file.Kind == ManifestKind.Definition && (file.Path.EndsWith(".props", StringComparison.OrdinalIgnoreCase) || file.Path.EndsWith(".targets", StringComparison.OrdinalIgnoreCase))))
        {
            if (!ProjectGraph.IsWellFormed(definition.Text))
            {
                return new Detection.Refused($"{PathSpelling.Spell(definition.Path)} is not well-formed XML, or holds a DOCTYPE, which is never processed; MSBuild would import it, so the facts of the projects cannot be told (fail closed); {Cure}");
            }
        }

        var facts = new List<ProjectFacts>();
        foreach (var project in projectFiles)
        {
            var (read, fault) = ProjectGraph.Read(project.Path, project.Text, NearestProps(props, ProjectGraph.Folder(project.Path)));
            if (fault is not null)
            {
                return new Detection.Refused($"{PathSpelling.Spell(fault.Path)} {fault.Why}; {Cure}");
            }

            facts.Add(read!);
        }

        var units = Units(solutions, facts);
        if (units.Count > MaxUnits)
        {
            return new Detection.Refused($"{ProjectGraph.Number(units.Count)} .NET units were found, over the limit of {ProjectGraph.Number(MaxUnits)}; {Cure}");
        }

        var optIn = RootRunnerIsMtp(set, out var globalRefusal);
        if (globalRefusal.Length > 0)
        {
            return new Detection.Refused(globalRefusal);
        }

        var hasEditorConfig = set.Files.Any(file => file.Kind == ManifestKind.Present && file.Path == ".editorconfig");
        var detected = new List<DetectedProject>();
        foreach (var unit in units.OrderBy(unit => unit.Path, StringComparer.Ordinal))
        {
            var answer = Plan(unit, facts, stack, optIn, hasEditorConfig);
            if (answer.Refusal.Length > 0)
            {
                return new Detection.Refused(answer.Refusal);
            }

            detected.Add(answer.Project!);
        }

        return new Detection.Detected(detected);
    }

    private sealed record Unit(string Path, string Folder, bool IsSolution);

    private static bool IsSolution(string path) => path.EndsWith(".slnx", StringComparison.Ordinal) || path.EndsWith(".sln", StringComparison.Ordinal);

    private static string SolutionRefusal(List<ManifestFile> solutions)
    {
        foreach (var group in solutions.GroupBy(file => ProjectGraph.Folder(file.Path)))
        {
            var files = group.Select(file => file.Path).Order(StringComparer.Ordinal).ToList();
            if (files.Count > 1)
            {
                return $"two solution files in one folder, {PathSpelling.Spell(files[0])} and {PathSpelling.Spell(files[1])}: coai cannot tell which is the repository's; {Cure}";
            }
        }

        foreach (var outer in solutions)
        {
            foreach (var inner in solutions.Where(inner => inner.Path != outer.Path))
            {
                if (ProjectGraph.Folder(inner.Path) != ProjectGraph.Folder(outer.Path) && ProjectGraph.Under(ProjectGraph.Folder(outer.Path), ProjectGraph.Folder(inner.Path)))
                {
                    return $"the solution {PathSpelling.Spell(inner.Path)} lies inside the folder of the solution {PathSpelling.Spell(outer.Path)}: coai cannot tell which units are the repository's; {Cure}";
                }
            }
        }

        return string.Empty;
    }

    internal static string NearestProps(Dictionary<string, string> props, string folder)
    {
        var current = folder;
        while (true)
        {
            if (props.TryGetValue(current, out var text))
            {
                return text;
            }

            if (current.Length == 0)
            {
                return string.Empty;
            }

            current = ProjectGraph.Folder(current);
        }
    }

    private static List<Unit> Units(List<ManifestFile> solutions, List<ProjectFacts> projects)
    {
        var units = solutions.Select(solution => new Unit(solution.Path, ProjectGraph.Folder(solution.Path), IsSolution: true)).ToList();
        var solutionUnits = units.ToList();

        // Only a SOLUTION owns the folder it lies in: two project files in one folder are two units (a project unit added a moment ago must not own the next one).
        foreach (var project in projects.Where(project => !solutionUnits.Any(unit => ProjectGraph.Under(unit.Folder, project.Path)) && ProjectGraph.ReferencedBy(project.Path, projects) == 0).ToList())
        {
            units.Add(new Unit(project.Path, ProjectGraph.Folder(project.Path), IsSolution: false));
        }

        return units;
    }

    private static bool RootRunnerIsMtp(ManifestSet set, out string refusal)
    {
        refusal = string.Empty;
        var global = set.Files.FirstOrDefault(file => file.Path == "global.json" && file.Kind == ManifestKind.Definition);
        if (global is null)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(global.Text, new JsonDocumentOptions { MaxDepth = 32, AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("test", out var test) && test.ValueKind == JsonValueKind.Object
                && test.TryGetProperty("runner", out var runner) && runner.ValueKind == JsonValueKind.String)
            {
                return string.Equals(runner.GetString(), MtpRunner, StringComparison.Ordinal);
            }
        }
        catch (JsonException)
        {
            refusal = $"'global.json' is not JSON that coai can read, so the test runner of the repository cannot be told; {Cure}";
        }

        return false;
    }

    private sealed record Planned(DetectedProject? Project, string Refusal);

    private static Planned Plan(Unit unit, List<ProjectFacts> all, StackDefaults stack, bool mtpOptIn, bool hasEditorConfig)
    {
        var owned = unit.IsSolution ? all.Where(project => ProjectGraph.Under(unit.Folder, project.Path)).ToList() : all.Where(project => project.Path == unit.Path).ToList();
        var tests = new List<(ProjectFacts Project, string Runner)>();
        var notes = new List<string>();
        foreach (var project in owned.OrderBy(project => project.Path, StringComparer.Ordinal))
        {
            if (project.Outside.Count > 0)
            {
                notes.Add($"{PathSpelling.Spell(project.Path)}: {ProjectGraph.Number(project.Outside.Count)} ProjectReference(s) that leave the repository were not read");
            }

            if (project.Unresolved.Count > 0)
            {
                notes.Add($"{PathSpelling.Spell(project.Path)}: {ProjectGraph.Number(project.Unresolved.Count)} ProjectReference(s) that hold a property or a wildcard cannot be resolved");
            }

            if (project.Conditioned > 0)
            {
                notes.Add($"{PathSpelling.Spell(project.Path)}: {ProjectGraph.Number(project.Conditioned)} element(s) with a condition were not evaluated and were left out");
            }

            if (RunnerOf(project) is { } runner)
            {
                if (runner.Length == 0)
                {
                    return new Planned(null, $"{PathSpelling.Spell(project.Path)} is a test project (IsTestProject) whose runner coai cannot tell: it neither references {ProjectGraph.VsTestPackage} nor sets an MTP property; {Cure}");
                }

                tests.Add((project, runner));
            }
        }

        var runners = tests.Select(test => test.Runner).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        if (runners.Count > 1)
        {
            return new Planned(null, $"the test projects of {PathSpelling.Spell(unit.Path)} have two runners (VSTest: {PathSpelling.SpellMany(tests.Where(t => t.Runner == "vstest").Select(t => t.Project.Path))}; MTP: {PathSpelling.SpellMany(tests.Where(t => t.Runner == "mtp").Select(t => t.Project.Path))}); {Cure}");
        }

        if (runners.Count == 1 && runners[0] == "mtp" && !mtpOptIn)
        {
            return new Planned(null, $"{PathSpelling.Spell(tests.First().Project.Path)} is an MTP test project, and 'dotnet test' refuses it unless global.json at the repository root sets \"test\": {{ \"runner\": \"{MtpRunner}\" }}; set it, or {Cure}");
        }

        if (runners.Count == 1 && runners[0] == "vstest" && mtpOptIn)
        {
            return new Planned(null, $"global.json at the repository root opts in to {MtpRunner}, and 'dotnet test' then refuses the VSTest project {PathSpelling.Spell(tests.First().Project.Path)}; {Cure}");
        }

        var steps = new List<DetectedStep>();
        void Add(string name, IReadOnlyList<string> argv, string subject)
        {
            var step = stack.Steps[name]!;
            steps.Add(new DetectedStep(name, [.. argv.Select(item => item.Replace("{unit}", "./" + subject, StringComparison.Ordinal))], step.Network, ".", subject, name == "format" ? step.Extensions : []));
        }

        Add("restore", stack.Steps["restore"]!.Argv, unit.Path);
        if (hasEditorConfig)
        {
            Add("format", stack.Steps["format"]!.Argv, unit.Path);
        }

        Add("build", stack.Steps["build"]!.Argv, unit.Path);
        var runnerName = runners.Count == 1 ? runners[0] : "none";
        foreach (var (project, runner) in tests)
        {
            Add("test", stack.Steps["test"]!.ByRunner[runner], project.Path);
        }

        var facts = new List<KeyValuePair<string, string>>
        {
            new("kind", unit.IsSolution ? "solution" : "project"),
            new("projects", string.Join(',', owned.Select(project => project.Path).Order(StringComparer.Ordinal))),
            new("testProjects", string.Join(',', tests.Select(test => test.Project.Path))),
            new("runner", runnerName),
            new("mtpOptIn", mtpOptIn ? "true" : "false"),
            new("editorconfig", hasEditorConfig ? "true" : "false"),
            new("tests", tests.Count == 0 ? "none" : ProjectGraph.Number(tests.Count)),
        };

        return new Planned(new DetectedProject("dotnet", unit.Path, unit.Folder.Length == 0 ? "." : unit.Folder, steps, facts, notes, TestsNone: tests.Count == 0), string.Empty);
    }

    /// <summary>The runner of a project as the measured facts say: <c>mtp</c>, <c>vstest</c>, empty for a test project whose runner cannot be told, null for a project that is not a test project.</summary>
    internal static string? RunnerOf(ProjectFacts project)
    {
        var mtp = (project.Properties.TryGetValue("UseMicrosoftTestingPlatformRunner", out var a) && a) || (project.Properties.TryGetValue("TestingPlatformDotnetTestSupport", out var b) && b);
        var vstest = project.Packages.Contains(ProjectGraph.VsTestPackage, StringComparer.OrdinalIgnoreCase);
        var isTest = project.Properties.TryGetValue("IsTestProject", out var flag) ? flag : mtp || vstest;
        if (!isTest)
        {
            return null;
        }

        return mtp ? "mtp" : vstest ? "vstest" : string.Empty;
    }
}
