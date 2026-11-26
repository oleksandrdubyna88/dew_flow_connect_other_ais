using System.Globalization;
using System.Text.Json;

namespace CoaiMcp.Core.Toolchain;

/// <summary>
/// The Node detector (epoch 3 E4 s2 t4): from the manifests of ONE commit it makes the npm projects of a repository and the steps of each, or refuses. <b>Pure</b>: a <see cref="ManifestSet"/> and the defaults in, a <see cref="Detection"/> out.
/// </summary>
/// <remarks>
/// <para><b>Projects.</b> Each <c>package.json</c> is a project; a root with <c>workspaces</c> is ONE project and every <c>package.json</c> under it is a member, not a project. The JSON is read with a depth limit of 32; text that is not JSON is a refusal that names the file.</para>
/// <para><b>Only npm.</b> <c>package-lock.json</c> or <c>npm-shrinkwrap.json</c> beside the manifest means npm. <c>pnpm-lock.yaml</c>, <c>yarn.lock</c>, <c>bun.lock</c> and <c>bun.lockb</c> are refused by the name of the package manager (owner's answer 2, 2026-11-25); two lock files and no lock file are refusals.</para>
/// <para><b>The steps come from facts only</b> (SPEC §A3, measured by s1): restore always (<c>npm ci --ignore-scripts</c>); build when <c>scripts.build</c> is there; typecheck when <c>scripts.typecheck</c> is there, else when <c>tsconfig.json</c> and the <c>typescript</c> package are both there; test when
/// <c>scripts.test</c> is there and is not npm's placeholder. The runner fact needs the package AND the word in <c>scripts.test</c>. Prettier is the formatter only when it is a devDependency AND has a configuration. <b>No argv item is copied from the manifest</b>: the argv are the table's.
/// A project with no build, typecheck and test step gets no step at all and is listed as unchecked.</para>
/// </remarks>
public static class NodeDetector
{
    /// <summary>The most workspace patterns one manifest may declare, and the longest one.</summary>
    internal const int MaxPatterns = 64;

    internal const int MaxPatternLength = 256;

    private const string Cure = "name the commands in " + ToolchainTables.FilePath;

    private static readonly string[] NpmLocks = ["package-lock.json", "npm-shrinkwrap.json"];

    private static readonly (string File, string Manager)[] OtherLocks = [("pnpm-lock.yaml", "pnpm"), ("yarn.lock", "yarn"), ("bun.lock", "bun"), ("bun.lockb", "bun")];

    /// <summary>Detects the Node projects of <paramref name="set"/>. No <c>package.json</c> at all is an empty answer.</summary>
    public static Detection Detect(ManifestSet set, ToolchainDefaults table)
    {
        var stack = table.Stacks["node"];
        var packages = set.Files.Where(file => file.Stack == "node" && file.Kind == ManifestKind.Manifest).OrderBy(file => file.Path, StringComparer.Ordinal).ToList();
        var parsed = new List<(ManifestFile File, JsonElement Root, JsonDocument Document)>();
        try
        {
            foreach (var package in packages)
            {
                try
                {
                    var document = JsonDocument.Parse(package.Text, new JsonDocumentOptions { MaxDepth = 32 });
                    if (document.RootElement.ValueKind != JsonValueKind.Object)
                    {
                        document.Dispose();

                        return new Detection.Refused($"{PathSpelling.Spell(package.Path)} is JSON but not an object, so it is not a package manifest; {Cure}");
                    }

                    parsed.Add((package, document.RootElement, document));
                }
                catch (JsonException e)
                {
                    return new Detection.Refused($"{PathSpelling.Spell(package.Path)} is not JSON that coai can read (line {((e.LineNumber ?? 0) + 1).ToString(CultureInfo.InvariantCulture)}, position {((e.BytePositionInLine ?? 0) + 1).ToString(CultureInfo.InvariantCulture)}); {Cure}");
                }
            }

            foreach (var (workspaceFile, workspaceRoot, _) in parsed)
            {
                if (WorkspacePatternsOf(workspaceRoot) is { } declared && PatternProblem(declared) is { Length: > 0 } problem)
                {
                    return new Detection.Refused($"{PathSpelling.Spell(workspaceFile.Path)} {problem}; {Cure}");
                }
            }

            var workspaceRoots = parsed.Select(item => (Folder: ProjectGraph.Folder(item.File.Path), Patterns: WorkspacePatternsOf(item.Root))).Where(item => item.Patterns is not null).Select(item => (item.Folder, Patterns: item.Patterns!)).ToList();
            var projects = new List<DetectedProject>();
            foreach (var (file, root, _) in parsed)
            {
                var folder = ProjectGraph.Folder(file.Path);
                if (workspaceRoots.Any(workspace => workspace.Folder != folder && IsMember(workspace.Folder, workspace.Patterns, folder)))
                {
                    continue;
                }

                var answer = Plan(file, root, folder, set, stack);
                if (answer.Refusal.Length > 0)
                {
                    return new Detection.Refused(answer.Refusal);
                }

                projects.Add(answer.Project!);
            }

            if (projects.Count > ManifestLimits.MaxProjects)
            {
                return new Detection.Refused($"{projects.Count.ToString(CultureInfo.InvariantCulture)} Node projects were found, over the limit of {ManifestLimits.MaxProjects.ToString(CultureInfo.InvariantCulture)}; {Cure}");
            }

            return new Detection.Detected(projects);
        }
        finally
        {
            foreach (var item in parsed)
            {
                item.Document.Dispose();
            }
        }
    }

