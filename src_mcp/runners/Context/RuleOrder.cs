using CoaiMcp.Core.Rounds;

namespace CoaiMcp.Runners.Context;

/// <summary>
/// One rule file of a mount, with the name a tier can actually address it by.
/// </summary>
/// <param name="Path">Repository-relative, as the bundle reports it.</param>
/// <param name="WithinMount">
/// Relative to the mount root — <c>common/security.md</c>, not
/// <c>.agents/conventions/common/security.md</c>.
/// </param>
/// <remarks>
/// The second field exists because a tier entry must name ONE file. Matching by path suffix cannot:
/// <c>.agents/conventions/common/legacy/common/security.md</c> also ends with
/// <c>/common/security.md</c>, so a tier entry would pull in a file nobody meant and spend the budget
/// of the rule it was impersonating. Measured as a red test while writing this story.
/// </remarks>
public sealed record RuleCandidate(string Path, string WithinMount);

/// <summary>
/// The order the MOUNTED family rules are considered in, once the budget starts refusing them.
/// </summary>
/// <remarks>
/// <para>A separate type because the order is the whole question. The corpus is larger than the
/// budget and selection is whole-file, so whatever comes first is what a reviewer is judged against
/// and whatever comes last is never shown. Leaving that to the alphabet is what starved
/// <c>testing.md</c>, <c>security.md</c>, <c>reuse-first.md</c> and all four doctrines until
/// 2026-09-06; leaving it to <see cref="Drawn()"/> fixed the starvation by making the gate unstable
/// instead, which is its own defect.</para>
/// <para>The instruction files and the repository's OWN rules are not ordered here. They come first
/// and are never dropped — see <c>RuleFiles.Candidates</c> — though they are not free: they are
/// collected under the same budget, so a large one leaves less for the mount.</para>
/// </remarks>
public sealed record RuleOrder
{
    private readonly Func<IReadOnlyList<RuleCandidate>, IEnumerable<string>> _mount;

    private RuleOrder(Func<IReadOnlyList<RuleCandidate>, IEnumerable<string>> mount) => _mount = mount;

    /// <summary>The deterministic order: the same tree gives the same bundle, every time.</summary>
    public static RuleOrder Walk { get; } = new(ByTier);

    /// <summary>
    /// The 2026-09-06 draw: a different part of the family rules each round.
    /// </summary>
    /// <remarks>
    /// <para>Measured 2026-09-06: the family set is ~199 KB against an 80 KB budget, and in
    /// enumeration order the first two files take a quarter of it — so <c>testing.md</c>,
    /// <c>security.md</c>, <c>reuse-first.md</c> and all four language doctrines were never shown to
    /// any reviewer, ever. Not because the budget was small: because they were last in line, and the
    /// line never changed.</para>
    /// <para>Raising the budget cannot fix that; a different draw each round can. That was the
    /// operator's call and the right one at the time: a rule shown sometimes is infinitely more than a
    /// rule shown never.</para>
    /// <para>Its cost is the reason <see cref="Walk"/> exists. A developer who pushes a fix and runs a
    /// second round is answered out of a different part of the rule book, which reads as noise — so
    /// the draw is kept only until every deterministic order is wired in, and then deleted.</para>
    /// </remarks>
    // S2245 wants a cryptographic generator. It is wrong about this call: nothing here guards a
    // secret, and the draw decides only WHICH rule files a reviewer is shown when they do not all
    // fit. The seeded overload is the tell — it exists so a test can assert an exact order, which a
    // cryptographic generator cannot give at all.
#pragma warning disable S2245 // Random is not used for security here — see above.
    public static RuleOrder Drawn() => new(candidates => Shuffled(candidates, Random.Shared));

    /// <param name="seed">Fixes the draw, so a test can assert an exact order.</param>
    public static RuleOrder Drawn(int seed) => new(candidates => Shuffled(candidates, new Random(seed)));
#pragma warning restore S2245

