using System.Text;
using CoaiMcp.Core.Work;
using CoaiMcp.Runners.Files;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>
/// The epic a BA job plans, found from <c>start_role(role: "ba", repoPath, taskFile: &lt;epic.yaml&gt;)</c> and judged BEFORE any folder or launch is made (spec C): a file called <c>epic.yaml</c> inside the repository, whose
/// <c>id</c> is its folder's name, in an <c>epoch-N</c> folder of a feature folder, and which has no story yet.
/// </summary>
/// <param name="RepoRoot">The repository, resolved to its real path.</param>
/// <param name="EpicFolder">The epic's folder, as a real path (the file was not a link and is inside the repository).</param>
/// <param name="EpicRel">The epic's folder from the repository's top, written with <c>/</c> — what a manifest and a prompt call it.</param>
/// <param name="FeatureFolder">The feature folder (the one with <c>feature.yaml</c>), as a real path: the root the overlay is validated from.</param>
/// <param name="EpicId">The epic's id (its folder's name).</param>
/// <param name="EpicText">The <c>epic.yaml</c> as written.</param>
/// <param name="PlanText">The text of the file the epic's <c>spec</c> key names, cut at <see cref="BaEpic.PlanCap"/> bytes; empty when the epic names none.</param>
/// <param name="PlanCut">True when the spec was longer than the cap.</param>
internal sealed record BaEpicSite(string RepoRoot, string EpicFolder, string EpicRel, string FeatureFolder, string EpicId, string EpicText, string PlanText, bool PlanCut);

/// <summary>Finds and judges the epic of a BA job. Nothing here writes.</summary>
internal static class BaEpic
{
    /// <summary>The name of the staging folder a BA job writes into, in the epic folder: <c>.coai-ba-staging-&lt;jobId&gt;</c>.</summary>
    public const string StagingPrefix = ".coai-ba-staging-";

    /// <summary>The plan text the role is handed, in bytes — 64 KiB, the same cap as a brief.</summary>
    public const int PlanCap = 64 * 1024;

    private const string EpicFile = "epic.yaml";

    private static StringComparison PathComparison => OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    /// <summary>The start's look at a BA request: empty when it is fit, else the sentence that refuses it.</summary>
    public static string Refusal(RoleJobRequest request) => Locate(request.RepoPath, request.TaskFile).Refusal;

    /// <summary>The site of the epic <paramref name="taskFile"/> names in <paramref name="repoPath"/>, or the sentence that says why there is none.</summary>
    public static (BaEpicSite? Site, string Refusal) Locate(string repoPath, string taskFile)
    {
        if (taskFile.Length == 0 || !taskFile.EndsWith(EpicFile, StringComparison.Ordinal) || (taskFile.Length > EpicFile.Length && taskFile[^(EpicFile.Length + 1)] != '/'))
        {
            return (null, $"the ba role plans one epic: send taskFile, the path of its {EpicFile} inside the repository (not a brief)");
        }

        var root = RealPath.Resolve(repoPath);
        var file = root.Ok ? RealPath.ResolveBelow(root.Path, Path.Combine(root.Path, taskFile), PathComparison) : root;
        if (!file.Ok || !File.Exists(file.Path))
        {
            return (null, $"taskFile '{taskFile}' is not an {EpicFile} file inside the repository{(file.Ok ? string.Empty : $": {file.Why}")}");
        }

        return Judged(root.Path, file.Path);
    }

    private static (BaEpicSite? Site, string Refusal) Judged(string root, string file)
    {
        var folder = Path.GetDirectoryName(file)!;
        var name = Path.GetFileName(folder);
        if (Text(file, StrictYamlReader.MaxBytes) is not { } text)
        {
            return (null, $"{Rel(root, file)} could not be read (or is over {StrictYamlReader.MaxBytes} bytes)");
        }

        if (new StrictYamlReader().Read(text) is not YamlRead.Parsed { Root: YamlMap map })
        {
            return (null, $"{Rel(root, file)} is not a YAML map the work-plan reader accepts; run validate_work on the feature first");
        }

        if (map.Find("id")?.Value is not YamlScalar { Kind: YamlScalarKind.String } id || id.Text != name || !WorkTables.IsId(WorkTables.EpicIdPattern, name))
        {
            return (null, $"the id of {Rel(root, file)} must be the name of its folder ('{name}') and match {WorkTables.EpicIdPattern}");
        }

        var epoch = Path.GetDirectoryName(folder);
        var feature = epoch is null ? null : Path.GetDirectoryName(epoch);
        if (epoch is null || !WorkNames.IsEpoch(Path.GetFileName(epoch)) || feature is null || !File.Exists(Path.Combine(feature, "feature.yaml")))
        {
            return (null, $"{Rel(root, file)} is not in an epoch-N folder of a feature folder (one with a feature.yaml): the plan is validated from the feature folder");
        }

        if (Stories(folder) is { Count: > 0 } stories)
        {
            return (null, $"epic '{name}' already has {stories.Count} {(stories.Count == 1 ? "story" : "stories")} ({string.Join(", ", stories.Take(3))}): the BA plans an epic that has none, so it never rewrites a plan someone built on");
        }

        return PlanOf(root, folder, feature, name, map, text) is { } site ? (site, string.Empty) : (null, $"the spec of {Rel(root, file)} could not be read inside the epic folder");
    }

