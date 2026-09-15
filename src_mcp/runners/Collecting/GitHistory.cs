using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Runners.Collecting;

/// <summary>What git said, and whether it was git that failed or the answer that was "no".</summary>
/// <remarks>
/// <b>The distinction is the whole type.</b> `git cat-file -e` exiting 1 means the object is not
/// there — an ANSWER. `git` timing out, or the launcher failing to start it, means we learned
/// nothing — a FAILURE. Collapsing the two is how an infrastructure problem gets filed as a
/// property of somebody's repository and hides for a month. (Plan round, codex.)
/// </remarks>
/// <param name="Ran">Whether git ran to completion at all, whatever it then said.</param>
public readonly record struct GitAnswer(bool Ran, bool Ok, string Out)
{
    internal static GitAnswer Broken => new(false, false, string.Empty);

    /// <summary>The output, split into non-empty lines.</summary>
    public IReadOnlyList<string> Lines =>
        Out.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>
/// The git reads the corpus collector needs, and nothing else.
/// </summary>
/// <remarks>
/// <para>Through <see cref="IProcessLauncher"/> like every other process in this repository, so the
/// timeout and the tree-kill are the ones already tested rather than a second launcher written for
/// this. No new dependency, and no library that would have to understand a repository's layout.</para>
/// <para><b>Nothing here reads a working tree.</b> Every read is <c>git show &lt;sha&gt;:&lt;path&gt;</c>,
/// because more than half of the commits this walks are on branches that no longer exist and are
/// reachable from no ref at all — their objects survive, their checkouts do not.</para>
/// </remarks>
public sealed class GitHistory(IProcessLauncher launcher)
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);

    /// <summary>Whether this path is a git repository we can ask questions of.</summary>
    public async Task<bool> IsRepositoryAsync(string repoPath, CancellationToken ct = default) =>
        Directory.Exists(repoPath) && (await RunAsync(repoPath, ["rev-parse", "--git-dir"], ct)).Ok;

    /// <summary>Whether the commit object is in this repository.</summary>
    /// <remarks>
    /// <c>^{commit}</c> and not the bare sha: a tree or a blob of the same name would satisfy a plain
    /// existence check and then fail at the first read.
    /// </remarks>
    public Task<GitAnswer> HasCommitAsync(string repoPath, string sha, CancellationToken ct = default) =>
        RunAsync(repoPath, ["cat-file", "-e", $"{sha}^{{commit}}"], ct);

    /// <summary>
    /// The refs that DESCEND from this commit — the only histories it is safe to walk forward in.
    /// </summary>
    /// <remarks>
    /// Not "any ref that exists". A fallback that accepted an unrelated ref as proof the commit was
    /// reachable, and then walked the session's branch anyway, would attribute some other line of
    /// work's edit as the fix. Asking which refs CONTAIN the commit answers the question actually
    /// being asked. (Plan round, codex.)
    /// </remarks>
    public Task<GitAnswer> RefsContainingAsync(string repoPath, string sha, CancellationToken ct = default) =>
        RunAsync(repoPath, ["for-each-ref", "--format=%(refname)", "--contains", sha], ct);

    /// <summary>Whether <paramref name="sha"/> is an ancestor of <paramref name="of"/>.</summary>
    public Task<GitAnswer> IsAncestorAsync(
        string repoPath, string sha, string of, CancellationToken ct = default) =>
        RunAsync(repoPath, ["merge-base", "--is-ancestor", sha, of], ct);

    /// <summary>One file as it was at one commit.</summary>
    public Task<GitAnswer> FileAtAsync(
        string repoPath, string sha, string path, CancellationToken ct = default) =>
        RunAsync(repoPath, ["show", $"{sha}:{path}"], ct);

    /// <summary>
    /// The commits that touched a file in <c>from..to</c>, OLDEST first.
    /// </summary>
    /// <remarks>
    /// Oldest first because the walk wants the earliest commit that actually changed the METHOD, and
    /// `git log` answers newest-first by default. Following renames is deliberate: a fix that also
    /// moved the file is still the fix.
    /// </remarks>
    public Task<GitAnswer> CommitsTouchingAsync(
        string repoPath, string from, string to, string path, CancellationToken ct = default) =>
        RunAsync(repoPath, ["log", "--reverse", "--format=%H", "--follow", $"{from}..{to}", "--", path], ct);

    private async Task<GitAnswer> RunAsync(string repoPath, string[] args, CancellationToken ct)
    {
        var result = await launcher.RunAsync(
            new ProcessRequest("git", args, repoPath) { Timeout = Budget }, ct);

        // A timeout or a cancellation is not an answer about the repository — it is us having learned
        // nothing, and the caller must record `failed` rather than a skip.
        return result.TimedOut || result.Cancelled
            ? GitAnswer.Broken
            : new GitAnswer(true, result.ExitCode == 0, result.StdOut);
    }
}
