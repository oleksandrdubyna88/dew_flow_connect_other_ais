using CoaiMcp.Core.Collecting;

namespace CoaiMcp.Runners.Collecting;

/// <summary>A file's text at a commit, or the reason there is none — one shape for every reader of one.</summary>
/// <param name="Text">The file, verbatim; empty when <paramref name="Reason"/> says why.</param>
/// <param name="Reason">Empty when the file was read; otherwise the collector's own word for what stopped it.</param>
public readonly record struct Reading(string Text, string Reason)
{
    public static Reading Of(string text) => new(text, string.Empty);

    public static Reading Not(string reason) => new(string.Empty, reason);
}

/// <summary>
/// One file at one commit — with "the commit is gone" and "the file was not in it" kept apart, and
/// git's own failure apart from both.
/// </summary>
/// <remarks>
/// <para>It was a private pair of methods inside <see cref="RealMethodReader"/>; opening a file at its
/// revision (story 3.1 of the review-page plan) needed the same three-way answer, and the reuse rule's
/// second move — extract the shared half — is cheaper than a second copy that drifts. Both readers now
/// answer the same reason for the same fact.</para>
/// <para>The commit is checked before the file so that <c>commit_unreachable</c> and
/// <c>file_not_in_commit</c> are two answers: <c>git show sha:path</c> alone exits 128 for both.
/// <c>cat-file -e sha^{commit}</c> is an OBJECT lookup, not a ref walk, so an orphaned commit —
/// squash-merged, its branch deleted — is found.</para>
/// </remarks>
public static class CommittedFile
{
    public static async Task<Reading> ReadAsync(
        GitHistory git, string repoPath, string sha, string path, CancellationToken ct = default)
    {
        var present = await git.HasCommitAsync(repoPath, sha, ct);
        if (!present.Ran)
        {
            return Reading.Not(RealMethodReason.GitFailed);
        }

        if (!present.Ok)
        {
            return Reading.Not(RealMethodReason.CommitUnreachable);
        }

        return await ReadAtAsync(git, repoPath, sha, path, ct);
    }

    /// <summary>
    /// The file at a commit the caller already knows is there — one process on the happy path.
    /// </summary>
    /// <remarks>
    /// The source resolver (plan §4.9) reads every file at ONE pinned head the round resolved itself,
    /// so checking that commit again per file would be a process spent on a question already answered.
    /// The reuse rule's first move — widen the existing thing — rather than a second copy of the read
    /// and its discriminator; <see cref="ReadAsync"/> is this after its commit check.
    /// </remarks>
    public static async Task<Reading> ReadAtAsync(
        GitHistory git, string repoPath, string sha, string path, CancellationToken ct = default)
    {
        var file = await git.FileAtAsync(repoPath, sha, path, ct);
        if (!file.Ran)
        {
            return Reading.Not(RealMethodReason.GitFailed);
        }

        return file.Ok ? Reading.Of(file.Out) : await WhyNotAsync(git, repoPath, sha, path, ct);
    }

    /// <summary>
    /// Why a read failed: the path was not there, or git could not read one that was.
    /// </summary>
    /// <remarks>
    /// <para>Asked only on the failure path, so the ordinary read still costs one process. Every
    /// non-zero exit used to become <c>file_not_in_commit</c>, which told a person the file had
    /// never been at that path when the truth might be a permission error or a broken object — a
    /// lie rather than a gap, and the page then suppresses the action for good rather than letting
    /// them press again. (Code round, codex.)</para>
    /// <para>It asks git rather than reading its prose: <c>cat-file -e sha:path</c> answers the
    /// narrow question, and git's wording changes between versions while its exit codes do not.</para>
    /// </remarks>
    private static async Task<Reading> WhyNotAsync(
        GitHistory git, string repoPath, string sha, string path, CancellationToken ct)
    {
        var there = await git.HasPathAsync(repoPath, sha, path, ct);

        return there.Ran && there.Ok
            ? Reading.Not(RealMethodReason.GitFailed)
            : Reading.Not(RealMethodReason.FileNotInCommit);
    }
}
