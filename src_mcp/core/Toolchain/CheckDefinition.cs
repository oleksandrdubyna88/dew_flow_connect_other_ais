using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace CoaiMcp.Core.Toolchain;

/// <summary>
/// One member of a check definition: a name a person can read in a refusal, the values it holds, and how a head may differ from the base. <see cref="AllowAdditions"/>: the base's values must all still be there, more are fine (a test project added); otherwise the values must be EQUAL.
/// <see cref="IgnoreWhenNew"/>: a member only the head has says nothing when it is empty or belongs to a project file the head added (such a project has its own definition, <c>definedByHead</c>).
/// </summary>
public sealed record DefinitionMember(string Name, IReadOnlyList<string> Values, bool AllowAdditions = false, bool IgnoreWhenNew = false);

/// <summary>
/// <b>The check definition of a detected or defaulted project</b> (epoch 3 E4 s3 t5; SPEC §A4, decision 11): what decides WHICH commands a default step runs and what they run. A coder's work is checked by commands coai chose from the base's manifests; if the head could change the manifest members those commands read — replace
/// <c>scripts.test</c> with <c>echo passed</c> — the checks would pass whatever the code did. So the definition of a project the base has must be the same at the head, or the job is refused before any step.
/// </summary>
/// <remarks>
/// <para><b>Node</b>: every member of <c>scripts</c> of the package and of every workspace member (a script ADDED is a change too), the name of the lock file, the members that configure a runner or a tool (<c>workspaces</c>, <c>overrides</c>, <c>jest</c>, <c>vitest</c>, <c>prettier</c>, <c>config</c>…), the version specifier of the packages a default command runs
/// (<c>typescript</c>, <c>prettier</c>, <c>jest</c>, <c>vitest</c>…: a changed one can be an alias or a <c>file:</c> package; a NEW dependency is not a change), and the configuration files that decide the run (<c>.npmrc</c>, <c>jest.config.*</c>, <c>vitest.config.*</c>, <c>tsconfig.json</c> when it is the typecheck, the prettier configuration…) by object id.</para>
/// <para><b>.NET</b>: the test projects the solution file names; the runner of each test project; the <c>test</c> member of <c>global.json</c> and EVERY <c>global.json</c> at or above the unit by object id (<c>sdk</c> and <c>msbuild-sdks</c> choose the code that runs at restore); for each project file and each <c>.props</c> and <c>.targets</c> that applies: the names of every <c>Target</c>,
/// <c>Import</c> and <c>UsingTask</c>, the <c>Sdk</c>, and the RAW test-fact elements with every condition around them and without case; the <c>.props</c> and <c>.targets</c> files in <c>obj</c> folders, which MSBuild imports by a wildcard of its own; and the files that configure the run (<c>nuget.config</c>,
/// <c>Directory.Build.rsp</c>, <c>MSBuild.rsp</c>, <c>*.runsettings</c>, <c>testconfig.json</c>) by object id.</para>
/// <para><b>Restore on the host network takes its addresses from these files</b> (a <c>nuget.config</c> source, an <c>.npmrc</c> registry, <c>msbuild-sdks</c>): a head that adds or changes one is refused. That is the interim decision for the owner (item 12 of the list of the s2 security verify); no wider policy is invented here.</para>
/// <para><b>Not a definition</b> (D14): a new or changed source or test file, a new <c>PackageReference</c>, <c>ProjectReference</c> or <c>PackageVersion</c>, a new dependency in <c>package.json</c> with its lock file. <b>Stated limits:</b> what a script or a target runs INSIDE the tree (a script file, the test code) is the head's, as in E2 — the code gate reads the diff; a wildcard or property
/// <c>Import</c> of the base can pull in a file the head adds in a place the search does not read; the contents of a lock file (its <c>resolved</c> addresses) are not compared.</para>
/// </remarks>
public static partial class CheckDefinition
{
    private const string Cure = "name the commands in " + ToolchainTables.FilePath;

    private static readonly string[] NodeConfigMembers = ["workspaces", "overrides", "resolutions", "packageManager", "jest", "vitest", "prettier", "config", "mocha", "ava", "babel"];

    private static readonly string[] WatchedPackages = ["typescript", "prettier", "jest", "vitest", "ts-jest", "ts-node", "mocha", "ava"];

    private static readonly string[] DependencySections = ["dependencies", "devDependencies", "optionalDependencies", "peerDependencies"];

    /// <summary>The definition of <paramref name="project"/> as <paramref name="set"/> (one commit's manifests) has it.</summary>
    public static IReadOnlyList<DefinitionMember> Of(DetectedProject project, ManifestSet set) =>
        project.Stack == "node" ? NodeMembers(project, set) : project.Stack == "dotnet" ? DotnetMembers(project, set) : [];

