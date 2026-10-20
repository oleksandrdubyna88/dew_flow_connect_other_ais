using CoaiMcp.Core.Authors;
using FluentAssertions;
using Xunit;
using static CoaiMcp.Tests.RoundAuthorsFixtures;

namespace CoaiMcp.Tests;

/// <summary>An agent that works across windows is ONE contributor active in both (a straddler, a resumed agent).</summary>
public sealed class AnAgentInTwoWindowsIsOneContributorTests
{
    [Fact]
    public void ItAppearsOnceWithBothWindows()
    {
        var journal = new[]
        {
            Observed("CodeReview", 1, "c0", Row("r1", model: "claude-opus-5-5", writeCalls: 2)),
            Observed("CodeReview", 2, "c0", Row("r1", number: 2, model: "claude-sonnet-5-5", association: Association.SameSession, writeCalls: 0)),
        };

        var contributor = Contributors.Of(journal, withBeforeOpen: false).Items.Should().ContainSingle().Which;

        contributor.ActiveIn.Should().Equal(new RoundRef("CodeReview", 1, WindowKind.Round), new RoundRef("CodeReview", 2, WindowKind.Round));
        contributor.Models.Should().Equal("claude-opus-5-5", "claude-sonnet-5-5");
        contributor.Associations.Should().Equal(Association.SameSession, Association.WroteHere);
        contributor.WriteCalls.Should().Be(2);
    }
}
