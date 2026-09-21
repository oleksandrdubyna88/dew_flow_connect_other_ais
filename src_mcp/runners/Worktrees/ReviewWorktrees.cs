using System.Security.Cryptography;
using System.Text;
using CoaiMcp.Core.Collecting;
using CoaiMcp.Runners.Collecting;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Runners.Worktrees;

/// <summary>Where one pair's tree comes from: the two coordinates the row already stores.</summary>
/// <param name="RepoPath">The checkout the session recorded — the store's own word, never a caller's.</param>
/// <param name="Sha">The commit the reviewers read.</param>
public sealed record TreePlace(string RepoPath, string Sha);

/// <summary>
/// A durable checkout of one commit, for a person to read — made once per (repository, commit) and
/// removed only when they say so.
/// </summary>
/// <remarks>
/// <para><b>Not a widening of <see cref="WorktreeManager"/>, deliberately.</b> That class hands out a
/// <see cref="WorktreeLease"/> whose <c>DisposeAsync</c> removes the tree: a round's tree exists for
/// the length of a fan-out and its correctness IS that it goes away. This one exists until a person
/// is finished with it, which is a different lifetime and therefore a different invariant — one
/// class cannot hold both without its every method asking which kind it is. What IS shared is
/// reused: <see cref="SubmodulePopulator"/> unchanged, <see cref="GitHistory"/> for every probe, and
/// the one sanctioned <see cref="IProcessLauncher"/>.</para>
/// <para><b>The defect this class exists around.</b> <c>WorktreeManager.PruneOursAsync</c> runs on
/// every gate <c>open</c> and has two halves: <c>worktree remove --force</c> over everything
/// <c>ListOursAsync</c> matches, and then <c>Directory.Delete(recursive: true)</c> over every
/// directory under the round storage root whose name starts with the round prefix. The second half
/// asks git no permission at all, so no lock can stop it — which is why a review tree lives under a
/// DIFFERENT ROOT with a DIFFERENT PREFIX. Either alone would be enough; both is what makes it true
/// by construction rather than by a name nobody must ever reuse.</para>
/// <para><b>The lock, and what it is worth.</b> The tree is created <c>--lock</c>ed with a reason,
/// which makes <c>git worktree remove --force</c> fail — measured, exit 128, <c>cannot remove a
/// locked working tree</c> — and that is exactly the call <see cref="WorktreeManager.RemoveAsync"/>
/// makes. It is a guard against ONE <c>--force</c>, not a vault: git's own message names the
/// override (<c>remove -f -f</c>), and nothing stops a person deleting the directory. Said plainly
/// because a comment that overclaims makes the next reader stop checking.</para>
/// <para><b>Nothing here writes to the rounds database</b> — not a row, not a column. What it writes
/// is a checkout on disk and one record beside it.</para>
/// </remarks>
public sealed class ReviewWorktrees(IProcessLauncher launcher, GitHistory git, string root)
{
    /// <summary>Distinct from the round prefix, which is what the gate's prune matches on.</summary>
    private const string Prefix = "coai-review-";

    /// <summary>
    /// How many trees this machine may hold. A cap enforced by REFUSING, never by evicting: every
    /// eviction rule that could make room is a rule that can delete a tree somebody is reading.
    /// </summary>
    internal const int Cap = 10;

    /// <summary>What git prints to anyone who tries to remove the tree with a single force.</summary>
    private const string LockReason =
        "coai review tree - remove it from the ConnectOtherAIs panel, not by hand";

    /// <summary>
    /// How long a directory with no record is assumed to be another press still working. Two minutes
    /// for the checkout plus a minute per submodule mount, with slack; past it, the maker died.
    /// </summary>
    private static readonly TimeSpan StillWorking = TimeSpan.FromMinutes(10);

    /// <summary>Longer than a read: a checkout of a large repository is minutes, not seconds.</summary>
    private static readonly TimeSpan Budget = TimeSpan.FromMinutes(5);

    private readonly SubmodulePopulator _submodules = new(launcher);

