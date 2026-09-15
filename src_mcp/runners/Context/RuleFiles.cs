using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Git;

namespace CoaiMcp.Runners.Context;

/// <summary>One rule document as the reviewers will see it.</summary>
/// <param name="WithinMount">
/// Relative to the mount root — <c>common/security.md</c> — or the path itself when the file is not
/// under a mount. It is what a stage tier names a rule by, and it travels with the file so coverage
/// can be counted from what a reviewer was SHOWN rather than from what the tree happened to contain.
/// </param>
/// <remarks>
/// <c>WithinMount</c> is REQUIRED, and was briefly optional. A default would let a caller build
/// <c>new RuleFile(path, text)</c>, have it render into the prompt, and have <c>MatchedCount</c> then
/// see an empty name and report NONE — a bundle telling a reviewer it was shown no tier rules while
/// showing them. An invariant with a default is an invariant with a way around it. (codex, code round.)
/// </remarks>
public sealed record RuleFile(string Path, string Text, string WithinMount);

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

    /// <summary>
    /// How many of a stage tier's rules this bundle actually SHOWS a reviewer.
    /// </summary>
    /// <remarks>
    /// <para>Counted from <see cref="Files"/> — what was rendered — and deliberately not from what the
    /// tree contains. A rule discovered and then dropped by the byte budget, or found and unreadable,
    /// is a rule the reviewer never saw; counting it as present would let a prompt say "all seven are
    /// here" over a section showing six, which is the false-compliance this number exists to prevent.
    /// A code round caught exactly that. What did not fit is still named by <see cref="Omitted"/>.</para>
    /// <para>Lives here rather than in a caller because <see cref="RuleFile.WithinMount"/> is set where
    /// the mounts are known; deriving it outside would mean matching by path suffix, which is the
    /// mistake an earlier code round caught in the tier matching itself.</para>
    /// </remarks>
    public int MatchedCount(IReadOnlyList<string> tier) =>
        tier.Count(entry => Files.Any(file => file.WithinMount.Equals(entry, StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// The sentence that tells a reviewer how much of its tier it is actually holding.
    /// </summary>
    /// <remarks>
    /// In the PROMPT, not only the log: a number a person must go and find in a log cannot prevent the
    /// failure it exists for — a round judged against NONE of its rules reads exactly like one judged
    /// against all of them, and the REVIEWER is who needs to know which it was. Three sentences,
    /// because the three cases mean different things and the last must never be read as compliance.
    /// It sits beside <see cref="Render"/> because that is already prompt text.
    /// </remarks>
    public string TierCoverage(IReadOnlyList<string> tier)
    {
        var matched = MatchedCount(tier);

        if (matched == tier.Count)
        {
            return $"> All {tier.Count} of the rules this stage is judged against are below.\n\n";
        }

        return matched == 0
            ? $"> **NONE of the {tier.Count} rules this stage is judged against are here** — this "
              + "repository pins a different revision of the shared rules, or none at all. Do not read "
              + "their absence as compliance; say that they were missing.\n\n"
            : $"> {matched} of the {tier.Count} rules this stage is judged against are below; the rest "
              + "are not in this repository or did not fit. Do not read their absence as compliance.\n\n";
    }

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
        ["CLAUDE.md", "AGENTS.md", "GEMINI.md", ".github/copilot-instructions.md", ".agents/PROJECT.md"];

    private const string NeutralMount = ".agents/conventions";

    // The mounted repository also carries research and its own project instructions. Only
    // canonical rule directories belong in a consumer's review sample. This is not a selector.
    private static readonly (string Dir, string Pattern)[] NeutralRuleFolders =
        [.. new[] { "common", "csharp", "rust", "typescript" }.Select(dir => ($"{NeutralMount}/{dir}", "*.md"))];

    /// <summary>Where conventions live once there are too many for one page.</summary>
    private static readonly (string Dir, string Pattern)[] RuleFolders =
        [(".claude/rules", "*.md"), (".cursor/rules", "*.mdc"), (".cursor/rules", "*.md"),
            (".agents/rules", "*.md"), .. NeutralRuleFolders];

    /// <summary>
    /// Every place this looks, as a person would read them out — for the messages that name them.
    /// </summary>
    /// <remarks>
    /// A message telling somebody their repository has no rules is telling them where to put some,
    /// so a hand-written list that names four of the six sends them to write a file this never
    /// reads. There was such a list; it named `CLAUDE.md`, `AGENTS.md`, `GEMINI.md` and
    /// `.claude/rules`, and left out `.github/copilot-instructions.md` and `.cursor/rules` — which
    /// is exactly the drift a second copy has. One list, derived from the two the reader uses.
    /// </remarks>
    public static readonly string[] SourceNames =
        [.. InstructionFiles, .. RuleFolders.Select(f => f.Dir).Distinct(StringComparer.Ordinal)];

    /// <summary>
    /// Rules are text and a prompt is finite. 80 KB is about a dozen and a half real rule files.
    /// </summary>
    /// <remarks>
    /// Raised from 40 KB on 2026-09-06, after measuring what this repository actually shows a
    /// reviewer: at 40 KB it was 8 files and 19 omitted, and the omitted list included `testing.md`,
    /// `security.md`, `reuse-first.md`, `git-workflow.md` and all three language doctrines — the rules
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

    public static RuleBundle Collect(string repoPath, int budgetBytes = DefaultBudgetBytes) =>
        Collect(repoPath, budgetBytes, RuleOrder.Walk);

    public static RuleBundle Collect(string repoPath, RuleOrder order) =>
        Collect(repoPath, DefaultBudgetBytes, order);

    /// <param name="order">
    /// Which of the MOUNTED rules the budget reaches first. A parameter rather than a constant
    /// because it is the whole question — see <see cref="RuleOrder"/>.
    /// </param>
    public static RuleBundle Collect(string repoPath, int budgetBytes, RuleOrder order)
    {
        if (!Directory.Exists(repoPath))
        {
            return RuleBundle.None;
        }

        var mounts = GitModules.In(repoPath);
        var folders = FolderFiles(repoPath, mounts);
        var kept = new List<RuleFile>();
        var omitted = new List<string>();
        var used = 0;

        foreach (var relative in Candidates(folders, mounts, order))
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

            kept.Add(new RuleFile(relative, text, Candidate(relative, mounts).WithinMount));
            used += text.Length;
        }

        return new RuleBundle(kept, omitted, used)
        {
            MissingMounts = EmptyRuleMounts(repoPath, mounts, folders),
        };
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
        IReadOnlyList<string> folders, IReadOnlyList<SubmoduleMount> mounts, RuleOrder order)
    {
        foreach (var file in InstructionFiles)
        {
            yield return file;
        }

        foreach (var path in folders.Where(p => !UnderAnyMount(p, mounts)))
        {
            yield return path;
        }

        foreach (var path in order.Mount([.. folders.Where(p => UnderAnyMount(p, mounts)).Select(p => Candidate(p, mounts))]))
        {
            yield return path;
        }
    }

    /// <summary>
    /// A mount's rule file, carrying the name a stage tier addresses it by.
    /// </summary>
    /// <remarks>
    /// The mount prefix is stripped HERE, where the mounts are known, so an order never has to guess
    /// where a mount root ends — which is the only way a tier entry can name one file rather than
    /// every path that happens to end the same way.
    /// </remarks>
    private static RuleCandidate Candidate(string relative, IReadOnlyList<SubmoduleMount> mounts) =>
        new(relative, mounts
            .Where(m => relative.StartsWith(m.Path + "/", StringComparison.OrdinalIgnoreCase))
            .Select(m => relative[(m.Path.Length + 1)..])
            .FirstOrDefault(relative));

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
    private static IReadOnlyList<string> EmptyRuleMounts(
        string repoPath, IReadOnlyList<SubmoduleMount> mounts, IReadOnlyList<string> folders) =>
        [.. mounts
            .Where(m => IsRulesMount(m.Path) && MissingRuleMount(repoPath, m, folders))
            .Select(m => m.Path)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)];

    private static bool IsRulesMount(string path) =>
        path.Equals(NeutralMount, StringComparison.OrdinalIgnoreCase) ||
        RuleFolders.Any(f => path.StartsWith(f.Dir + "/", StringComparison.OrdinalIgnoreCase));

    private static bool MissingRuleMount(string repoPath, SubmoduleMount mount, IReadOnlyList<string> folders) =>
        mount.Path.Equals(NeutralMount, StringComparison.OrdinalIgnoreCase)
            ? !folders.Any(p => UnderAnyMount(p, [mount]))
            : IsEmptyDirectory(Path.Combine(repoPath, mount.Path.Replace('/', Path.DirectorySeparatorChar)));

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
