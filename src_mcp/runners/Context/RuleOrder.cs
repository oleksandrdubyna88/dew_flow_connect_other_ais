using CoaiMcp.Core.Rounds;

namespace CoaiMcp.Runners.Context;

/// <summary>
/// The order the MOUNTED family rules are considered in, once the budget starts refusing them.
/// </summary>
/// <remarks>
/// <para>A separate type because the order is the whole question. The corpus is larger than the
/// budget and selection is whole-file, so whatever comes first is what a reviewer is judged against
/// and whatever comes last is never shown. Leaving that to the alphabet is what starved
/// <c>testing.md</c>, <c>security.md</c>, <c>reuse-first.md</c> and all four doctrines until
/// 2026-09-06; leaving it to <see cref="Drawn"/> fixed the starvation by making the gate unstable
/// instead, which is its own defect.</para>
/// <para>The instruction files and the repository's OWN rules are not ordered here. They come first
/// and they always fit — see <c>RuleFiles.Candidates</c>.</para>
/// </remarks>
public sealed class RuleOrder
{
    private readonly Func<IReadOnlyList<string>, IEnumerable<string>> _mount;

    private RuleOrder(Func<IReadOnlyList<string>, IEnumerable<string>> mount) => _mount = mount;

    /// <summary>The deterministic order: the same tree gives the same bundle, every time.</summary>
    public static RuleOrder Walk { get; } = new(ByPath);

    /// <summary>
    /// The 2026-09-06 draw: a different part of the family rules each round.
    /// </summary>
    /// <remarks>
    /// <para>Measured 2026-09-06: the family set is ~199 KB against an 80 KB budget, and in
    /// enumeration order the first two files take a quarter of it — so <c>testing.md</c>,
    /// <c>security.md</c>, <c>reuse-first.md</c> and all four language doctrines were never shown to
    /// any reviewer, ever. Not because the budget was small: because they were last in line, and the
    /// line never changed.</para>
    /// <para>Raising the budget cannot fix that; a different draw each round can. At 80 KB of 199 a
    /// round sees about two fifths of the family rules, so a given rule is shown roughly every second
    /// or third round. That was the operator's call and the right one at the time: a rule shown
    /// sometimes is infinitely more than a rule shown never.</para>
    /// <para>Its cost is the reason <see cref="Walk"/> exists. A developer who pushes a fix and runs a
    /// second round is answered out of a different part of the rule book, which reads as noise —
    /// so the draw is kept only until every deterministic order is in place, and then deleted.</para>
    /// </remarks>
    /// <param name="seed">Fixes the draw, for a test. Left alone in production.</param>
    // S2245 wants a cryptographic generator. It is wrong about this call: nothing here guards a
    // secret, and the draw decides only WHICH rule files a reviewer is shown when they do not all
    // fit. The seed parameter is the tell — it exists so a test can assert an exact order, which a
    // cryptographic generator cannot give at all.
#pragma warning disable S2245 // Random is not used for security here — see above.
    public static RuleOrder Drawn(int? seed) =>
        new(paths => SeededShuffle.Of(paths, seed is { } fixedSeed ? new Random(fixedSeed) : Random.Shared));
#pragma warning restore S2245

    /// <summary>Orders the mount's rule paths. Total, and never drops one.</summary>
    public IEnumerable<string> Mount(IReadOnlyList<string> paths) => _mount(paths);

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
    /// <para>Matched by path SUFFIX so one entry serves every mount layout, and unmatched names fall
    /// through to ordinal order rather than being dropped — a rule invented tomorrow is ranked last,
    /// never lost.</para>
    /// </remarks>
    private static readonly string[] Tiers =
    [
        "/doctrine.md",
        "common/security.md",
        "common/testing.md",
        "common/reuse-first.md",
        "common/coding-style.md",
        "common/knowledge-base.md",
    ];

    private static IEnumerable<string> ByPath(IReadOnlyList<string> paths) =>
        paths.OrderBy(Tier).ThenBy(path => path, StringComparer.Ordinal);

    private static int Tier(string path)
    {
        var rank = Array.FindIndex(Tiers, suffix => path.EndsWith(suffix, StringComparison.Ordinal));

        return rank < 0 ? Tiers.Length : rank;
    }
}
