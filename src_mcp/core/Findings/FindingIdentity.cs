using System.Collections.Immutable;

namespace CoaiMcp.Core.Findings;

/// <summary>
/// Whether two <see cref="Finding"/> values are the SAME finding — every field equal, the collection-valued ones by their contents.
/// </summary>
/// <remarks>
/// <para>The record's generated equality is not this: <see cref="Finding.Providers"/> and <see cref="Finding.AlsoSeenBy"/> are
/// <see cref="ImmutableArray{T}"/>, which compares its backing array by REFERENCE, so a value-equal copy whose list was newly allocated
/// (a gate that rebuilds what it counted, a round-trip through a file) is "not equal" to the original.</para>
/// <para>This is IDENTITY, not <c>FindingDedup.SameDefect</c>: that one is the fuzzy "two reviewers raised the same defect" rule that merges
/// findings; this one answers "is this the very finding I hold", so nothing is similar-enough, and two findings that differ only in their vendors
/// are two findings.</para>
/// </remarks>
public sealed class FindingIdentity : IEqualityComparer<Finding>
{
    public static FindingIdentity Instance { get; } = new();

    public static bool Same(Finding a, Finding b) => Instance.Equals(a, b);

    public bool Equals(Finding? a, Finding? b) =>
        ReferenceEquals(a, b) || (a is not null && b is not null && SameScalars(a, b) && SameCollections(a, b));

    public int GetHashCode(Finding finding) =>
        HashCode.Combine(finding.Severity, finding.Category, finding.File, finding.Line, finding.Title, finding.Role, Items(finding.Providers).Length);

    private static bool SameScalars(Finding a, Finding b) =>
        (a.Severity, a.Category, a.File, a.Line, a.Title, a.Why, a.Fix, a.Role, a.CapReason)
        == (b.Severity, b.Category, b.File, b.Line, b.Title, b.Why, b.Fix, b.Role, b.CapReason)
        && a.Reproduction == b.Reproduction
        && a.AttackEvidence == b.AttackEvidence;

    private static bool SameCollections(Finding a, Finding b) =>
        Items(a.Providers).SequenceEqual(Items(b.Providers), StringComparer.Ordinal)
        && Items(a.AlsoSeenBy).SequenceEqual(Items(b.AlsoSeenBy));

    // A default (never-assigned) array reads as empty, the way Finding's own AlsoSeenBy normalises it.
    private static ImmutableArray<T> Items<T>(ImmutableArray<T> items) => items.IsDefault ? [] : items;
}
