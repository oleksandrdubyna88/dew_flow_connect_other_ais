using CoaiMcp.Runners.Collecting;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>
/// The commit a coder's worktree starts at, settled when <c>start_role</c> is called (epoch 3 E2 s3 t4): the <c>baseSha</c> the caller named after it is shown to be a commit of the repository, or — when it named none — the
/// HEAD of the repository at THAT moment. Written into <c>request.json</c>, so a commit made after the start is never the coder's base, and so every later reader of the job (the plan binding of story s5 among them) reads one fact.
/// </summary>
/// <remarks>An argument that would be silently ignored is a trap (<c>PlanInputs</c> says the same): a role other than the coder has no base commit, and naming one is refused.</remarks>
internal static class CoderBase
{
    /// <summary>The request with its <c>BaseSha</c> settled, or the sentence that refuses the start. The request is unchanged for a role that is not the coder when none was named.</summary>
    public static async Task<(RoleJobRequest Request, string Refusal)> SettledAsync(RoleJobRequest request, string? baseSha, CancellationToken ct = default)
    {
        var named = baseSha?.Trim() ?? string.Empty;
        if (request.Role != RoleDescriptors.Coder)
        {
            return named.Length == 0 ? (request, string.Empty) : (request, $"baseSha is an argument of the {RoleDescriptors.Coder} role only: role '{request.Role}' has no base commit");
        }

        var git = new GitHistory(new ProcessLauncher());

        return named.Length == 0 ? await HeadAsync(git, request, ct) : await NamedAsync(git, request, named, ct);
    }

    private static async Task<(RoleJobRequest Request, string Refusal)> HeadAsync(GitHistory git, RoleJobRequest request, CancellationToken ct)
    {
        var looked = await git.RootAndHeadAsync(request.RepoPath, ct);

        return !looked.Ran
            ? (request, "git did not answer in time while the HEAD of repoPath was read; try again in a moment")
            : looked.Ok && looked.Lines.Count >= 2 && GitHistory.IsCommitish(looked.Lines[1])
                ? (request with { BaseSha = looked.Lines[1] }, string.Empty)
                : (request, $"repoPath '{request.RepoPath}' has no commit at HEAD, so the coder has no base commit; commit first or name one with baseSha");
    }

    private static async Task<(RoleJobRequest Request, string Refusal)> NamedAsync(GitHistory git, RoleJobRequest request, string named, CancellationToken ct)
    {
        if (!GitHistory.IsCommitish(named))
        {
            return (request, "baseSha must be a full 40-character commit id; a branch name or a short id is not accepted");
        }

        var found = await git.HasCommitAsync(request.RepoPath, named, ct);

        return !found.Ran
            ? (request, "git did not answer in time while baseSha was looked up; try again in a moment")
            : found.Ok
                ? (request with { BaseSha = named.ToLowerInvariant() }, string.Empty)
                : (request, $"baseSha {named} is not a commit of the repository at '{request.RepoPath}'");
    }
}
