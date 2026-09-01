using CoaiMcp.Core.Rounds;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A round's prompts are DEALT across the vendors, not handed to all of them.
/// </summary>
/// <remarks>
/// <para>Every vendor used to run every role's prompt, so two vendors answered the same question and
/// the dedup merged what they agreed on. That agreement was the strongest signal this product
/// produced. The operator traded it deliberately: dealing the prompts out means every lens gets
/// asked instead of one lens being asked twice, at half the launches.</para>
/// <para>The properties that matter: no prompt goes to two vendors in one round, the load is
/// balanced, the assignment is random but REPRODUCIBLE from a recorded seed, and one vendor means
/// one vendor does all of it.</para>
/// </remarks>
public sealed class PromptDealTests
{
    private static readonly string[] TwoVendors = ["codex", "antigravity"];

    [Fact]
    public void NoItemGoesToTwoVendors()
    {
        var deal = PromptDeal.Deal(["a", "b", "c"], TwoVendors, seed: 7);

        deal.Should().HaveCount(3);
        deal.Select(d => d.Item).Should().OnlyHaveUniqueItems("a prompt asked twice is the thing this replaced");
    }

    [Fact]
    public void EveryItemIsDealt_NoneQuietlyDropped()
    {
        var deal = PromptDeal.Deal(["a", "b", "c", "d", "e"], TwoVendors, seed: 1);

        deal.Select(d => d.Item).Should().BeEquivalentTo(["a", "b", "c", "d", "e"]);
    }

    [Fact]
    public void TheLoadIsBalanced_WithinOne()
    {
        var deal = PromptDeal.Deal(["a", "b", "c"], TwoVendors, seed: 3);

        var perVendor = deal.GroupBy(d => d.Vendor).Select(g => g.Count()).OrderBy(n => n).ToList();
        perVendor.Should().BeEquivalentTo([1, 2], "three items over two vendors is two and one");
    }

    [Fact]
    public void OneVendorTakesEverything_BecauseThereIsNoAlternative()
    {
        var deal = PromptDeal.Deal(["a", "b", "c"], ["codex"], seed: 42);

        deal.Should().OnlyContain(d => d.Vendor == "codex");
        deal.Should().HaveCount(3);
    }

    [Fact]
    public void TheSameSeed_DealsTheSameHand()
    {
        // A round that cannot be reproduced cannot be investigated, which is why the seed is
        // recorded with the round rather than taken from the clock at the point of use.
        var first = PromptDeal.Deal(["a", "b", "c", "d"], TwoVendors, seed: 99);
        var again = PromptDeal.Deal(["a", "b", "c", "d"], TwoVendors, seed: 99);

        again.Should().BeEquivalentTo(first, o => o.WithStrictOrdering());
    }

    [Fact]
    public void DifferentSeeds_DealDifferentHands()
    {
        var hands = Enumerable.Range(1, 40)
            .Select(seed => string.Join(",", PromptDeal.Deal(["a", "b", "c", "d"], TwoVendors, seed).Select(d => $"{d.Item}:{d.Vendor}")))
            .Distinct()
            .ToList();

        hands.Should().HaveCountGreaterThan(1, "an assignment that never varies is not random");
    }

    [Fact]
    public void NoVendors_DealsNothing_RatherThanThrowing()
    {
        PromptDeal.Deal(["a"], [], seed: 1).Should().BeEmpty();
    }

    [Fact]
    public void NoItems_DealsNothing()
    {
        PromptDeal.Deal([], TwoVendors, seed: 1).Should().BeEmpty();
    }
}
