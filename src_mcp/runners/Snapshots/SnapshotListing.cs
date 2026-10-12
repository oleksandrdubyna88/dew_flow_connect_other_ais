using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Worktrees;

namespace CoaiMcp.Runners.Snapshots;

/// <summary>What one index entry is, by its mode: a file to copy, or a symbolic link the snapshot refuses.</summary>
internal enum EntryKind
{
    File,
    Symlink,
}

/// <summary>One path git listed, snapshot-relative with forward slashes.</summary>
internal sealed record SnapshotEntry(string Path, EntryKind Kind);

/// <summary>What listing a tree came to.</summary>
internal abstract record Listing
{
    private Listing()
    {
    }

    public sealed record Listed(IReadOnlyList<SnapshotEntry> Entries, IReadOnlyList<SnapshotUnavailable> Unavailable) : Listing;

    public sealed record Failed(string Reason) : Listing;
}

/// <summary>
/// Asks git what a tree holds — the index with modes, and for a live tree the untracked-not-ignored files — descending
/// into every populated submodule, through the repository's one process launcher.
/// </summary>
/// <remarks>
/// <para><c>ls-files --stage</c> rather than a plain <c>ls-files</c>, because the MODE is what tells a symbolic link
/// (<c>120000</c>) and a submodule (<c>160000</c>) from a file, and both are decided here before any byte is read: a
/// committed link is refused on every platform, even one whose git checked it out as a plain file; a gitlink is either
/// descended into — its own index, at the revision checked out there — or named unavailable when nothing is checked out
/// there (<c>SubmodulePopulator</c> says what "populated" means, and the same answer is used).</para>
/// <para><c>ls-files --others --exclude-standard</c> is the untracked list the consultant's context already reads
/// (<c>ContextAssembler.CollectWorkingTreeAsync</c>); it lists files, never directories, so a whole untracked tree
/// arrives file by file and ignored paths never arrive at all.</para>
/// </remarks>
internal sealed class SnapshotListing(IProcessLauncher launcher)
{
    /// <summary>Submodules inside submodules inside …: eight levels is more than any repository here, and a bound is owed.</summary>
    private const int NestingLimit = 8;

    private const string SymlinkMode = "120000";
    private const string GitlinkMode = "160000";

    public async Task<Listing> ListAsync(SnapshotSource source, CancellationToken ct)
    {
        var entries = new List<SnapshotEntry>();
        var unavailable = new List<SnapshotUnavailable>();
        var failure = await GatherAsync(source.Directory, string.Empty, source.IncludesUntracked, 0, entries, unavailable, ct);

        return failure.Length > 0 ? new Listing.Failed(failure) : new Listing.Listed(entries, unavailable);
    }

    /// <summary>One repository's worth, into the lists — a failure sentence, or empty when it all went in.</summary>
    private async Task<string> GatherAsync(
        string dir, string prefix, bool untracked, int depth, List<SnapshotEntry> entries, List<SnapshotUnavailable> unavailable, CancellationToken ct)
    {
        var staged = await GitAsync(dir, ["ls-files", "-z", "--stage"], ct);
        if (!staged.Ran)
        {
            return staged.Reason;
        }

        foreach (var (mode, path) in Records(staged.Out).DistinctBy(record => record.Path, StringComparer.Ordinal))
        {
            var failure = await TakeAsync(dir, prefix, mode, path, untracked, depth, entries, unavailable, ct);
            if (failure.Length > 0)
            {
                return failure;
            }
        }

        return await UntrackedAsync(dir, prefix, untracked, entries, unavailable, ct);
    }

    /// <summary>The untracked files when the tree includes them (<see cref="OthersAsync"/>); nothing to do when it does not.</summary>
    private Task<string> UntrackedAsync(
        string dir, string prefix, bool untracked, List<SnapshotEntry> entries, List<SnapshotUnavailable> unavailable, CancellationToken ct) =>
        untracked ? OthersAsync(dir, prefix, entries, unavailable, ct) : Task.FromResult(string.Empty);

