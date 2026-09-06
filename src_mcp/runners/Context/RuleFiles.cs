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

    /// <summary>
    /// Rules are text and a prompt is finite. 80 KB is about a dozen and a half real rule files.
    /// </summary>
    /// <remarks>
    /// Raised from 40 KB on 2026-09-06, after measuring what this repository actually shows a
    /// reviewer: at 40 KB it was 8 files and 19 omitted, and the omitted list included `testing.md`,
    /// `security.md`, `reuse-first.md`, `git-workflow.md` and all four language doctrines — the rules
    /// most findings are written against. Raised to 80 KB the same day, on the operator's call, and
    /// measured again there: 12 files, 77 KB, 16 omitted. The extra 20 KB buys ONE file, because
    /// `development-workflow.md` (14 KB) and `http-contracts.md` (11 KB) are collected first and take a
    /// quarter of the budget between them — so the honest reading is that the ORDER, not the size, is
    /// what keeps `testing.md` and `security.md` out. The whole family set is about 199 KB, so this
    /// stays a budget rather than a fix: what it cannot fit is NAMED in the prompt (the bundle renders
    /// an "omitted for length" note) precisely so a reviewer cannot read an absence as compliance.
    /// </remarks>
    public const int DefaultBudgetBytes = 80_000;

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

    /// <param name="seed">
    /// Fixes the draw, for a test. Left alone in production, so two rounds see two different halves
    /// of the family rules.
    /// </param>
    public static RuleBundle Collect(string repoPath, int budgetBytes = DefaultBudgetBytes, int? seed = null)
    {
        if (!Directory.Exists(repoPath))
        {
            return RuleBundle.None;
        }

        var mounts = GitModules.In(repoPath);
        var kept = new List<RuleFile>();
        var omitted = new List<string>();
        var used = 0;

        foreach (var relative in Candidates(repoPath, mounts, seed))
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
    private static IEnumerable<string> Candidates(
        string repoPath, IReadOnlyList<SubmoduleMount> mounts, int? seed)
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

        foreach (var path in Shuffled(folders.Where(p => UnderAnyMount(p, mounts)).ToList(), seed))
        {
            yield return path;
        }
    }

    /// <summary>
    /// The mounted family rules, in a different order every round.
    /// </summary>
    /// <remarks>
    /// <para>Measured 2026-09-06: the family set is ~199 KB against an 80 KB budget, and in
    /// enumeration order the first two files take a quarter of it — so `testing.md`, `security.md`,
    /// `reuse-first.md` and all four language doctrines were never shown to any reviewer, ever. Not
    /// because the budget was small: because they were last in line, and the line never changed.</para>
    /// <para>Raising the budget cannot fix that; a different draw each round can. At 80 KB of 199 a
    /// round sees about two fifths of the family rules, so a given rule is shown roughly every second
    /// or third round — and across the rounds of one change, with several reviewers each, most of the
    /// set gets read. The operator's call, and the right one: a rule shown sometimes is infinitely
    /// more than a rule shown never.</para>
    /// <para>What is NOT shuffled: the instruction files and the repository's OWN rules. They are few,
    /// they are the entry points, and they are the rules a diff in this repository can break — they
    /// come first and they always fit.</para>
    /// <para>The omitted list still names everything that did not fit, so a round says which rules it
    /// did not see rather than implying it saw them all.</para>
    /// </remarks>
    // CA1859: the array IS the concrete type the caller foreaches over, and returning it as one
    // costs nothing here.
    // S2245 wants a cryptographic generator. It is wrong about this call: nothing here guards a
    // secret, and the draw decides only WHICH rule files a reviewer is shown when they do not all
    // fit. The `seed` parameter is the tell — it exists so a test can assert an exact order, which
    // a cryptographic generator cannot give at all. Swapping it would break the tests and protect
    // nothing.
#pragma warning disable S2245 // Random is not used for security here — see above.
    private static string[] Shuffled(IReadOnlyList<string> paths, int? seed)
    {
        var random = seed is { } fixed_ ? new Random(fixed_) : Random.Shared;
        var drawn = paths.ToArray();
        for (var index = drawn.Length - 1; index > 0; index--)
        {
            var swap = random.Next(index + 1);
            (drawn[index], drawn[swap]) = (drawn[swap], drawn[index]);
        }

        return drawn;
    }
#pragma warning restore S2245

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
