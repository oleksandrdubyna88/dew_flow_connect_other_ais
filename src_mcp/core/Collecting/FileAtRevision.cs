namespace CoaiMcp.Core.Collecting;

/// <summary>
/// One file as it was at the commit the reviewers read — what <c>--file-at</c> answers — or why it
/// cannot be read.
/// </summary>
/// <remarks>
/// <para><b>A VIEW and never a payload</b>, on <see cref="RealMethod"/>'s precedent and for its
/// reasons: nothing here is stored, nothing here is sent, and nothing here is reachable from
/// <c>UploadRun.Wire</c>, which is a function of <see cref="StoredPair"/> alone. The review page asks
/// for it when a person presses <i>Open at &lt;sha&gt;</i>, and the extension shows the text in a
/// read-only document of its own scheme.</para>
/// <para><b>The blob, not the checkout.</b> <c>head_sha</c> is orphaned 55.7 % of the time (72 % in
/// this repository, 100 % in every worktree) and 99.6 % of orphaned blobs still read — so the revision
/// a person wants is usually unreachable as a REF and almost always readable as an OBJECT, and this is
/// the object. A file deleted or renamed since is still here, which is the whole point.</para>
/// <para><see cref="Reason"/> is empty exactly when <see cref="Text"/> is the file; otherwise it is
/// one of <see cref="FileAtReason"/>, and each is a different fact a person acts on differently.</para>
/// </remarks>
/// <param name="FindingId">The pair asked about.</param>
/// <param name="Sha">The commit read, as the pair stores it — echoed so the document can name it.</param>
/// <param name="Path">The path read, repository-relative, as the pair stores it.</param>
/// <param name="Reason">Empty when the file is shown; otherwise a <see cref="FileAtReason"/>.</param>
/// <param name="Text">The file, verbatim, as it was at that commit.</param>
public sealed record FileAtRevision(
    long FindingId = 0,
    string Sha = "",
    string Path = "",
    string Reason = "",
    string Text = "");

/// <summary>
/// Why a file could not be read at its revision — spelled once, and where the collector or the
/// real-method view already has a word for the same fact, THAT word.
/// </summary>
/// <remarks>
/// The one new word is <see cref="PathRefused"/>: a stored path that is not repository-relative —
/// traversal, an absolute or drive-qualified form, a NUL — is refused before git is asked anything,
/// and neither the collector nor <c>--real-method</c> has a word for it because neither checks. Not
/// added to <see cref="SkipReason"/>: that vocabulary is what a collect run WRITES to a column and
/// the funnel counts, and a word the collector never writes would be a bucket that is always zero.
/// </remarks>
public static class FileAtReason
{
    /// <summary>The database has no pair for that finding — recollected under the page, or never there.</summary>
    public const string PairNotFound = RealMethodReason.PairNotFound;

    /// <summary>The recorded checkout is gone, or is not a git repository any more.</summary>
    public const string RepoPathMissing = RealMethodReason.RepoPathMissing;

    /// <summary>git itself failed — a timeout, a permission, a broken object. We learned nothing.</summary>
    public const string GitFailed = RealMethodReason.GitFailed;

    /// <summary>The commit object is not in the repository at all — or the stored sha is not one.</summary>
    public const string CommitUnreachable = RealMethodReason.CommitUnreachable;

    /// <summary>The path did not exist at that commit — it moved, or was added later.</summary>
    public const string FileNotInCommit = RealMethodReason.FileNotInCommit;

    /// <summary>The stored path is not repository-relative, so nothing was asked of git.</summary>
    public const string PathRefused = "path_refused";
}