    /// <summary>The story folders of an epic (a staging folder, a file and a folder that is not a story's are not stories) — the same names the validator takes for stories.</summary>
    public static IReadOnlyList<string> Stories(string epicFolder)
    {
        var stories = Path.Combine(epicFolder, "stories");

        return Directory.Exists(stories)
            ? [.. new DirectoryInfo(stories).EnumerateDirectories().Select(folder => folder.Name).Where(WorkNames.IsStory).Order(StringComparer.Ordinal)]
            : [];
    }

    private static BaEpicSite? PlanOf(string root, string folder, string feature, string name, YamlMap map, string epicText)
    {
        var rel = Rel(root, folder);
        var empty = new BaEpicSite(root, folder, rel, feature, name, epicText, string.Empty, false);
        if (map.Find("spec")?.Value is not YamlScalar { Kind: YamlScalarKind.String } spec)
        {
            return empty;
        }

        var (relative, refusal) = RoleRequestChecks.RelativeFile(spec.Text, "spec");
        var path = refusal.Length > 0 ? default : RealPath.ResolveBelow(folder, Path.Combine(folder, relative), PathComparison);
        if (refusal.Length > 0 || !path.Ok || !File.Exists(path.Path) || Text(path.Path, int.MaxValue, PlanCap) is not { } plan)
        {
            return null;
        }

        return empty with { PlanText = plan.Text, PlanCut = plan.Cut };
    }

    /// <summary>The prompt's view of the epic's surroundings: the epoch's other epics, the architecture files, one finished epic.</summary>
    public static BaPromptInputs Inputs(BaEpicSite site)
    {
        var epoch = Path.GetDirectoryName(site.EpicFolder)!;
        var others = new StringBuilder();
        foreach (var other in Directory.EnumerateDirectories(epoch).Where(path => WorkNames.IsEpic(Path.GetFileName(path)) && !string.Equals(path, site.EpicFolder, PathComparison)).Order(StringComparer.Ordinal))
        {
            var file = Path.Combine(other, EpicFile);
            if (File.Exists(file) && Text(file, StrictYamlReader.MaxBytes) is { } text)
            {
                others.Append("### `").Append(Rel(site.RepoRoot, file)).Append("`\n\n```yaml\n").Append(text.TrimEnd()).Append("\n```\n\n");
            }
        }

        return new BaPromptInputs(
            $"{site.EpicRel}/{EpicFile}", site.EpicRel, site.EpicId, site.EpicText.TrimEnd(), PlanWithNote(site), others.Length > 0 ? others.ToString().TrimEnd() : "(this epoch has no other epic)",
            ArchitecturePaths(site), FinishedEpic(site));
    }

    private static string PlanWithNote(BaEpicSite site) =>
        site.PlanText.Length == 0
            ? "(the epic names no spec file: work from the epic.yaml above, and from the repository)"
            : site.PlanCut ? site.PlanText + $"\n[… the plan text was cut at {PlanCap / 1024} KiB]" : site.PlanText;

    private static string ArchitecturePaths(BaEpicSite site)
    {
        var files = new List<string>();
        var architecture = Path.Combine(site.FeatureFolder, "architecture");
        if (Directory.Exists(architecture))
        {
            files.AddRange(Directory.EnumerateFiles(architecture, "*.md").Order(StringComparer.Ordinal).Select(path => Rel(site.RepoRoot, path)));
        }

        var top = Path.Combine(site.RepoRoot, "ARCHITECTURE.md");
        if (File.Exists(top))
        {
            files.Add("ARCHITECTURE.md");
        }

        return files.Count == 0 ? "- (none found)" : string.Join('\n', files.Select(path => $"- `{path}`"));
    }

    /// <summary>One epic that has a story with a task — the first by epoch and epic number that is not this one — as the folder to read, or a sentence saying there is none yet.</summary>
    private static string FinishedEpic(BaEpicSite site)
    {
        var feature = site.FeatureFolder;
        foreach (var epoch in WorkNames.InOrder(Directory.EnumerateDirectories(feature).Select(Path.GetFileName).OfType<string>().Where(WorkNames.IsEpoch)))
        {
            var epochFolder = Path.Combine(feature, epoch);
            foreach (var epic in WorkNames.InOrder(Directory.EnumerateDirectories(epochFolder).Select(Path.GetFileName).OfType<string>().Where(WorkNames.IsEpic)))
            {
                var folder = Path.Combine(epochFolder, epic);
                if (!string.Equals(folder, site.EpicFolder, PathComparison) && Stories(folder) is { Count: > 0 } stories)
                {
                    return $"`{Rel(site.RepoRoot, folder)}` (its stories are in `stories/`, for example `{stories[0]}`)";
                }
            }
        }

        return "(no epic of this feature has stories yet)";
    }

    /// <summary>The text of a file when it is not over <paramref name="mostBytes"/> (null when it is, or cannot be read); with <paramref name="cutAt"/>, longer text is cut there instead and marked.</summary>
    private static string? Text(string file, int mostBytes) => Text(file, mostBytes, mostBytes)?.Text;

    private static (string Text, bool Cut)? Text(string file, int mostBytes, int cutAt)
    {
        try
        {
            var info = new FileInfo(file);
            if (!info.Exists || info.Length > mostBytes)
            {
                return null;
            }

            using var stream = info.OpenRead();
            var buffer = new byte[Math.Min(info.Length, cutAt)];
            var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);

            return (new UTF8Encoding(false).GetString(buffer, 0, read), info.Length > cutAt);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>A path from the repository's top, written with <c>/</c>.</summary>
    public static string Rel(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
}
