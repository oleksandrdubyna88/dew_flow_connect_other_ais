using CoaiMcp.Core.Authors;
using FluentAssertions;
using Xunit;
using static CoaiMcp.Tests.RoundAuthorsFixtures;

namespace CoaiMcp.Tests;

/// <summary>Only rows make a contributor: ambiguous, elsewhere and claimed-by-peer totals never do (parent §6, never guessed).</summary>
public sealed class AmbiguousActivityNeverMakesAContributorTests
{
    [Fact]
    public void ARoundWhoseOnlyActivityIsAmbiguous_HasNoContributorsAndWasSeen()
    {
        var ambiguous = new ActivityTotals(2, 1, 5, new TokenTerms(5, 5, 5, 5));
        var round = Observed("CodeReview", 1, "c0") with { Ambiguous = ambiguous, ClaimedByPeer = ambiguous, Elsewhere = ambiguous };

        var set = Contributors.Of([round], withBeforeOpen: true);

        set.Items.Should().BeEmpty();
        set.NotSeen.Should().BeEmpty("it was seen: observed means looked at, and nothing associated is not nothing seen");
    }
}