    /// <summary>The patterns of <c>workspaces</c> (an array of texts, or an object with a <c>packages</c> array), or null when the manifest declares none. A root that declares an empty list is still a workspace root.</summary>
    internal static List<string>? WorkspacePatternsOf(JsonElement root)
    {
        if (!root.TryGetProperty("workspaces", out var workspaces))
        {
            return null;
        }

        var list = workspaces.ValueKind == JsonValueKind.Object && workspaces.TryGetProperty("packages", out var packages) ? packages : workspaces;
        if (workspaces.ValueKind is not (JsonValueKind.Array or JsonValueKind.Object))
        {
            return null;
        }

        return list.ValueKind == JsonValueKind.Array ? [.. list.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!)] : [];
    }

    /// <summary>Whether the package at <paramref name="folder"/> is a member of the workspace rooted at <paramref name="rootFolder"/>: it matches a pattern and no negated pattern (<c>!x</c>). A package under the root that no pattern names is NOT a member: it is a project of its own, so a coder cannot hide one by adding a workspace root above it.</summary>
    internal static bool IsMember(string rootFolder, IReadOnlyList<string> patterns, string folder)
    {
        if (!ProjectGraph.Under(rootFolder, folder + "/x"))
        {
            return false;
        }

        var relative = rootFolder.Length == 0 ? folder : folder[(rootFolder.Length + 1)..];
        var segments = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var included = false;
        foreach (var raw in patterns)
        {
            var negated = raw.StartsWith('!');
            var pattern = (negated ? raw[1..] : raw).Trim().Replace('\\', '/');
            while (pattern.StartsWith("./", StringComparison.Ordinal))
            {
                pattern = pattern[2..];
            }

            if (GlobMatches(Fold(pattern.TrimEnd('/').Split('/', StringSplitOptions.RemoveEmptyEntries)), segments))
            {
                included = !negated;
            }
        }

        return included;
    }

    /// <summary>Folds every run of <c>**</c> segments into one, so that the matcher below sees the shortest pattern that means the same.</summary>
    private static string[] Fold(string[] pattern)
    {
        var folded = new List<string>();
        foreach (var segment in pattern)
        {
            if (segment == "**" && folded.Count > 0 && folded[^1] == "**")
            {
                continue;
            }

            folded.Add(segment);
        }

        return [.. folded];
    }

    /// <summary>The sentence that says a list of workspace patterns is over a bound — a count, a length, or more segments than a package can be deep — or empty. Over a bound is a refusal, never a cut list.</summary>
    internal static string PatternProblem(IReadOnlyList<string> patterns)
    {
        if (patterns.Count > MaxPatterns)
        {
            return $"declares {patterns.Count.ToString(CultureInfo.InvariantCulture)} workspace patterns, over the limit of {MaxPatterns.ToString(CultureInfo.InvariantCulture)}";
        }

        for (var index = 0; index < patterns.Count; index++)
        {
            if (patterns[index].Length > MaxPatternLength)
            {
                return $"has workspace pattern {(index + 1).ToString(CultureInfo.InvariantCulture)} of {patterns[index].Length.ToString(CultureInfo.InvariantCulture)} characters, over the limit of {MaxPatternLength.ToString(CultureInfo.InvariantCulture)}";
            }

            var segments = Fold(patterns[index].Split('/', StringSplitOptions.RemoveEmptyEntries)).Length;
            if (segments > ManifestLimits.MaxFolderDepth + 2)
            {
                return $"has workspace pattern {(index + 1).ToString(CultureInfo.InvariantCulture)} of {segments.ToString(CultureInfo.InvariantCulture)} segments, over the limit of {(ManifestLimits.MaxFolderDepth + 2).ToString(CultureInfo.InvariantCulture)} (a package is at most {ManifestLimits.MaxFolderDepth.ToString(CultureInfo.InvariantCulture)} folders deep)";
            }
        }

        return string.Empty;
    }

    /// <summary>Segment-wise glob match with <c>**</c> for any number of folders (zero too): one pass with the last <c>**</c> remembered, no recursion, O(pattern x path). A pattern that is over its bounds has already been refused.</summary>
    private static bool GlobMatches(string[] pattern, string[] path)
    {
        int p = 0, s = 0, starP = -1, starS = 0;
        while (s < path.Length)
        {
            if (p < pattern.Length && pattern[p] == "**")
            {
                starP = p++;
                starS = s;
            }
            else if (p < pattern.Length && ManifestSearch.Matches(pattern[p], path[s]))
            {
                p++;
                s++;
            }
            else if (starP >= 0)
            {
                p = starP + 1;
                s = ++starS;
            }
            else
            {
                return false;
            }
        }

        while (p < pattern.Length && pattern[p] == "**")
        {
            p++;
        }

        return p == pattern.Length;
    }

    private sealed record Planned(DetectedProject? Project, string Refusal);

    private static Planned Plan(ManifestFile file, JsonElement root, string folder, ManifestSet set, StackDefaults stack)
    {
        var here = set.Of(ManifestKind.Present).Where(present => ProjectGraph.Folder(present.Path) == folder).Select(present => present.Path.Split('/')[^1]).ToList();
        var locks = here.Where(name => NpmLocks.Contains(name)).ToList();
        var others = OtherLocks.Where(other => here.Contains(other.File)).ToList();
        if (others.Count > 0)
        {
            var manager = others[0].Manager;

            return new Planned(null, $"{PathSpelling.Spell(file.Path)} is a {manager} project (it has {others[0].File}); coai runs npm only. Name the commands in {ToolchainTables.FilePath}, or use npm");
        }

        if (locks.Count > 1)
        {
            return new Planned(null, $"{PathSpelling.Spell(file.Path)} has two lock files ({string.Join(" and ", locks)}), so coai cannot tell what `npm ci` would install; keep one, or {Cure}");
        }

        if (locks.Count == 0)
        {
            return new Planned(null, $"{PathSpelling.Spell(file.Path)} has no lock file beside it, and `npm ci` needs one; commit package-lock.json, or {Cure}");
        }

        var scripts = StringMap(root, "scripts");
        var dependencies = new HashSet<string>(Names(root, "dependencies").Concat(Names(root, "devDependencies")), StringComparer.Ordinal);
        var testScript = scripts.GetValueOrDefault("test") ?? string.Empty;
        var hasTest = scripts.ContainsKey("test") && !IsPlaceholder(testScript);
        var tsconfig = here.Contains("tsconfig.json");
        var hasTypecheckScript = scripts.ContainsKey("typecheck");
        var typecheck = hasTypecheckScript || (tsconfig && dependencies.Contains("typescript"));
        var hasBuild = scripts.ContainsKey("build");
        var prettier = dependencies.Contains("prettier") && (root.TryGetProperty("prettier", out _) || here.Any(name => ManifestSearch.Matches(".prettierrc*", name) || ManifestSearch.Matches("prettier.config.*", name)));
        var runner = RunnerOf(testScript, dependencies);

        var steps = new List<DetectedStep>();
        if (hasBuild || typecheck || hasTest)
        {
            var workingFolder = folder.Length == 0 ? "." : folder;
            void Add(string name, IReadOnlyList<string> argv)
            {
                var step = stack.Steps[name]!;
                steps.Add(new DetectedStep(name, argv, step.Network, workingFolder, file.Path, name == "format" ? step.Extensions : []));
            }

            Add("restore", stack.Steps["restore"]!.Argv);
            if (prettier)
            {
                Add("format", stack.Steps["format"]!.Argv);
            }

            if (hasBuild)
            {
                Add("build", stack.Steps["build"]!.Argv);
            }

            if (typecheck)
            {
                Add("typecheck", hasTypecheckScript ? stack.Steps["typecheck"]!.Argv : stack.Steps["typecheck"]!.Fallback);
            }

            if (hasTest)
            {
                Add("test", stack.Steps["test"]!.ByRunner[runner]);
            }
        }

        var facts = new List<KeyValuePair<string, string>>
        {
            new("packageManager", "npm"),
            new("lock", locks[0]),
            new("workspaces", WorkspacePatternsOf(root) is null ? "false" : "true"),
            new("build", hasBuild ? "script" : "none"),
            new("typecheck", hasTypecheckScript ? "script" : typecheck ? "tsc" : "none"),
            new("test", hasTest ? "script" : scripts.ContainsKey("test") ? "placeholder" : "none"),
            new("runner", hasTest ? runner : "none"),
            new("tsconfig", tsconfig ? "true" : "false"),
            new("prettier", prettier ? "true" : "false"),
            new("unchecked", steps.Count == 0 ? "true" : "false"),
        };

        return new Planned(new DetectedProject("node", file.Path, folder.Length == 0 ? "." : folder, steps, facts, [], TestsNone: !hasTest), string.Empty);
    }

    /// <summary>npm's own placeholder for a package with no test (<c>echo "Error: no test specified" &amp;&amp; exit 1</c>), however it is quoted.</summary>
    private static bool IsPlaceholder(string script) => script.Contains("no test specified", StringComparison.OrdinalIgnoreCase);

    /// <summary>The runner fact: jest and vitest need the package AND the word in <c>scripts.test</c>; node:test needs <c>node --test</c> (or a <c>--test</c> flag of node) in it; else unknown.</summary>
    private static string RunnerOf(string testScript, HashSet<string> dependencies) =>
        dependencies.Contains("jest") && ContainsWord(testScript, "jest") ? "jest"
        : dependencies.Contains("vitest") && ContainsWord(testScript, "vitest") ? "vitest"
        : ContainsWord(testScript, "node") && (testScript.Contains("--test", StringComparison.Ordinal) || testScript.Contains("node:test", StringComparison.Ordinal)) ? "node-test"
        : "unknown";

    private static bool ContainsWord(string text, string word)
    {
        var at = 0;
        while ((at = text.IndexOf(word, at, StringComparison.Ordinal)) >= 0)
        {
            var before = at == 0 || !char.IsLetterOrDigit(text[at - 1]) && text[at - 1] != '-';
            var after = at + word.Length >= text.Length || !char.IsLetterOrDigit(text[at + word.Length]) && text[at + word.Length] != '-';
            if (before && after)
            {
                return true;
            }

            at += word.Length;
        }

        return false;
    }

    private static Dictionary<string, string> StringMap(JsonElement root, string name)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject().Where(property => property.Value.ValueKind == JsonValueKind.String))
            {
                map[property.Name] = property.Value.GetString()!;
            }
        }

        return map;
    }

    private static IEnumerable<string> Names(JsonElement root, string name) =>
        root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.Object ? element.EnumerateObject().Select(property => property.Name) : [];

}
