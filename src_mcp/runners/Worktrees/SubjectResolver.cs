using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Runners.Worktrees;

/// <summary>
/// Turns a repository, a branch and a base into an attestation both sides can recompute.
/// </summary>
/// <remarks>
/// <para>Every value here is git's own: the base and head commits the round is pinned to, the head
/// commit's TREE — git's content hash of every reviewed byte — and the repository's common
/// directory, which is what makes "the same path" and "the same repository" different claims. A
/// caller holding the same checkout runs the same four commands and gets the same answer, so the
/// attestation is checkable rather than believed.</para>
/// <para>Separate from <see cref="WorktreeManager"/> because it takes nothing and leases nothing: it
/// reads four ids and returns a value, which is a thing a unit test can hold still.</para>
/// </remarks>
public sealed class SubjectResolver(IProcessLauncher launcher)
{
    /// <summary>
    /// Resolves what a round over <paramref name="branch"/> since <paramref name="baseRef"/> reads.
    /// </summary>
    /// <exception cref="WorktreeException">
    /// Any id that will not resolve. A partial attestation is refused rather than returned with a
    /// blank in it: the whole value of this record is that nothing in it was left to a default.
    /// </exception>
    public async Task<SubjectAttestation> ResolveAsync(string repoPath, string branch, string baseRef)
    {
        var identity = await OneLine(repoPath, "the repository", "rev-parse", "--absolute-git-dir");
        var headSha = await OneLine(repoPath, $"branch '{branch}'", "rev-parse", "--verify", $"{branch}^{{commit}}");
        var baseSha = await OneLine(repoPath, $"base '{baseRef}'", "rev-parse", "--verify", $"{baseRef}^{{commit}}");
        // The tree of the COMMIT, not of the working directory: it is what the reviewers' pinned
        // worktree will contain, and it changes whenever any reviewed byte does.
        var treeSha = await OneLine(repoPath, $"the tree of {headSha}", "rev-parse", "--verify", $"{headSha}^{{tree}}");

        return new SubjectAttestation(Normalise(identity), baseRef, baseSha, headSha, treeSha);
    }

    /// <summary>
    /// One id, or a refusal naming what would not resolve.
    /// </summary>
    /// <remarks>
    /// Trimmed and required to be a single line: git answers these with exactly one, and anything
    /// else means the command did something other than what was asked.
    /// </remarks>
    private async Task<string> OneLine(string repoPath, string what, params string[] args)
    {
        var result = await launcher.RunAsync(new ProcessRequest("git", args, repoPath)
        {
            Timeout = TimeSpan.FromMinutes(2),
        });
        var value = result.StdOut.Trim();
        if (result.ExitCode != 0 || value.Length == 0 || value.Contains('\n'))
        {
            throw new WorktreeException(
                "rev-parse",
                $"cannot resolve {what} in '{repoPath}': {result.StdErr.Trim()}");
        }

        return value;
    }

    /// <summary>
    /// The git directory as one comparable string.
    /// </summary>
    /// <remarks>
    /// Separators and trailing slash normalised, and lower-cased — this is compared against a value
    /// stored on a previous run of a Windows server, where the same directory reaches us spelled
    /// several ways. It identifies the repository, so a checkout replaced at the same path is a
    /// different subject and says so.
    /// </remarks>
    private static string Normalise(string gitDir) =>
        gitDir.Replace('\\', '/').TrimEnd('/').ToLowerInvariant();
}
