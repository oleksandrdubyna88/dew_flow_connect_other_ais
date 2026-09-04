using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Runners.Worktrees;

/// <summary>A git operation the round cannot proceed without failed; the sentence says which.</summary>
public sealed class WorktreeException(string operation, string detail)
    : Exception($"git {operation}: {detail}");

/// <summary>
/// One round's shared, read-only tree. Disposal is the <c>finally</c> the epic demands: however
/// the fan-out ends, the worktree goes.
/// </summary>
public sealed class WorktreeLease(WorktreeManager manager, string repoPath, string path, string sha)
    : IAsyncDisposable
{
    public string Path { get; } = path;

    public string Sha { get; } = sha;

    public async ValueTask DisposeAsync() => await manager.RemoveAsync(repoPath, Path);
}

/// <summary>
/// One worktree per ROUND, pinned to a SHA, outside the repository, shared by every reviewer in
/// the round — six read-only reviewers share a tree safely, and six checkouts of a moving branch
/// would be six different inputs to what is meant to be one comparison.
/// </summary>
/// <remarks>
/// <para>Lifecycle, because an orphan worktree blocks the next run: <c>open</c> prunes whatever a
/// killed session left; every round removes its own tree in a <c>finally</c> (the lease); paths
/// carry the <c>coai-wt-</c> prefix and live under OUR storage root, so pruning can tell ours
/// from a human's and never removes one it did not create.</para>
/// </remarks>
public sealed class WorktreeManager(IProcessLauncher launcher, string storageRoot)
{
    private const string Prefix = "coai-wt-";

    private readonly SubmodulePopulator _submodules = new(launcher);

    public async Task<string> ResolveShaAsync(string repoPath, string branch)
    {
        var result = await Git(repoPath, "rev-parse", "--verify", $"{branch}^{{commit}}");
        return result.ExitCode == 0
            ? result.StdOut.Trim()
            : throw new WorktreeException("rev-parse", $"cannot resolve '{branch}': {result.StdErr.Trim()}");
    }

    public async Task<WorktreeLease> AddAsync(string repoPath, string sha, string sessionId, int round)
    {
        Directory.CreateDirectory(storageRoot);
        var path = Path.Combine(storageRoot, $"{Prefix}{sessionId}-r{round}");
        var result = await Git(repoPath, "worktree", "add", "--detach", path, sha);
        if (result.ExitCode != 0)
        {
            throw new WorktreeException("worktree add", result.StdErr.Trim());
        }

        // A linked worktree gets no submodules from git, and in this family the project's own
        // written rules are exactly that — see SubmodulePopulator for what the reviewers were
        // being handed instead.
        await _submodules.PopulateAsync(repoPath, path);

        return new WorktreeLease(this, repoPath, path, sha);
    }

    public async Task RemoveAsync(string repoPath, string path)
    {
        var result = await Git(repoPath, "worktree", "remove", "--force", path);
        if (result.ExitCode != 0 && Directory.Exists(path))
        {
            throw new WorktreeException("worktree remove", result.StdErr.Trim());
        }
    }

    /// <summary>
    /// Clears what a killed session left behind: OUR worktrees only (the prefix under the storage
    /// root), then <c>git worktree prune</c> for metadata whose directories are already gone.
    /// </summary>
    public async Task PruneOursAsync(string repoPath)
    {
        foreach (var path in await ListOursAsync(repoPath))
        {
            await Git(repoPath, "worktree", "remove", "--force", path);
        }

        await Git(repoPath, "worktree", "prune");

        // Directories left after a remove that half-failed (or an add that never registered):
        // ours by name, so deletable by name.
        if (Directory.Exists(storageRoot))
        {
            foreach (var dir in Directory.GetDirectories(storageRoot, $"{Prefix}*"))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    internal async Task<IReadOnlyList<string>> ListOursAsync(string repoPath)
    {
        var result = await Git(repoPath, "worktree", "list", "--porcelain");
        var root = Path.GetFullPath(storageRoot);
        return [.. result.StdOut
            .Split('\n', StringSplitOptions.TrimEntries)
            .Where(l => l.StartsWith("worktree ", StringComparison.Ordinal))
            .Select(l => l["worktree ".Length..])
            .Where(p => Path.GetFileName(p).StartsWith(Prefix, StringComparison.Ordinal)
                        && Path.GetFullPath(p).StartsWith(root, StringComparison.OrdinalIgnoreCase))];
    }

    private Task<ProcessResult> Git(string repoPath, params string[] args) =>
        launcher.RunAsync(new ProcessRequest("git", args, repoPath)
        {
            Timeout = TimeSpan.FromMinutes(2),
        });
}
