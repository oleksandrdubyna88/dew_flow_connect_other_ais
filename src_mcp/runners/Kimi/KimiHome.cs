using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Vibe;

namespace CoaiMcp.Runners.Kimi;

/// <summary>What disposing a Kimi home came to — a closed union: gone (and whether its junction was there), or stopped with the reason and nothing more deleted.</summary>
public abstract record Disposal
{
    private Disposal()
    {
    }

    /// <summary>Nothing left to lose: the home is gone (or was only unlinked, for <see cref="KimiHome.DisposeAllUnder"/>).</summary>
    public sealed record Clean(bool LinkWasThere) : Disposal;

    /// <summary>The disposal stopped, deleting nothing more; a caller LOGS the reason and keeps the home for its next beat.</summary>
    public sealed record Stopped(string Reason) : Disposal;
}

/// <summary>
/// The coai-owned Kimi home (D19 of <c>todo/PLAN_vibe_and_kimi_runtimes.md</c>; E4.2 brief sections 8 and 14): <c>home/</c> — the
/// <c>credentials</c> junction to the person's own folder plus ONE coai-written <c>config.toml</c> — beside an empty <c>os/</c>.
/// Made once, checked as an ALLOWLIST before EVERY launch, and disposed junction-first with the person's folder verified.
/// </summary>
/// <remarks>
/// <para><b>The check is the <c>VibeHome</c> walk with one rule inverted.</b> Vibe's home is checked against what Vibe writes; Kimi's
/// against what Kimi 2.1.1 writes into its home (research/RESULTS_kimi_confinement.md section 18): the folders <c>cache</c>,
/// <c>logs</c>, <c>sessions</c>, <c>user-history</c>, <c>file-history</c>, <c>telemetry</c>, <c>updates</c>; the files
/// <c>session_index.jsonl</c> and <c>workspaces.json</c>, and — found by E4.3's live measurement (M-K15, research/RESULTS_kimi_confinement.md section 27) —
/// <c>device_id</c>, which Kimi writes when a launch's token refresh rotates the sign-in; <c>workspace-trust</c> only EMPTY. Everything else is refused by name — an
/// allowlist, so a <c>mcp.json</c>, a <c>hooks.toml</c>, <c>skills/</c>, <c>AGENTS.md</c> or a name nobody listed never reaches a
/// launch. At <c>credentials</c> the rule inverts: it must be a link to the person's folder and is never descended. Reads only; never
/// follows a link; a read failure refuses rather than starting on a home nobody checked. The root may hold more than <c>home/</c> and
/// <c>os/</c> (S5's conversation root holds <c>tree/</c> and the turn lock); Kimi never reads it, so it is judged only for being a link.</para>
/// <para><b>The disposal's order is the code's order</b> (<see cref="Dispose"/>): the live-tree probe, the junction unlinked by its own
/// name, the person's folder verified, only then the deleter. D19's "junction first" is therefore done explicitly wherever the caller
/// KNOWS it holds a Kimi home, on top of the in-deleter check every caller gets (E4.1).</para>
/// <para><b>The stated limit</b> (risk consultation 7de9430b, decision 11): the deleter's link checks are path checks at one moment;
/// a folder swapped for a junction between the check and the enumeration could still be entered. Only a same-user process can swap a
/// folder inside coai's owned root, and that process can open the person's <c>credentials</c> directly — coai is no deputy to confuse.</para>
/// </remarks>
public static class KimiHome
{
    /// <summary>The prefix of a reviewer's home inside a round's scratch, beside Vibe's <c>vh-</c>.</summary>
    public const string Prefix = "kh-";

    private const string LinkSentence = "is a link, and an owned home is never reached through one";

    private static readonly string[] AdmittedFolders = ["cache", "logs", "sessions", "user-history", "file-history", "telemetry", "updates"];

    private static readonly string[] AdmittedFiles = ["session_index.jsonl", "workspaces.json", "device_id"];

    private static readonly AsyncLocal<Action<string>?> _afterUnlink = new();

    /// <summary>TEST ONLY — null in the product: runs between <see cref="Dispose"/>'s unlink of <c>credentials</c> and its second read of the target, so a test can make a loss happen in that window. Per async flow.</summary>
    internal static Action<string>? AfterUnlink
    {
        get => _afterUnlink.Value;
        set => _afterUnlink.Value = value;
    }