    /// <summary>
    /// The order for a gate with NO diff: the named tier, and nothing else from the mount.
    /// </summary>
    /// <remarks>
    /// <para>The one order here that FILTERS. A plan or document round has no change to select from,
    /// so the mount's other rules are not "lower priority" — they are not what the stage is judged
    /// against at all, and letting them in would spend a budget the tier needs. When epic 2 brings a
    /// resolver manifest, this same list ORDERS the selection instead of replacing it.</para>
    /// <para><b>When nothing matches</b> it yields nothing, and that is not a failure: the instruction
    /// files and the repository's own rules are outside every order, so the reviewer still sees this
    /// repository's own written rules and simply none of the family's. A caller that must tell the two
    /// apart reads the bundle — the mount contributed no file.</para>
    /// </remarks>
    public static RuleOrder Staged(IReadOnlyList<string> tier) =>
        new(candidates => tier.SelectMany(entry => Named(candidates, entry)));

    /// <summary>Orders the mount's rule files. Total, and never drops one — except <see cref="Staged"/>, which filters.</summary>
    public IEnumerable<string> Mount(IReadOnlyList<RuleCandidate> candidates) => _mount(candidates);

    /// <summary>
    /// The rules that go first when the budget cannot hold them all, in order.
    /// </summary>
    /// <remarks>
    /// <para>A table rather than the alphabet, because the alphabet is what caused the starvation:
    /// <c>development-workflow.md</c> (14 KB) and <c>http-contracts.md</c> (11 KB) sort before
    /// <c>security.md</c> and <c>testing.md</c> and take a quarter of the budget between them.</para>
    /// <para>Why these five, in this order. A language doctrine is the only entry that is about the
    /// code in front of the reviewer rather than about the project — a C# diff breaks C# rules first,
    /// and the doctrines are small. Then <c>security.md</c>, because a missed security finding is the
    /// most expensive thing this gate can fail to say; then <c>testing.md</c>, which more findings are
    /// written against than any other rule here; then <c>reuse-first.md</c>, <c>coding-style.md</c> and
    /// <c>knowledge-base.md</c>, the three a reviewer cites when a change is built wrong rather than
    /// written wrong.</para>
    /// <para>Entries are mount-relative and matched EXACTLY, so one entry names one rule; a name the
    /// table has never heard of ranks last and is ordered alphabetically rather than dropped.</para>
    /// </remarks>
    private static readonly string[] Tiers =
    [
        "csharp/doctrine.md",
        "rust/doctrine.md",
        "typescript/doctrine.md",
        "common/security.md",
        "common/testing.md",
        "common/reuse-first.md",
        "common/coding-style.md",
        "common/knowledge-base.md",
    ];

    /// <remarks>
    /// <para>One comparison rule for the whole pipeline: <c>RuleFiles.FolderFiles</c> de-duplicates
    /// and sorts its candidates with <see cref="StringComparer.OrdinalIgnoreCase"/>, so a
    /// case-SENSITIVE tier match here would be a second rule inside one collection — and the rule it
    /// would starve is whichever one somebody committed with a capital letter.</para>
    /// <para>The ordinal pass at the end is what keeps the order TOTAL: two paths differing only in
    /// case tie under the first comparer, and a tie is where an order stops being deterministic.</para>
    /// </remarks>
    private static IEnumerable<string> ByTier(IReadOnlyList<RuleCandidate> candidates) =>
        candidates
            .OrderBy(candidate => Tier(candidate.WithinMount))
            .ThenBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.Path, StringComparer.Ordinal)
            .Select(candidate => candidate.Path);

    private static int Tier(string withinMount)
    {
        var rank = Array.FindIndex(Tiers, entry => entry.Equals(withinMount, StringComparison.OrdinalIgnoreCase));

        return rank < 0 ? Tiers.Length : rank;
    }

    /// <summary>Every candidate that IS the named rule — one entry names one file, not a family of tails.</summary>
    private static IEnumerable<string> Named(IReadOnlyList<RuleCandidate> candidates, string entry) =>
        candidates
            .Where(candidate => candidate.WithinMount.Equals(entry, StringComparison.OrdinalIgnoreCase))
            .OrderBy(candidate => candidate.Path, StringComparer.Ordinal)
            .Select(candidate => candidate.Path);

    private static IEnumerable<string> Shuffled(IReadOnlyList<RuleCandidate> candidates, Random random) =>
        SeededShuffle.Of([.. candidates.Select(candidate => candidate.Path)], random);
}
