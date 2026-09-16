namespace CoaiMcp.Core.Collecting;

/// <summary>What a model was asked to rank, and what it said.</summary>
/// <remarks>
/// The ask is by ID and nothing else travels back: a model that answers with prose, or with the
/// findings restated, is answering a different question. What comes back is an ORDER.
/// </remarks>
public readonly record struct Ranked(long FindingId, int Rank);

/// <summary>
/// Putting the corpus in the order a model thought most worth reading.
/// </summary>
/// <remarks>
/// <para><b>Ranking, not scoring.</b> Ask for <i>the best ten of these two hundred, ranked</i>. An
/// unanchored 1–5 scale makes a model cluster at 4s and 5s, so a threshold over it filters nothing —
/// the ordering is the product, and it is applied in code rather than trusted as a number.</para>
/// <para><b>A model's answer is external unvalidated data</b>, and the plan's first draft specified
/// the prompt and nothing about the reply. Two reviewers said so. Everything below is what happens
/// when the reply is wrong, which it will be: an id that was never sent is DROPPED, an id that was
/// sent and not returned keeps its original place rather than vanishing, and two ids claiming one
/// rank are separated by the order they were OFFERED in. Nothing here throws, because a ranking that
/// cannot be trusted is still a list a person can read.</para>
/// </remarks>
public static class Ranking
{
    /// <summary>
    /// The ids in the order to show them.
    /// </summary>
    /// <param name="offered">The ids that were sent, in the order they would otherwise appear.</param>
    /// <param name="answered">What the model said. Anything at all.</param>
    /// <remarks>
    /// <para><b>Every offered id comes back exactly once.</b> That is the property worth having and
    /// the one a test can hold: a ranking that loses a pair silently is a pair a person will never
    /// review, and a ranking that duplicates one wastes the only attention this corpus gets.</para>
    /// <para>Ranked ids lead, in rank order; everything the model did not place follows in the order
    /// it was offered. A reply that places nothing is therefore the original order, which is exactly
    /// what "the model had no opinion" should look like.</para>
    /// </remarks>
    public static IReadOnlyList<long> Order(
        IReadOnlyList<long> offered, IReadOnlyList<Ranked> answered)
    {
        var known = new HashSet<long>(offered);
        var placed = new Dictionary<long, int>();

        // First mention wins. A model that lists the same id twice has contradicted itself, and the
        // later claim is not more true than the earlier one — but it must not shift the first.
        foreach (var one in answered)
        {
            if (known.Contains(one.FindingId))
            {
                _ = placed.TryAdd(one.FindingId, one.Rank);
            }
        }

        // The tie-break is the offered order, and it is a STABLE sort over that sequence rather than
        // a comparison on rank alone: two ids sharing a rank would otherwise come back in whichever
        // order the dictionary happened to enumerate, and the same reply would order differently on
        // two machines.
        var ranked = offered
            .Where(placed.ContainsKey)
            .OrderBy(id => placed[id])
            .ToList();

        return [.. ranked, .. offered.Where(id => !placed.ContainsKey(id))];
    }

    /// <summary>Whether a reply placed anything at all that was actually asked about.</summary>
    /// <remarks>
    /// The difference between "the model ranked them" and "the model answered something that left
    /// the list exactly as it was". A person pressing Rank and seeing no change deserves to be told
    /// which of those happened.
    /// </remarks>
    public static bool Placed(IReadOnlyList<long> offered, IReadOnlyList<Ranked> answered)
    {
        var known = new HashSet<long>(offered);

        return answered.Any(one => known.Contains(one.FindingId));
    }

    /// <summary>What to tell a person when a ranking changed nothing.</summary>
    public const string NothingPlaced =
        "the model placed none of the pairs it was given; the list is in the order it was collected";
}
