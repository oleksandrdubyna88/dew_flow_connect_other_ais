using CoaiMcp.Core.Authors;
using FluentAssertions;
using Xunit;
using static CoaiMcp.Tests.RoundAuthorsFixtures;

namespace CoaiMcp.Tests;

/// <summary>Equal rows in another order give the same contributors, in the same order: caller, main first, then agent id — ordinal.</summary>
public sealed class ContributorsAreOrderedTheSameWayEveryTimeTests
{
    [Fact]
    public void APermutedInputGivesEqualOutput()
    {
        var rows = new[] { Row("zed"), Row("main", association: Association.SameSession), Row("alpha", model: "claude-sonnet-5-5") };
        var forward = Contributors.Of([Observed("CodeReview", 1, "c0", rows)], withBeforeOpen: false);
        var backward = Contributors.Of([Observed("CodeReview", 1, "c0", [.. rows.Reverse()])], withBeforeOpen: false);

        forward.Items.Select(item => item.Agent.Id).Should().Equal("main", "alpha", "zed");
        System.Text.Json.JsonSerializer.Serialize(backward).Should().Be(System.Text.Json.JsonSerializer.Serialize(forward));
    }
}
