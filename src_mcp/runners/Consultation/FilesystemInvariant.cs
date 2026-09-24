using System.Collections.Immutable;
using System.Security.Cryptography;
using CoaiMcp.Core.Consultation;
using CoaiMcp.Runners.Context;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Runners.Consultation;

/// <summary>
/// Collects the fingerprints <see cref="FilesystemSnapshot"/> compares — the impure half of the
/// invariant that refuses to trust a third-party binary's read-only flag.
/// </summary>
/// <remarks>
/// <para>Three sources, each closing a hole the plan round named. <c>git status</c> with every
/// untracked file and the ignored entries lists what git can see. A read of every listed FILE catches
/// what git does not hash: an overwritten ignored <c>.env</c>, an already-modified tracked file
/// modified again. A read of the repository's own metadata catches an edited hook or config, which no
/// status line ever shows.</para>
/// <para><b>The residual, named:</b> a file changed deep inside an ignored DIRECTORY is not seen — git
/// lists the directory as one entry and walking its contents would cost seconds on every call. An
/// OS-level write audit is out of scope. A person's OWN git operation during a consultation — a
/// checkout, a commit, an edit — is a real change to the tree and is reported as one; that is the
/// honest behaviour, not a false positive.</para>
/// </remarks>
public sealed class FilesystemInvariant(IProcessLauncher launcher)
{
    /// <summary>
    /// The repository metadata a change to is never innocent.
    /// </summary>
    /// <remarks>
    /// <b><c>index</c> is deliberately NOT here, and neither is the <c>.git</c> directory itself.</b>
    /// <c>git status</c> refreshes the index's stat cache and rewrites the file — so the invariant's
    /// own FIRST snapshot changes it, and the second one then reports the consultant for it. Found the
    /// moment the scenario test ran: every consultation failed closed with
    /// <c>&lt;git&gt;/index (.git metadata changed)</c> and no consultant had touched anything. The
    /// directory's own mtime moves with the index for the same reason. What is left is what a
    /// read-only git command does not write: where HEAD points, the configuration, and the hooks.
    /// </remarks>
    private static readonly string[] Metadata = ["HEAD", "config"];

    /// <summary>What is watched in the COMMON directory of a linked worktree: its config, not its HEAD.</summary>
    private static readonly string[] SharedMetadata = ["config"];

    /// <summary>
    /// How much of a file is hashed. Past it, size and mtime stand in.
    /// </summary>
    /// <remarks>
    /// The hash is what makes a same-size, same-mtime rewrite visible — a compromised consultant can
    /// set both back, and <c>utime</c> is not a privileged call (codex, Blocking, code round). Hashing
    /// is bounded because an ignored directory can hold a gigabyte of build output: past this size a
    /// file falls back to the stat pair, which is the weaker guarantee, and the files that MATTER
    /// here — a config, a hook, a `.env` — are kilobytes.
    /// </remarks>
    private const long HashUpTo = 1024 * 1024;

    public async Task<FilesystemSnapshot> SnapshotAsync(string repoPath, CancellationToken ct = default)
    {
        var status = await launcher.RunAsync(
            new ProcessRequest("git", ["status", "--porcelain=v1", "-z", "--untracked-files=all", "--ignored=matching"], repoPath), ct);
        if (status.ExitCode != 0)
        {
            throw new ContextException("status", status.StdErr.Trim());
        }

        var entries = ImmutableSortedDictionary.CreateBuilder<string, TreeEntry>(StringComparer.Ordinal);
        foreach (var (code, path) in PorcelainReader.Read(status.StdOut))
        {
            entries[path] = new TreeEntry(KindOf(code), $"{code}|{Fingerprint(Path.Combine(repoPath, path))}");
        }

        foreach (var (name, full) in GitMetadata(repoPath))
        {
            var fingerprint = Path.GetFileName(full) == "config" ? await ConfigFingerprintAsync(full, ct) : Fingerprint(full);
            entries["<git>/" + name] = new TreeEntry("git", fingerprint);
        }

        return new FilesystemSnapshot(entries.ToImmutable());
    }

