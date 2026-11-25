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
    /// <param name="secret">The key the turn ran on, when it ran on a pool key: the staged files are searched for its exact text BEFORE the commit, and a hit means no commit — the job fails <c>key-in-output</c> (epoch 3 E2 s7).</param>
    public static async Task<(string Head, RoleWorkOutcome.Failed? Failure)> CommitAsync(GitHistory git, CoderTree tree, string message, CancellationToken ct, ClaudeKeySecret? secret = null)
    {
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

        if (secret is not null && pending.Out.Length > 0 && HoldsTheKey(tree.Root, pending.Out, secret))
        {
            return (string.Empty, new RoleWorkOutcome.Failed(RoleStopWords.KeyInOutput, "the exact text of the turn's claude key is in a file the coder wrote, so nothing was committed, the key is dead (exposed) and the job stops; revoke the key at the vendor"));
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
