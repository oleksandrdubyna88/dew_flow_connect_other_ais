using CoaiMcp.Runners.Git;

namespace CoaiMcp.Runners.Context;

/// <summary>One rule document as the reviewers will see it.</summary>
public sealed record RuleFile(string Path, string Text);

/// <summary>
/// The project's own written conventions, as a block a reviewer can be judged against.
/// </summary>
/// <param name="Omitted">Paths the budget could not fit. Named, never dropped in silence.</param>
public sealed record RuleBundle(IReadOnlyList<RuleFile> Files, IReadOnlyList<string> Omitted, int Bytes)
{
    public static readonly RuleBundle None = new([], [], 0);

    /// <summary>
    /// Rule mounts the repository DECLARES and this tree does not have — a submodule that was not
    /// populated.
    /// </summary>
    /// <remarks>
    /// A different absence from <see cref="Omitted"/>, and the more dangerous one: an omitted file
    /// was seen and dropped, while these were never on disk to be counted. Without this the bundle
    /// would report zero files, zero omissions and no problem, which is precisely how a reviewer
    /// comes to certify compliance with rules nobody showed it.
    /// </remarks>
    public IReadOnlyList<string> MissingMounts { get; init; } = [];

    public bool HasRules => Files.Count > 0 || MissingMounts.Count > 0;

    /// <summary>
    /// The block that goes into a prompt: each file named, then its text verbatim.
    /// </summary>
    /// <remarks>
    /// Named because a finding has to CITE the rule it breaks, and "some convention somewhere"
    /// cannot be checked by the person reading the finding. The omissions are printed for the same
    /// reason in reverse: a reviewer told nothing about what it was not shown would report
    /// compliance with rules it never saw, which turns an absence of evidence into a clean bill of
    /// health.
    /// </remarks>
    public string Render() => HasRules ? Body() + OmittedNote() + MissingNote() : string.Empty;

    private string Body() => string.Join("\n", Files.Select(f => $"### {f.Path}\n\n{f.Text.TrimEnd()}\n"));

    private string OmittedNote() =>
        Omitted.Count == 0
            ? string.Empty
            : $"\n> {Omitted.Count} further rule file(s) omitted for length: {string.Join(", ", Omitted)}.\n" +
              "> A rule you were not shown is not a rule this change complies with — say so if it matters.\n";

    private string MissingNote() =>
        MissingMounts.Count == 0
            ? string.Empty
            : $"\n> This project keeps rules at {string.Join(", ", MissingMounts)}, and they are NOT in " +
              "the tree you were given — a submodule this round could not populate.\n" +
              "> Those rules were not shown to you. Do not read their absence as compliance.\n";
}

/// <summary>
/// Finding the conventions a repository writes down, so its reviewers are held to the same ones
/// its authors are.
/// </summary>
/// <remarks>
/// <para>Read from the WORKTREE the round already checked out rather than from anybody's live tree:
/// the reviewer must see the rules as of the commit under review, not as of this afternoon.</para>
/// <para>Four instruction files because four CLIs read them, and the rule FOLDERS because that is
/// where the real conventions end up once a repo has more than a page of them. Ordered with the
/// instruction files first: they are the entry points, they reference the rest, and they are what
/// survives a tight budget.</para>
/// </remarks>
public static class RuleFiles
{
    /// <summary>The files a CLI is told to read. Order is priority under the budget.</summary>
    public static readonly string[] InstructionFiles =
        ["CLAUDE.md", "AGENTS.md", "GEMINI.md", ".github/copilot-instructions.md"];

    /// <summary>Where conventions live once there are too many for one page.</summary>
    private static readonly (string Dir, string Pattern)[] RuleFolders =
        [(".claude/rules", "*.md"), (".cursor/rules", "*.mdc"), (".cursor/rules", "*.md")];

    /// <summary>Rules are text and a prompt is finite; 40 KB is about a dozen real rule files.</summary>
    public const int DefaultBudgetBytes = 40_000;

    /// <summary>Somebody else's conventions, vendored or generated, are not this project's rules.</summary>
    private static readonly string[] NotOurs =
        ["node_modules", "bin", "obj", ".git", "dist", "out", "artifacts", "vendor", "packages"];

    /// <summary>
    /// What a mounted rules REPOSITORY carries that is not a rule: its own open plans, its settings
    /// reference copy, its tooling and fixtures.
    /// </summary>
    /// <remarks>
    /// Scoped to inside a mount, never matched by name alone. A repository is perfectly entitled to
    /// its own <c>.claude/rules/todo/</c>, and filtering by the word would throw away one of its own
    /// rules to tidy up somebody else's repository.
    /// </remarks>
    private static readonly string[] MountHousekeepingDirs = ["todo", "settings", "tools"];

    /// <summary>
    /// A rules repository's front matter, at ITS root — about that repository, not a rule here.
    /// </summary>
    /// <remarks>
    /// The instruction files are on this list for the same reason as the README, and it is the less
    /// obvious half: a mounted repository's own <c>CLAUDE.md</c> tells an AI how to work on THAT
    /// repository. Collected as a rule it would sit in the prompt beside the consumer's own
    /// <c>CLAUDE.md</c>, under a name a reviewer cannot tell apart from it, saying different things.
    /// </remarks>
    private static readonly string[] MountHousekeepingFiles =
        ["README.md", "ROLLOUT.md", "POST_DEPLOY.md", "CLAUDE.md", "AGENTS.md", "GEMINI.md"];

