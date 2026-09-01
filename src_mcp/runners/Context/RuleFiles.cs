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

    public bool HasRules => Files.Count > 0;

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
    public string Render()
    {
        if (!HasRules)
        {
            return string.Empty;
        }

        var parts = Files.Select(f => $"### {f.Path}\n\n{f.Text.TrimEnd()}\n");
        var omitted = Omitted.Count == 0
            ? string.Empty
            : $"\n> {Omitted.Count} further rule file(s) omitted for length: {string.Join(", ", Omitted)}.\n" +
              "> A rule you were not shown is not a rule this change complies with — say so if it matters.\n";
        return string.Join("\n", parts) + omitted;
    }
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

    public static RuleBundle Collect(string repoPath, int budgetBytes = DefaultBudgetBytes)
    {
        if (!Directory.Exists(repoPath))
        {
            return RuleBundle.None;
        }

        var kept = new List<RuleFile>();
        var omitted = new List<string>();
        var used = 0;

        foreach (var relative in Candidates(repoPath))
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

        return new RuleBundle(kept, omitted, used);
    }

    /// <summary>Every rule path, instruction files first, then folders in a stable order.</summary>
    private static IEnumerable<string> Candidates(string repoPath)
    {
        foreach (var file in InstructionFiles)
        {
            yield return file;
        }

        foreach (var (dir, pattern) in RuleFolders)
        {
            var root = Path.Combine(repoPath, dir.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(root))
            {
                continue;
            }

            var found = Enumerate(root, pattern)
                .Select(p => Relative(repoPath, p))
                .Where(p => !Excluded(p))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase);
            foreach (var path in found)
            {
                yield return path;
            }
        }
    }

    private static IEnumerable<string> Enumerate(string root, string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories);
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
