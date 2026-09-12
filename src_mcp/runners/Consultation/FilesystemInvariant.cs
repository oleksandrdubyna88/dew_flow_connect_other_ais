using System.Collections.Immutable;
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
/// untracked file and the ignored entries lists what git can see. A <c>stat</c> of every listed FILE
/// catches what git does not hash: an overwritten ignored <c>.env</c>, an already-modified tracked
/// file modified again. A <c>stat</c> of <c>.git</c>'s own metadata catches an edited hook or config,
/// which no status line ever shows.</para>
/// <para><b>The residual, named:</b> a file changed deep inside an ignored DIRECTORY is not seen — git
/// lists the directory as one entry and walking its contents would cost seconds on every call. An
/// OS-level write audit is out of scope.</para>
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
            entries["<git>/" + name] = new TreeEntry("git", Fingerprint(full));
        }

        return new FilesystemSnapshot(entries.ToImmutable());
    }

    private static string KindOf(string code) => code switch
    {
        "??" => "untracked",
        "!!" => "ignored",
        _ => "tracked",
    };

    /// <summary>Size and modification time, or what stands in for them.</summary>
    private static string Fingerprint(string full)
    {
        if (File.Exists(full))
        {
            var info = new FileInfo(full);
            return $"{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        }

        return Directory.Exists(full)
            ? $"dir|{Directory.GetLastWriteTimeUtc(full).Ticks}"
            : "missing";
    }

    /// <summary>
    /// The repository metadata worth watching: <c>HEAD</c>, <c>config</c>, <c>index</c> and every hook.
    /// </summary>
    /// <remarks>
    /// <c>.git</c> is a FILE in a linked worktree (<c>gitdir: …</c>), which is how this feature is being
    /// built; then the worktree's own directory holds <c>HEAD</c> and <c>index</c>, and <c>config</c>
    /// and the hooks live in the common directory it points at. Both shapes are read.
    /// </remarks>
    private static IEnumerable<(string Name, string Full)> GitMetadata(string repoPath)
    {
        var dotGit = Path.Combine(repoPath, ".git");
        var gitDir = Directory.Exists(dotGit) ? dotGit : GitDirOf(dotGit);
        if (gitDir.Length == 0)
        {
            yield break;
        }

        foreach (var name in Metadata)
        {
            yield return (name, Path.Combine(gitDir, name));
        }

        var hooks = Path.Combine(gitDir, "hooks");
        if (Directory.Exists(hooks))
        {
            foreach (var hook in Directory.EnumerateFiles(hooks).Order(StringComparer.Ordinal))
            {
                yield return ("hooks/" + Path.GetFileName(hook), hook);
            }
        }
    }

    /// <summary>The directory a <c>.git</c> FILE points at, or empty when it is neither a file nor readable.</summary>
    private static string GitDirOf(string dotGitFile)
    {
        if (!File.Exists(dotGitFile))
        {
            return string.Empty;
        }

        try
        {
            var pointer = File.ReadAllText(dotGitFile).Trim();
            return pointer.StartsWith("gitdir:", StringComparison.Ordinal)
                ? Path.GetFullPath(pointer["gitdir:".Length..].Trim(), Path.GetDirectoryName(dotGitFile)!)
                : string.Empty;
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }
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
