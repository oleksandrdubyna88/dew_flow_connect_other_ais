using CoaiMcp.Runners.Collecting;
using CoaiMcp.Runners.Worktrees;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>
/// Puts a coder worktree back to a coai commit (epoch 3 E2 s4 t3; <c>v2/architecture/server.md</c> §§ 16, 20): what a model that stopped had written and coai had not committed is dropped, and coai PROVES it with <c>git status</c> before the next model is started on
/// the tree. Every command goes through the hardened door (<see cref="GitHistory.InCoderTreeAsync"/>) — the <c>.git</c> pointer compared first, no hook, no fsmonitor — because the files being dropped are the untrusted ones.
/// </summary>
/// <remarks>
/// <para><b>What is dropped:</b> edits of tracked files, staged changes, untracked files and — because the next model must meet exactly the last coai commit and nothing a failed model left — ignored files too (<c>git reset --hard</c>, then <c>git clean -ffdx</c>;
/// the second <c>-f</c> also removes a folder the model made into a git repository of its own). The worktree is coai's, so nothing of the person's is below it.</para>
/// <para><b>The proof is not the commands' exit code.</b> After them HEAD is read and <c>git status --porcelain --untracked-files=all --ignored</c> must show nothing; a worktree that is not clean is never handed to a model, and the job fails with the sentence.</para>
/// </remarks>
internal static class CoderRestore
{
    private const int NamedPaths = 8;

    /// <param name="commit">The full id of the commit to go back to: the last one coai made, or the base commit when it has made none.</param>
    /// <returns>Empty when the tree is at <paramref name="commit"/> and git status shows nothing; otherwise the sentence that says why not.</returns>
    public static async Task<string> RestoreAsync(GitHistory git, CoderTree tree, string commit, CancellationToken ct)
    {
        if (!GitHistory.IsCommitish(commit))
        {
            return $"'{commit}' is not a commit id, so the worktree was not put back and no other model was started on it";
        }

        foreach (var (what, args) in new[] { ("git reset --hard", new[] { "reset", "--hard", "--quiet", commit }), ("git clean", ["clean", "-ffdxq"]) })
        {
            var done = await git.InCoderTreeAsync(tree, args, ct);
            if (!done.Ran || !done.Ok)
            {
                return Said(what, done);
            }
        }

        var head = await CoderCommits.HeadAsync(git, tree, ct);
        if (head.Failure is { } failure)
        {
            return $"the worktree could not be read after it was put back: {failure.Reason}";
        }

        if (!string.Equals(head.Head, commit, StringComparison.Ordinal))
        {
            return $"the worktree is at {head.Head}, not at {commit}, after it was put back";
        }

        var status = await git.InCoderTreeAsync(tree, ["--no-optional-locks", "status", "--porcelain=v1", "-z", "--untracked-files=all", "--ignored"], ct);
        if (!status.Ran || !status.Ok)
        {
            return Said("git status after the restore", status);
        }

        if (status.Out.Length == 0)
        {
            return string.Empty;
        }

        var left = status.Out.Split('\0', StringSplitOptions.RemoveEmptyEntries).Take(NamedPaths).Select(entry => entry.Length > 3 ? entry[3..] : entry);

        return $"git status still shows changes after the worktree was put back ({string.Join(", ", left)}), so no other model was started on it";
    }

    private static string Said(string doing, GitAnswer answer) =>
        answer.Refusal.Length > 0 ? answer.Refusal : $"{doing} failed{(answer.Ran ? string.Empty : " or did not finish in time")}, so the worktree was not put back and no other model was started on it";
}