    /// <summary>
    /// Before the launch's turn: the home made when it is fresh — empty on success, else the sentence. A FRESH home (no
    /// <c>credentials</c>, no <c>config.toml</c>) gets the junction and the config; one that has either is written to by nobody here,
    /// and the validation inside the turn judges it.
    /// </summary>
    public static string Made(HostKind host, KimiPlaces places, byte[] expectedConfig)
    {
        if (OwnedPaths.FirstLink(places.OwnedRoot, places.Home, places.Os) is { Length: > 0 } link)
        {
            return $"{link} {LinkSentence}";
        }

        try
        {
            Directory.CreateDirectory(places.Home);
            Directory.CreateDirectory(places.Os);

            return Fresh(places) ? Fill(host, places, expectedConfig) : string.Empty;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return $"the coai-owned Kimi home {places.OwnedRoot} could not be made ({e.Message})";
        }
    }

    /// <summary>
    /// Inside the turn: the FIRST problem with the home — the root, <c>home/</c> or <c>os/</c> a link, <c>os/</c> missing or
    /// holding anything, then the walk of <c>home/</c> — or empty when it holds only what Kimi writes and what coai put there.
    /// </summary>
    public static string Refusal(KimiPlaces places, byte[] expectedConfig)
    {
        try
        {
            return new Func<string>[]
            {
                () => Links(places),
                () => OsProblem(places),
                () => HomeProblem(places, expectedConfig),
            }
            .Select(check => check())
            .FirstOrDefault(problem => problem.Length > 0) ?? string.Empty;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Sentence(places, $"could not be read ({e.Message}); the launch is refused rather than started on a home nobody checked");
        }
    }

    /// <summary>A reviewer's home folder in a round's scratch: <c>kh-</c> and the first ten hex of the vendor/role hash — <c>VibeRuntime</c>'s rule, one hash, two prefixes.</summary>
    public static string HomeFor(string scratch, string provider, string role) =>
        Path.Combine(scratch, Prefix + VibeRuntime.Short($"{provider}/{role}"));

    /// <summary>
    /// The home disposed in D19's order, for every caller that knows it holds a Kimi home: (1) the live-tree probe, then
    /// <c>credentials</c> unlinked by its own name and the person's folder verified; (2) a real folder or nothing there — nothing to
    /// unlink; (3) only then the shared deleter. A <see cref="Disposal.Stopped"/> deletes nothing more and is LOGGED by every caller.
    /// </summary>
    public static Disposal Dispose(string root, Action<string> warn)
    {
        var unlinked = Unlinked(root);

        return unlinked is Disposal.Clean clean ? Removed(root, clean, warn) : unlinked;
    }

    /// <summary>
    /// <see cref="Dispose"/>'s step (1) on the folder itself when it holds a home (S5's conversation root) and on every direct child
    /// named <see cref="Prefix"/>…, answering how many were clean. The rest of each folder is left to the caller's deleter, which
    /// meets no link any more; a <see cref="Disposal.Stopped"/> is warned and the walk goes on.
    /// </summary>
    public static int DisposeAllUnder(string folder, Action<string> warn)
    {
        IEnumerable<string> itself = Directory.Exists(Path.Combine(folder, "home")) ? [folder] : [];

        // A root that is itself a link (or whose home/ is one) is NOT a home of this folder's: unlinking `credentials` through it would reach
        // a folder outside coai's tree. It is left to the shared deleter, which removes the link by its own name and never enters it.
        return itself.Concat(Children(folder)).Where(root => ThroughNoLink(root).Length == 0).Select(Unlinked).Count(disposal => Said(disposal, warn));
    }

