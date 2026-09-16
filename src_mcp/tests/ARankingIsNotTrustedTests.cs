using CoaiMcp.Core.Collecting;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What happens when the model's ranking is wrong — which it will be.
/// </summary>
/// <remarks>
/// <para>A model's answer is external unvalidated data by this repository's own coding-style rule,
/// and the story-5 plan specified the prompt and nothing about the reply. Two plan reviewers said so
/// independently: an id the model invented, an id it dropped, a duplicate rank and a reply that will
/// not parse all had no defined outcome.</para>
/// <para><b>The property every case below is a face of:</b> every offered pair comes back exactly
/// once. A ranking that loses one silently is a pair nobody will ever review; one that duplicates a
/// pair wastes the only attention this corpus gets.</para>
/// </remarks>
public sealed class ARankingIsNotTrustedTests
{
    private static readonly long[] Offered = [10, 20, 30, 40];

    [Fact]
    public void AGoodRankingIsObeyed() =>
        Ranking.Order(Offered, [new(30, 1), new(10, 2), new(40, 3), new(20, 4)])
            .Should().Equal([30, 10, 40, 20]);

    /// <summary>An id nobody offered is not a pair, whatever the model thinks.</summary>
    [Fact]
    public void AnInventedIdIsDropped() =>
        Ranking.Order(Offered, [new(999, 1), new(30, 2)])
            .Should().Equal([30, 10, 20, 40], "999 is not in this corpus");

    /// <summary>
    /// An id the model forgot keeps its place rather than disappearing.
    /// </summary>
    /// <remarks>
    /// The sharp one. Dropping unranked pairs would mean a model that returned "the best 10" quietly
    /// deleted the other 190 from the review — and the person would never know there had been more.
    /// </remarks>
    [Fact]
    public void AnOmittedIdKeepsItsPlace_RatherThanVanishing()
    {
        var ordered = Ranking.Order(Offered, [new(40, 1)]);

        ordered.Should().Equal([40, 10, 20, 30]);
        ordered.Should().HaveCount(Offered.Length, "a ranking must not delete the corpus");
    }

    /// <summary>Two ids claiming one rank are separated by the order they were offered in.</summary>
    /// <remarks>
    /// Not by whichever the dictionary enumerated first: the same reply must order the same way on
    /// two machines, or a screenshot of the review page means nothing.
    /// </remarks>
    [Fact]
    public void ADuplicateRankBreaksTieByTheOfferedOrder() =>
        // 40 is placed first on its own rank; 20 and 30 tie, and the tie is broken by the order they
        // were OFFERED in (20 before 30) rather than by the order the model listed them (30 first).
        // The distinction is the point: the same reply must order the same way on two machines.
        Ranking.Order(Offered, [new(30, 1), new(20, 1), new(40, 0)])
            .Should().Equal([40, 20, 30, 10]);

    /// <summary>A model that contradicts itself is held to its first answer.</summary>
    [Fact]
    public void AnIdRankedTwiceKeepsItsFirstRank() =>
        Ranking.Order(Offered, [new(40, 1), new(40, 9)])
            .Should().Equal([40, 10, 20, 30]);

    /// <summary>A reply that placed nothing is the order it was already in.</summary>
    /// <remarks>
    /// Which is exactly what "the model had no opinion" should look like — and a person is told,
    /// because a list that did not move is otherwise indistinguishable from a button that did not
    /// work.
    /// </remarks>
    [Fact]
    public void AnEmptyRankingLeavesTheListAlone()
    {
        Ranking.Order(Offered, []).Should().Equal(Offered);
        Ranking.Placed(Offered, []).Should().BeFalse();
        Ranking.Placed(Offered, [new(999, 1)]).Should().BeFalse("it placed nothing that exists");
        Ranking.Placed(Offered, [new(10, 1)]).Should().BeTrue();
    }

    /// <summary>Whatever the reply, every pair comes back exactly once.</summary>
    /// <remarks>
    /// The property itself, over the nastiest reply this test file can write: inventions, omissions,
    /// duplicates and negative ranks at the same time.
    /// </remarks>
    [Fact]
    public void EveryPairComesBackExactlyOnce()
    {
        var nasty = new Ranked[]
        {
            new(999, 1), new(40, -5), new(40, 2), new(10, 0), new(-1, 3), new(20, int.MaxValue),
        };

        var ordered = Ranking.Order(Offered, nasty);

        ordered.Should().BeEquivalentTo(Offered, "nothing gained, nothing lost");
        ordered.Should().OnlyHaveUniqueItems();
    }

    /// <summary>A negative rank is still an order, and sorts where it says.</summary>
    /// <remarks>
    /// Rather than being refused: the number is meaningless on its own — it is unanchored, which is
    /// why the plan asks for a ranking and not a score — and all that matters is what it orders
    /// before and after.
    /// </remarks>
    [Fact]
    public void RanksAreAnOrder_NotAScale() =>
        Ranking.Order(Offered, [new(10, 100), new(20, -100)])
            .Should().Equal([20, 10, 30, 40]);
}
