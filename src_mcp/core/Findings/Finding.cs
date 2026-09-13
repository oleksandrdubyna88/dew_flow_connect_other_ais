using System.Collections.Immutable;

namespace CoaiMcp.Core.Findings;

/// <summary>Only <see cref="Blocking"/> and <see cref="Major"/> ever gate a round.</summary>
public enum Severity
{
    Blocking,
    Major,
    Minor,
    Nit,
}

/// <summary>
/// What a finding is ABOUT. The first six describe code; the last four describe a document.
/// </summary>
/// <remarks>
/// <para><b>One list, not one per stage.</b> A document round may answer <c>Security</c> or
/// <c>Reliability</c> — a policy has both — and a code round answering <c>Clarity</c> is a
/// legitimate remark about a name or a comment. Two lists would be two things to keep in step, which
/// is the drift <c>FindingSchemaTests</c> exists because of.</para>
/// <para><b>Four, not fourteen.</b> A list a reviewer has to think about is a list reviewers answer
/// inconsistently, and dedup counts agreement across vendors.</para>
/// </remarks>
public enum Category
{
    Architecture,
    Security,
    Reliability,
    Performance,
    Ux,
    Convention,

    /// <summary>It can be read two ways, and the two ways imply different work.</summary>
    Clarity,

    /// <summary>Something a reader must know in order to act is not in it.</summary>
    Completeness,

    /// <summary>Two parts of the document contradict each other.</summary>
    Consistency,

    /// <summary>It asks for what cannot be done as described, or not for the stated cost.</summary>
    Feasibility,
}

/// <summary>
/// One reviewer remark, normalised — counting and dedup never know which vendor produced it.
/// </summary>
/// <param name="File">Empty for a repo-level finding (a plan-stage remark has no file).</param>
/// <param name="Line">0 when the finding names no line.</param>
/// <param name="Providers">Who raised it; dedup merges these, so two vendors agreeing is one
/// finding with two names — stronger evidence, not twice the work.</param>
public sealed record Finding(
    Severity Severity,
    Category Category,
    string File,
    int Line,
    string Title,
    string Why,
    string Fix,
    ImmutableArray<string> Providers)
{
    /// <summary>
    /// Which reviewer ROLE raised it, so it can be counted against that role's threshold.
    /// </summary>
    /// <remarks>
    /// <para>An init property rather than a constructor parameter, deliberately: every existing call
    /// site keeps working and a finding from an older session file simply has none.</para>
    /// <para>Empty means unattributed — a plan-stage remark, or a file written before this existed.
    /// It is still counted, against the whole-stage threshold; dropping it would let a round pass on
    /// findings nobody looked at.</para>
    /// </remarks>
    public string Role { get; init; } = string.Empty;

    public bool IsGating => Severity is Severity.Blocking or Severity.Major;
}

/// <summary>An entry a vendor sent that could not become a <see cref="Finding"/> — named, never dropped.</summary>
public sealed record RejectedEntry(int Index, string Reason);

/// <summary>What one reviewer's answer normalised into.</summary>
public sealed record NormalisedReview(ImmutableArray<Finding> Findings, ImmutableArray<RejectedEntry> Rejected)
{
    /// <summary>
    /// This reviewer's prose about the whole document — the summary, when one was asked for.
    /// </summary>
    /// <remarks>
    /// <para><b>It can never gate.</b> A verdict is computed from gating FINDINGS; prose has no
    /// severity and must not acquire one because of the words in it. A round whose entire content is
    /// a summary passes its gate, which is correct: nobody found anything wrong.</para>
    /// <para><b>Never merged.</b> Findings are deduplicated because two vendors agreeing is stronger
    /// evidence of one defect; three vendors' accounts of one document are three accounts, and
    /// merging them destroys the only property that makes reading them worthwhile.</para>
    /// <para>Empty, never null, and empty is what a code round always has: no code prompt asks for
    /// notes, so the field arrives absent and this is the answer.</para>
    /// </remarks>
    public string Notes { get; init; } = string.Empty;
}
