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

        return Read(await git.FileAtAsync(repoPath, sha, path, ct));
    }

    /// <summary>What `git show` said about a file, as a reading: text, absent, or git did not run.</summary>
    private static Reading Read(GitAnswer file) =>
        file.Ran
            ? file.Ok ? Reading.Of(file.Out) : Reading.Not(RealMethodReason.FileNotInCommit)
            : Reading.Not(RealMethodReason.GitFailed);
}