    /// <summary>The names of the members of the base's definition that the head changed (or, for a member a head may not add to, added), in the order of the definition. Empty: the same.</summary>
    public static IReadOnlyList<string> Changed(IReadOnlyList<DefinitionMember> atBase, IReadOnlyList<DefinitionMember> atHead)
    {
        var changed = new List<string>();
        foreach (var member in atBase)
        {
            var head = atHead.FirstOrDefault(candidate => candidate.Name == member.Name);
            var headValues = head?.Values ?? [];
            var same = member.AllowAdditions ? member.Values.All(value => headValues.Contains(value, StringComparer.Ordinal)) : member.Values.SequenceEqual(headValues, StringComparer.Ordinal);
            if (!same)
            {
                changed.Add(member.Name);
            }
        }

        changed.AddRange(atHead.Where(member => !member.IgnoreWhenNew && member.Values.Count > 0 && atBase.All(candidate => candidate.Name != member.Name)).Select(member => member.Name));

        return changed;
    }

    /// <summary>The refusal for a changed definition: it names the member and the project and the cure; empty when nothing changed. Member names carry only spelled paths (<see cref="PathSpelling"/>); no value of the head is in it.</summary>
    public static string Refusal(string projectId, IReadOnlyList<string> changed) =>
        changed.Count == 0
            ? string.Empty
            : $"the worktree changed {changed[0]} of {PathSpelling.Bare(projectId)}{(changed.Count > 1 ? $" (and {changed.Count - 1} more: {string.Join(", ", changed.Skip(1).Take(PathSpelling.MaxListed))})" : string.Empty)}; what a detected check runs is decided by the base commit, so a job cannot change what it is checked with, and nothing was run. Land the change in a commit of its own (it is then in the base of the next job), or {Cure}";

    // ---- node ----

    private static List<DefinitionMember> NodeMembers(DetectedProject project, ManifestSet set)
    {
        var members = new List<DefinitionMember> { new("the lock file", [project.Fact("lock")]) };
        var folder = project.Folder == "." ? string.Empty : project.Folder;
        var package = set.Files.FirstOrDefault(file => file.Path == project.Id && file.Kind == ManifestKind.Manifest);
        if (package is null)
        {
            return members;
        }

        try
        {
            using var document = JsonDocument.Parse(package.Text, new JsonDocumentOptions { MaxDepth = 32 });
            var root = document.RootElement;
            members.AddRange(ScriptsOf(root, string.Empty));
            foreach (var name in NodeConfigMembers)
            {
                members.Add(new DefinitionMember($"package.json {name}", root.TryGetProperty(name, out var value) ? [Compact(value)] : []));
            }

            foreach (var name in WatchedPackages)
            {
                members.Add(new DefinitionMember($"the version of {name}", [.. DependencySections.Where(section => root.TryGetProperty(section, out var table) && table.ValueKind == JsonValueKind.Object && table.TryGetProperty(name, out _)).Select(section => $"{section}:{Compact(root.GetProperty(section).GetProperty(name))}")]));
            }

            if (project.Fact("workspaces") == "true" && NodeDetector.WorkspacePatternsOf(root) is { } patterns)
            {
                foreach (var member in set.Files.Where(file => file.Stack == "node" && file.Kind == ManifestKind.Manifest && file.Path != package.Path && NodeDetector.IsMember(folder, patterns, ProjectGraph.Folder(file.Path))))
                {
                    try
                    {
                        using var memberDocument = JsonDocument.Parse(member.Text, new JsonDocumentOptions { MaxDepth = 32 });
                        members.AddRange(ScriptsOf(memberDocument.RootElement, $" of {PathSpelling.Bare(member.Path)}"));
                    }
                    catch (JsonException)
                    {
                        members.Add(new DefinitionMember($"package.json of {PathSpelling.Bare(member.Path)}", ["unreadable"]));
                    }
                }
            }
        }
        catch (JsonException)
        {
            members.Add(new DefinitionMember("package.json", ["unreadable"]));
        }

        members.Add(new DefinitionMember($"the configuration files of {PathSpelling.Bare(project.Id)}", NodeConfigFiles(project, set, folder)));

        return members;
    }

