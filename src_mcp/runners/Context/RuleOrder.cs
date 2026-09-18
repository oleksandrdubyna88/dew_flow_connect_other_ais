using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

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
/// <c>testing.md</c>, <c>security.md</c>, <c>reuse-first.md</c> and all three doctrines until
/// 2026-09-06; leaving it to a per-round SHUFFLE fixed the starvation by making the gate unstable
/// instead, which was its own defect and is what <see cref="ForBranch"/> replaced on 2026-09-15.</para>
/// <para>The instruction files and the repository's OWN rules are not ordered here. They come first
/// and are never dropped — see <c>RuleFiles.Candidates</c> — though they are not free: they are
/// collected under the same budget, so a large one leaves less for the mount.</para>
/// </remarks>
public sealed record RuleOrder
{
    private readonly Func<IReadOnlyList<RuleCandidate>, IEnumerable<string>> _mount;

    private RuleOrder(Func<IReadOnlyList<RuleCandidate>, IEnumerable<string>> mount) => _mount = mount;

    /// <summary>The deterministic order with no branch to rotate the tail by.</summary>
    public static RuleOrder Walk { get; } = ForBranch(string.Empty);

    /// <summary>
    /// The order for one branch: the tier first, then the rest rotated by the branch.
    /// </summary>
    /// <remarks>
    /// <para><b>What this replaced.</b> Until 2026-09-15 the mount was SHUFFLED, with
    /// <c>Random.Shared</c>, once per round. That was installed on 2026-09-06 against a measured
    /// starvation — in plain enumeration order the two longest files took a quarter of the budget and
    /// <c>testing.md</c>, <c>security.md</c>, <c>reuse-first.md</c> and every language doctrine reached
    /// no reviewer at all — and it worked, at the price of a gate whose answer changed between two
    /// rounds of ONE fix. That price is what the whole plan was opened to stop paying.</para>
    /// <para><b>Why the branch, and not simply a fixed order.</b> The tier fills the budget: measured
    /// against the real corpus at 13 files and 78 672 of 80 000 bytes
    /// (<c>research/RESULTS_rules_selection_budget.md</c>), which leaves the other 24 rules shown to
    /// nobody, ever, on the path a code round actually takes today. The draw covered them by rotating.
    /// A BRANCH is what a round is about, and it does not change while a developer fixes what the last
    /// round found — so rotating the tail by it removes the instability without giving up the draw's
    /// coverage in principle.</para>
    /// <para><b>In principle, and measured: in THIS repository the rotation currently reaches almost
    /// nothing.</b> The base and the tier spend 77 562 of the 80 000-byte budget, leaving 2 438 bytes
    /// against a smallest non-tier rule of 2 213 — so the tail takes ONE rule and the rest are skipped
    /// as oversized every round. That is the same number on every platform since 2026-09-18, when
    /// <c>RuleFiles.Read</c> stopped spending the budget on carriage returns: it read 78 855 on a CRLF
    /// checkout until then, which left 1 145 bytes, nothing fitting, and a Windows machine handed one
    /// rule fewer than a Linux one for the same commit. That one is <c>common/durable-status.md</c> on EVERY branch, being the
    /// only rule small enough to be eligible, so nothing actually rotates even there: the order is
    /// correct and costs nothing, it simply has no room to act yet. What would give it room is
    /// rule modularization or the resolver, both recorded as follow-ups.
    /// <c>research/RESULTS_rules_selection_budget.md</c> carries the numbers and
    /// <c>StageRulesTests.TheRotatedTail_CurrentlyFitsAtMostOneRule</c> fails the day this
    /// changes.</para>
    /// <para><b>Why SHA-256 and not <see cref="object.GetHashCode"/>.</b> .NET randomises string
    /// hashing per PROCESS, so a GetHashCode-ordered tail would differ between two rounds of one fix
    /// on one machine — the exact defect this removes, reintroduced by its own fix. This is a pure
    /// function of (branch, rule name): same inputs, same order, on every machine, in every process,
    /// for ever. Nothing here reads a clock, a counter or a random source, and anyone holding the
    /// branch name can reproduce the order exactly.</para>
    /// </remarks>
    public static RuleOrder ForBranch(string branch)
    {
        // Trimmed, because the branch arrives as a tool argument and " fix/x " must not be a
        // different rule order from "fix/x" — that would be this epic's own defect, returned through
        // the front door. NOT lower-cased: git refs are case-sensitive, so `fix/A` and `fix/a` are two
        // branches and folding them would be a lie about which change is being reviewed.
        var key = branch.Trim();

        return new(candidates => candidates
            .OrderBy(candidate => Tier(candidate.WithinMount))
            .ThenBy(candidate => TailKey(key, candidate.WithinMount), StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Path, StringComparer.Ordinal)
            .Select(candidate => candidate.Path));
    }

