using CoaiMcp.Core.Authors;
using FluentAssertions;
using Xunit;
using static CoaiMcp.Tests.AssignmentViews;

namespace CoaiMcp.Tests;

/// <summary>
/// Ambiguous, elsewhere and claimed-by-peer activity is NEVER guessed into a row (parent §6): it appears only in the totals, whose
/// agent counts are distinct agents of THEIR items — and can be in two counts, because they do not partition.
/// </summary>
public sealed class AmbiguousAndElsewhereActivityHasNoRowsTests
{
    private static RoundAuthors A1() => AttributionCases.Case1().AuthorsOf("A", "CodeReview", 1);

    private static RoundAuthors B() => AttributionCases.Case1().AuthorsOf("B", "CodeReview", 1);

    [Fact]
    public void TheyAppearOnlyInTheTotals()
    {
        var authors = A1();

        authors.Rows.Select(row => row.Agent.Id).Distinct().Should().Equal("cx");
        authors.Rows.Select(row => row.Association).Should().OnlyContain(association => association == Association.WroteHere);
        authors.Ambiguous.Should().Be(new ActivityTotals(3, 1, 3, new TokenTerms(3, 300, 30, 3000)), "main -5 (before-open), main +20 and s1 +45 with its launch (round); the round-level agent count is the sum of the windows'");
        authors.Elsewhere.Should().Be(new ActivityTotals(1, 1, 1, new TokenTerms(1, 100, 10, 1000)), "cy +35 and cy's launch");
    }

    [Fact]
    public void ClaimedByPeerItemsAreOnlyInClaimedByPeer()
    {
        var authors = B();

        authors.ClaimedByPeer.Should().Be(new ActivityTotals(2, 1, 3, new TokenTerms(3, 300, 30, 3000)), "main -5, main +20 (before-open) and s1 +45 with its launch (round)");
        authors.Rows.Select(row => row.Agent.Id).Should().Equal("cy").And.NotContain("main");
        authors.Ambiguous.Turns.Should().Be(1, "s2 +80 is B's own ambiguous");
    }

    [Fact]
    public void AnObservedRoundWithNothingAmbiguousHasZeroTotalsNotUnknown()
    {
        var authors = AttributionCases.Case3().AuthorsOf("A", "CodeReview", 1);

        authors.Ambiguous.Should().Be(ActivityTotals.Zero);
        authors.Elsewhere.Should().Be(ActivityTotals.Zero);
        authors.ClaimedByPeer.Should().Be(ActivityTotals.Zero);
    }
}
