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
/// reused: <see cref="SubmodulePopulator"/> unchanged, <see cref="GitHistory"/> for every probe, the
/// one sanctioned <see cref="IProcessLauncher"/>, and <see cref="Files.AtomicFile"/> for the record.</para>
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
/// <para><b>Nothing is deleted that has not just been proved to hold nothing.</b> Every removal path
/// here re-checks cleanliness immediately before it acts, refuses on a record it could not read, and
/// refuses on any path that is not under our own root. The code round found all three.</para>
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
    /// How long a directory with no record is taken for another press still working, measured from
    /// its LAST WRITE and not its creation.
    /// </summary>
    /// <remarks>
    /// Creation time is fixed the moment <c>worktree add</c> makes the directory, so a checkout of a
    /// large repository whose submodules take longer than this would have been judged abandoned while
    /// git was still writing into it — and then inspected and rebuilt underneath itself. Last-write
    /// moves as the checkout progresses, so it says "nothing has happened here for ten minutes",
    /// which is the question actually being asked. (Code round, gemini.)
    /// </remarks>
    private static readonly TimeSpan Untouched = TimeSpan.FromMinutes(10);

    /// <summary>A probe or an inspection: seconds of work, and a minute is already generous.</summary>
    private static readonly TimeSpan Asking = TimeSpan.FromMinutes(1);

    /// <summary>
    /// A checkout and its submodules. Ten minutes, matching the extension's own cap on the call.
    /// </summary>
    /// <remarks>
    /// It was five, which is the one true thing in a cluster of code-round findings that claimed
    /// these commands had no timeout at all — every one of them goes through <see cref="Git"/>, which
    /// sets one. But a `worktree add` plus submodules on a large repository can exceed five minutes,
    /// and the client waits ten, so the server abandoning first would have turned a slow success into
    /// a half-made tree the next press has to clean up.
    /// </remarks>
    private static readonly TimeSpan Checking = TimeSpan.FromMinutes(10);

    /// <summary>Long enough for a real tree, short enough that a held handle is not forever.</summary>
    private static readonly TimeSpan Erasing = TimeSpan.FromMinutes(2);

    /// <summary>How long to wait for another process to finish its own create before giving up.</summary>
    private static readonly TimeSpan ForTheSlot = TimeSpan.FromSeconds(5);

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

        return Reason(await git.HasCommitAsync(place.RepoPath, place.Sha, ct), ReviewTreeReason.CommitUnreachable);
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
        var answer = await Git(place.RepoPath, ["rev-parse", "--path-format=absolute", "--git-common-dir"], Asking, ct);

        return answer.Ran && answer.Ok ? answer.Out.Trim() : string.Empty;
    }

    /// <summary>The tree for this identity: handed back, awaited, repaired, refused, or made.</summary>
    private async Task<ReviewTree> AtAsync(long findingId, TreePlace place, string repository, CancellationToken ct)
    {
        var name = NameOf(repository, place.Sha);
        var path = Path.Combine(root, name);
        var read = ReviewTreeRecords.Read(ReviewTreeRecords.FileFor(root, name));

        return Ready(read, repository, place.Sha) && Directory.Exists(path)
            ? Made(findingId, place, repository, path, read.Record.EmptyMounts, reused: true)
            : await MakeAsync(findingId, place, repository, name, ct);
    }

    /// <summary>
    /// Whether a record proves THIS tree is the one asked for.
    /// </summary>
    /// <remarks>
    /// The directory name is a truncated digest and a short sha, so two identities could in principle
    /// land on one name. Rather than lengthening the name — which would not remove the possibility,
    /// only shrink it — the record carries the FULL identity and is compared before its tree is
    /// handed back. A name collision then costs a rebuild, never the wrong repository opened.
    /// (Code round, codex.)
    /// </remarks>
    private static bool Ready(RecordRead read, string repository, string sha) =>
        read.State == RecordState.Found
        && string.Equals(Normalised(read.Record.Repository), Normalised(repository), StringComparison.Ordinal)
        && string.Equals(read.Record.Sha, sha, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A stable directory name for one (repository, commit): a short digest of the common dir so the
    /// name is a legal directory on every filesystem, and the short sha so a human reading a path
    /// can tell which commit it holds. The full identity lives in the record — see <see cref="Ready"/>.
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
        Directory.CreateDirectory(root);

        // The cap check and the creation are one step, or they are not a cap: with nine trees held,
        // two presses that both read nine would both create and leave eleven. The slot is a
        // machine-wide file lock held until the record is written. (Code round, codex, three times.)
        using var slot = await SlotAsync(ct);
        if (!slot.Taken)
        {
            return Refused(findingId, place, repository, ReviewTreeReason.InProgress);
        }

        var unfinished = await UnfinishedAsync(place.RepoPath, path, repository, place.Sha, ct);
        if (unfinished.Length > 0)
        {
            return Refused(findingId, place, repository, unfinished, Names(unfinished) ? path : "");
        }

        var held = ReviewTreeRecords.All(root);

        return held.Count >= Cap
            ? Full(findingId, place, repository, held)
            : await AddAsync(findingId, place, repository, name, path, ct);
    }

    /// <summary>The one refusal that names a directory, because a person is being asked to look at it.</summary>
    private static bool Names(string reason) => reason == ReviewTreeReason.IncompleteAndDirty;

    /// <summary>
    /// What to do about a directory that exists without a record proving it finished.
    /// </summary>
    /// <remarks>
    /// <para>Touched recently means another press is still working, and the answer is to try again
    /// shortly. Untouched for ten minutes means its maker died: the tree is then rebuilt if it is
    /// provably clean, and if it is not — somebody has typed into it since — it is named and left
    /// exactly where it is.</para>
    /// <para><b>"We could not ask" is never "it is clean".</b> A git that did not run answers
    /// <c>git_failed</c>; a git that ran and refused to call it a worktree cannot prove it empty, so
    /// that is refused by name too; and a record file that EXISTS but could not be read means we
    /// cannot establish that the tree never finished, so nothing is removed at all.</para>
    /// </remarks>
    private async Task<string> UnfinishedAsync(
        string repoPath, string path, string repository, string sha, CancellationToken ct)
    {
        if (!Directory.Exists(path))
        {
            return string.Empty;
        }

        var read = ReviewTreeRecords.Read(ReviewTreeRecords.FileFor(root, Path.GetFileName(path)));
        if (read.State == RecordState.Unreadable)
        {
            return ReviewTreeReason.IncompleteAndDirty;
        }

        if (DateTime.UtcNow - Directory.GetLastWriteTimeUtc(path) < Untouched)
        {
            return ReviewTreeReason.InProgress;
        }

        return await ScrapIfCleanAsync(repoPath, path, ct);
    }

    /// <summary>
    /// Whether a tree holds nothing of anybody's: tracked changes, untracked files AND what the
    /// submodules hold, because a half-populated tree is exactly where a person's edit hides.
    /// </summary>
    private async Task<GitAnswer> CleanAsync(string path, CancellationToken ct)
    {
        var answer = await Git(
            path, ["status", "--porcelain", "--untracked-files=all", "--ignore-submodules=none"], Asking, ct);

        return answer.Ran && answer.Ok
            ? new GitAnswer(true, answer.Out.Trim().Length == 0, answer.Out)
            : answer with { Ok = false };
    }

    /// <summary>
    /// Removes a tree that never finished and holds nothing — checking that twice, with the second
    /// check as late as it can be made.
    /// </summary>
    /// <remarks>
    /// A person can start reading, or typing into, an abandoned directory between the first clean
    /// check and the removal; the second check immediately before the delete is what shrinks that
    /// window to the smallest this can make it. It cannot be closed entirely without holding a lock
    /// on the tree itself, and saying so is better than implying otherwise. (Code round, codex.)
    /// </remarks>
    private async Task<string> ScrapIfCleanAsync(string repoPath, string path, CancellationToken ct)
    {
        var clean = await CleanAsync(path, ct);
        if (!clean.Ran)
        {
            return ReviewTreeReason.GitFailed;
        }

        if (!clean.Ok)
        {
            return ReviewTreeReason.IncompleteAndDirty;
        }

        // Unlock first: the tree was created locked, and a locked tree refuses `remove --force` — the
        // very guard that protects a finished one. Then force, because a tree with populated
        // submodules refuses a plain remove (measured: `working trees containing submodules cannot be
        // moved or removed`).
        await Git(repoPath, ["worktree", "unlock", path], Asking, ct);
        var again = await CleanAsync(path, ct);
        if (!again.Ran || !again.Ok)
        {
            return ReviewTreeReason.IncompleteAndDirty;
        }

        await Git(repoPath, ["worktree", "remove", "--force", path], Asking, ct);
        await Git(repoPath, ["worktree", "prune"], Asking, ct);

        return await GoneAsync(path, ct) ? string.Empty : ReviewTreeReason.GitFailed;
    }

    /// <summary>
    /// Deletes what git's own removal left behind, under a budget and never outside our root.
    /// </summary>
    /// <remarks>
    /// <para><b>Inside the root, checked.</b> The path is built from our own constants today, but a
    /// recursive delete is the one call in this class that could destroy something outside it, and a
    /// guard costs two lines. .NET does not follow a directory symlink when deleting recursively, so
    /// the remaining risk is a root that is itself not what it says; the check answers that too.</para>
    /// <para><b>Off the calling thread, with a timeout.</b> A held file handle — an open editor, a
    /// virus scanner — makes <c>Directory.Delete</c> block for as long as the handle lives, and the
    /// whole request would have hung behind it. It now gives up, and the tree is answered as one that
    /// could not be cleared. (Code round, gemini and local.)</para>
    /// </remarks>
    private async Task<bool> GoneAsync(string path, CancellationToken ct)
    {
        if (!Inside(path))
        {
            return false;
        }

        // git's own removal is the normal path and it leaves nothing behind; this method exists for
        // the removal that half-failed. Asking first is not a redundant check — `Directory.Delete` on
        // an absent directory throws, and the catch below would read that as "could not clear it".
        if (!Directory.Exists(path))
        {
            return true;
        }

        try
        {
            await Task.Run(() => Directory.Delete(path, recursive: true), ct).WaitAsync(Erasing, ct);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or TimeoutException)
        {
            return false;
        }

        return !Directory.Exists(path);
    }

    /// <summary>Whether a path really is under our own root — asked before anything is deleted.</summary>
    private bool Inside(string path)
    {
        var below = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        return Path.GetFullPath(path).StartsWith(below, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Creates the tree, fills its submodules, and writes the record that says it is ready.</summary>
    private async Task<ReviewTree> AddAsync(
        long findingId, TreePlace place, string repository, string name, string path, CancellationToken ct)
    {
        var added = await AddedAsync(place, path, ct);
        if (!added.Ran || !added.Ok)
        {
            return LostTheRace(findingId, place, repository, name, path);
        }

        var mounts = await EmptyMountsAsync(place.RepoPath, path, ct);
        ReviewTreeRecords.Write(
            ReviewTreeRecords.FileFor(root, name),
            new HeldRecord(repository, place.RepoPath, place.Sha, DateTime.UtcNow.ToString("O"), mounts));

        return Made(findingId, place, repository, path, mounts, reused: false);
    }

    /// <summary>
    /// The checkout itself — and, if git refuses because the path is registered to a worktree whose
    /// directory is no longer there, one prune and one retry.
    /// </summary>
    /// <remarks>
    /// A person who deletes a review checkout with their file manager leaves git's registration of it
    /// behind, and every later press at that identity then failed with *missing but already
    /// registered* — the feature dead at that commit until somebody ran `git worktree prune` by hand.
    /// Found by the test written for the code-round finding about stale records, which is a better
    /// reason to believe it than the finding was. The prune is only reached when the directory really
    /// is absent, so it can never remove a registration whose tree exists.
    /// </remarks>
    private async Task<GitAnswer> AddedAsync(TreePlace place, string path, CancellationToken ct)
    {
        string[] add = ["worktree", "add", "--detach", "--lock", "--reason", LockReason, path, place.Sha];

        var added = await Git(place.RepoPath, add, Checking, ct);
        if (added.Ran && added.Ok || Directory.Exists(path))
        {
            return added;
        }

        await Git(place.RepoPath, ["worktree", "prune"], Asking, ct);

        return await Git(place.RepoPath, add, Checking, ct);
    }

    /// <summary>
    /// <c>worktree add</c> refused. The interesting case is not an error at all: git's own refusal to
    /// create an existing directory IS the mutex, so of two presses at once exactly one creates and
    /// the other arrives here. If the winner has finished, hand back its tree; if it is still
    /// working, say so; only a refusal with no directory behind it is a real failure.
    /// </summary>
    private ReviewTree LostTheRace(
        long findingId, TreePlace place, string repository, string name, string path)
    {
        var read = ReviewTreeRecords.Read(ReviewTreeRecords.FileFor(root, name));
        if (Ready(read, repository, place.Sha) && Directory.Exists(path))
        {
            return Made(findingId, place, repository, path, read.Record.EmptyMounts, reused: true);
        }

        return Refused(
            findingId,
            place,
            repository,
            Directory.Exists(path) ? ReviewTreeReason.InProgress : ReviewTreeReason.GitFailed);
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

        var declared = await Git(path, ["config", "-f", ".gitmodules", "--get-regexp", "path"], Asking, ct);

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

    /// <summary>Lazy: it stops at the first entry, so an enormous submodule costs one directory read.</summary>
    private static bool IsEmpty(string mount) =>
        !Directory.Exists(mount) || !Directory.EnumerateFileSystemEntries(mount).Any();

    /// <summary>
    /// The machine-wide claim that makes the cap a cap: one creator at a time under this root.
    /// </summary>
    /// <remarks>
    /// A file opened with no sharing is the cheapest cross-process mutex that works on every
    /// filesystem this runs on, and it is released by the process dying as well as by the
    /// <c>using</c>. A press that cannot take it inside a few seconds answers <c>in_progress</c>,
    /// which is true: somebody else is creating.
    /// </remarks>
    private async Task<Slot> SlotAsync(CancellationToken ct)
    {
        var until = DateTime.UtcNow + ForTheSlot;
        while (DateTime.UtcNow < until)
        {
            var taken = Slot.Try(Path.Combine(root, ".creating.lock"));
            if (taken.Taken)
            {
                return taken;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), ct);
        }

        return Slot.None;
    }

    /// <summary>A held file lock, or the absence of one — never a null in the caller.</summary>
    private sealed class Slot(FileStream? held) : IDisposable
    {
        public static Slot None { get; } = new(null);

        public bool Taken => held is not null;

        public static Slot Try(string path)
        {
            try
            {
                return new Slot(new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return None;
            }
        }

        public void Dispose() => held?.Dispose();
    }

    private static ReviewTree Made(
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
        long findingId, TreePlace place, string repository, IReadOnlyList<HeldRecord> held) =>
        new()
        {
            FindingId = findingId,
            Sha = place.Sha,
            Repository = repository,
            Reason = ReviewTreeReason.Budget,
            Trees = [.. held.Select(r => new ReviewTreeRow(
                r.Repository, r.Sha, Path.Combine(root, NameOf(r.Repository, r.Sha)), r.Created))],
        };

    private async Task<GitAnswer> Git(string workingDirectory, string[] args, TimeSpan budget, CancellationToken ct)
    {
        var result = await launcher.RunAsync(
            new ProcessRequest("git", args, workingDirectory) { Timeout = budget }, ct);

        return result.TimedOut || result.Cancelled
            ? GitAnswer.Broken
            : new GitAnswer(true, result.ExitCode == 0, result.StdOut);
    }
}
