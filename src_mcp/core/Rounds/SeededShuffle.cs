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
    // S2245 — `Random` is not used for security here, and cannot be replaced by one that is.
    // Everything this shuffles is a PUBLIC ordering: which vendor is asked first, which lens a
    // reviewer draws, which rule files fit in a budget. Nothing is a secret, nothing is a token, and
    // the whole value of the class is that a SEED reproduces an order — which a cryptographic
    // generator cannot do at all. The justification travelled here with the loop: it lived beside
    // `RuleFiles.Shuffled` before these two copies were merged.
#pragma warning disable S2245
    /// <summary>A new list in a random order drawn from <paramref name="random"/>.</summary>
    /// <remarks>
    /// Read-only on the way out: a caller who could reorder the result in place would make the
    /// reproducibility this exists for a promise about the first line of the call site rather than
    /// about the value. The copy stays inside.
    /// </remarks>
    public static IReadOnlyList<T> Of<T>(IReadOnlyList<T> items, Random random)
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
    public static IReadOnlyList<T> Of<T>(IReadOnlyList<T> items, int seed) => Of(items, new Random(seed));
#pragma warning restore S2245
}
