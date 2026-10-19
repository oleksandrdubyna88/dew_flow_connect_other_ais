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
/// <para>A Vibe disk row asks the same question of every repository it finds (D9 of
/// <c>todo/PLAN_vibe_reads_a_folder_of_repositories.md</c>): a linked worktree is a checkout whose git dir is not its
/// common dir, and the key of that common dir says which main checkout it belongs to.</para>
/// </remarks>
public static class RepositoryIdentity
{
    /// <summary>What git is asked: the common dir, absolute.</summary>
    public static IReadOnlyList<string> CommonDirArgs { get; } = ["rev-parse", "--path-format=absolute", "--git-common-dir"];

    /// <summary>
    /// How this machine's paths compare — case-blind on Windows, exact elsewhere — the one answer the keys a person's disk is
    /// compared by use (the snapshot's own table, a row's copies).
    /// </summary>
    public static StringComparison PathComparison { get; } =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>
    /// The identity KEY, not a path to ask the filesystem about: separators one way, no trailing slash, case
    /// folded. Nothing in this form is ever handed to <c>Directory.Exists</c>.
    /// </summary>
    /// <param name="comparison">
    /// How the path compares. The default, <see cref="StringComparison.OrdinalIgnoreCase"/>, is the key the stores already hold —
    /// unchanged. Anything else is a case-SENSITIVE path (Linux, macOS' default volume), where folding the case or swapping a
    /// backslash would make two different folders one: only a trailing slash is dropped, and the keys are then compared exactly.
    /// </param>
    public static string Normalised(string commonDir, StringComparison comparison = StringComparison.OrdinalIgnoreCase) =>
        comparison == StringComparison.OrdinalIgnoreCase
            ? commonDir.Trim().Replace('\\', '/').TrimEnd('/').ToLowerInvariant()
            : commonDir.Trim().TrimEnd('/');

    /// <summary>
    /// The key of an absolute folder git named, on THIS machine's rules (<see cref="PathComparison"/>): the full path, no trailing
    /// separator, then <see cref="Normalised(string, StringComparison)"/>. Empty for an empty path — "git did not say".
    /// </summary>
    public static string KeyOf(string absolutePath) =>
        string.IsNullOrWhiteSpace(absolutePath)
            ? string.Empty
            : Normalised(Path.TrimEndingDirectorySeparator(Path.GetFullPath(absolutePath.Trim())), PathComparison);
}