    private static IEnumerable<string> Children(string folder) =>
        Directory.Exists(folder)
            ? Directory.EnumerateDirectories(folder).Where(path => Path.GetFileName(path).StartsWith(Prefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal)
            : [];

    /// <summary>Whether the disposal was clean; a stop is warned.</summary>
    private static bool Said(Disposal disposal, Action<string> warn)
    {
        if (disposal is Disposal.Stopped stopped)
        {
            warn(stopped.Reason);
        }

        return disposal is Disposal.Clean;
    }

    // ---------- the make ----------

    private static bool Fresh(KimiPlaces places) =>
        !OwnedPaths.IsLinkIfThere(places.Link) && !Directory.Exists(places.Link) && !File.Exists(places.Config);

    private static string Fill(HostKind host, KimiPlaces places, byte[] expectedConfig)
    {
        var linked = Junctions.Make(host, places.Link, places.Credentials);
        if (linked.Length > 0)
        {
            return $"the Kimi sign-in could not be linked: {linked}";
        }

        File.WriteAllBytes(places.Config, expectedConfig);

        return string.Empty;
    }

    // ---------- the check ----------

    private static string Links(KimiPlaces places) =>
        OwnedPaths.FirstLink(places.OwnedRoot, places.Home, places.Os) is { Length: > 0 } link ? Sentence(places, $"{link} {LinkSentence}") : string.Empty;

    private static string OsProblem(KimiPlaces places)
    {
        if (!Directory.Exists(places.Os))
        {
            return Sentence(places, $"{places.Os} is missing — the empty OS home Kimi is given");
        }

        return Directory.EnumerateFileSystemEntries(places.Os).Order(StringComparer.Ordinal).FirstOrDefault() is { } entry
            ? Sentence(places, $"os/ holds {Path.GetFileName(entry)}: the empty OS home coai gives Kimi received a file")
            : string.Empty;
    }

    private static string HomeProblem(KimiPlaces places, byte[] expectedConfig)
    {
        if (!Directory.Exists(places.Home))
        {
            return Sentence(places, "has no home/ folder");
        }

        // The two names the home MUST hold are asked first, so an absent one is a refusal and not a silence.
        return new[] { Credentials(places), Config(places, places.Config, expectedConfig) }.FirstOrDefault(problem => problem.Length > 0)
            ?? Directory.EnumerateFileSystemEntries(places.Home).Order(StringComparer.Ordinal)
                .Select(entry => Judge(places, entry, expectedConfig))
                .FirstOrDefault(problem => problem.Length > 0)
            ?? string.Empty;
    }

    /// <summary>One top-level entry of <c>home/</c>: the two special names, a link anywhere else, <c>bin</c>, the admitted set, or refused by name.</summary>
    private static string Judge(KimiPlaces places, string entry, byte[] expectedConfig)
    {
        var name = Path.GetFileName(entry);
        return name is "credentials" or "config.toml" ? string.Empty
            : OwnedPaths.IsLinkIfThere(entry) ? Sentence(places, $"holds {name}, which {LinkSentence}")
            : Admitted(places, entry, name);
    }

    private static string Credentials(KimiPlaces places)
    {
        if (!OwnedPaths.IsLinkIfThere(places.Link))
        {
            return Sentence(places, Directory.Exists(places.Link)
                ? $"credentials is a real folder, not the junction to {places.Credentials}"
                : "has no credentials junction");
        }

        var read = Junctions.Target(places.Link);

        return Junctions.SameFolder(read, places.Credentials) ? string.Empty : Sentence(places, $"credentials points at {read}, not at {places.Credentials}");
    }

    private static string Config(KimiPlaces places, string entry, byte[] expectedConfig) =>
        OwnedPaths.IsLinkIfThere(entry) ? Sentence(places, $"holds config.toml, which {LinkSentence}")
        : File.Exists(entry) && File.ReadAllBytes(entry).AsSpan().SequenceEqual(expectedConfig)
            ? string.Empty
            : Sentence(places, "config.toml is not what coai writes for this person and this model — tampered, or your Kimi sign-in changed since the home was made (a re-login)");

    private static string Admitted(KimiPlaces places, string entry, string name)
    {
        if (name == "bin")
        {
            return Sentence(places, $"holds bin/, which means Kimi downloaded ripgrep or fd into the home — coai refuses code in an owned home; install rg and fd ({KimiTools.RipgrepInstall}, {KimiTools.FdInstall})");
        }

        if (name == "workspace-trust")
        {
            return TrustProblem(places, entry);
        }

        return AdmittedFolders.Contains(name, StringComparer.Ordinal) && Directory.Exists(entry) ? Inside(places, entry)
            : AdmittedFiles.Contains(name, StringComparer.Ordinal) && File.Exists(entry) ? string.Empty
            : Sentence(places, $"holds {name}{(Directory.Exists(entry) ? "/" : string.Empty)}, which is not something Kimi writes into its home");
    }

    /// <summary>An owned home trusts nothing: <c>workspace-trust/</c> is admitted only EMPTY.</summary>
    private static string TrustProblem(KimiPlaces places, string entry) =>
        Directory.Exists(entry) && Directory.EnumerateFileSystemEntries(entry).Order(StringComparer.Ordinal).FirstOrDefault() is { } trusted
            ? Sentence(places, $"holds workspace-trust/{Path.GetFileName(trusted)}, which trusts a folder — an owned home trusts nothing")
            : Directory.Exists(entry) ? string.Empty : Sentence(places, "holds workspace-trust, which is not a folder Kimi writes");

    /// <summary>Inside an admitted folder anything is admitted but a link; a link is never entered.</summary>
    private static string Inside(KimiPlaces places, string folder)
    {
        var pending = new Stack<string>([folder]);
        while (pending.Count > 0)
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(pending.Pop()).Order(StringComparer.Ordinal))
            {
                if (OwnedPaths.IsLinkIfThere(entry))
                {
                    return Sentence(places, $"holds {Relative(places, entry)}, which {LinkSentence}");
                }

                if (Directory.Exists(entry))
                {
                    pending.Push(entry);
                }
            }
        }

