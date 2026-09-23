using CoaiMcp.Core.Collecting;
using CoaiMcp.Runners.Collecting;

namespace CoaiMcp.Runners.Worktrees;

/// <summary>What an inspection found, before it becomes an answer.</summary>
/// <param name="Reason">Empty when the tree holds nothing; otherwise why it may not go.</param>
/// <param name="InTheWay">The paths, named — a refusal nobody can act on is not a refusal.</param>
/// <param name="Ignored">How many ignored files are in it.</param>
/// <param name="IgnoredSample">A few of them, so the number can be judged rather than trusted.</param>
internal sealed record Looked(
    string Reason,
    IReadOnlyList<string> InTheWay,
    int Ignored,
    IReadOnlyList<string> IgnoredSample)
{
    /// <summary>The submodule mounts that were inspected — their own files are named instead.</summary>
    internal IReadOnlyList<string> Mounts { get; init; } = [];

    internal static Looked Clean(int ignored, IReadOnlyList<string> sample) => new("", [], ignored, sample);

    internal static Looked No(string reason) => new(reason, [], 0, []);
}

/// <summary>
/// Listing the review trees this machine holds, and giving one back.
/// </summary>
/// <remarks>
/// <para><b>The invariant, stated as narrowly as it is true.</b> Nothing this product deletes was
/// dirty at the moment it was checked, and that check is the last thing that happens before the
/// delete. It is NOT a promise that a file written between the check and the delete survives: a
/// reviewer was right that no second status can close that window, and saying otherwise would be a
/// comment that overclaims. What the window is bounded by is that the check and the removal are
/// consecutive git calls on the same tree.</para>
/// <para><b>One at a time, by NAME, from our own records.</b> There is no mode, flag or method here
/// that removes more than one tree — not as a convenience, not for an empty root, not for cleanup.
/// The list is built from OUR records and OUR directories, so a round worktree or a person's own
/// worktree cannot appear in it, and what cannot be listed cannot be asked for.</para>
/// <para><b>Ignored files are somebody's until they say otherwise.</b> The first draft called them
/// reproducible and swept them along with the tree; a <c>.env</c>, a local config and a globally
/// ignored <c>notes.md</c> are none of those things, and no plain status shows them. They are
/// counted, sampled and refused, and the person asking again — per tree — is the only confirmation
/// this product has.</para>
/// </remarks>
public sealed class ReviewTreeKeeper(ReviewTreeRoot root)
{
    /// <summary>Enough names to judge a number by, without turning a row into a file listing.</summary>
    private const int Sample = 5;

