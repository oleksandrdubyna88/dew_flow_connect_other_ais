namespace CoaiMcp.Store;

/// <summary>
/// A pair as the REVIEW PAGE reads it: the stored pair, where the defect was, and what the reviewers
/// said about it.
/// </summary>
/// <remarks>
/// <para><b>A record of its own, and deliberately not a wider <see cref="Core.Collecting.StoredPair"/>.</b>
/// The send's projection (<c>UploadRun.Wire</c>) is a function OF <c>StoredPair</c>, and
/// <c>OnlyThreeFieldsLeaveTests</c> constructs one by name to prove what that mapping leaves behind.
/// Widening that type would route a repository path, a file and the reviewers' prose through the very
/// type the upload reads — an edit that looks like a simplification, which is the one that test exists
/// to refuse. So the page has its own row, <see cref="RoundsDb.Sendable"/> keeps answering the narrow
/// one, and nothing declared here is reachable from the send.
/// <c>TheWhereAndTheWhyAreOnThePagesRecord_AndNeverOnTheSends</c> pins that on the types.</para>
/// <para><b>Nothing here is anonymous, and nothing here leaves the machine.</b> The two skeletons carry
/// the zero-knowledge guarantee; the rest is a person's own database read back to them so they can
/// decide about a method they can actually place. The boundary that matters is the upload, and this
/// record is on the near side of it.</para>
/// <para><b>Two revisions, named apart.</b> <paramref name="HeadSha"/> is the commit the reviewers
/// read: the BEFORE skeleton is the method at that commit, at <paramref name="File"/>:<paramref name="Line"/>.
/// <paramref name="FixSha"/> is the commit the collector found the fix in
/// (<c>Collector.LocateThenWalkAsync</c> normalises the AFTER side from <c>touched.Sha</c>): the AFTER
/// skeleton is the method THERE. A page that labelled both with one sha would be confidently wrong
/// about one of them, and a complexity number "of the method" has to say which method.</para>
/// <para>Every text column is empty rather than absent when the database does not know — a round or
/// session row that has gone, a finding recorded before the collector columns existed — so the
/// extension's reader, which defaults an absent field to the empty string anyway, meets one shape
/// from a server of any age.</para>
/// </remarks>
/// <param name="Keep">-1 nobody has looked, 0 dropped, 1 kept — <see cref="Core.Collecting.Keep"/>.</param>
/// <param name="RepoPath">The checkout the round reviewed, as the session recorded it. Empty when unknown.</param>
/// <param name="HeadSha">The commit the reviewers read; the BEFORE skeleton is the method there.</param>
/// <param name="FixSha">The commit the fix was found in; the AFTER skeleton is the method there.</param>
/// <param name="File">The finding's path, relative to <paramref name="RepoPath"/>, at <paramref name="HeadSha"/>.</param>
/// <param name="Line">The finding's line at <paramref name="HeadSha"/>; 0 when none was recorded.</param>
/// <param name="Why">The reviewers' cause, verbatim. Empty when none was recorded — never invented.</param>
/// <param name="Fix">The reviewers' proposed fix, verbatim. Empty when none was recorded.</param>
public sealed record ReviewPair(
    long FindingId,
    string SymbolName,
    string Language,
    string SkeletonBefore,
    string SkeletonAfter,
    int Keep,
    string Severity,
    string Category,
    string Title,
    string RepoPath,
    string HeadSha,
    string FixSha,
    string File,
    int Line,
    string Why,
    string Fix);
