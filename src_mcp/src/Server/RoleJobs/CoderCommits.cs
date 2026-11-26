using CoaiMcp.Runners.Collecting;
using CoaiMcp.Runners.Worktrees;
using CoaiMcp.Server.Keys;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>
/// coai's own git on a coder's worktree, through the hardened door (<see cref="GitHistory.InCoderTreeAsync"/>): the ONE commit of a turn, the head, and the list of files a range changed. The coder has no shell and cannot commit — every commit of a coder job
/// is made here, one per turn (turn 0 and each fix turn), and the file list comes from git alone (epoch 3 E2 s3 t3, s4 t2).
/// </summary>
internal static class CoderCommits
{
    /// <summary>The message of turn 0's commit; a fix turn's is <see cref="FixMessage"/>.</summary>
    public static string Message(string jobId, string taskId) => $"coder {jobId}: {taskId}";

    /// <summary>The message of the commit of fix turn <paramref name="turn"/> (1-based).</summary>
    public static string FixMessage(string jobId, string taskId, int turn) => $"coder {jobId}: {taskId} fix {turn}";

    /// <summary>The message of the commit of the turn 0 of the <paramref name="attempt"/>-th model that answered (2 or more): a model that took over after another stopped made it.</summary>
    public static string AttemptMessage(string jobId, string taskId, int attempt) => $"coder {jobId}: {taskId} attempt {attempt}";

    /// <summary>Stages everything the coder wrote and makes ONE commit of it; no change means no commit and the head stays where it was.</summary>
    /// <param name="secret">
    /// The key the turn ran on, when it ran on a pool key (epoch 3 E2 s7): the files are searched for its exact text BEFORE <c>git add</c> (so a leak's blob is never written) and the STAGED BLOBS — what git will commit, after any
    /// <c>.gitattributes</c> conversion — and the file names are searched again before the commit. A hit means no commit and the job fails <c>key-in-output</c>. A search that cannot be made (git said no, the output was cut) is a failure too, never a pass.
    /// </param>
    public static async Task<(string Head, RoleWorkOutcome.Failed? Failure)> CommitAsync(GitHistory git, CoderTree tree, string message, CancellationToken ct, ClaudeKeySecret? secret = null)
    {
        if (secret is not null && await SearchWorkingTreeAsync(git, tree, secret, ct) is { } before)
        {
            return (string.Empty, before);
        }

        // Git does not see a file written inside the empty folder of a gitlink (it takes the folder for a submodule's own and ignores what is in it), so the folders are looked at on the disk BEFORE anything is staged.
        if (WrittenInTheMounts(tree) is { } written)
        {
            return (string.Empty, Protected(written));
        }

        var staged = await git.InCoderTreeAsync(tree, ["add", "-A"], ct);
        if (!staged.Ran || !staged.Ok)
        {
            return (string.Empty, Failed("git add in the worktree", staged));
        }

        var pending = await git.InCoderTreeAsync(tree, ["diff", "--cached", "--name-only", "-z"], ct);
        if (!pending.Ran || !pending.Ok)
        {
            return (string.Empty, Failed("git diff --cached in the worktree", pending));
        }

        if (ProtectedHit(tree, pending.Out) is { } hit)
        {
            // What was staged is unstaged again: the refusal leaves the index as the turn found it, and the worktree as the coder left it.
            await git.InCoderTreeAsync(tree, ["reset", "-q"], ct);

            return (string.Empty, Protected(hit));
        }

        if (secret is not null && pending.Out.Length > 0 && await SearchStagedAsync(git, tree, pending.Out, secret, ct) is { } after)
        {
            return (string.Empty, after);
        }

        if (pending.Out.Length > 0)
        {
            var made = await git.InCoderTreeAsync(tree, ["commit", "--no-verify", "--no-gpg-sign", "-m", message], ct);
            if (!made.Ran || !made.Ok)
            {
                return (string.Empty, Failed("git commit in the worktree (is a git identity, user.name and user.email, configured?)", made));
            }
        }

        return await HeadAsync(git, tree, ct);
    }

