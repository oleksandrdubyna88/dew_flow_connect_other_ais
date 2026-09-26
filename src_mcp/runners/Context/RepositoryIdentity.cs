namespace CoaiMcp.Runners.Context;

/// <summary>
/// Which REPOSITORY a checkout belongs to, as against which directory it is — the git common dir.
/// </summary>
/// <remarks>
/// <para>A worktree is registered in exactly one object store, so every linked worktree of a repository and
/// every spelling of one checkout answer the same common dir. The review trees learnt this first
/// (<c>ReviewWorktrees.CommonDirAsync</c>); the consultation cadence needs the same answer, because a plan
/// built in a worktree is still the repository's plan (<c>research/PLAN_consult_on_a_cadence.md</c>, the
/// epic-1-3 consultation, point 8). One set of arguments and one normalisation, so the two cannot drift
/// (epic 2's plan round, gemini) — and it lives HERE, the neutral side, with the review trees delegating
/// to it, so neither capability depends on the other (epic 2's code round, codex).</para>
/// </remarks>
public static class RepositoryIdentity
{
    /// <summary>What git is asked: the common dir, absolute.</summary>
    public static IReadOnlyList<string> CommonDirArgs { get; } = ["rev-parse", "--path-format=absolute", "--git-common-dir"];

    /// <summary>
    /// The identity KEY, not a path to ask the filesystem about: separators one way, no trailing slash, case
    /// folded. Nothing in this form is ever handed to <c>Directory.Exists</c>.
    /// </summary>
    public static string Normalised(string commonDir) => commonDir.Trim().Replace('\\', '/').TrimEnd('/').ToLowerInvariant();
}