    private static string KindOf(string code) => code switch
    {
        "??" => "untracked",
        "!!" => "ignored",
        _ => "tracked",
    };

    /// <summary>The file's content hash where that is affordable, and its stat pair otherwise.</summary>
    /// <remarks>
    /// <b>A DIRECTORY fingerprints as its existence and nothing more.</b> git lists an ignored
    /// directory as ONE entry, so its mtime was standing in for everything inside it — and a build,
    /// a language server or an editor writing one temporary file into <c>bin/</c> or
    /// <c>node_modules/</c> between the two snapshots moves that mtime while the contents end up
    /// exactly as they were. Every consultation on a machine with a watcher running would have failed
    /// closed over it. Found by the CONSULTANT, asked on this feature's own live check where this
    /// invariant's most likely false positive was, and reproduced as a red test before it was
    /// believed. What is kept is the entry itself: a directory that appears or disappears is still a
    /// change. What is given up is child churn inferred from a timestamp, which was never a fact
    /// about the consultant — see the residual named in the class remarks.
    /// </remarks>
    private static string Fingerprint(string full)
    {
        if (Directory.Exists(full))
        {
            return "dir";
        }

        if (!File.Exists(full))
        {
            return "missing";
        }

        var info = new FileInfo(full);

        return info.Length <= HashUpTo
            ? $"{info.Length}|{Hash(full)}"
            : $"{info.Length}|{info.LastWriteTimeUtc.Ticks}";
    }

    private static string Hash(string full)
    {
        try
        {
            using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Unreadable is itself a fingerprint, and a STABLE one: a file locked in both snapshots
            // compares equal, while one that becomes unreadable between them is a change and is
            // reported as one.
            return "unreadable:" + e.GetType().Name;
        }
    }

    /// <summary>
    /// The repository metadata worth watching: <c>HEAD</c>, <c>config</c> and every hook — in the
    /// worktree's own git directory AND in the common one it shares.
    /// </summary>
    /// <remarks>
    /// <c>.git</c> is a FILE in a linked worktree (<c>gitdir: …</c>), which is how this feature is
    /// being built. Its own directory holds <c>HEAD</c>; <c>config</c> and the hooks live in the
    /// COMMON directory that <c>commondir</c> points at, and watching only the first leaves an edited
    /// shared hook invisible to both snapshots (codex, code round).
    /// </remarks>
    private static IEnumerable<(string Name, string Full)> GitMetadata(string repoPath)
    {
        foreach (var (label, dir) in GitDirectories(repoPath))
        {
            // The COMMON directory's HEAD is the main worktree's, not this checkout's: it moves whenever
            // anybody switches branch there, and watching it withheld consultations over a sibling's
            // `git switch` (issue #376). This checkout's own HEAD is in its own directory, watched below.
            foreach (var name in label.Length == 0 ? Metadata : SharedMetadata)
            {
                yield return ($"{label}{name}", Path.Combine(dir, name));
            }

            foreach (var hook in HooksIn(Path.Combine(dir, "hooks")))
            {
                yield return ($"{label}hooks/{Path.GetFileName(hook)}", hook);
            }
        }
    }