    private static RoleWorkOutcome.Failed Protected(string name) =>
        new("coder-commit", $"the coder's change touches '{name}', which is the base's own (.gitmodules or a submodule's path) and is never a coder's to change, so nothing was committed");

    /// <summary>
    /// The first protected submodule path whose folder in the worktree is no longer the empty folder git leaves for a gitlink — something was written in it, or it was replaced by a file or a link — or null. Null too for a tree that protects nothing.
    /// </summary>
    internal static string? WrittenInTheMounts(CoderTree tree) =>
        tree.Protected.Where(name => name != CoderMounts.GitmodulesFile).FirstOrDefault(name => Touched(Path.Combine(tree.Root, name)));

    private static bool Touched(string full) =>
        Runners.Files.RealPath.Lstat(full).Kind switch
        {
            Runners.Files.EntryKind.File or Runners.Files.EntryKind.Link => true,
            Runners.Files.EntryKind.Directory => Directory.EnumerateFileSystemEntries(full).Any(),
            _ => false,
        };

    /// <summary>
    /// The first name of git's NUL-separated list of staged paths that is a protected name of the tree (<see cref="CoderTree.Protected"/>) or lies below one — <c>.gitmodules</c>, a submodule's path (a deleted gitlink and every file written where it is). Null when none, and always
    /// null for a tree that protects nothing.
    /// </summary>
    internal static string? ProtectedHit(CoderTree tree, string namesZ) =>
        tree.Protected.Count == 0
            ? null
            : namesZ.Split('\0', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(name => tree.Protected.Any(guarded => string.Equals(name, guarded, StringComparison.Ordinal) || name.StartsWith(guarded + "/", StringComparison.Ordinal)));

    private static RoleWorkOutcome.Failed Exposed() =>
        new(RoleStopWords.KeyInOutput, "the exact text of the turn's claude key is in what the coder wrote, so nothing was committed, the key is dead (exposed) and the job stops; revoke the key at the vendor");

    private static RoleWorkOutcome.Failed Unsearchable(string why) =>
        new("coder-commit", $"the files the coder wrote could not be searched for the turn's claude key ({why}), so nothing was committed");

    /// <summary>
    /// The files git would add (modified and new, not ignored — git's own list) searched as they are on disk, BEFORE anything is staged. A link is not followed: what git commits is the link, not what it points at. Null when nothing was found.
    /// </summary>
    private static async Task<RoleWorkOutcome.Failed?> SearchWorkingTreeAsync(GitHistory git, CoderTree tree, ClaudeKeySecret secret, CancellationToken ct)
    {
        var listed = await git.InCoderTreeAsync(tree, ["ls-files", "-z", "--others", "--modified", "--exclude-standard"], ct);
        if (!listed.Ran || !listed.Ok || listed.Truncated)
        {
            return Unsearchable("git could not list the changed files");
        }

        return HoldsTheKey(tree.Root, listed.Out, secret) ? Exposed() : null;
    }

    /// <summary>
    /// What is STAGED, searched as git will commit it (the working-tree bytes can differ: <c>working-tree-encoding</c>, a clean filter): the whole staged diff as text in one command, and when its output was cut, each staged blob by its id. The names are searched too.
    /// </summary>
    private static async Task<RoleWorkOutcome.Failed?> SearchStagedAsync(GitHistory git, CoderTree tree, string namesZ, ClaudeKeySecret secret, CancellationToken ct)
    {
        if (secret.IsIn(System.Text.Encoding.UTF8.GetBytes(namesZ)))
        {
            return Exposed();
        }

        var diff = await git.InCoderTreeAsync(tree, ["diff", "--cached", "--text", "--no-textconv", "--no-ext-diff", "--no-renames"], ct);
        if (!diff.Ran || !diff.Ok)
        {
            return Unsearchable("git could not show the staged changes");
        }

        if (!diff.Truncated)
        {
            return secret.IsIn(System.Text.Encoding.UTF8.GetBytes(diff.Out)) ? Exposed() : null;
        }

        // Too much to read in one piece: every staged blob on its own, by id.
        var raw = await git.InCoderTreeAsync(tree, ["diff", "--cached", "--raw", "-z", "--no-renames", "--no-ext-diff"], ct);
        if (!raw.Ran || !raw.Ok || raw.Truncated)
        {
            return Unsearchable("the staged changes are too many to list");
        }

        foreach (var id in BlobsOf(raw.Out))
        {
            var blob = await git.InCoderTreeAsync(tree, ["cat-file", "blob", id], ct);
            if (!blob.Ran || !blob.Ok || blob.Truncated)
            {
                return Unsearchable("a staged file is too large to read");
            }

            if (secret.IsIn(System.Text.Encoding.UTF8.GetBytes(blob.Out)))
            {
                return Exposed();
            }
        }

        return null;
    }

    /// <summary>The ids of the new blobs of <c>git diff --raw -z</c> (<c>:&lt;mode&gt; &lt;mode&gt; &lt;old&gt; &lt;new&gt; &lt;status&gt;</c>, NUL, the path, NUL): not a deletion, not a submodule.</summary>
    internal static IEnumerable<string> BlobsOf(string rawZ) =>
        rawZ.Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Where(field => field.StartsWith(':'))
            .Select(field => field.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(part => part.Length >= 5 && part[1] != "160000" && part[3].Any(character => character != '0'))
            .Select(part => part[3]);

    /// <summary>
    /// Whether the exact text of <paramref name="secret"/> is in any of the staged files (<paramref name="namesZ"/> is git's NUL-separated list, relative to <paramref name="root"/>). A link is not followed — what git would commit is the link, not what it points at —
    /// and a file that was deleted is not there to search. The same chunked search <c>--keys scan</c> makes, so a text cut by a chunk edge is found.
    /// </summary>
    private static bool HoldsTheKey(string root, string namesZ, ClaudeKeySecret secret) =>
        namesZ.Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Select(name => Path.GetFullPath(Path.Combine(root, name)))
            .Where(file => new FileInfo(file) is { Exists: true, LinkTarget: null })
            .Any(file => KeyScanner.Walk(file, [secret], string.Empty).Hits.Count > 0);

    /// <summary>The commit the worktree is on.</summary>
    public static async Task<(string Head, RoleWorkOutcome.Failed? Failure)> HeadAsync(GitHistory git, CoderTree tree, CancellationToken ct)
    {
        var head = await git.InCoderTreeAsync(tree, ["rev-parse", "HEAD"], ct);

        return head.Ran && head.Ok && head.Lines.Count > 0 && GitHistory.IsCommitish(head.Lines[0])
            ? (head.Lines[0], null)
            : (string.Empty, Failed("git rev-parse HEAD in the worktree", head));
    }

    /// <summary><c>git diff --name-status</c> between the base and the head — the ONLY source of the file list. Null when git could not say.</summary>
    public static async Task<IReadOnlyList<CoderFileChange>?> ChangesAsync(GitHistory git, CoderTree tree, string baseSha, string head, CancellationToken ct)
    {
        var listed = await git.InCoderTreeAsync(tree, ["diff", "--name-status", "-z", "--no-renames", baseSha, head], ct);

        return listed.Ran && listed.Ok ? CoderEnvelope.ParseNameStatus(listed.Out) : null;
    }

    public static RoleWorkOutcome.Failed Failed(string doing, GitAnswer answer) =>
        new("coder-commit", answer.Refusal.Length > 0 ? answer.Refusal : $"{doing} failed{(answer.Ran ? string.Empty : " or did not finish in time")}; the worktree was left as it is");
}
