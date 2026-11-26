using System.Globalization;

namespace CoaiMcp.Core.Toolchain;

/// <summary>What the formatter is given: the state of the selection and the files, as repository paths.</summary>
/// <param name="State"><c>files</c> when there is something to give, <c>none</c> when no file qualifies, <c>too-many-files</c> above <see cref="FileArguments.MaxFiles"/>.</param>
/// <param name="Files">The repository paths, in the order of the listing.</param>
public sealed record FormatFiles(string State, IReadOnlyList<string> Files);

/// <summary>
/// The files a formatter is given and how they reach its argv (E4 s5 t1; SPEC D items 1 and 2). Pure.
/// </summary>
/// <remarks>
/// <para><b>Which files:</b> the files the turn's commit added or changed, inside the project, with an extension of the adapter's list, that are not a manifest or a lock file, and whose name holds no control character or line break. More than <see cref="MaxFiles"/> is no run: <c>too-many-files</c>.</para>
/// <para><b>How a name reaches the argv:</b> the item <c>{file}</c> is replaced IN PLACE by one item per file, each written <c>./&lt;path from the working folder&gt;</c>, so a file named <c>--write-everything</c> reaches the tool as a file and never as an option.</para>
/// </remarks>
public static class FileArguments
{
    public const int MaxFiles = 200;

    private static readonly string[] LockNames = ["package-lock.json", "npm-shrinkwrap.json", "yarn.lock", "pnpm-lock.yaml", "bun.lock", "bun.lockb", "packages.lock.json"];

    /// <summary>The files to give: of <paramref name="changed"/> (what the turn's commit added or changed), those inside <paramref name="projectFolder"/> (empty for the root) with an extension of <paramref name="extensions"/>.</summary>
    public static FormatFiles Select(IEnumerable<string> changed, string projectFolder, IReadOnlyList<string> extensions, ToolchainDefaults table)
    {
        var manifests = table.AllManifestNames;
        var files = changed.Where(path => (projectFolder.Length == 0 || ProjectGraph.Under(projectFolder, path)) && IsSafe(path) && HasExtension(path, extensions) && !IsManifestOrLock(path, manifests)).Distinct(StringComparer.Ordinal).ToList();

        return files.Count > MaxFiles ? new FormatFiles("too-many-files", []) : new FormatFiles(files.Count == 0 ? "none" : "files", files);
    }

    /// <summary>The argv with the item <c>{file}</c> replaced in place by <c>./&lt;path relative to the working folder&gt;</c> for each file, and <c>{jobs}</c> by <paramref name="jobs"/>.</summary>
    public static IReadOnlyList<string> Argv(IReadOnlyList<string> argv, IReadOnlyList<string> files, string workingFolder, int jobs)
    {
        var folder = workingFolder == "." ? string.Empty : workingFolder;
        var result = new List<string>();
        foreach (var item in argv)
        {
            if (item == ToolchainDefaults.FilePlaceholder)
            {
                result.AddRange(files.Select(path => "./" + (folder.Length > 0 && path.StartsWith(folder + "/", StringComparison.Ordinal) ? path[(folder.Length + 1)..] : path)));
            }
            else
            {
                result.Add(item.Replace("{jobs}", jobs.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));
            }
        }

        return result;
    }

    private static bool IsSafe(string path) => path.Length > 0 && !path.Any(char.IsControl);

    private static bool HasExtension(string path, IReadOnlyList<string> extensions) =>
        extensions.Count == 0 || extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static bool IsManifestOrLock(string path, IReadOnlyList<string> manifestNames)
    {
        var name = path[(path.LastIndexOf('/') + 1)..];

        return LockNames.Contains(name, StringComparer.OrdinalIgnoreCase) || manifestNames.Any(pattern => ManifestSearch.Matches(pattern, name, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>What the formatter turn did, as the envelope's <c>format</c> of a round says it (SPEC D).</summary>
/// <param name="State"><c>formatted</c>, <c>unchanged</c>, <c>not_configured</c>, <c>failed</c>, <c>refused</c> or <c>too-many-files</c>. Never a verdict: it makes no round red or green.</param>
/// <param name="Files">How many files were given.</param>
/// <param name="Commit">The format commit, or empty.</param>
/// <param name="Note">One sentence; names at most a few spelled paths.</param>
public sealed record FormatResult(string State, int Files, string Commit, string Note)
{
    public static FormatResult NotConfigured { get; } = new("not_configured", 0, string.Empty, "no formatter in the resolved toolchain, so no format was run");
}
