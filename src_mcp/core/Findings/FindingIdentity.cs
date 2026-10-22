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
/// <para>Every public property of <see cref="Finding"/> is compared (or is derived from one that is); <c>FindingIdentityCoversEveryFieldTests</c>
/// fails when a property is added to <see cref="Finding"/> without a decision here.</para>
/// <para>A <c>null</c> element in a collection (malformed reviewer JSON can produce one) is a value like any other: it equals another <c>null</c>
/// and no string, and never throws.</para>
/// </remarks>
public static class FindingIdentity
{
    public static bool Same(Finding a, Finding b) =>
        ReferenceEquals(a, b) || (SameScalars(a, b) && SameCollections(a, b));

    private static bool SameScalars(Finding a, Finding b) =>
        (a.Severity, a.Category, a.File, a.Line, a.Title, a.Why, a.Fix, a.Role, a.CapReason)
        == (b.Severity, b.Category, b.File, b.Line, b.Title, b.Why, b.Fix, b.Role, b.CapReason)
        && a.Reproduction == b.Reproduction
        && a.AttackEvidence == b.AttackEvidence;

    // Both element types are reference types that malformed JSON can leave null. Neither comparison throws on one: `StringComparer.Ordinal.Equals`
    // is `string.Equals(x, y, Ordinal)` (null equals null only) and `SequenceEqual` without a comparer uses `EqualityComparer<T>.Default` (a record's
    // null-safe `==`). `SequenceEqual` never asks the comparer for a hash code. FindingIdentityTests pins this, so a rewrite that dereferences an element fails.
    private static bool SameCollections(Finding a, Finding b) =>
        Items(a.Providers).SequenceEqual(Items(b.Providers), StringComparer.Ordinal)
        && Items(a.AlsoSeenBy).SequenceEqual(Items(b.AlsoSeenBy));

    // A default (never-assigned) array reads as empty, the way Finding's own AlsoSeenBy normalises it.
    private static ImmutableArray<T> Items<T>(ImmutableArray<T> items) => items.IsDefault ? [] : items;
}
