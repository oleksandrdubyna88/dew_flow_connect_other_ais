using CoaiMcp.Core.Authors;
using FluentAssertions;
using Xunit;
using static CoaiMcp.Tests.RoundAuthorsFixtures;

namespace CoaiMcp.Tests;

/// <summary>A round that was not seen is named, with its reason, and never silently skipped; a partial round contributes and is named too.</summary>
public sealed class AnUnobservedRoundIsNamedNotSilentTests
{
    [Fact]
    public void ItIsNamedAndTheOtherRoundsStillContribute()
    {
        var journal = new[]
        {
            Observed("CodeReview", 1, "c0", Row("a1")),
            Round("CodeReview", 2, "c0", ObservationKinds.Unobserved, "transcript not found", Row("ghost", number: 2)),
            Round("CodeReview", 3, "c0", ObservationKinds.Partial, "peer running", Row("a3", number: 3)),
        };

        var set = Contributors.Of(journal, withBeforeOpen: false);

        set.Items.Select(item => item.Agent.Id).Should().Equal("a1", "a3");
        set.NotSeen.Should().Equal(new NotSeen("CodeReview", 2, "unobserved: transcript not found"), new NotSeen("CodeReview", 3, "partial: peer running"));
    }
}
