namespace CoaiMcp.Core.Rounds;

/// <summary>
/// Fisher-Yates, once, for everything in this product that needs a reproducible random order.
/// </summary>
/// <remarks>
/// <para>There were two private copies of this loop before there was a reason for a third —
/// <c>PromptDeal</c> shuffling prompt ids and <c>RuleFiles</c> shuffling rule folders — and the two
/// had already drifted in the small way copies do: one takes a seed, the other takes a nullable seed
/// and falls back to <c>Random.Shared</c>. Neither difference is about shuffling.</para>
/// <para><b>Seeded, and that is the point rather than a detail.</b> A round that cannot be replayed
/// cannot be investigated, and "which vendor got the security lens" or "which vendor was asked
/// first" is exactly the question somebody asks about a round that went oddly. The seed travels with
/// the round; <c>Random.Shared</c> is for the callers that genuinely want a different answer every
/// time and say so by passing it.</para>
/// </remarks>
public static class SeededShuffle
{
    /// <summary>A new list in a random order drawn from <paramref name="random"/>.</summary>
    public static List<T> Of<T>(IReadOnlyList<T> items, Random random)
    {
        var order = items.ToList();
        for (var i = order.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        return order;
    }

    /// <summary>The same seed always produces the same order.</summary>
    public static List<T> Of<T>(IReadOnlyList<T> items, int seed) => Of(items, new Random(seed));
}
