using CoaiMcp.Core.Authors;
using FluentAssertions;
using Xunit;
using static CoaiMcp.Tests.RoundAuthorsFixtures;

namespace CoaiMcp.Tests;

/// <summary>Work before the gate session opened is left out unless asked for (parent §8), and a contributor keeps its later windows.</summary>
public sealed class BeforeOpenWorkIsLeftOutUnlessAskedTests
{
    private static IReadOnlyList<RoundAuthors> Journal() =>
    [
        Observed("CodeReview", 1, "c0", Row("early", window: WindowKind.BeforeOpen), Row("both", window: WindowKind.BeforeOpen), Row("both")),
    ];

    [Fact]
    public void LeftOutByDefault_ADroppedContributorHadOnlyBeforeOpen()
    {
        var set = Contributors.Of(Journal(), withBeforeOpen: false);

        set.Items.Select(item => item.Agent.Id).Should().Equal("both");
        set.Items.Single().ActiveIn.Should().Equal(new RoundRef("CodeReview", 1, WindowKind.Round));
    }

    [Fact]
    public void WhenAsked_BeforeOpenIsTaggedNotDropped()
    {
        var set = Contributors.Of(Journal(), withBeforeOpen: true);

        set.Items.Select(item => item.Agent.Id).Should().Equal("both", "early");
        set.Items.Single(item => item.Agent.Id == "both").ActiveIn.Select(round => round.Window).Should().Equal(WindowKind.BeforeOpen, WindowKind.Round);
    }
}
