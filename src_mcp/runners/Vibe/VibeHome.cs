using System.Security.Cryptography;
using CoaiMcp.Runners.Snapshots;

namespace CoaiMcp.Runners.Vibe;

/// <summary>
/// The check every coai-owned <c>VIBE_HOME</c> passes before EVERY Vibe launch (D17 of
/// <c>todo/PLAN_vibe_and_kimi_runtimes.md</c>): it holds only what Vibe itself writes, or the launch is refused with the
/// path named.
/// </summary>
/// <remarks>
/// <para><b>Why a home of coai's own.</b> Vibe reads the person's hooks (a user <c>hooks.toml</c> fired in an untrusted
/// folder 3 of 3, research/RESULTS_vibe_confinement.md §14), their MCP servers (§7: a user server started 3 of 3, and
/// <c>VIBE_MCP_SERVERS=[]</c> did not stop it), their agents (a <c>~/.vibe/agents/plan.toml</c> replaces the built-in
/// <c>plan</c>, §15) and their trust list (§8) from its home. The sign-in is not there — it is in the OS keyring, and an
/// empty home ran signed in 24 of 24 (§12) — so coai gives Vibe a home of its own and never reads the person's.</para>
/// <para><b>Why it is checked before every launch, not once.</b> Vibe writes into the home while it runs, and a second
/// turn (a repair, a retry) starts on what the first left. What is admitted is exactly what an empty home is measured to
/// gain (§22, and the hash manifest of §24): Vibe's bundled plugin files under the manifest's pinned prefix, each by its
/// SHA-256 for the INSTALLED version; Vibe's caches, logs and sessions by name (the manifest's <c>varying</c>); and
/// <c>trusted_folders.toml</c> only with both of its lists empty. Anything else — a <c>hooks.toml</c>, any
/// <c>config.toml</c> (Vibe writes none, §22 and §24, so one that declares hooks, MCP servers or agents is refused with
/// every other one), an <c>agents/</c> or <c>tools/</c> folder, a link anywhere — refuses the launch. The cure is always the
/// same and the sentence says it: delete the home, and the next launch starts from an empty one.</para>
/// <para>Reads the disk and nothing else; never writes, never follows a link.</para>
/// </remarks>
public static class VibeHome
{
    /// <summary>The trust list Vibe writes into an empty home, as its lines (§24: exactly <c>trusted = []</c> and <c>untrusted = []</c>).</summary>
    private const string TrustFile = "trusted_folders.toml";

    /// <summary>
    /// The first thing in <paramref name="home"/> that the installed Vibe (<paramref name="entry"/>) does not write — the
    /// refusal sentence — or empty when the home holds only what Vibe writes. A home not made yet is empty.
    /// </summary>
    public static string Refusal(string home, VibeHomeManifest manifest, VibeHomeEntry entry) =>
        Checked(home, new Rules(manifest, entry, byHash: true));

    /// <summary>
    /// The home's SHAPE alone — every name, folder and link rule, and the trust list — with any file under the pinned
    /// prefix admitted without its hash: what can be asked before the installed version is known, so that nothing Vibe
    /// does not write is in the home when its <c>--version</c> starts.
    /// </summary>
    public static string Refusal(string home, VibeHomeManifest manifest) =>
        Checked(home, new Rules(manifest, new VibeHomeEntry(string.Empty, []), byHash: false));

    private static string Checked(string home, Rules rules)
    {
        try
        {
            return !Directory.Exists(home) ? string.Empty
                : SnapshotLinks.IsLink(home) ? $"the coai-owned Vibe home {home} is a link, and an owned home is never reached through one — delete it"
                : FirstProblem(home, rules);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return $"the coai-owned Vibe home {home} could not be read ({e.Message}); the launch is refused rather than started on a home nobody checked";
        }
    }

    private static string FirstProblem(string home, Rules rules)
    {
        var pending = new Stack<string>([home]);
        while (pending.Count > 0)
        {
            var problem = Inside(home, pending.Pop(), rules, pending);
            if (problem.Length > 0)
            {
                return problem;
            }
        }

        return string.Empty;
    }

    /// <summary>The first problem among one directory's entries; its admitted sub-directories are pushed for later.</summary>
    private static string Inside(string home, string directory, Rules rules, Stack<string> pending)
    {
        foreach (var path in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.Ordinal))
        {
            var problem = Judge(home, path, rules, pending);
            if (problem.Length > 0)
            {
                return problem;
            }
        }

