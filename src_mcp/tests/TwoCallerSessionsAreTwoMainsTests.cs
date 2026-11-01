using CoaiMcp.Core.Authors;
using FluentAssertions;
using Xunit;
using static CoaiMcp.Tests.RoundAuthorsFixtures;

namespace CoaiMcp.Tests;

/// <summary>Identity is (caller session, agent): the <c>main</c> of two caller sessions are two contributors.</summary>
public sealed class TwoCallerSessionsAreTwoMainsTests
{
    [Fact]
    public void TheSameKeyUnderTwoCallers_IsTwoContributors()
    {
        var journal = new[]
        {
            Observed("CodeReview", 1, "c0", Row("main")),
            Observed("CodeReview", 2, "c1", Row("main", number: 2)),
        };

        Contributors.Of(journal, withBeforeOpen: false).Items.Select(item => (item.CallerSessionId, item.Agent.Id)).Should().Equal(("c0", "main"), ("c1", "main"));
    }
}