    /// <summary>Every review tree this machine holds, oldest first.</summary>
    public async Task<ReviewTrees> ListAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(root.Path))
        {
            return new ReviewTrees { Root = root.Path };
        }

        IReadOnlyList<string> names;
        try
        {
            names = [.. Names()];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A root this process cannot read is not a machine holding nothing, and answering an
            // empty list would invite somebody to check a commit out again into a place that already
            // holds ten. (Code round, codex.)
            return new ReviewTrees { Root = root.Path, Reason = $"the review-tree folder could not be read: {e.Message}" };
        }

        var seen = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<HeldReviewTree>();
        foreach (var name in names)
        {
            rows.Add(await RowAsync(name, seen, ct));
        }

        return new ReviewTrees
        {
            Root = root.Path,
            Trees = [.. rows.OrderBy(r => r.Created, StringComparer.Ordinal).ThenBy(r => r.Name, StringComparer.Ordinal)],
        };
    }

    /// <summary>
    /// Every name under the root, from BOTH sides of the join.
    /// </summary>
    /// <remarks>
    /// Reading only the records would miss a directory whose process died before writing one — which
    /// is precisely the state the list exists to show, because nothing else will ever mention it.
    /// (Plan round, codex.)
    /// </remarks>
    private IEnumerable<string> Names() =>
        Directory.EnumerateDirectories(root.Path, $"{ReviewTreeRoot.Prefix}*")
            .Select(Path.GetFileName)
            .Concat(Directory.EnumerateFiles(root.Path, $"{ReviewTreeRoot.Prefix}*.json")
                .Select(Path.GetFileNameWithoutExtension))
            .OfType<string>()
            .Where(ReviewTreeRoot.IsOurName)
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private async Task<HeldReviewTree> RowAsync(
        string name, Dictionary<string, IReadOnlyList<string>> seen, CancellationToken ct)
    {
        var path = root.TreeAt(name);
        var read = ReviewTreeRecords.Read(ReviewTreeRecords.FileFor(root.Path, name));
        if (!Directory.Exists(path))
        {
            return Row(name, path, read, ReviewTreeState.Vanished);
        }

        // A record whose identity does not recompute to THIS name describes another tree, so it
        // proves nothing about this one — the same check a reuse makes before handing a tree back,
        // and the same one a removal makes before it hands a `RepoPath` to git.
        return Mine(read, name)
            ? Row(name, path, read, await StateOfAsync(read.Record, path, seen, ct))
            : Row(name, path, read, ReviewTreeState.Incomplete);
    }

    /// <summary>Whether a record describes THIS tree — its identity recomputed to its own name.</summary>
    private static bool Mine(RecordRead read, string name) =>
        read.State == RecordState.Found
        && string.Equals(
            ReviewTreeRoot.NameOf(read.Record.Repository, read.Record.Sha),
            name,
            StringComparison.OrdinalIgnoreCase);

    private static HeldReviewTree Row(string name, string path, RecordRead read, string state) =>
        new(name, read.Record.Repository, read.Record.RepoPath, read.Record.Sha, path, read.Record.Created, state);

    /// <summary>Ready, unregistered, or unreachable — what git says about a tree that is on disk.</summary>
    private async Task<string> StateOfAsync(
        HeldRecord record, string path, Dictionary<string, IReadOnlyList<string>> seen, CancellationToken ct)
    {
        if (!Directory.Exists(record.RepoPath))
        {
            return ReviewTreeState.Unreachable;
        }

        var registered = await RegisteredAsync(record.RepoPath, seen, ct);

        return registered.Any(one => WorktreePaths.Same(one, path)) ? ReviewTreeState.Ready : ReviewTreeState.Unregistered;
    }

    /// <summary>
    /// The worktree paths a repository lists — asked once per repository, however many trees it holds.
    /// </summary>
    private async Task<IReadOnlyList<string>> RegisteredAsync(
        string repoPath, Dictionary<string, IReadOnlyList<string>> seen, CancellationToken ct)
    {
        if (seen.TryGetValue(repoPath, out var held))
        {
            return held;
        }

        var listed = await root.GitAsync(repoPath, ["worktree", "list", "--porcelain"], ReviewTreeRoot.Asking, ct);
        IReadOnlyList<string> paths = listed.Ran && listed.Ok
            ? [.. listed.Lines
                .Where(l => l.StartsWith("worktree ", StringComparison.Ordinal))
                .Select(l => l["worktree ".Length..].Trim())]
            : [];
        seen[repoPath] = paths;

        return paths;
    }

    /// <summary>Gives one tree back — or says, by name, what is in the way.</summary>
    public async Task<ReviewTreeRemoval> RemoveAsync(
        string name, bool withIgnored = false, CancellationToken ct = default)
    {
        if (!ReviewTreeRoot.IsOurName(name) || !Directory.Exists(root.Path))
        {
            return Refused(name, ReviewTreeRemovalReason.NotOurs);
        }

        var path = root.TreeAt(name);
        var read = ReviewTreeRecords.Read(ReviewTreeRecords.FileFor(root.Path, name));
        if (!Directory.Exists(path))
        {
            return Forget(name, read);
        }

        // The SAME check the listing makes before it calls a tree `ready`. Without it, a stale or
        // copied record beside a valid directory would hand its `RepoPath` to `worktree unlock` and
        // `worktree remove` — deregistering a worktree of a repository this tree never belonged to.
        // (Code round 2, codex and gemini.) What it costs is that an unfinished tree cannot be
        // removed from here; what it buys is that nothing is deregistered on an unverified word, and
        // pressing `Check out` on that commit again clears a clean one, which `--tree-at` already
        // does and the sentence says.
        return Mine(read, name)
            ? await TakeAsync(name, path, read, withIgnored, ct)
            : Refused(name, ReviewTreeRemovalReason.Incomplete);
    }

    /// <summary>
    /// The directory is already gone: drop the record and say so.
    /// </summary>
    /// <remarks>
    /// Git's registration is deliberately left. <c>worktree prune</c> is the only tool git offers for
    /// it and has no path filter — it clears EVERY unreachable registration in that repository,
    /// including a person's own worktree on an unmounted drive. The next checkout at this identity
    /// prunes under its own narrow guard. (Plan round, codex, Blocking.)
    /// </remarks>
    private ReviewTreeRemoval Forget(string name, RecordRead read)
    {
        if (read.State == RecordState.Absent)
        {
            return Refused(name, ReviewTreeRemovalReason.NotOurs);
        }

        return Dropped(name)
            ? new ReviewTreeRemoval { Name = name, Reason = ReviewTreeRemovalReason.Forgotten }
            : Refused(name, ReviewTreeRemovalReason.GitFailed);
    }

    /// <summary>
    /// Removes the record, and says whether it went.
    /// </summary>
    /// <remarks>
    /// The first draft swallowed the failure and answered <c>forgotten</c> anyway, so a record locked
    /// by another process was reported as dropped and came back on the next list. Untidy rather than
    /// unsafe — nothing is deleted on the strength of a record — but a product that says it did
    /// something it did not is the thing this whole story is trying not to be. (Code round, three
    /// findings.)
    /// </remarks>
    private bool Dropped(string name)
    {
        try
        {
            File.Delete(ReviewTreeRecords.FileFor(root.Path, name));

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Inspect, inspect again, then remove — and stop at the first thing in the way.</summary>
    private async Task<ReviewTreeRemoval> TakeAsync(
        string name, string path, RecordRead read, bool withIgnored, CancellationToken ct)
    {
        var looked = await LookedAtAsync(path, withIgnored, ct);
        if (looked.Reason.Length > 0)
        {
            return Said(name, looked);
        }

        // The last thing before the delete, so the window between knowing and acting is two
        // consecutive git calls. It cannot be closed — a file written inside it is lost, and the
        // docblock says so rather than promising otherwise. (Plan round, codex.)
        var again = await LookedAtAsync(path, withIgnored, ct);

        return again.Reason.Length > 0
            ? Said(name, again)
            : await GoneAsync(name, path, read, again, ct);
    }

    /// <summary>Unlock, remove, drop the record — in a repository that must still be there.</summary>
    private async Task<ReviewTreeRemoval> GoneAsync(
        string name, string path, RecordRead read, Looked looked, CancellationToken ct)
    {
        var repoPath = read.Record.RepoPath;
        if (repoPath.Length == 0 || !Directory.Exists(repoPath))
        {
            return Refused(name, ReviewTreeRemovalReason.Unreachable);
        }

        // Unlock first: the tree was created locked, and a locked tree refuses `remove --force` — the
        // very guard that protects it from the gate. Then force, because a tree with populated
        // submodules refuses a plain remove (measured: `working trees containing submodules cannot be
        // moved or removed`). The force is reached only after the inspection came back empty, twice.
        await root.GitAsync(repoPath, ["worktree", "unlock", path], ReviewTreeRoot.Asking, ct);
        var removed = await root.GitAsync(
            repoPath, ["worktree", "remove", "--force", path], ReviewTreeRoot.Checking, ct);

        // git's own removal is what deregisters the tree. Deleting the directory after it FAILED
        // would leave the registration behind and the files gone — a state neither this product nor
        // git can make sense of afterwards. So a failed remove stops here, with everything intact.
        // (Code round, gemini.) `EnsureGoneAsync` is then only for the remove that half-succeeded.
        if (!removed.Ran || !removed.Ok || !await root.EnsureGoneAsync(path, ct))
        {
            return Refused(name, ReviewTreeRemovalReason.GitFailed);
        }

        Dropped(name);

        return new ReviewTreeRemoval
        {
            Name = name,
            Reason = ReviewTreeRemovalReason.Removed,
            Ignored = looked.Ignored,
            IgnoredSample = looked.IgnoredSample,
        };
    }

    /// <summary>
    /// What is in the tree: tracked changes, untracked files, what the submodules hold, and what is
    /// ignored.
    /// </summary>
    /// <remarks>
    /// <para><b>Exit 0 is required, not merely empty output.</b> A permission failure or an
    /// inaccessible submodule can produce no lines while git exits non-zero, and "empty" would then
    /// have meant "clean". (Plan round, codex.)</para>
    /// <para><b>git ran and refused</b> — *not a git repository*, a broken <c>.git</c> link — is not a
    /// failure to retry and not a licence to delete: it is exactly the state in which this product
    /// cannot know whose the files are, so it is refused by its own name.</para>
    /// </remarks>
    private async Task<Looked> LookedAtAsync(string path, bool withIgnored, CancellationToken ct)
    {
        var status = await root.GitAsync(
            path,
            ["status", "--porcelain=v2", "--untracked-files=all", "--ignore-submodules=none", "--ignored=matching"],
            ReviewTreeRoot.Asking,
            ct);

        if (!status.Ran)
        {
            return Looked.No(ReviewTreeRemovalReason.GitFailed);
        }

        if (!status.Ok)
        {
            return Looked.No(ReviewTreeRemovalReason.Unregistered);
        }

        return await JudgedAsync(path, status.Lines, withIgnored, ct);
    }

    private async Task<Looked> JudgedAsync(
        string path, IReadOnlyList<string> lines, bool withIgnored, CancellationToken ct)
    {
        var inside = await InsideEveryMountAsync(path, lines, ct);
        if (inside.Reason.Length > 0)
        {
            return inside;
        }

        IReadOnlyList<string> ignored = [.. Ignored(lines), .. inside.IgnoredSample];
        IReadOnlyList<string> theirs = [.. Named(lines, inside), .. inside.InTheWay];

        if (theirs.Count > 0)
        {
            return new Looked(ReviewTreeRemovalReason.Dirty, theirs, ignored.Count, Few(ignored));
        }

        return ignored.Count > 0 && !withIgnored
            ? new Looked(ReviewTreeRemovalReason.HasIgnored, [], ignored.Count, Few(ignored))
            : Looked.Clean(ignored.Count, Few(ignored));
    }

    /// <summary>The parent's own ignored paths — its `!` lines.</summary>
    private static IEnumerable<string> Ignored(IReadOnlyList<string> lines) =>
        lines.Where(l => l.StartsWith("! ", StringComparison.Ordinal)).Select(l => l[2..].Trim());

    /// <summary>
    /// What the parent says is the person's, with a flagged submodule's MOUNT dropped: the mount is
    /// not a file anybody can act on, and the files inside it have been named separately.
    /// </summary>
    private static IEnumerable<string> Named(IReadOnlyList<string> lines, Looked inside) =>
        lines.Where(Ours).Select(PathIn).Where(one => !inside.Mounts.Contains(one, StringComparer.Ordinal));

    /// <summary>
    /// Every POPULATED submodule, inspected in its own right — which is the only way to see what is
    /// in it.
    /// </summary>
    /// <remarks>
    /// <para><b>Measured 2026-09-21, and it is why this is not an optimisation but the correctness of
    /// the whole story.</b> A parent's <c>git status --ignore-submodules=none --ignored=matching</c>
    /// reports an ignored file in the PARENT (<c>! ignored-here.txt</c>) and is <b>completely blind</b>
    /// to an ignored file inside a populated submodule: empty output, no flag, nothing. A `.env` in a
    /// submodule would therefore have read as a clean tree and been deleted by
    /// <c>worktree remove --force</c> without the confirmation the design rests on. Three reviewers
    /// raised it independently and all three were right.</para>
    /// <para>So every populated mount is statused, not only the ones the parent flagged: the flags say
    /// what is MODIFIED or UNTRACKED there, and say nothing at all about what is ignored. It costs one
    /// process per mount on a removal, which is a deliberate and rare act; the alternative is losing
    /// somebody's file silently.</para>
    /// <para>An inspection that could not RUN is neither clean nor dirty — it is
    /// <see cref="ReviewTreeRemovalReason.GitFailed"/>, because calling it dirty would be a fact we
    /// do not have, and calling it clean would delete on one.</para>
    /// </remarks>
    private async Task<Looked> InsideEveryMountAsync(
        string path, IReadOnlyList<string> lines, CancellationToken ct)
    {
        var mounts = await SubmoduleMounts.PopulatedAsync(root, path, ct);
        var reads = new List<Looked>(mounts.Count);
        foreach (var mount in mounts)
        {
            var read = await OneMountAsync(path, mount, ct);
            if (read.Reason.Length > 0)
            {
                return read;
            }

            reads.Add(read);
        }

        IReadOnlyList<string> theirs = [.. reads.SelectMany(r => r.InTheWay)];
        IReadOnlyList<string> ignored = [.. reads.SelectMany(r => r.IgnoredSample)];

        return new Looked("", theirs, ignored.Count, ignored) { Mounts = mounts };
    }

    /// <summary>One submodule's own view of itself, with its mount prefixed onto every path.</summary>
    private async Task<Looked> OneMountAsync(string path, string mount, CancellationToken ct)
    {
        var status = await root.GitAsync(
            Path.Combine(path, mount),
            ["status", "--porcelain=v2", "--untracked-files=all", "--ignore-submodules=none", "--ignored=matching"],
            ReviewTreeRoot.Asking,
            ct);

        if (!status.Ran || !status.Ok)
        {
            return Looked.No(ReviewTreeRemovalReason.GitFailed);
        }

        return new Looked(
            "",
            [.. status.Lines.Where(Ours).Select(l => $"{mount}/{PathIn(l)}")],
            0,
            [.. Ignored(status.Lines).Select(one => $"{mount}/{one}")]);
    }

    private static IReadOnlyList<string> Few(IReadOnlyList<string> all) => [.. all.Take(Sample)];

    /// <summary>A line about something of the person's: a change, or a file git does not track.</summary>
    private static bool Ours(string line) =>
        line.StartsWith("1 ", StringComparison.Ordinal)
        || line.StartsWith("2 ", StringComparison.Ordinal)
        || line.StartsWith("u ", StringComparison.Ordinal)
        || line.StartsWith("? ", StringComparison.Ordinal);

    /// <summary>The path a porcelain v2 line ends with — the last field, and it may contain spaces.</summary>
    private static string PathIn(string line)
    {
        if (line.StartsWith("? ", StringComparison.Ordinal))
        {
            return line[2..].Trim();
        }

        var parts = line.Split(' ', 9);

        return parts.Length == 9 ? parts[8].Trim() : line.Trim();
    }

    private static ReviewTreeRemoval Refused(string name, string reason) =>
        new() { Name = name, Reason = reason };

    private static ReviewTreeRemoval Said(string name, Looked looked) =>
        new()
        {
            Name = name,
            Reason = looked.Reason,
            InTheWay = looked.InTheWay,
            Ignored = looked.Ignored,
            IgnoredSample = looked.IgnoredSample,
        };
}
