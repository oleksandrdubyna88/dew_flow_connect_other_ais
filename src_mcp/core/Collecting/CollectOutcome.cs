namespace CoaiMcp.Core.Collecting;

/// <summary>What became of one candidate.</summary>
/// <remarks>
/// <para><b><see cref="Skipped"/> and <see cref="Failed"/> are not the same thing, and keeping them
/// apart is the point of having three states.</b> Skipped means the DATA cannot support a case — a
/// language nobody parses, a commit no ref reaches, a fix that is not in this repository. Expected;
/// the rate is a measurement, and the measurement is what decides whether the fix resolution moves to
/// a forge API. Failed means OUR CODE did not do its job: git timed out, a file could not be read, a
/// skeleton came back with a name in it.</para>
/// <para>Folding the second into the first is how an infrastructure failure hides for a month as a
/// slightly worse skip rate. (Plan round, codex.)</para>
/// </remarks>
public enum CollectState
{
    /// <summary>Nothing has looked at it.</summary>
    Pending,

    /// <summary>Both versions of the method were read, and the fix commit is known.</summary>
    Collected,

    /// <summary>The data cannot support a case. Expected; measure the rate.</summary>
    Skipped,

    /// <summary>Our code did not do its job. Should be ~0, and is never a property of the input.</summary>
    Failed,
}

/// <summary>
/// Why a candidate was skipped or failed, as a code the funnel can count.
/// </summary>
/// <remarks>
/// <para>Constants rather than an enum because they are written to a database column and read back by
/// tools that are not this one; a string that means the same thing in SQL, in JSON and in a panel
/// beats three spellings of one idea.</para>
/// <para><b>Each is attributed to the STAGE that produced it</b>, and the funnel is reported per
/// stage for a reason measured before any of this was written: <c>language_unsupported</c> would
/// otherwise mask <c>fix_commit_not_found</c>, and only the second has a decision attached to it.</para>
/// </remarks>
public static class SkipReason
{
    // The guard, before anything is parsed or read.

    /// <summary>A scratch directory — 30 % of raw candidates, and none of them has a history.</summary>
    public const string RepoPathTransient = "repo_path_transient";

    /// <summary>The path is gone, or is not a git repository any more.</summary>
    public const string RepoPathMissing = "repo_path_missing";

    /// <summary>The commit object is not in this repository at all.</summary>
    public const string HeadShaUnreachable = "head_sha_unreachable";

    /// <summary>
    /// Present, but no ref reaches it — squash-merge, or a rebase, or a deleted branch.
    /// </summary>
    /// <remarks>
    /// 55.7 % of measured candidates, and NOT the end of the road: the objects survive, so the method
    /// at that commit is still readable and a bounded interval inside the same session can still be
    /// walked. This code is recorded only when no interval is available either.
    /// </remarks>
    public const string HeadShaOrphaned = "head_sha_orphaned";

    // Locating the method.

    /// <summary>The path did not exist at that commit.</summary>
    public const string FileNotInCommit = "file_not_in_commit";

    /// <summary>Not C#, TypeScript or JavaScript.</summary>
    public const string LanguageUnsupported = "language_unsupported";

    /// <summary>The line lands inside no function — a field, a using block, a blank line.</summary>
    public const string SymbolNotResolved = "symbol_not_resolved";

    // Walking to the fix.

    /// <summary>No commit in the interval changed the method.</summary>
    public const string FixCommitNotFound = "fix_commit_not_found";

    /// <summary>
    /// The method changed, but only in ways a skeleton cannot see — a rename, a reformat.
    /// </summary>
    /// <remarks>
    /// The walk continues past these rather than stopping, so this is recorded only when the interval
    /// ends without a structural change. Treating the first textual difference as the fix would file a
    /// variable rename as a defect's cure. (Plan round, codex.)
    /// </remarks>
    public const string MethodUnchanged = "method_unchanged";

    /// <summary>More than one function of that name is there, so the name is not an identity.</summary>
    /// <remarks>
    /// An overload set shares a name. Comparing the first match would record an unrelated overload's
    /// change as this defect's fix, with a commit sha to prove it — so an ambiguous name skips
    /// instead. Wrong-but-skipped is the safe direction; wrong-but-collected is not.
    /// </remarks>
    public const string SymbolAmbiguous = "symbol_ambiguous";

    /// <summary>The method is not in the later commit under that name any more.</summary>
    public const string SymbolGone = "symbol_gone";

    /// <summary>No ref descends from the commit, so there is no history to walk forward through.</summary>
    /// <remarks>
    /// Distinct from <see cref="HeadShaOrphaned"/>: that one says nothing reaches the commit, this one
    /// says nothing continues from it. A fallback that accepted ANY ref as proof of reachability and
    /// then walked a different one would attribute an unrelated descendant's edit as the fix.
    /// (Plan round, codex.)
    /// </remarks>
    public const string HistoryUnavailable = "history_unavailable";

    // Not a skip.

    /// <summary>git itself failed — a timeout, a permission, a broken object.</summary>
    /// <remarks>
    /// Carried here for the vocabulary's sake, but it is only ever written with
    /// <see cref="CollectState.Failed"/>: an infrastructure failure is not a fact about the candidate.
    /// </remarks>
    public const string GitFailed = "git_failed";

    /// <summary>A skeleton came back carrying a word that is neither placeholder nor vocabulary.</summary>
    /// <remarks>
    /// Always <see cref="CollectState.Failed"/>. A normaliser that lets a name through is a defect in
    /// our code, and recording it as a property of the data is how it would go unnoticed.
    /// </remarks>
    public const string NotAnonymous = "not_anonymous";
}

/// <summary>What one candidate came to, and the evidence for it.</summary>
/// <param name="Reasons">
/// Empty when collected. More than one is legal — a guard can refuse for two reasons at once — and
/// they are stored comma-joined, as <c>providers</c> already is.
/// </param>
/// <param name="FixSha">The commit that changed the method, or empty.</param>
public sealed record CollectOutcome(
    CollectState State,
    IReadOnlyList<string> Reasons,
    string FixSha = "",
    string SymbolName = "",
    string SkeletonBefore = "",
    string SkeletonAfter = "")
{
    /// <summary>The data could not support a case, for these reasons.</summary>
    public static CollectOutcome Skip(params string[] reasons) => new(CollectState.Skipped, reasons);

    /// <summary>Our code did not do its job.</summary>
    public static CollectOutcome Fail(params string[] reasons) => new(CollectState.Failed, reasons);

    /// <summary>The reasons as the database stores them.</summary>
    public string Reason => string.Join(',', Reasons);
}