    /// <summary>
    /// How the rule folders are walked: a reparse point is never followed.
    /// </summary>
    /// <remarks>
    /// A mounted rules repository is somebody else's content, and a committed symlink in it — or a
    /// junction under it — would be read by <see cref="Read"/> and put verbatim into a prompt that
    /// leaves this machine. Hidden and system files stay skipped, which is what the default
    /// enumeration did before this option existed here.
    /// </remarks>
    private static readonly EnumerationOptions RuleWalk = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System,
    };

    public static RuleBundle Collect(string repoPath, int budgetBytes = DefaultBudgetBytes)
    {
        if (!Directory.Exists(repoPath))
        {
            return RuleBundle.None;
        }

        var mounts = GitModules.In(repoPath);
        var kept = new List<RuleFile>();
        var omitted = new List<string>();
        var used = 0;

        foreach (var relative in Candidates(repoPath, mounts))
        {
            var full = Path.Combine(repoPath, relative.Replace('/', Path.DirectorySeparatorChar));
            if (Read(full) is not { } text)
            {
                continue;
            }

            // Whole files or nothing: half a rule file is a rule cut in the middle of a sentence,
            // and a reviewer quoting it would be quoting something the project never wrote.
            if (used + text.Length > budgetBytes && kept.Count > 0)
            {
                omitted.Add(relative);
                continue;
            }

            kept.Add(new RuleFile(relative, text));
            used += text.Length;
        }

        return new RuleBundle(kept, omitted, used) { MissingMounts = EmptyRuleMounts(repoPath, mounts) };
    }

    /// <summary>
    /// Every rule path: instruction files, then the repository's OWN rule folders, then the mounts.
    /// </summary>
    /// <remarks>
    /// The last two used to be one alphabetical list, which decided the budget race by directory
    /// name — a local <c>.claude/rules/workflows/</c> sorts after <c>shared/</c>, so 208 KB of family
    /// rules would be read first and the repository's own rule dropped. The rules a diff in THIS
    /// repository can break come first; the family's are the same in six checkouts.
    /// </remarks>
    private static IEnumerable<string> Candidates(string repoPath, IReadOnlyList<SubmoduleMount> mounts)
    {
        foreach (var file in InstructionFiles)
        {
            yield return file;
        }

        var folders = FolderFiles(repoPath, mounts);
        foreach (var path in folders.Where(p => !UnderAnyMount(p, mounts)))
        {
            yield return path;
        }

        foreach (var path in folders.Where(p => UnderAnyMount(p, mounts)))
        {
            yield return path;
        }
    }

    /// <summary>Every rule file under the rule folders, de-duplicated and in a stable order.</summary>
    private static IReadOnlyList<string> FolderFiles(string repoPath, IReadOnlyList<SubmoduleMount> mounts) =>
        [.. RuleFolders
            .SelectMany(f => Enumerate(Path.Combine(repoPath, f.Dir.Replace('/', Path.DirectorySeparatorChar)), f.Pattern))
            .Select(p => Relative(repoPath, p))
            .Where(p => !Excluded(p) && !Housekeeping(p, mounts))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)];

    private static bool UnderAnyMount(string relative, IReadOnlyList<SubmoduleMount> mounts) =>
        mounts.Any(m => relative.StartsWith(m.Path + "/", StringComparison.OrdinalIgnoreCase));

    private static bool Housekeeping(string relative, IReadOnlyList<SubmoduleMount> mounts) =>
        mounts.Any(m => HousekeepingIn(m.Path, relative));

    private static bool HousekeepingIn(string mount, string relative)
    {
        var prefix = mount + "/";
        if (!relative.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rest = relative[prefix.Length..];
        var first = rest.Split('/')[0];

        return rest.Contains('/')
            ? MountHousekeepingDirs.Contains(first, StringComparer.OrdinalIgnoreCase)
            : MountHousekeepingFiles.Contains(first, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Rule mounts this repository declares that are not actually here — the reviewer is told.
    /// </summary>
    private static IReadOnlyList<string> EmptyRuleMounts(string repoPath, IReadOnlyList<SubmoduleMount> mounts) =>
        [.. mounts
            .Where(m => IsRulesMount(m.Path) && IsEmptyDirectory(Path.Combine(repoPath, m.Path.Replace('/', Path.DirectorySeparatorChar))))
            .Select(m => m.Path)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)];

    private static bool IsRulesMount(string path) =>
        RuleFolders.Any(f => path.StartsWith(f.Dir + "/", StringComparison.OrdinalIgnoreCase));

    private static bool IsEmptyDirectory(string path)
    {
        try
        {
            return !Directory.Exists(path) || !Directory.EnumerateFileSystemEntries(path).Any();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static IEnumerable<string> Enumerate(string root, string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(root, pattern, RuleWalk);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string Relative(string repoPath, string full) =>
        Path.GetRelativePath(repoPath, full).Replace('\\', '/');

    private static bool Excluded(string relative) =>
        relative.Split('/').Any(segment => NotOurs.Contains(segment, StringComparer.OrdinalIgnoreCase));

    /// <summary>A rule file that cannot be read is not a round that fails.</summary>
    private static string? Read(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
