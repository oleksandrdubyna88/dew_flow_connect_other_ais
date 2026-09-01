namespace CoaiMcp.Core.Rounds;

/// <summary>One item of work and the vendor it was dealt to.</summary>
public sealed record DealtItem(string Item, string Vendor);

/// <summary>
/// Dealing a round's prompts across the vendors instead of handing every prompt to all of them.
/// </summary>
/// <remarks>
/// <para><b>What this trades.</b> Every vendor used to run every role's prompt, so two vendors
/// answered the same question and <c>FindingDedup</c> merged what they agreed on. That agreement was
/// the strongest signal this product produced — a finding two vendors raise independently is worth
/// more than either of them saying it. Dealing the prompts out removes it, and buys two things: every
/// lens gets asked rather than one lens being asked twice, and a round costs half the launches. The
/// operator made that trade knowingly; this paragraph is here so nobody later reads it as an
/// oversight.</para>
/// <para><b>Reproducible.</b> The assignment is random and the seed is recorded with the round,
/// because a round that cannot be replayed cannot be investigated — and "which vendor got the
/// security lens" is exactly the question somebody asks about a round that went oddly.</para>
/// <para>Pure, so the properties are a test rather than something inferred from a log.</para>
/// </remarks>
public static class PromptDeal
{
    /// <summary>
    /// Deals every item to exactly one vendor, balanced within one, in a seeded random order.
    /// </summary>
    /// <remarks>
    /// The ITEMS are shuffled and then handed out round-robin, rather than the vendors being picked
    /// at random per item: round-robin over a shuffled list is balanced by construction, while an
    /// independent random choice per item is not — with three items and two vendors it puts all three
    /// on one vendor once every four rounds, which is the imbalance this exists to avoid.
    /// </remarks>
    public static IReadOnlyList<DealtItem> Deal(
        IReadOnlyList<string> items,
        IReadOnlyList<string> vendors,
        int seed)
    {
        if (items.Count == 0 || vendors.Count == 0)
        {
            return [];
        }

        var shuffled = Shuffled(items, seed);
        return [.. shuffled.Select((item, index) => new DealtItem(item, vendors[index % vendors.Count]))];
    }

    /// <summary>Fisher-Yates with a seeded generator: the same seed always deals the same hand.</summary>
    private static List<string> Shuffled(IReadOnlyList<string> items, int seed)
    {
        var random = new Random(seed);
        var order = items.ToList();
        for (var i = order.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        return order;
    }
}