    /// <summary>
    /// The hook files in a directory, or none when it cannot be listed.
    /// </summary>
    /// <remarks>
    /// The enumeration is guarded because the whole SNAPSHOT hangs off it: a hooks path that is a
    /// symlink to somewhere gone, or a permission that changes between the two calls, would otherwise
    /// throw out of the invariant and reach the client as an exception rather than a sentence — and
    /// the failure would be ours, on a check that exists to report somebody else's. Listing nothing is
    /// the honest answer: the two snapshots then agree, which is what an unreadable directory means.
    /// (local, code round.)
    /// </remarks>
    private static IEnumerable<string> HooksIn(string hooks)
    {
        try
        {
            return Directory.Exists(hooks)
                ? [.. Directory.EnumerateFiles(hooks).Order(StringComparer.Ordinal)]
                : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// A config compared by what it SAYS (<see cref="ConfigMeaning"/>), not by its bytes — issue #376.
    /// </summary>
    /// <remarks>
    /// Read with <c>git config --file … --list -z</c>, WITHOUT <c>--includes</c>, so an include being added is
    /// itself a visible change. A config git cannot read falls back to the byte fingerprint — the strict
    /// direction — and a missing one to <see cref="Fingerprint"/>'s own marker. Measured: an empty file
    /// lists nothing and exits 0; a malformed one exits 128 (and <c>git status</c> refuses it too).
    /// </remarks>
    private async Task<string> ConfigFingerprintAsync(string config, CancellationToken ct)
    {
        if (!File.Exists(config))
        {
            return Fingerprint(config);
        }

        var listed = await launcher.RunAsync(
            new ProcessRequest("git", ["config", "--file", config, "--list", "-z"], Path.GetDirectoryName(config) ?? "."), ct);

        return listed.ExitCode == 0
            ? "config|" + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(ConfigMeaning.Of(listed.StdOut))))
            : Fingerprint(config);
    }

    /// <summary>The worktree's git directory, and the common directory when it is a different one.</summary>
    private static IEnumerable<(string Label, string Directory)> GitDirectories(string repoPath)
    {
        var dotGit = Path.Combine(repoPath, ".git");
        var gitDir = Directory.Exists(dotGit) ? dotGit : PointedAt(dotGit);
        if (gitDir.Length == 0)
        {
            yield break;
        }

        yield return (string.Empty, gitDir);

        var common = CommonDirOf(gitDir);
        if (common.Length > 0 && !SamePath(common, gitDir))
        {
            yield return ("common/", common);
        }
    }

    /// <summary>Where a <c>commondir</c> file points, resolved against the worktree's git directory.</summary>
    private static string CommonDirOf(string gitDir) => Resolved(Path.Combine(gitDir, "commondir"), gitDir, string.Empty);

    /// <summary>The directory a <c>.git</c> FILE points at, or empty when it is neither a file nor readable.</summary>
    private static string PointedAt(string dotGitFile) =>
        Resolved(dotGitFile, Path.GetDirectoryName(dotGitFile) ?? ".", "gitdir:");

    private static string Resolved(string pointerFile, string relativeTo, string prefix)
    {
        if (!File.Exists(pointerFile))
        {
            return string.Empty;
        }

        try
        {
            var pointer = File.ReadAllText(pointerFile).Trim();
            if (prefix.Length > 0)
            {
                pointer = pointer.StartsWith(prefix, StringComparison.Ordinal) ? pointer[prefix.Length..].Trim() : string.Empty;
            }

            return pointer.Length > 0 ? Path.GetFullPath(pointer, relativeTo) : string.Empty;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    private static bool SamePath(string one, string other) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(one)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(other)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}

/// <summary>
/// Reads <c>git status --porcelain=v1 -z</c>: <c>XY path\0</c>, with a rename or copy carrying a
/// second <c>\0</c>-terminated path — the ORIGINAL — that must be consumed, not read as an entry.
/// </summary>
public static class PorcelainReader
{
    public static IReadOnlyList<(string Code, string Path)> Read(string output)
    {
        var fields = output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var entries = new List<(string, string)>();
        for (var i = 0; i < fields.Length; i++)
        {
            var field = fields[i];
            if (field.Length < 4)
            {
                continue;
            }

            var code = field[..2];
            entries.Add((code, field[3..].TrimEnd('/')));
            if (code[0] is 'R' or 'C' || code[1] is 'R' or 'C')
            {
                i++; // the path it came from
            }
        }

        return entries;
    }
}
