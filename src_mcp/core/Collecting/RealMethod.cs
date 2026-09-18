namespace CoaiMcp.Core.Collecting;

/// <summary>
/// One side of a pair as it really was — the method's own text, un-anonymised — or why it cannot be shown.
/// </summary>
/// <remarks>
/// <para><b>This record is a VIEW and never a payload.</b> It is what <c>--real-method</c> answers
/// to the review page so a person can read the method they are deciding about. Nothing here is
/// stored, nothing here is sent, and nothing here is reachable from <c>UploadRun.Wire</c>, which is a
/// function of <see cref="StoredPair"/> alone. <c>TheRealTextIsOnTheModesRecord_AndOnNothingTheSendTouches</c>
/// pins that on the types.</para>
/// <para><b>A side that cannot be shown says WHY, in the collector's own vocabulary.</b> The commit
/// being absent, the file not being in it, the line inside no named function, two functions of one
/// name and a function renamed away are five different facts a person acts on differently, and
/// collapsing them into "unavailable" would send them to the wrong place. The reason is empty
/// exactly when <see cref="Source"/> is the real text.</para>
/// </remarks>
/// <param name="Reason">Empty when the method is shown; otherwise a <see cref="RealMethodReason"/>.</param>
/// <param name="Source">The function's own text, verbatim, as it was at that commit.</param>
/// <param name="ClassName">
/// The innermost type the function sits in — a class, struct, record or interface — or empty for a
/// top-level function and for a grammar with no type node around it. Never guessed.
/// </param>
/// <param name="Kind">The grammar's own word for the function, e.g. <c>method_declaration</c>.</param>
/// <param name="StartLine">1-based and inclusive, as a finding's line is.</param>
/// <param name="EndLine">1-based and inclusive.</param>
public sealed record MethodSide(
    string Reason = "",
    string Source = "",
    string ClassName = "",
    string Kind = "",
    int StartLine = 0,
    int EndLine = 0)
{
    /// <summary>A side that cannot be shown, for this reason.</summary>
    public static MethodSide Unavailable(string reason) => new(Reason: reason);
}

/// <summary>
/// The real method behind one pair, at both of its commits — what <c>--real-method</c> answers.
/// </summary>
/// <remarks>
/// <para><b>A record of its own, and not a wider <see cref="StoredPair"/>, <c>ReviewPair</c> or
/// <c>NormalizeAnswer</c>.</b> The first is the upload's type. The second is the page's row and is
/// read in bulk. The third's whole contract is the anonymised skeleton — it carries <c>Leaks</c>,
/// documented as "NON-EMPTY IS A DEFECT IN THIS CODE" — and putting real source on it would route the
/// one thing the normaliser exists to remove through the type whose job is to prove it was removed.
/// Story 2.1 of the review-page plan took a new record for the page for the same reason, and this
/// story takes one for the view.</para>
/// <para><b>The two sides are independent.</b> The BEFORE side is found by LINE at the commit the
/// reviewers read; the AFTER side is found by NAME at the commit the fix was found in, and the name is
/// the one the pair already stores — so a head commit that has since been pruned still leaves the
/// after side readable, and the other way round. 55.7 % of recorded head commits are orphaned and
/// 99.6 % of those still read (measured for the corpus plan), so most rows answer on both sides.</para>
/// </remarks>
/// <param name="FindingId">The pair asked about.</param>
/// <param name="Language">What the normaliser decided the file is in; empty when nothing was read.</param>
/// <param name="Name">The function's name, as the pair stores it. Local metadata; never uploaded.</param>
/// <param name="Reason">
/// A reason that stops BOTH sides — the pair is not in the database, the checkout is gone, the
/// language is not read, git did not answer — or empty when the sides speak for themselves.
/// </param>
public sealed record RealMethod(
    long FindingId = 0,
    string Language = "",
    string Name = "",
    string Reason = "")
{
    /// <summary>The method at the commit the reviewers read, found by line.</summary>
    public MethodSide Before { get; init; } = new();

    /// <summary>The method at the commit the fix was found in, found by name.</summary>
    public MethodSide After { get; init; } = new();
}

/// <summary>
/// Why a real method, or one side of it, could not be shown — spelled once.
/// </summary>
/// <remarks>
/// <para>Where the collector has a word for the same fact, this IS that word — a constant of a
/// constant, so the page meets one spelling for one thing whether it came from a collect run or from
/// this view. The two that are new are new because the collector never meets them: it never asks
/// about a pair that is not there, and its head commit is checked before anything else, so "the
/// commit is absent" is <see cref="SkipReason.HeadShaUnreachable"/> for it and a fact about EITHER
/// commit here.</para>
/// <para>Not added to <see cref="SkipReason"/> itself: that vocabulary is what a collect run WRITES
/// to a column and the funnel counts, and a word the collector never writes would be a bucket that
/// is always zero.</para>
/// </remarks>
public static class RealMethodReason
{
    /// <summary>The database has no pair for that finding — recollected under the page, or never there.</summary>
    public const string PairNotFound = "pair_not_found";

    /// <summary>The commit object is not in the repository at all — on either side.</summary>
    public const string CommitUnreachable = "commit_unreachable";

    /// <summary>Not C#, TypeScript or JavaScript.</summary>
    public const string LanguageUnsupported = SkipReason.LanguageUnsupported;

    /// <summary>The recorded checkout is gone, or is not a git repository any more.</summary>
    public const string RepoPathMissing = SkipReason.RepoPathMissing;

    /// <summary>git itself failed — a timeout, a permission, a broken object. We learned nothing.</summary>
    public const string GitFailed = SkipReason.GitFailed;

    /// <summary>The path did not exist at that commit.</summary>
    public const string FileNotInCommit = SkipReason.FileNotInCommit;

    /// <summary>The line lands inside no NAMED function — a field, a using block, a lambda.</summary>
    public const string SymbolNotResolved = SkipReason.SymbolNotResolved;

    /// <summary>More than one function of that name is there, so the name is not an identity.</summary>
    public const string SymbolAmbiguous = SkipReason.SymbolAmbiguous;

    /// <summary>No function of that name is in the commit any more.</summary>
    public const string SymbolGone = SkipReason.SymbolGone;
}