        return string.Empty;
    }

    private static string Relative(KimiPlaces places, string path) => Path.GetRelativePath(places.Home, path).Replace('\\', '/');

    private static string Sentence(KimiPlaces places, string problem) =>
        $"the coai-owned Kimi home {places.OwnedRoot}: {problem} — coai refuses to start Kimi on it (D19 of todo/PLAN_vibe_and_kimi_runtimes.md); "
        + $"delete {places.OwnedRoot} and the next launch makes a fresh home";

    // ---------- the disposal ----------

    /// <summary>Step (1) alone: the live-tree probe, the junction unlinked by its own name, the person's folder verified. Clean when there is nothing (more) to unlink.</summary>
    private static Disposal Unlinked(string root)
    {
        if (ThroughNoLink(root) is { Length: > 0 } through)
        {
            return new Disposal.Stopped(through);
        }

        var link = Path.Combine(root, "home", "credentials");
        if (!OwnedPaths.IsLinkIfThere(link))
        {
            return new Disposal.Clean(false);
        }

        return Alive(root) is { Length: > 0 } alive ? new Disposal.Stopped(alive) : Unlinking(root, link);
    }

    /// <summary>
    /// Why `<paramref name="root"/>/home/credentials` cannot be reached without passing through a link — the root or its `home/` is one — or
    /// empty. Checked BEFORE anything is unlinked: a path through a link names a folder outside coai's tree, and the junction at its end
    /// is somebody else's. The deleter (E4.1) removes such a link by its own name.
    /// </summary>
    private static string ThroughNoLink(string root) =>
        OwnedPaths.FirstLink(root, Path.Combine(root, "home")) is { Length: > 0 } link
            ? $"{link} is a link, and an owned home is never unlinked through one — it is left to the deleter, which removes the link by its own name and never enters it"
            : string.Empty;

    /// <summary>Whether a tree is alive on THIS home — asked of the home's own file, before anything is unlinked and before anything outside coai's tree is opened. Free proceeds; Held or Unknown stop.</summary>
    private static string Alive(string root)
    {
        var tree = Path.Combine(root, KimiCredentialsLock.HomeHoldName);

        return KimiCredentialsLock.HomeProbe(root) == LockProbe.Free
            ? string.Empty
            : $"a Kimi started on {root} is still running (its tree hold {tree} is held) — the home is kept until it ends";
    }

    private static Disposal Unlinking(string root, string link)
    {
        var target = Junctions.Target(link);
        var before = GitScratch.Look(target, isDirectory: true);
        try
        {
            Directory.Delete(link, recursive: false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new Disposal.Stopped($"{link} could not be unlinked ({e.Message}) — nothing was deleted");
        }

        AfterUnlink?.Invoke(link);

        return Verified(root, link, target, before);
    }

    private static Disposal Verified(string root, string link, string target, GitScratch.Held before)
    {
        if (OwnedPaths.IsLinkIfThere(link) || Directory.Exists(link))
        {
            return new Disposal.Stopped($"{link} still exists after its unlink — nothing was deleted");
        }

        var missing = GitScratch.StillMissing(target, before, isDirectory: true);

        return missing.Count == 0
            ? new Disposal.Clean(true)
            : new Disposal.Stopped($"the folder {target} lost {string.Join(", ", missing)} during the unlink of {link} — nothing more was deleted; check it before deleting {root} again");
    }

    /// <summary>Step (3): the rest of the home by the shared deleter, whose own check meets no link at <c>credentials</c> any more; a link elsewhere that lost its target stops here.</summary>
    private static Disposal Removed(string root, Disposal.Clean clean, Action<string> warn)
    {
        if (!Directory.Exists(root) && !OwnedPaths.IsLinkIfThere(root))
        {
            return clean;
        }

        try
        {
            GitScratch.DeleteEvenIfReadOnly(root);

            return clean;
        }
        catch (LinkTargetLostException e)
        {
            return new Disposal.Stopped(e.Message);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            warn($"the coai-owned Kimi home {root} could not be removed and is kept for the next sweep: {e.Message}");

            return new Disposal.Stopped(e.Message);
        }
    }
}
