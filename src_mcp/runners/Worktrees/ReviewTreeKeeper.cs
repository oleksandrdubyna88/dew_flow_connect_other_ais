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

        var seen = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<HeldReviewTree>();
        foreach (var name in Names())
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

        return read.State == RecordState.Found
            ? Row(name, path, read, await StateOfAsync(read.Record, path, seen, ct))
            : Row(name, path, read, ReviewTreeState.Incomplete);
    }

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

        return registered.Any(one => Same(one, path)) ? ReviewTreeState.Ready : ReviewTreeState.Unregistered;
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

    private static bool Same(string one, string other) =>
        string.Equals(
            Path.GetFullPath(one).Replace('\\', '/').TrimEnd('/'),
            Path.GetFullPath(other).Replace('\\', '/').TrimEnd('/'),
            StringComparison.OrdinalIgnoreCase);

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

        return await TakeAsync(name, path, read, withIgnored, ct);
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

        Drop(name);

        return new ReviewTreeRemoval { Name = name, Reason = ReviewTreeRemovalReason.Forgotten };
    }

    private void Drop(string name)
    {
        try
        {
            File.Delete(ReviewTreeRecords.FileFor(root.Path, name));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The record outliving its tree is untidy, not unsafe: the next list shows it as vanished
            // and the next attempt drops it. Nothing is deleted on the strength of it.
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
        await root.GitAsync(repoPath, ["worktree", "remove", "--force", path], ReviewTreeRoot.Checking, ct);

        if (!await root.EnsureGoneAsync(path, ct))
        {
            return Refused(name, ReviewTreeRemovalReason.GitFailed);
        }

        Drop(name);

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
        var ignored = lines.Where(l => l.StartsWith("! ", StringComparison.Ordinal))
            .Select(l => l[2..].Trim())
            .ToList();
        var theirs = await TheirsAsync(path, lines, ct);

        if (theirs.Count > 0)
        {
            return new Looked(ReviewTreeRemovalReason.Dirty, theirs, ignored.Count, Few(ignored));
        }

        return ignored.Count > 0 && !withIgnored
            ? new Looked(ReviewTreeRemovalReason.HasIgnored, [], ignored.Count, Few(ignored))
            : Looked.Clean(ignored.Count, Few(ignored));
    }

    private static IReadOnlyList<string> Few(IReadOnlyList<string> all) => [.. all.Take(Sample)];

    /// <summary>
    /// Everything in the tree that is somebody's, named — descending into a submodule only when the
    /// parent's own status has already said that submodule holds something.
    /// </summary>
    /// <remarks>
    /// Measured 2026-09-21 against real git: one status in the parent SEES all four dirty states,
    /// including an untracked file inside a populated submodule, but reports the submodule cases as
    /// the MOUNT (<c>mods/sub</c>) and never the file. So the verdict needs one call and the NAMES
    /// need one more per flagged mount — and only per flagged mount, because descending into every
    /// one would be work per submodule on every press for a fact already in hand.
    /// </remarks>
    private async Task<IReadOnlyList<string>> TheirsAsync(
        string path, IReadOnlyList<string> lines, CancellationToken ct)
    {
        var named = new List<string>();
        foreach (var line in lines.Where(Ours))
        {
            named.Add(PathIn(line));
        }

        foreach (var mount in lines.Where(Stirred).Select(PathIn).Distinct(StringComparer.Ordinal))
        {
            named.Remove(mount);
            named.AddRange(await InsideAsync(path, mount, ct));
        }

        return named;
    }

    /// <summary>A line about something of the person's: a change, or a file git does not track.</summary>
    private static bool Ours(string line) =>
        line.StartsWith("1 ", StringComparison.Ordinal)
        || line.StartsWith("2 ", StringComparison.Ordinal)
        || line.StartsWith("u ", StringComparison.Ordinal)
        || line.StartsWith("? ", StringComparison.Ordinal);

    /// <summary>
    /// A line about a SUBMODULE with modified or untracked content — porcelain v2's fourth field is
    /// <c>S&lt;c&gt;&lt;m&gt;&lt;u&gt;</c>, and it is the only place this fact is available.
    /// </summary>
    private static bool Stirred(string line)
    {
        var parts = line.Split(' ');

        return parts.Length > 2
            && parts[0] == "1"
            && parts[2].Length == 4
            && parts[2][0] == 'S'
            && (parts[2][2] == 'M' || parts[2][3] == 'U');
    }

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

    /// <summary>The files inside one submodule, prefixed with its mount so a person can find them.</summary>
    private async Task<IReadOnlyList<string>> InsideAsync(string path, string mount, CancellationToken ct)
    {
        var status = await root.GitAsync(
            Path.Combine(path, mount),
            ["status", "--porcelain=v2", "--untracked-files=all", "--ignore-submodules=none"],
            ReviewTreeRoot.Asking,
            ct);

        return status.Ran && status.Ok
            ? [.. status.Lines.Where(Ours).Select(l => $"{mount}/{PathIn(l)}")]
            : [$"{mount} (its own files could not be listed)"];
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