    /// <summary>
    /// Machine-local, and deliberately NOT under the configured data dir: a linked worktree's
    /// <c>.git</c> file holds an absolute path into the parent repository's admin directory, so a
    /// tree is bound to one machine and one OS — and this product's data dir is routinely a network
    /// share, where such a tree would be broken for every other machine that mounted it and slow to
    /// index for the one that made it.
    /// </summary>
    public static string DefaultRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "coai-mcp",
        "review-worktrees");

    /// <summary>The tree for this pair — made, reused, or refused with a reason. Always data.</summary>
    public async Task<ReviewTree> PrepareAsync(long findingId, TreePlace place, CancellationToken ct = default)
    {
        var refused = RefusedLexically(place);
        if (refused.Length > 0)
        {
            return Refused(findingId, place, string.Empty, refused);
        }

        var probed = await ProbedAsync(place, ct);
        if (probed.Length > 0)
        {
            return Refused(findingId, place, string.Empty, probed);
        }

        var repository = await CommonDirAsync(place, ct);

        return repository.Length == 0
            ? Refused(findingId, place, string.Empty, ReviewTreeReason.GitFailed)
            : await AtAsync(findingId, place, repository, ct);
    }

    /// <summary>What the row itself rules out, before any process is started.</summary>
    private static string RefusedLexically(TreePlace place)
    {
        if (place.RepoPath.Length == 0)
        {
            return ReviewTreeReason.RepoPathMissing;
        }

        return GitHistory.IsCommitish(place.Sha) ? string.Empty : ReviewTreeReason.CommitUnreachable;
    }

    /// <summary>
    /// The two questions worth a process before anything is created: is that still a repository, and
    /// is the commit in it.
    /// </summary>
    /// <remarks>
    /// <c>HasCommitAsync</c> is an OBJECT lookup, not a ref walk, which is the whole reason this
    /// works for the 55.7 % of recorded commits that are reachable from no branch at all. A worktree
    /// checked out at such a commit then becomes a gc root and preserves it — measured: after
    /// <c>reflog expire --expire=now --all</c> and <c>gc --prune=now</c> the commit is still there.
    /// </remarks>
    private async Task<string> ProbedAsync(TreePlace place, CancellationToken ct)
    {
        var repository = await git.IsRepositoryAsync(place.RepoPath, ct);
        if (!repository.Ran)
        {
            return ReviewTreeReason.GitFailed;
        }

        if (!repository.Ok)
        {
            return ReviewTreeReason.RepoPathMissing;
        }

        var commit = await git.HasCommitAsync(place.RepoPath, place.Sha, ct);

        return Reason(commit, ReviewTreeReason.CommitUnreachable);
    }

    /// <summary>A git answer as a reason word: nothing ran is never the same fact as git said no.</summary>
    private static string Reason(GitAnswer answer, string said) =>
        answer.Ran ? (answer.Ok ? string.Empty : said) : ReviewTreeReason.GitFailed;

    /// <summary>
    /// The repository half of a tree's identity: the git COMMON dir, absolute.
    /// </summary>
    /// <remarks>
    /// A worktree is registered in exactly one object store, so the object store is what a tree can
    /// be shared across and a remote URL is structurally the wrong key — two clones of one remote
    /// cannot share a tree however much they look alike. Two spellings of one checkout, and any
    /// linked worktree of it, all answer the same common dir, which is story 2.2's identity lesson
    /// arriving from git rather than from our own path arithmetic.
    /// </remarks>
    private async Task<string> CommonDirAsync(TreePlace place, CancellationToken ct)
    {
        var answer = await Git(place.RepoPath, ["rev-parse", "--path-format=absolute", "--git-common-dir"], ct);

        return answer.Ran && answer.Ok ? answer.Out.Trim() : string.Empty;
    }

    /// <summary>The tree for this identity: handed back, awaited, repaired, refused, or made.</summary>
    private async Task<ReviewTree> AtAsync(long findingId, TreePlace place, string repository, CancellationToken ct)
    {
        var name = NameOf(repository, place.Sha);
        var record = ReviewTreeRecords.Read(ReviewTreeRecords.FileFor(root, name));

        return ReviewTreeRecords.Exists(record)
            ? Ready(findingId, place, repository, Path.Combine(root, name), record.EmptyMounts, reused: true)
            : await MakeAsync(findingId, place, repository, name, ct);
    }

    /// <summary>
    /// A stable directory name for one (repository, commit): a short digest of the common dir so the
    /// name is a legal directory on every filesystem, and the short sha so a human reading a path
    /// can tell which commit it holds.
    /// </summary>
    private static string NameOf(string repository, string sha) =>
        $"{Prefix}{Digest(Normalised(repository))}-{sha[..12].ToLowerInvariant()}";

    /// <summary>
    /// The identity KEY, not a path to ask the filesystem about: separators one way, no trailing
    /// slash, case folded. Story 2.2 measured the cost of folding case before a filesystem call and
    /// this is the other side of that line — nothing here is ever handed to <c>Directory.Exists</c>.
    /// </summary>
    private static string Normalised(string repository) =>
        repository.Replace('\\', '/').TrimEnd('/').ToLowerInvariant();

    private static string Digest(string key) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..8];

    /// <summary>Everything that must be true before git is asked to create anything.</summary>
    private async Task<ReviewTree> MakeAsync(
        long findingId, TreePlace place, string repository, string name, CancellationToken ct)
    {
        var path = Path.Combine(root, name);

        var unfinished = await UnfinishedAsync(place.RepoPath, path, ct);
        if (unfinished.Length > 0)
        {
            return Refused(findingId, place, repository, unfinished, unfinished == ReviewTreeReason.IncompleteAndDirty ? path : "");
        }

        var held = ReviewTreeRecords.All(root);

        return held.Count >= Cap
            ? Full(findingId, place, repository, held)
            : await AddAsync(findingId, place, repository, name, path, ct);
    }

    /// <summary>
    /// What to do about a directory that exists with no record beside it — which is the only state a
    /// crash can leave, because the record is written last.
    /// </summary>
    /// <remarks>
    /// Young means another press is still working and the answer is to try again shortly. Old means
    /// its maker died: the tree is then rebuilt if it is provably clean, and if it is not — somebody
    /// has typed into it since — it is named and left exactly where it is. "We could not ask" is
    /// never "it is clean": a git that did not run answers <c>git_failed</c>, and a git that ran and
    /// refused to call it a worktree cannot prove it empty, so that is refused by name too.
    /// </remarks>
    private async Task<string> UnfinishedAsync(string repoPath, string path, CancellationToken ct)
    {
        if (!Directory.Exists(path))
        {
            return string.Empty;
        }

        if (DateTime.UtcNow - Directory.GetCreationTimeUtc(path) < StillWorking)
        {
            return ReviewTreeReason.InProgress;
        }

        var clean = await CleanAsync(path, ct);
        if (!clean.Ran)
        {
            return ReviewTreeReason.GitFailed;
        }

        return clean.Ok ? await ScrappedAsync(repoPath, path, ct) : ReviewTreeReason.IncompleteAndDirty;
    }

    /// <summary>
    /// Whether a tree holds nothing of anybody's: tracked changes, untracked files AND what the
    /// submodules hold, because a half-populated tree is exactly where a person's edit hides.
    /// </summary>
    private async Task<GitAnswer> CleanAsync(string path, CancellationToken ct)
    {
        var answer = await Git(
            path, ["status", "--porcelain", "--untracked-files=all", "--ignore-submodules=none"], ct);

        return answer.Ran && answer.Ok
            ? new GitAnswer(true, answer.Out.Trim().Length == 0, answer.Out)
            : answer with { Ok = false };
    }

    /// <summary>Removes a tree that never finished and holds nothing, so it can be built again.</summary>
    /// <remarks>
    /// Unlock first: the tree was created locked, and a locked tree refuses <c>remove --force</c> —
    /// the very guard that protects a finished one. Then force, because a tree with populated
    /// submodules refuses a plain remove (measured: <c>working trees containing submodules cannot be
    /// moved or removed</c>). The directory delete is the last resort for a remove that half-failed,
    /// and it is only ever reached for a tree this method has just proved clean.
    /// </remarks>
    private async Task<string> ScrappedAsync(string repoPath, string path, CancellationToken ct)
    {
        await Git(repoPath, ["worktree", "unlock", path], ct);
        await Git(repoPath, ["worktree", "remove", "--force", path], ct);
        await Git(repoPath, ["worktree", "prune"], ct);

        return Gone(path) ? string.Empty : ReviewTreeReason.GitFailed;
    }

    private static bool Gone(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }

            return !Directory.Exists(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Creates the tree, fills its submodules, and writes the record that says it is ready.</summary>
    private async Task<ReviewTree> AddAsync(
        long findingId, TreePlace place, string repository, string name, string path, CancellationToken ct)
    {
        Directory.CreateDirectory(root);

        var added = await Git(
            place.RepoPath,
            ["worktree", "add", "--detach", "--lock", "--reason", LockReason, path, place.Sha],
            ct);

        if (!added.Ran || !added.Ok)
        {
            return await LostTheRaceAsync(findingId, place, repository, name, path);
        }

        var mounts = await EmptyMountsAsync(place.RepoPath, path, ct);
        ReviewTreeRecords.Write(
            ReviewTreeRecords.FileFor(root, name),
            new ReviewTreeRecord(repository, place.Sha, DateTime.UtcNow.ToString("O"), mounts));

        return Ready(findingId, place, repository, path, mounts, reused: false);
    }

    /// <summary>
    /// <c>worktree add</c> refused. The interesting case is not an error at all: git's own refusal to
    /// create an existing directory IS the mutex, so of two presses at once exactly one creates and
    /// the other arrives here. If the winner has finished, hand back its tree; if it is still
    /// working, say so; only a refusal with no directory behind it is a real failure.
    /// </summary>
    private async Task<ReviewTree> LostTheRaceAsync(
        long findingId, TreePlace place, string repository, string name, string path)
    {
        var record = ReviewTreeRecords.Read(ReviewTreeRecords.FileFor(root, name));
        if (ReviewTreeRecords.Exists(record))
        {
            return Ready(findingId, place, repository, path, record.EmptyMounts, reused: true);
        }

        return await Task.FromResult(Refused(
            findingId,
            place,
            repository,
            Directory.Exists(path) ? ReviewTreeReason.InProgress : ReviewTreeReason.GitFailed));
    }

    /// <summary>
    /// Fills the submodules from the parent and reports the mounts that stayed empty.
    /// </summary>
    /// <remarks>
    /// A linked worktree gets no submodules from git, and in this family a project's own rules and
    /// dependencies ARE submodules — a tree without them is a tree whose language server sees holes,
    /// which is most of what the tree was for. Population is bounded and never throws
    /// (<see cref="SubmodulePopulator"/>), so a mount that could not be filled is reported rather
    /// than fatal: the person navigating into it is better served knowing.
    /// </remarks>
    private async Task<IReadOnlyList<string>> EmptyMountsAsync(string repoPath, string path, CancellationToken ct)
    {
        await _submodules.PopulateAsync(repoPath, path);

        var declared = await Git(path, ["config", "-f", ".gitmodules", "--get-regexp", "path"], ct);

        return declared.Ran && declared.Ok
            ? [.. Mounts(declared.Out).Where(m => IsEmpty(Path.Combine(path, m)))]
            : [];
    }

    /// <summary>The mount paths out of <c>submodule.&lt;name&gt;.path &lt;mount&gt;</c> lines.</summary>
    private static IEnumerable<string> Mounts(string config) =>
        config.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split(' ', 2))
            .Where(parts => parts.Length == 2)
            .Select(parts => parts[1].Trim());

    private static bool IsEmpty(string mount) =>
        !Directory.Exists(mount) || !Directory.EnumerateFileSystemEntries(mount).Any();

    private static ReviewTree Ready(
        long findingId,
        TreePlace place,
        string repository,
        string path,
        IReadOnlyList<string> mounts,
        bool reused) =>
        new()
        {
            FindingId = findingId,
            Sha = place.Sha,
            Path = path,
            Repository = repository,
            Reused = reused,
            EmptyMounts = mounts,
        };

    private static ReviewTree Refused(
        long findingId, TreePlace place, string repository, string reason, string path = "") =>
        new()
        {
            FindingId = findingId,
            Sha = place.Sha,
            Repository = repository,
            Reason = reason,
            Path = path,
        };

    /// <summary>The cap refusal, which names every tree held — see <see cref="ReviewTreeRow"/>.</summary>
    private ReviewTree Full(
        long findingId, TreePlace place, string repository, IReadOnlyList<ReviewTreeRecord> held) =>
        new()
        {
            FindingId = findingId,
            Sha = place.Sha,
            Repository = repository,
            Reason = ReviewTreeReason.Budget,
            Trees = [.. held.Select(r => new ReviewTreeRow(
                r.Repository, r.Sha, Path.Combine(root, NameOf(r.Repository, r.Sha)), r.Created))],
        };

    private async Task<GitAnswer> Git(string workingDirectory, string[] args, CancellationToken ct)
    {
        var result = await launcher.RunAsync(
            new ProcessRequest("git", args, workingDirectory) { Timeout = Budget }, ct);

        return result.TimedOut || result.Cancelled
            ? GitAnswer.Broken
            : new GitAnswer(true, result.ExitCode == 0, result.StdOut);
    }
}