        return string.Empty;
    }

    private static string Judge(string home, string path, Rules rules, Stack<string> pending)
    {
        var relative = Path.GetRelativePath(home, path).Replace('\\', '/');
        if (SnapshotLinks.IsLink(path))
        {
            return Sentence(home, relative, "is a link, and an owned home is never reached through one");
        }

        return Directory.Exists(path) ? Folder(home, path, relative, rules, pending) : Sentence(home, relative, rules.FileProblem(relative, path));
    }

    private static string Folder(string home, string path, string relative, Rules rules, Stack<string> pending)
    {
        if (!rules.FolderAdmitted(relative))
        {
            return Sentence(home, relative + "/", "is not a folder Vibe writes into its home");
        }

        pending.Push(path);

        return string.Empty;
    }

    /// <summary>The refusal for one path, or empty when there is no problem with it.</summary>
    private static string Sentence(string home, string relative, string problem) =>
        problem.Length == 0
            ? string.Empty
            : $"the coai-owned Vibe home {home} holds {relative}, which {problem} — coai refuses to start Vibe on it "
              + $"(D17 of todo/PLAN_vibe_and_kimi_runtimes.md); delete {home} and the next launch starts from an empty home";

    /// <summary>The manifest's three answers for one home, held together so each check is one call.</summary>
    /// <param name="byHash">False for the shape check: a file or folder under the pinned prefix is admitted by place alone.</param>
    private sealed class Rules(VibeHomeManifest manifest, VibeHomeEntry entry, bool byHash)
    {
        private readonly IReadOnlyList<string[]> _varying = [.. manifest.Varying.Select(glob => glob.Split('/'))];

        /// <summary>Why this file may not be in the home, or empty.</summary>
        public string FileProblem(string relative, string path) =>
            relative.StartsWith(manifest.PinnedPrefix, StringComparison.Ordinal) ? Pinned(relative, path)
            : !_varying.Any(glob => Matches(glob, relative.Split('/'))) ? "is not a file Vibe writes into its home"
            : relative == TrustFile ? TrustProblem(path)
            : string.Empty;

        /// <summary>
        /// A folder on the way to an admitted file — a proper prefix of a varying name or of a pinned path; for the shape
        /// check, also any folder under the pinned prefix.
        /// </summary>
        public bool FolderAdmitted(string relative)
        {
            var parts = relative.Split('/');

            return _varying.Any(glob => Leads(glob, parts)) || UnderPinned(relative);
        }

        private bool UnderPinned(string relative) =>
            (!byHash && (relative + "/").StartsWith(manifest.PinnedPrefix, StringComparison.Ordinal))
            || manifest.PinnedPrefix.StartsWith(relative + "/", StringComparison.Ordinal)
            || entry.Pinned.Any(file => file.Path.StartsWith(relative + "/", StringComparison.Ordinal));

        private string Pinned(string relative, string path) => (byHash, entry.At(relative)) switch
        {
            (false, _) => string.Empty,
            (_, []) => $"is not one of the files Vibe {entry.Version} unpacks (shared/vibe-home-manifest.json)",
            (_, [var pinned]) when pinned.Sha256 == Sha256Of(path) => string.Empty,
            _ => $"does not have the bytes Vibe {entry.Version} writes there (shared/vibe-home-manifest.json)",
        };
    }

    /// <summary>A trust list that trusts nothing, as Vibe writes it into an empty home; anything else is refused.</summary>
    private static string TrustProblem(string path) =>
        File.ReadAllLines(path).Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith('#'))
            .All(line => line is "trusted = []" or "untrusted = []")
            ? string.Empty
            : "trusts or distrusts a folder — an owned home's trust list is empty";

    /// <summary>
    /// Whether <paramref name="parts"/> matches the glob: <c>*</c> is one segment, a final <c>**</c> one or more.
    /// </summary>
    internal static bool Matches(IReadOnlyList<string> glob, IReadOnlyList<string> parts) =>
        glob.Count > 0 && glob[^1] == "**"
            ? parts.Count >= glob.Count && Prefix(glob, parts, glob.Count - 1)
            : parts.Count == glob.Count && Prefix(glob, parts, glob.Count);

    /// <summary>Whether <paramref name="parts"/> is a folder a file matching the glob could live in.</summary>
    internal static bool Leads(IReadOnlyList<string> glob, IReadOnlyList<string> parts) =>
        glob.Count > 0 && glob[^1] == "**"
            ? parts.Count < glob.Count - 1 ? Prefix(glob, parts, parts.Count) : Prefix(glob, parts, glob.Count - 1)
            : parts.Count < glob.Count && Prefix(glob, parts, parts.Count);

    private static bool Prefix(IReadOnlyList<string> glob, IReadOnlyList<string> parts, int count) =>
        Enumerable.Range(0, count).All(at => glob[at] == "*" || glob[at] == parts[at]);

    private static string Sha256Of(string path)
    {
        using var stream = File.OpenRead(path);

        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
