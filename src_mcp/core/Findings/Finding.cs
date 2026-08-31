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

public enum Category
{
    Architecture,
    Security,
    Reliability,
    Performance,
    Ux,
    Convention,
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
    public bool IsGating => Severity is Severity.Blocking or Severity.Major;
}

/// <summary>An entry a vendor sent that could not become a <see cref="Finding"/> — named, never dropped.</summary>
public sealed record RejectedEntry(int Index, string Reason);

/// <summary>What one reviewer's answer normalised into.</summary>
public sealed record NormalisedReview(ImmutableArray<Finding> Findings, ImmutableArray<RejectedEntry> Rejected);