    private async Task<string> TakeAsync(
        string dir, string prefix, string mode, string path, bool untracked, int depth,
        List<SnapshotEntry> entries, List<SnapshotUnavailable> unavailable, CancellationToken ct)
    {
        switch (mode)
        {
            case SymlinkMode:
                entries.Add(new SnapshotEntry(prefix + path, EntryKind.Symlink));
                return string.Empty;
            case GitlinkMode:
                return await SubmoduleAsync(dir, prefix, path, untracked, depth, entries, unavailable, ct);
            default:
                entries.Add(new SnapshotEntry(prefix + path, EntryKind.File));
                return string.Empty;
        }
    }

    /// <summary>A populated submodule is listed like the tree it is; an unpopulated one is named, never shown as source.</summary>
    private async Task<string> SubmoduleAsync(
        string dir, string prefix, string path, bool untracked, int depth,
        List<SnapshotEntry> entries, List<SnapshotUnavailable> unavailable, CancellationToken ct)
    {
        var at = Path.Combine(dir, path.Replace('/', Path.DirectorySeparatorChar));
        if (depth >= NestingLimit)
        {
            unavailable.Add(new SnapshotUnavailable(prefix + path, $"a submodule nested more than {NestingLimit} levels deep is not copied"));
            return string.Empty;
        }

        if (!SubmodulePopulator.HasGit(at))
        {
            unavailable.Add(new SnapshotUnavailable(prefix + path, "an unpopulated submodule: nothing is checked out there, so its content is not reviewed source"));
            return string.Empty;
        }

        return await GatherAsync(at, prefix + path + "/", untracked, depth + 1, entries, unavailable, ct);
    }

    /// <summary>
    /// The untracked-not-ignored files. An untracked NESTED REPOSITORY is listed by git as <c>dir/</c> — one entry with
    /// a trailing slash and nothing beneath — and is named unavailable rather than copied or refused: it is not the
    /// tree's content, and a person who wants it reviewed adds it as a submodule (the review of S1).
    /// </summary>
    private async Task<string> OthersAsync(
        string dir, string prefix, List<SnapshotEntry> entries, List<SnapshotUnavailable> unavailable, CancellationToken ct)
    {
        var others = await GitAsync(dir, ["ls-files", "-z", "--others", "--exclude-standard"], ct);
        if (!others.Ran)
        {
            return others.Reason;
        }

        foreach (var path in others.Out.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            if (path.EndsWith('/'))
            {
                unavailable.Add(new SnapshotUnavailable(prefix + path.TrimEnd('/'), "an untracked nested repository, not copied: add it as a submodule to have it reviewed"));
            }
            else
            {
                entries.Add(new SnapshotEntry(prefix + path, EntryKind.File));
            }
        }

        return string.Empty;
    }

    /// <summary><c>&lt;mode&gt; &lt;object&gt; &lt;stage&gt;\t&lt;path&gt;</c> records, NUL-separated.</summary>
    private static IEnumerable<(string Mode, string Path)> Records(string stdout) =>
        stdout.Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Select(record => (Tab: record.IndexOf('\t'), Record: record))
            .Where(r => r.Tab > 0)
            .Select(r => (r.Record[..r.Record.IndexOf(' ')], r.Record[(r.Tab + 1)..]));

    /// <summary>git's answer, or why it gave none — a missing directory never starts a process (see <c>ReviewTreeRoot.GitAsync</c>).</summary>
    private async Task<(bool Ran, string Out, string Reason)> GitAsync(string dir, string[] args, CancellationToken ct)
    {
        if (!Directory.Exists(dir))
        {
            return (false, string.Empty, $"{dir} does not exist");
        }

        var result = await launcher.RunAsync(new ProcessRequest("git", args, dir) { Timeout = ReviewTreeRoot.Asking }, ct);

        return result.ExitCode == 0 && !result.TimedOut
            ? (true, result.StdOut, string.Empty)
            : (false, string.Empty, $"git {string.Join(' ', args)} in {dir}: {Failure(result)}");
    }

    /// <summary>Why git gave no answer: it ran out of time, or the fatal line of what it wrote.</summary>
    private static string Failure(ProcessResult result) =>
        result.TimedOut ? "did not answer in time" : WorktreeManager.FatalLine(result.StdErr);
}