    private static IEnumerable<DefinitionMember> ScriptsOf(JsonElement root, string suffix) =>
        root.TryGetProperty("scripts", out var scripts) && scripts.ValueKind == JsonValueKind.Object
            ? scripts.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal).Select(property => new DefinitionMember($"scripts.{property.Name}{suffix}", [property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString()! : property.Value.GetRawText()])).ToList()
            : [];

    private static string Compact(JsonElement value) => value.GetRawText().Replace(" ", string.Empty, StringComparison.Ordinal).Replace("\n", string.Empty, StringComparison.Ordinal).Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\t", string.Empty, StringComparison.Ordinal);

    /// <summary>The configuration files of a node project by object id: those at its folder or above it that a runner or npm reads. <c>tsconfig.json</c> counts when it is the typecheck, the prettier configuration when prettier is the formatter; a lock file never does (D14).</summary>
    private static List<string> NodeConfigFiles(DetectedProject project, ManifestSet set, string folder)
    {
        var typecheckIsTsc = project.Fact("typecheck") == "tsc";
        var prettier = project.Fact("prettier") == "true";

        return [.. set.Files
            .Where(file => file.Stack == "node" && file.Kind == ManifestKind.Present && AtOrAround(file.Path, folder))
            .Where(file => IsNodeConfig(file.Path.Split('/')[^1], typecheckIsTsc, prettier))
            .OrderBy(file => file.Path, StringComparer.Ordinal)
            .Select(file => $"{file.Path}@{file.Oid}")];
    }

    private static bool IsNodeConfig(string name, bool typecheckIsTsc, bool prettier) =>
        string.Equals(name, ".npmrc", StringComparison.OrdinalIgnoreCase)
        || new[] { "jest.config.*", "vitest.config.*", "vitest.workspace.*", "vite.config.*", ".mocharc*", "babel.config.*", ".babelrc*" }.Any(pattern => ManifestSearch.Matches(pattern, name, StringComparison.OrdinalIgnoreCase))
        || (typecheckIsTsc && string.Equals(name, "tsconfig.json", StringComparison.OrdinalIgnoreCase))
        || (prettier && (ManifestSearch.Matches(".prettierrc*", name, StringComparison.OrdinalIgnoreCase) || ManifestSearch.Matches("prettier.config.*", name, StringComparison.OrdinalIgnoreCase)));

    // ---- dotnet ----

    private static List<DefinitionMember> DotnetMembers(DetectedProject project, ManifestSet set)
    {
        var folder = project.Folder == "." ? string.Empty : project.Folder;
        var members = new List<DefinitionMember>();
        var props = set.Files.Where(file => file.Stack == "dotnet" && file.Kind == ManifestKind.Definition && file.Path.Split('/')[^1] == "Directory.Build.props").ToDictionary(file => ProjectGraph.Folder(file.Path), file => file.Text, StringComparer.Ordinal);
        var owned = project.Fact("projects").Split(',', StringSplitOptions.RemoveEmptyEntries);
        var tests = new List<string>();
        foreach (var path in owned)
        {
            var file = set.Files.FirstOrDefault(candidate => candidate.Path == path);
            if (file is null)
            {
                continue;
            }

            var (facts, _) = ProjectGraph.Read(path, file.Text, DotnetDetector.NearestProps(props, ProjectGraph.Folder(path)));
            if (facts is not null && DotnetDetector.RunnerOf(facts) is { Length: > 0 } runner)
            {
                tests.Add(path);
                members.Add(new DefinitionMember($"the runner of {PathSpelling.Bare(path)}", [runner], IgnoreWhenNew: true));
            }
        }

        members.Insert(0, new DefinitionMember("the test projects of " + PathSpelling.Bare(project.Id), tests, AllowAdditions: true));
        var solution = set.Files.FirstOrDefault(file => file.Path == project.Id && file.Kind == ManifestKind.Manifest && (file.Path.EndsWith(".slnx", StringComparison.Ordinal) || file.Path.EndsWith(".sln", StringComparison.Ordinal)));
        if (solution is not null)
        {
            var named = SolutionProjects(solution);
            members.Insert(1, new DefinitionMember($"the test projects {PathSpelling.Bare(project.Id)} names", [.. tests.Where(test => named.Contains(test, StringComparer.Ordinal))], AllowAdditions: true));
        }

        var globals = set.Files.Where(file => file.Stack == "dotnet" && file.Kind == ManifestKind.Definition && string.Equals(file.Path.Split('/')[^1], "global.json", StringComparison.OrdinalIgnoreCase) && AtOrAround(file.Path, folder)).OrderBy(file => file.Path, StringComparer.Ordinal).ToList();
        members.Add(new DefinitionMember("the test member of global.json", [GlobalTest(globals.FirstOrDefault(file => file.Path == "global.json"))]));
        members.Add(new DefinitionMember("the global.json files at or above " + PathSpelling.Bare(project.Id), [.. globals.Select(file => $"{file.Path}@{file.Oid}")]));
        foreach (var file in set.Files.Where(file => file.Stack == "dotnet" && file.Kind is ManifestKind.Manifest or ManifestKind.Definition && Applies(file.Path, folder, owned)))
        {
            var isProject = file.Kind == ManifestKind.Manifest;
            var label = PathSpelling.Bare(file.Path);
            members.Add(new DefinitionMember($"the Targets, Imports and UsingTasks of {label}", ProjectGraph.BuildElements(file.Text) ?? ["unreadable"], IgnoreWhenNew: isProject));
            members.Add(new DefinitionMember($"the test-fact elements of {label}", ProjectGraph.TestFactElements(file.Text) ?? ["unreadable"], IgnoreWhenNew: isProject));
        }

        members.Add(new DefinitionMember($"the configuration files of {PathSpelling.Bare(project.Id)}", [.. set.Files.Where(file => file.Stack == "dotnet" && file.Kind == ManifestKind.Present && IsDotnetConfig(file.Path.Split('/')[^1]) && AtOrAround(file.Path, folder)).OrderBy(file => file.Path, StringComparer.Ordinal).Select(file => $"{file.Path}@{file.Oid}")]));
        members.Add(new DefinitionMember($"the .props and .targets files in obj folders of {PathSpelling.Bare(project.Id)}", [.. set.Files.Where(file => file.Kind == ManifestKind.Present && ManifestSearch.IsGeneratedImport(file.Path) && ProjectGraph.Under(folder, file.Path)).OrderBy(file => file.Path, StringComparer.Ordinal).Select(file => $"{file.Path}@{file.Oid}")]));

        return members;
    }

    /// <summary>A file in the project's folder, below it, or in a folder above it: where a tool reading from the project's folder can find it.</summary>
    private static bool AtOrAround(string path, string folder) => ProjectGraph.Under(folder, path) || ProjectGraph.Under(ProjectGraph.Folder(path), folder + "/x");

    private static bool IsDotnetConfig(string name) =>
        string.Equals(name, "nuget.config", StringComparison.OrdinalIgnoreCase)
        || new[] { "Directory.Build.rsp", "MSBuild.rsp", "testconfig.json" }.Any(candidate => string.Equals(name, candidate, StringComparison.OrdinalIgnoreCase))
        || name.EndsWith(".runsettings", StringComparison.OrdinalIgnoreCase);

    /// <summary>A file the unit's definition reads: one of its owned project files, a <c>.props</c> or <c>.targets</c> under its folder, or a <c>Directory.Build.*</c> file above it.</summary>
    private static bool Applies(string path, string folder, string[] owned)
    {
        if (path.EndsWith(".csproj", StringComparison.Ordinal) || path.EndsWith(".fsproj", StringComparison.Ordinal) || path.EndsWith(".vbproj", StringComparison.Ordinal))
        {
            return owned.Contains(path, StringComparer.Ordinal);
        }

        if (!(path.EndsWith(".props", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".targets", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var name = path.Split('/')[^1];

        return ProjectGraph.Under(folder, path) || (name.StartsWith("Directory.Build.", StringComparison.OrdinalIgnoreCase) && ProjectGraph.Under(ProjectGraph.Folder(path), folder + "/x"));
    }

    private static string GlobalTest(ManifestFile? global)
    {
        if (global is null)
        {
            return "none";
        }

        try
        {
            using var document = JsonDocument.Parse(global.Text, new JsonDocumentOptions { MaxDepth = 32, AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });

            return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("test", out var test) ? Compact(test) : "none";
        }
        catch (JsonException)
        {
            return "unreadable";
        }
    }

    /// <summary>The project files a solution file names, as repository paths: <c>.slnx</c> is XML (<c>&lt;Project Path="…"/&gt;</c>), <c>.sln</c> has <c>Project("{type}") = "name", "path", "{guid}"</c> lines.</summary>
    public static IReadOnlyList<string> SolutionProjects(ManifestFile solution)
    {
        var folder = ProjectGraph.Folder(solution.Path);
        var paths = new List<string>();
        if (solution.Path.EndsWith(".slnx", StringComparison.Ordinal))
        {
            try
            {
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = ManifestLimits.MaxManifestBytes };
                using var reader = XmlReader.Create(new StringReader(solution.Text), settings);
                var document = XDocument.Load(reader);
                paths.AddRange(document.Descendants().Where(element => element.Name.LocalName == "Project").Select(element => (string?)element.Attribute("Path") ?? string.Empty));
            }
            catch (XmlException)
            {
                return [];
            }
        }
        else
        {
            paths.AddRange(SlnProject().Matches(solution.Text).Select(match => match.Groups[1].Value));
        }

        return [.. paths.Select(path => Resolve(folder, path)).Where(path => path.Length > 0).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    private static string Resolve(string folder, string include)
    {
        var parts = new List<string>(folder.Length == 0 ? [] : folder.Split('/'));
        foreach (var segment in include.Trim().Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (parts.Count == 0)
                {
                    return string.Empty;
                }

                parts.RemoveAt(parts.Count - 1);

                continue;
            }

            parts.Add(segment);
        }

        return string.Join('/', parts);
    }

    [GeneratedRegex("^Project\\(\"[^\"]*\"\\)\\s*=\\s*\"[^\"]*\"\\s*,\\s*\"([^\"]+)\"", RegexOptions.Multiline | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SlnProject();
}
