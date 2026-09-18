using CoaiMcp.Core.Collecting;

namespace CoaiMcp.Runners.Collecting;

/// <summary>Where one pair's file is: the three coordinates the pair already stores.</summary>
/// <param name="RepoPath">The checkout the session recorded — the store's own word, never a caller's.</param>
/// <param name="Sha">The commit the reviewers read.</param>
/// <param name="Path">The finding's path at that commit, relative to the repository root.</param>
public sealed record FilePlace(string RepoPath, string Sha, string Path);

/// <summary>
/// Reads one pair's file back out of git as it was at the commit the reviewers read.
/// </summary>
/// <remarks>
/// <para><b>Every coordinate comes from the row, and every one is checked before git sees it.</b>
/// The path is validated LEXICALLY — traversal, an absolute or drive-qualified form, a NUL — and
/// never against today's filesystem: a file deleted or renamed since the recorded revision has no
/// current path to canonicalise, and canonicalising would refuse exactly the blob this exists to
/// read. The symlink question belongs to git, whose object spec resolves nothing outside the object
/// database. The sha goes through <see cref="GitHistory"/>'s one validator, which is also what keeps
/// a value beginning with <c>--</c> from becoming an option.</para>
/// <para>The lexical refusals come FIRST, before the repository is probed, so a malformed row costs
/// no process at all — <c>APathThatIsNotRepositoryRelative_NeverReachesGit</c> asserts the launcher
/// is never called.</para>
/// <para>Nothing here writes: no row, no file, no column.</para>
/// </remarks>
public sealed class FileAtReader(GitHistory git)
{
    /// <summary>The file at that revision, or why it cannot be had — always as data.</summary>
    public async Task<FileAtRevision> ReadAsync(long findingId, FilePlace place, CancellationToken ct = default)
    {
        var refused = RefusedLexically(place);
        if (refused.Length > 0)
        {
            return Answer(findingId, place, Reading.Not(refused));
        }

        var repository = await git.IsRepositoryAsync(place.RepoPath, ct);
        if (!repository.Ran)
        {
            return Answer(findingId, place, Reading.Not(FileAtReason.GitFailed));
        }

        if (!repository.Ok)
        {
            return Answer(findingId, place, Reading.Not(FileAtReason.RepoPathMissing));
        }

        return Answer(findingId, place, await CommittedFile.ReadAsync(git, place.RepoPath, place.Sha, place.Path, ct));
    }

    /// <summary>
    /// What the row itself rules out, before any process: a path that is not repository-relative, or
    /// a sha that is not one.
    /// </summary>
    /// <remarks>
    /// A malformed sha answers <c>commit_unreachable</c> — the word <c>--real-method</c> answers for
    /// the same row through <see cref="GitHistory.HasCommitAsync"/>'s own refusal — so the page meets
    /// one spelling for one row; here it is merely decided before the repository is probed.
    /// </remarks>
    private static string RefusedLexically(FilePlace place)
    {
        if (!GitHistory.IsRepoRelative(place.Path))
        {
            return FileAtReason.PathRefused;
        }

        return GitHistory.IsCommitish(place.Sha) ? string.Empty : FileAtReason.CommitUnreachable;
    }

    private static FileAtRevision Answer(long findingId, FilePlace place, Reading reading) =>
        new(findingId, place.Sha, place.Path, reading.Reason, reading.Text);
}
