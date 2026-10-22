using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Security;
using FluentAssertions;
using System.Collections.Immutable;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// <see cref="FindingIdentity.Same"/> — the "is this the very finding I hold" rule. Malformed reviewer JSON can leave a null inside a collection;
/// identity must treat it as a value, never throw on it (round 6 of the review, 2026-10-22).
/// </summary>
public sealed class FindingIdentityTests
{
    private static Finding Raised(ImmutableArray<string> providers) =>
        new(Severity.Blocking, Category.Completeness, string.Empty, 0, "The rollback is missing", "nothing undoes the migration", "add the down step", providers);

    private static SecuritySighting Sighting(string provider) => new(provider, "prompt", Severity.Major, null, string.Empty);

    [Fact]
    public void TwoFindingsWhoseProvidersHoldANullElement_CompareWithoutThrowing_AndAreTheSameWhenTheListsMatch()
    {
        var one = Raised([null!, "codex"]);
        var other = Raised([null!, "codex"]);

        FindingIdentity.Same(one, other).Should().BeTrue("a null element equals another null element, so the lists match");
    }

    [Fact]
    public void ANullProviderIsNotAnyString_AndAMissingOneDoesNotMatchAPresentOne()
    {
        FindingIdentity.Same(Raised([null!]), Raised([string.Empty])).Should().BeFalse("null is not the empty string");
        FindingIdentity.Same(Raised(["codex"]), Raised([null!])).Should().BeFalse("a null element is not a vendor name");
    }

    [Fact]
    public void ANullSighting_IsComparedAsAValue_NotAThrow()
    {
        var withNull = Raised(["codex"]) with { AlsoSeenBy = [null!, Sighting("gemini")] };
        var same = Raised(["codex"]) with { AlsoSeenBy = [null!, Sighting("gemini")] };
        var other = Raised(["codex"]) with { AlsoSeenBy = [Sighting("gemini"), null!] };

        FindingIdentity.Same(withNull, same).Should().BeTrue("the same sightings in the same order, null included");
        FindingIdentity.Same(withNull, other).Should().BeFalse("the order of sightings is part of the finding");
    }

    [Fact]
    public void AFindingWithNeverAssignedArrays_ReadsAsEmpty_NotAsAThrow()
    {
        var unassigned = Raised(default);

        FindingIdentity.Same(unassigned, Raised([])).Should().BeTrue("a default array reads as an empty one, as Finding's own AlsoSeenBy does");
    }
}