    /// <summary>
    /// The order for a gate with NO diff: the named tier, and nothing else from the mount.
    /// </summary>
    /// <remarks>
    /// <para>The one order here that FILTERS, and it filters ONLY. A plan or document round has no
    /// change to select from, so the mount's other rules are not "lower priority" — they are not what
    /// the stage is judged against at all, and letting them in would spend a budget the tier needs.</para>
    /// <para><b>It is not the epic 2 ordering.</b> An earlier draft of this comment claimed the same
    /// list would "order a manifest instead of filtering"; a code round pointed out that it cannot —
    /// handed a manifest carrying rules outside the tier, this would silently DROP them. The tier list
    /// is shared with that future ordering; this entry point is not. Epic 2 adds its own, and until it
    /// does, nothing here may be called with a manifest.</para>
    /// <para><b>An entry that matches nothing is skipped in silence, and a tier that matches nothing
    /// yields nothing.</b> That is not a failure and it is not softened by a fallback: falling back to
    /// <see cref="Walk"/> would hand a plan reviewer the code rules the tier exists to exclude. What
    /// the reviewer sees in that case is the instruction files and this repository's OWN rules — which
    /// are outside every order — and none of the family's. A caller that must tell "the tier was
    /// satisfied" from "the tier matched nothing" reads the bundle: the mount contributed no file.
    /// <c>StageRulesTests.EveryTierEntry_ResolvesInThePinnedConventionsMount</c> is what keeps a typo
    /// or an upstream rename from reaching that silence.</para>
    /// </remarks>
    public static RuleOrder Staged(IReadOnlyList<string> tier)
    {
        // Snapshotted: a caller's array stays theirs to mutate, and an order that changed under a
        // later round would be exactly the instability this whole plan exists to remove.
        string[] entries = [.. tier];

        return new(candidates => entries.SelectMany(entry => Named(candidates, entry)));
    }

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
    /// <para><b>Public because a second copy of this list is the defect it would cause.</b> A code
    /// round found the canary in <c>StageRulesTests</c> classifying rules against a hand-typed tier
    /// assembled from <c>StageRules.Plan</c> — a DIFFERENT stage's tier — so a genuinely reachable
    /// tail rule was read as tier and never counted. Anything that must tell tier from tail asks
    /// <see cref="IsTier"/> rather than keeping its own list.</para>
    /// </remarks>
    public static ImmutableArray<string> TierRules { get; } =
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

    /// <summary>Whether a mount-relative rule name is a tier rule, by the comparison the order uses.</summary>
    public static bool IsTier(string withinMount) => Tier(withinMount) < TierRules.Length;

    /// <remarks>
    /// <para>One comparison rule for the whole pipeline: <c>RuleFiles.FolderFiles</c> de-duplicates
    /// and sorts its candidates with <see cref="StringComparer.OrdinalIgnoreCase"/>, so a
    /// case-SENSITIVE tier match here would be a second rule inside one collection — and the rule it
    /// would starve is whichever one somebody committed with a capital letter.</para>
    /// <para>The ordinal pass at the end is what keeps the order TOTAL: two paths differing only in
    /// case tie under the first comparer, and a tie is where an order stops being deterministic.</para>
    /// </remarks>
    private static int Tier(string withinMount)
    {
        var rank = TierRules.IndexOf(withinMount, 0, TierRules.Length, StringComparer.OrdinalIgnoreCase);

        return rank < 0 ? TierRules.Length : rank;
    }

    /// <summary>
    /// Every candidate that IS the named rule — one entry names one file, not a family of tails.
    /// </summary>
    /// <remarks>
    /// A code round asked for <c>Take(1)</c> here, against two mounts both carrying
    /// <c>common/security.md</c>. Writing that test showed the state is unreachable: a mount is only
    /// collected when it sits at one of <c>RuleFiles.RuleFolders</c>' known paths, and two mounts
    /// cannot share one path — the test passed with the guard removed, which is the definition of a
    /// guard worth nothing. Left ungated rather than carrying defensive code no input can reach.
    /// </remarks>
    private static IEnumerable<string> Named(IReadOnlyList<RuleCandidate> candidates, string entry) =>
        candidates
            .Where(candidate => candidate.WithinMount.Equals(entry, StringComparison.OrdinalIgnoreCase))
            .OrderBy(candidate => candidate.Path, StringComparer.Ordinal)
            .Select(candidate => candidate.Path);

    /// <summary>
    /// A rule's place in the tail for one branch: stable, reproducible, and the same everywhere.
    /// </summary>
    /// <remarks>
    /// <para>Hex of the SHA-256 over <c>branch \0 rule</c>, ordered as text. The NUL is what stops
    /// <c>("fix/a", "b/c.md")</c> and <c>("fix/a\0b", "c.md")</c> hashing alike — and git itself
    /// forbids NUL and control characters in a ref name, so neither half can carry one.</para>
    /// <para>EMPTY for a tier rule and for an empty branch, so neither is hashed at all: the tier is
    /// fixed and must not depend on a branch even in principle, and a caller with no branch —
    /// <see cref="Walk"/> — gets the plain tier-then-ordinal walk its name promises. Both were code
    /// round findings: hashing a tier member coupled the fixed half to the rotating one, and a
    /// hash-ordered <c>Walk</c> was a walk in name only.</para>
    /// </remarks>
    private static string TailKey(string branch, string withinMount) =>
        branch.Length == 0 || IsTier(withinMount)
            ? string.Empty
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{branch}\0{withinMount}")));
}
