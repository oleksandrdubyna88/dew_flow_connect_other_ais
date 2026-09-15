using System.Text.RegularExpressions;
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

/// <summary>One commit that touched a file, and the path the file had THERE.</summary>
/// <remarks>
/// The path travels with the commit because `--follow` will happily report a commit from before a
/// rename, where the file is called something else — and reading it under today's name then fails,
/// filing a collectable candidate as a skip. (Code round, codex.)
/// </remarks>
public readonly record struct TouchedAt(string Sha, string Path);

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
public sealed partial class GitHistory(IProcessLauncher launcher)
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);

    /// <summary>
    /// A git object id and nothing else — never something git could read as a flag.
    /// </summary>
    /// <remarks>
    /// <b>A revision beginning with <c>--</c> is an OPTION to git, not a revision.</b> These shas come
    /// out of a database whose rows were written by reviewers' answers, so "it is always a hex id" is
    /// an assumption rather than a guarantee — and `--upload-pack=…` in a revision position is a
    /// command, not a bad lookup. Quoting cannot help, because there is no shell to quote for; only
    /// refusing the value can. The same check `ContextAssembler` already applies for the same reason.
    /// (Code round, gemini.)
    /// </remarks>
    /// <remarks>
    /// EXACTLY forty: `rounds.head_sha` is written from `%H`, so a short id here is a malformed row
    /// rather than an abbreviation somebody meant. Accepting four-to-sixty-four let a truncated value
    /// reach git, which then answers about whatever object it happens to disambiguate to.
    /// </remarks>
    [GeneratedRegex("^[0-9a-fA-F]{40}$", RegexOptions.CultureInvariant)]
    private static partial Regex ObjectId { get; }

    private static bool IsCommitish(string value) => ObjectId.IsMatch(value);

    /// <summary>A ref NAME, which the end of an interval may legitimately be.</summary>
    /// <remarks>
    /// `refs/heads/main` is not a hex id, and requiring one here silently emptied every open-ended
    /// walk — the interval end comes from `for-each-ref`, so it is a name far more often than a sha.
    /// What must still be refused is anything git could read as an OPTION, which is the leading dash
    /// and nothing else.
    /// </remarks>
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._/@^~-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex RefName { get; }

    private static bool IsRevision(string value) => IsCommitish(value) || RefName.IsMatch(value);

    /// <summary>Whether this path is a git repository we can ask questions of.</summary>
    /// <remarks>
    /// <b>A tri-state, not a bool.</b> It used to answer false for a probe that TIMED OUT as well as
    /// for a directory that is not a repository, so an infrastructure outage was recorded as
    /// `repo_path_missing` and the candidate was marked processed — the outage disappearing into the
    /// skip funnel, which is the one thing this story exists to prevent. (Code round, codex.)
    /// </remarks>
    public Task<GitAnswer> IsRepositoryAsync(string repoPath, CancellationToken ct = default) =>
        Directory.Exists(repoPath)
            ? RunAsync(repoPath, ["rev-parse", "--git-dir"], ct)
            : Task.FromResult(new GitAnswer(true, false, string.Empty));

    /// <summary>Whether the commit object is in this repository.</summary>
    /// <remarks>
    /// <c>^{commit}</c> and not the bare sha: a tree or a blob of the same name would satisfy a plain
    /// existence check and then fail at the first read.
    /// </remarks>
    public Task<GitAnswer> HasCommitAsync(string repoPath, string sha, CancellationToken ct = default) =>
        IsCommitish(sha)
            ? RunAsync(repoPath, ["cat-file", "-e", $"{sha}^{{commit}}"], ct)
            : Refused;

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
        IsCommitish(sha)
            ? RunAsync(repoPath, ["for-each-ref", "--format=%(refname)", "--contains", sha], ct)
            : Refused;

    /// <summary>Whether <paramref name="sha"/> is an ancestor of <paramref name="of"/>.</summary>
    public Task<GitAnswer> IsAncestorAsync(
        string repoPath, string sha, string of, CancellationToken ct = default) =>
        IsCommitish(sha) && IsRevision(of)
            ? RunAsync(repoPath, ["merge-base", "--is-ancestor", sha, of], ct)
            : Refused;

    /// <summary>One file as it was at one commit.</summary>
    /// <remarks>
    /// <b>No <c>--</c> here.</b> That separator divides revisions from PATHS, and <c>sha:path</c> is
    /// one object spec rather than a path — `git show -- HEAD:file` looks for a file actually called
    /// `HEAD:file` and finds nothing. It was added here for symmetry with the option-injection fix
    /// and broke every read in the walk. The spec cannot be mistaken for an option anyway: it begins
    /// with the validated hex sha.
    /// </remarks>
    public Task<GitAnswer> FileAtAsync(
        string repoPath, string sha, string path, CancellationToken ct = default) =>
        IsCommitish(sha)
            ? RunAsync(repoPath, ["show", $"{sha}:{path}"], ct)
            : Refused;

    /// <summary>
    /// The commits that touched a file in <c>from..to</c>, OLDEST first, each with its path THERE.
    /// </summary>
    /// <remarks>
    /// <para>Oldest first because the walk wants the earliest commit that actually changed the METHOD,
    /// and `git log` answers newest-first by default.</para>
    /// <para><c>--name-only</c> rides along so a renamed file can still be read: `--follow` reports
    /// commits from before the rename, where the path is different, and reading those under today's
    /// name fails. (Code round, codex.)</para>
    /// <para><paramref name="cap"/> bounds the walk. A file with ten thousand commits would otherwise
    /// spend one candidate's budget on a history nobody is going to read to the end of, and the
    /// collector's contract is that a run finishes.</para>
    /// </remarks>
    public async Task<(bool Ran, IReadOnlyList<TouchedAt> Commits)> CommitsTouchingAsync(
        string repoPath, string from, string to, string path, int cap, CancellationToken ct = default)
    {
        if (!IsCommitish(from) || !IsRevision(to))
        {
            return (true, []);
        }

        var answer = await RunAsync(
            repoPath,
            ["log", "--reverse", "--format=%H", "--name-only", "--follow", $"-n{cap}", $"{from}..{to}", "--", path],
            ct);

        return answer.Ran ? (true, Touched(answer.Out)) : (false, []);
    }

    /// <summary>
    /// `%H` then the paths it touched, blank-line separated — folded back into commit and path.
    /// </summary>
    /// <remarks>
    /// A commit may list several paths when `--follow` crosses a rename; the LAST one is the name the
    /// file has at that commit, which is the one to read it by.
    /// </remarks>
    private static IReadOnlyList<TouchedAt> Touched(string log)
    {
        var touched = new List<TouchedAt>();
        var sha = string.Empty;
        var path = string.Empty;
        foreach (var line in log.Split('\n').Select(l => l.Trim()))
        {
            if (line.Length == 0)
            {
                continue;
            }

            // `%H` is always forty characters. A path that happens to be hex — `src/abcdef` — must
            // not be read as a commit, so the length is exact rather than a floor.
            if (line.Length == 40 && IsCommitish(line))
            {
                Remember(touched, sha, path);
                (sha, path) = (line, string.Empty);
                continue;
            }

            path = line;
        }

        Remember(touched, sha, path);

        return touched;
    }

    private static void Remember(List<TouchedAt> touched, string sha, string path)
    {
        if (sha.Length > 0 && path.Length > 0)
        {
            touched.Add(new TouchedAt(sha, path));
        }
    }

    /// <summary>A value git must never be handed: answered as "ran, and said no".</summary>
    /// <remarks>
    /// Not a failure — nothing broke — and not a silent pass. The candidate skips for the reason its
    /// own guard gives it, and no process is started with a value that could be an option.
    /// </remarks>
    private static Task<GitAnswer> Refused => Task.FromResult(new GitAnswer(true, false, string.Empty));

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
