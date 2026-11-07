using CoaiMcp.Runners.Context;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What a gate with NO diff is judged against.
/// </summary>
/// <remarks>
/// <para>The plan and document stages review a document, so there is no change to select rules from
/// and the rules have to be named. Naming them is only half of it: the corpus plus the instruction
/// files is larger than the byte budget, so the list has to be an ORDER, or whatever sorts first
/// decides what a reviewer sees.</para>
/// <para>Two of these tests exist because the plan round said the ones originally proposed could not
/// fail: an existence check whose fixture WRITES every name it then looks for proves only that the
/// fixture and the table agree, and a pair of negative assertions cannot notice a tier that silently
/// emits one of its seven entries.</para>
/// </remarks>
public sealed class StageRulesTests : IDisposable
{
    private readonly string _repo = Directory.CreateTempSubdirectory("coai-stage-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_repo, recursive: true);
        }
        catch (IOException) { }
    }

    private void Write(string relative, string content)
    {
        var path = Path.Combine(_repo, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static string Filler(string marker, int bytes) => marker + new string('x', bytes);

    private void WriteMount() =>
        Write(".gitmodules", "[submodule \"conventions\"]\n path = .agents/conventions\n url = https://example.invalid/rules\n");

    /// <summary>Every rule of a tier, all the same size, so only the ORDER can decide who fits.</summary>
    private void WriteTier(IReadOnlyList<string> tier, int bytes = 10_000)
    {
        WriteMount();
        foreach (var entry in tier)
        {
            Write($".agents/conventions/{entry}", Filler(entry, bytes));
        }
    }

    private static IReadOnlyList<string> Mounted(IReadOnlyList<string> tier) =>
        [.. tier.Select(entry => $".agents/conventions/{entry}")];

    [Fact]
    public void APlanRound_GetsTheHighLevelRules_AndNotTheBuildRecipes()
    {
        WriteTier(StageRules.Plan);
        Write(".agents/conventions/csharp/dotnet-build.md", Filler("dotnet-build", 2_000));
        Write(".agents/conventions/csharp/nuget-packages.md", Filler("nuget", 2_000));
        Write(".agents/conventions/common/logging-serilog.md", Filler("logging", 2_000));

        var paths = RuleFiles.Collect(_repo, 200_000, RuleOrder.Staged(StageRules.Plan))
            .Files.Select(file => file.Path).ToList();

        paths.Should().Contain(".agents/conventions/common/reuse-first.md");
        paths.Should().NotContain([
            ".agents/conventions/csharp/dotnet-build.md",
            ".agents/conventions/csharp/nuget-packages.md",
            ".agents/conventions/common/logging-serilog.md",
        ]);
    }

    /// <summary>
    /// The whole tier, in its own order — not a subset that happens to satisfy a negative assertion.
    /// </summary>
    [Fact]
    public void TheWholeTier_IsEmittedInItsDeclaredOrder()
    {
        WriteTier(StageRules.Plan);

        var paths = RuleFiles.Collect(_repo, 200_000, RuleOrder.Staged(StageRules.Plan))
            .Files.Select(file => file.Path);

        paths.Should().Equal(Mounted(StageRules.Plan));
    }

    /// <summary>With no manifest the tier is the WHOLE of what the mount contributes.</summary>
    [Fact]
    public void WithNoManifest_NothingOutsideTheTierIsOffered()
    {
        WriteTier(StageRules.Document);
        Write(".agents/conventions/common/git-workflow.md", Filler("git-workflow", 2_000));
        Write(".agents/conventions/csharp/doctrine.md", Filler("csharp", 2_000));

        var bundle = RuleFiles.Collect(_repo, 200_000, RuleOrder.Staged(StageRules.Document));

        bundle.Files.Select(file => file.Path).Should().Equal(Mounted(StageRules.Document));
        bundle.Omitted.Should().BeEmpty();
    }

    [Fact]
    public void TheDocumentTier_IsNotThePlanTier()
    {
        StageRules.Document.Should().NotContain("common/testing.md");
        StageRules.Document.Should().NotContain("common/development-workflow.md");
        StageRules.Plan.Should().Contain("common/security.md");
    }

    /// <summary>
    /// The tier names rules that are really in the corpus this repository MOUNTS.
    /// </summary>
    /// <remarks>
    /// The test the plan round demanded, and the one the original proposal could not be: a fixture
    /// that writes every name it then asserts proves the fixture agrees with the table, not that the
    /// table agrees with reality. A rule renamed upstream, a typo, or a promotion that never reached
    /// this pin all fail HERE instead of silently dropping a rule out of a gate. Skipped rather than
    /// failed when the submodule is not populated — that is a checkout that has not run
    /// <c>git submodule update --init</c>, not a broken table.
    /// <para><b>The skip is not a hole in CI.</b> A code round asked whether a job that forgets the
    /// submodule would report green with this quietly skipped. It cannot: <c>.github/workflows/ci.yml</c>
    /// initialises <c>.agents/conventions</c> before the test step, and the two steps between them —
    /// <c>npm ci --prefix .agents/conventions</c> and <c>rules.mjs check</c> — both fail outright on an
    /// empty mount, so the suite never runs at all without it. The skip exists for a local offline
    /// checkout, and a local run is not the authoritative one.</para>
    /// </remarks>
    [Fact]
    public void EveryTierEntry_ResolvesInThePinnedConventionsMount()
    {
        var corpus = RequireTheMount("the stage-tier resolution test");

        foreach (var entry in StageRules.Plan.Concat(StageRules.Document).Concat(StageRules.Feature).Distinct(StringComparer.Ordinal))
        {
            File.Exists(Path.Combine(corpus, entry.Replace('/', Path.DirectorySeparatorChar)))
                .Should().BeTrue($"{entry} is named by a stage tier and must exist in the pinned corpus");
        }
    }

    /// <summary>
    /// The base and the eight tier rules spend exactly 80 023 characters of the budget — the number every
    /// other pin in this file stands on.
    /// </summary>
    /// <remarks>
    /// <para><b>The reproduction path, and the drift guard.</b> Measured 2026-10-16 on this repository with
    /// the pinned conventions (<c>e29883f8</c>): the instruction files and this repository's own rules
    /// (the "base", 17 883) plus the eight tier rules (62 061) were 79 944. Against the old 80 000 budget that
    /// left 56 and nothing outside the tier could fit; against 90 000 it left 10 056. <b>Re-baselined twice
    /// since:</b> 2026-10-18, when <c>.agents/PROJECT.md</c> named E5's <c>--kimi-clear-marker</c> (+23, base
    /// 17 906, 79 967), and 2026-10-19 on the merged tree of E6, when it also named the one-shot mode
    /// <c>--round-authors</c> (+19, which that file must do — <c>ARequestFaultIsNotAnOldBinaryTests</c>): base
    /// 17 925, the pair 79 986, 10 014 left; the three canary tests below were re-run unchanged. To reproduce, run
    /// this test and read the two numbers in its failure message.</para>
    /// <para>It goes red when the conventions pin moves a tier rule, or when this repository's own
    /// instruction files or rules change by one character. That is the point, not a nuisance: the tail
    /// canaries below are measured against this number, so a change here means they have to be
    /// re-measured in the same change (<c>research/RESULTS_rules_selection_budget.md</c>, last section).</para>
    /// <para><b>Re-baselined 2026-10-20 (E7 merged with `main`, which holds E6).</b> The base is 17 947: `.agents/PROJECT.md` must
    /// name every one-shot mode (`ARequestFaultIsNotAnOldBinaryTests`) and now names both `--round-authors` (E6, +19, 17 925 above) and
    /// `--backfill-authors` (E7, +22). The total is 80 008, leaving 9 992 of 90 000; the tail canaries below are unaffected (the three smallest
    /// tail rules are 8 683, the fourth adds 3 460). Measured once on the merged tree from this test's own failure message.</para>
    /// <para><b>Re-baselined again 2026-10-21 (E1.S2 of the native-Linux plan, merged with `main`).</b> `.agents/PROJECT.md` also names the one-shot mode
    /// `--host-facts` (+16, which it must — `ARequestFaultIsNotAnOldBinaryTests`). The base is 17 963, the total 80 024, leaving 9 976 of 90 000; the tail
    /// canaries below ran unchanged and green (the fourth smallest tail rule, 3 460, still does not fit). Measured on the merged tree: the pinned numbers above
    /// are the ones this test reads back.</para>
    /// <para><b>Re-baselined 2026-10-22 (E6.S1 merged after E1.S2, `todo/PLAN_the_round_counter_tail_E6.md`).</b> `.agents/PROJECT.md` also names
    /// `--round-counts-merged` (+25): the base is 17 988, the total 80 049, leaving 9 951 of 90 000; the tail canaries were re-run unchanged.</para>
    /// <para><b>Re-baselined 2026-11-01 (the conventions pin moved to release <c>105d53c</c>).</b> The tier is 62 035, 26 fewer than
    /// 62 061: the shared rules the tier names changed by 26 characters between <c>115bbf8</c> and <c>105d53c</c>. The base is unchanged at 17 963.
    /// The pair is 79 998, leaving 10 002 of 90 000 (9 976 until then). <c>common/test-leftovers.md</c> is now an 8 403-character core
    /// (8 483 bytes) and its detail, <c>common/test-leftovers-detail.md</c> (30 893 characters), does not fit any tail; <c>common/plan-change-control.md</c>
    /// (12 204 characters) is larger than the free tail, so no branch shows it — by owner decision it is for agents only, not for reviewers. The tail
    /// canaries below were re-measured in the same change.</para>
    /// <para><b>Re-baselined 2026-11-05 (E6.S1 merged with `main`, which holds the pin at release <c>5863c60</c> and the 2026-11-01 re-baseline above).</b> `.agents/PROJECT.md` names `--round-counts-merged` (+25) on top of main's base 17 963: the base is 17 988, the tier stays at main's 62 035, the pair is 80 023, leaving 9 977 of 90 000 (10 002 on main). Measured on the merged tree from this test's own failure message; the tail canaries below were re-run unchanged and green (all 22 <c>StageRulesTests</c>). The figures quoted below were measured at 10 002 free. Re-measured at 9 977 with <c>RuleBudgetMeasurementTests</c> (explicit): the same 7 of 100 names draw <c>common/test-leftovers.md</c> (indexes 0, 9, 21, 34, 36, 42, 95), the widest tail is 3 and the narrowest 1, the canary tail is <c>common/gpu-lease.md</c>, <c>csharp/nuget-packages.md</c> and <c>common/durable-status.md</c>; over 3 000 names it is drawn on 153 (5.1 %) and the tail takes one rule on 930, two on 1 833, three on 237.</para>
    /// <para><b>Re-baselined 2026-11-06 (the conventions pin moved to release <c>37b9aa5</c>; the owner raised the budget to 93 301).</b> The tier is 65 336,
    /// 3 301 more than 62 035: <c>common/testing.md</c>, a tier rule, grew by 3 301 characters. The base is unchanged at 17 988. The pair is 83 324, which
    /// at the old 90 000 left 6 676 and put <c>common/test-leftovers.md</c> on no branch; the owner's decision (after two consultants, consult 53578f8a) raised
    /// <c>RuleFiles.DefaultBudgetBytes</c> to 93 301, which leaves 9 977 (93 301 - 83 324), exactly what it was before this pin. Measured with
    /// <c>RuleBudgetMeasurementTests</c> (explicit): base 17 988, tier 65 336, free 9 977. The pins below were changed first and read back.</para>
    /// <para>The unit is the one the collector spends: characters of the line-ending-normalised text
    /// (<c>RuleFile.Text.Length</c>), which is what <c>RuleFiles.DefaultBudgetBytes</c> calls bytes. They
    /// differ for the non-ASCII characters in these files, and are equal on every platform.</para>
    /// </remarks>
    [Fact]
    public void AtTheDefaultBudget_TheBaseAndTheTier_SpendExactlyTheirPinnedBytes()
    {
        // No budget in the way, so the whole corpus is in the bundle and the split is ours to make.
        var everything = CollectTheRealCorpus("canary/the-tail", budget: 10_000_000, out _);

        var baseBytes = everything.Files
            .Where(file => file.Path.Equals(file.WithinMount, StringComparison.Ordinal))
            .Sum(file => file.Text.Length);
        var tierBytes = everything.Files
            .Where(file => !file.Path.Equals(file.WithinMount, StringComparison.Ordinal) && RuleOrder.IsTier(file.WithinMount))
            .Sum(file => file.Text.Length);

        var message = $"base {baseBytes} + tier {tierBytes} = {baseBytes + tierBytes} (pinned: 17 988 + 65 336 = 83 324 of 93 301, since the conventions pin moved to 37b9aa5 and the owner raised the budget on 2026-11-06; 17 988 + 62 035 = 80 023 of 90 000, since E6.S1's --round-counts-merged met the conventions pin 105d53c on 2026-11-05; 17 988 + 62 061 = 80 049 on the E6.S1 branch before the merge; 17 963 + 62 035 = 79 998 since the conventions pin moved to 105d53c on 2026-11-01; 17 963 + 62 061 = 80 024 until then; 17 947 + 62 061 = 80 008 before E1.S2's --host-facts; 17 925 + 62 061 = 79 986 before E7). "
            + "WHEN THIS FAILS the headroom the tail canaries are measured against has moved: re-measure "
            + "research/RESULTS_rules_selection_budget.md, then re-baseline the canaries below in the same change.";

        tierBytes.Should().Be(65_336, message);
        baseBytes.Should().Be(17_988, message);
        (baseBytes + tierBytes).Should().Be(83_324, message);
    }

    /// <summary>
    /// At the 90 KB budget the ROTATED TAIL fits at most three rules — and on the canary branch it is exactly
    /// these three.
    /// </summary>
    /// <remarks>
    /// <para><b>A canary over the real mounted corpus, re-measured 2026-10-16 at the operator's 90 000-byte
    /// budget (it was 80 000, and read zero).</b> Base plus tier came to 79 944 (80 008 since 2026-10-20)
    /// (<see cref="AtTheDefaultBudget_TheBaseAndTheTier_SpendExactlyTheirPinnedBytes"/>), leaving 9 992 of 90 000 (it was 10 056);
    /// the smallest rule outside the tier is <c>common/durable-status.md</c> at 2 197. At 80 000 the leftover
    /// was 56 and NOTHING in the tail fitted on any branch, so <c>common/test-leftovers.md</c> (4 706) reached
    /// no reviewer. Now the walk takes whatever fits in the order the branch draws: over 3 000 branch names
    /// one rule on 30 %, two on 62 %, three on 8 % — never four, because the three smallest rules together
    /// are 8 683 and the fourth smallest adds 3 460 (12 143 together) against 9 992.</para>
    /// <para><b>Two assertions.</b> On the canary branch the tail is EXACTLY <c>common/gpu-lease.md</c>,
    /// <c>csharp/nuget-packages.md</c> and <c>common/durable-status.md</c> in that order (88 730 characters
    /// in all) — the identities and the count, not a bound, because "at most three" is satisfied by a tail
    /// of the wrong three. And over one hundred fixed branch names no branch reaches more than three, the
    /// measured maximum, so a tail that grows — a bigger budget, a shrinking base or tier, a pin that moves a
    /// rule — fails here as news rather than as a defect.</para>
    /// <para><b>Re-measured 2026-11-01 (pin 105d53c).</b> The leftover is 10 002 of 90 000. The identities and the count are unchanged: the canary
    /// branch's tail is still these three rules, now 88 721 characters in all with the base and the tier. The smallest rules outside the tier are
    /// 2 197, 3 066, 3 421 and 3 460: the three smallest are 8 684 and the fourth adds 3 460 (12 144 together), still more than 10 002, so
    /// never four. Over 3 000 branch names: one rule on 35 %, two on 58 %, three on 7 %.</para>
    /// <para><b>Re-measured 2026-11-06 (pin 37b9aa5, budget 93 301).</b> The leftover is 9 977 of 93 301 (93 301 - 83 324), the figure it was before this pin
    /// (10 002 at pin 105d53c, 9 977 since the 2026-11-05 merge). The canary list is unchanged: the tail on the canary branch is still these three
    /// rules, now 92 047 characters in all with the base and the tier. The smallest rules outside the tier are still 2 197, 3 066, 3 421 and 3 460, so
    /// the three smallest are 8 684 and the fourth adds 3 460 (12 144 together), more than 9 977: never four. Over 3 000 branch names: one rule on 1 102
    /// (36.7 %), two on 1 655 (55.2 %), three on 243 (8.1 %); the widest tail among the hundred is 3.</para>
    /// <para>The identities are a pure function of (branch, rule name, sizes): the SHA-256 order is the same
    /// on every machine. They move when the budget, a rule's size or this repository's own files move —
    /// which the base-and-tier test above turns red first.</para>
    /// </remarks>
    [Fact]
    public void AtTheDefaultBudget_TheRotatedTail_FitsAtMostThreeRules()
    {
        var bundle = CollectTheRealCorpus("canary/the-tail", out var corpus);

        AssertTheWholeTierWasShown(bundle, corpus);

        TailOf(bundle).Should().Equal(
            ["common/gpu-lease.md", "csharp/nuget-packages.md", "common/durable-status.md"],
            "measured 2026-10-16 at 90 000 bytes and again 2026-11-06 at 93 301: these three, in the order the branch draws them. "
            + "WHEN THIS FAILS the tail has changed, which is news and not a defect. Re-baseline in the same "
            + "change: (1) re-measure research/RESULTS_rules_selection_budget.md, (2) update the claim in "
            + "RuleOrder.ForBranch, RuleFiles.DefaultBudgetBytes and research/module_runners.md, (3) re-pin "
            + "this list and the bound below.");

        var widest = TailsOfTheFixedBranches.Value.MaxBy(entry => entry.Tail.Count);

        widest.Tail.Should().HaveCountLessThanOrEqualTo(3,
            $"branch '{widest.Branch}' reached [{string.Join(", ", widest.Tail)}] and at most three rules can "
            + "fit: the leftover is 9 977 of 93 301 since 2026-11-06 (10 002 of 90 000 before the pin 37b9aa5, 9 977 again since the 2026-11-05 merge, 9 976 before 2026-11-01, 9 992 before 2026-10-21), and the smallest rules outside the tier are 2 197, 3 066, 3 421 "
            + "and 3 460. That number is the same on every platform since the collector stopped spending the "
            + "budget on carriage returns.");
    }

    /// <summary>
    /// The room the 90 KB budget bought exists on EVERY branch, not only on the canary one.
    /// </summary>
    /// <remarks>
    /// The other half of the canary above, and the one that would have caught the budget being
    /// lowered or the base growing back to the brim: a bound of "at most three" is satisfied by zero,
    /// which is exactly the state at 80 000 bytes — 56 bytes left, nothing eligible, a new rule that no
    /// reviewer could ever read. Measured 2026-10-16: at least one tail rule on every one of 3 000 branch
    /// names, so every one of these hundred.
    /// <para><b>Re-measured 2026-11-06 (pin 37b9aa5, budget 93 301).</b> Unchanged: the narrowest tail among the hundred is still one rule
    /// (<c>feat/branch-0</c>), with 9 977 free and the smallest rule outside the tier at 2 197. At the old 90 000 the free tail would have been 6 676
    /// and the smallest rule would still have fit; what the owner's raise protects is <c>common/test-leftovers.md</c>, pinned below.</para>
    /// </remarks>
    [Fact]
    public void AtTheDefaultBudget_EveryBranchReachesAtLeastOneTailRule()
    {
        var emptiest = TailsOfTheFixedBranches.Value.MinBy(entry => entry.Tail.Count);

        emptiest.Tail.Should().HaveCountGreaterThanOrEqualTo(1,
            $"branch '{emptiest.Branch}' reached no tail rule. The tier leaves about 10 KB of a 90 000-byte "
            + "budget and the smallest rule outside it is 2 197: if nothing fits, the base or the tier grew into "
            + "the headroom the operator paid for on 2026-10-16 (it was 56 bytes at 80 000, and a rule added to "
            + "the corpus then reached nobody)");
    }

    /// <summary>
    /// <c>common/test-leftovers.md</c> reaches reviewers through the tail — on the branches that draw it.
    /// </summary>
    /// <remarks>
    /// <para>The rule the budget was raised for. It is outside the tier, so it is drawn, not owed: over
    /// 3 000 branch names it was shown on 9.8 % when it was 4 706 bytes. Two branches that draw it and two that do not are
    /// pinned, because "reaches reviewers" is only true while BOTH halves hold — a rule that every
    /// branch shows belongs in the tier, and one no branch shows is the 80 000-byte state again. The
    /// branches are arbitrary and the draw is a pure function of (branch, rule), so the answer is the same
    /// on every machine.</para>
    /// <para>Fails when the rule is resized, the budget or base moves, or the hash order changes — each is
    /// news. Re-measure and re-pick the branches in the same change.</para>
    /// <para><b>Re-picked 2026-11-01 (pin 105d53c), from the documented population.</b> The rule is now an 8 403-character core (8 483 bytes) and
    /// the free tail is 10 002, so it fits only as the first rule the branch draws. Measured through the production collector over the
    /// hundred names of <c>TheFixedBranches</c> (<c>feat/branch-&lt;i&gt;</c> for even i, <c>fix/issue-&lt;i&gt;-thing</c> for odd, i in 0..99):
    /// <b>7 of 100 draw it</b> (indexes 0, 9, 21, 34, 36, 42, 95); over 3 000 names of the same shape 147 do (4.9 %). The pinned rows are the
    /// two LOWEST indexes that draw it (<c>feat/branch-0</c>, <c>fix/issue-9-thing</c>) and the two lowest that do not (<c>fix/issue-1-thing</c>,
    /// <c>feat/branch-2</c>). Both halves hold, so the rule is neither a tier rule nor out of reach.</para>
    /// <para><b>Re-measured 2026-11-06 (pin 37b9aa5, budget 93 301).</b> The core is 8 794 characters (8 403 until 2026-11-02, pin 5863c60), unchanged by this pin, and the owner's raise
    /// to 93 301 leaves 9 977 free, so it still fits only as the first rule a branch draws (9 977 - 8 794 = 1 183, less than the smallest other tail rule,
    /// 2 197). At 90 000 the free tail would have been 6 676 and the core would have fit on no branch: the four rows would have read false. Measured through the
    /// production collector with <c>RuleBudgetMeasurementTests</c>: 7 of the 100 fixed branches draw it, the same indexes as before (0, 9, 21, 34, 36, 42, 95);
    /// over 3 000 names it is drawn on 153 (5.1 %). The four pinned rows are unchanged and were confirmed, not assumed.</para>
    /// </remarks>
    [Theory]
    [InlineData("feat/branch-0", true)]
    [InlineData("fix/issue-9-thing", true)]
    [InlineData("fix/issue-1-thing", false)]
    [InlineData("feat/branch-2", false)]
    public void AtTheDefaultBudget_TheTestLeftoversRule_IsShownOnTheBranchesThatDrawIt(string branch, bool shown)
    {
        var bundle = CollectTheRealCorpus(branch, out _);

        TailOf(bundle).Contains("common/test-leftovers.md").Should().Be(shown,
            $"measured 2026-11-01 at 90 000 bytes (pin 105d53c) and again 2026-11-06 at 93 301 (pin 37b9aa5); 7 of the 100 fixed branches draw it: branch '{branch}' {(shown ? "draws" : "does not draw")} "
            + "common/test-leftovers.md into the rotated tail");
    }

    /// <summary>
    /// A fixed, arbitrary set of branch names — the first hundred of the three thousand the measurement
    /// used (<c>feat/branch-&lt;i&gt;</c> for even i, <c>fix/issue-&lt;i&gt;-thing</c> for odd).
    /// </summary>
    private static IEnumerable<string> TheFixedBranches =>
        Enumerable.Range(0, 100).Select(i => i % 2 == 0 ? $"feat/branch-{i}" : $"fix/issue-{i}-thing");

    /// <summary>
    /// The tail the PRODUCTION collector selects for each of <see cref="TheFixedBranches"/>, worked out ONCE
    /// for the class.
    /// </summary>
    /// <remarks>
    /// The bound test and the reachability test both ask the same hundred questions of the same corpus, and
    /// each question is a full read of every rule file. Neither <c>RuleFiles</c> nor <c>RuleOrder</c> takes
    /// the files' text from memory, and a production seam added only for a test is the wrong trade, so the
    /// saving is taken at this level: one pass shared by both tests (a hundred collections instead of two
    /// hundred) with every assertion unchanged. <c>PublicationOnly</c> so that a failure — or the skip a
    /// missing mount raises — is not cached for the tests that follow.
    /// </remarks>
    private static readonly Lazy<IReadOnlyList<(string Branch, IReadOnlyList<string> Tail)>> TailsOfTheFixedBranches =
        new(() => [.. TheFixedBranches.Select(branch => (Branch: branch, Tail: TailOf(CollectTheRealCorpus(branch, out _))))],
            LazyThreadSafetyMode.PublicationOnly);

    /// <summary>
    /// The real corpus this repository mounts, through the PRODUCTION collector at the default budget.
    /// </summary>
    /// <remarks>
    /// Through the PRODUCTION collector, not a byte-count beside it. Three reviewers made the same
    /// point and they were right: a parallel calculation certifies its own arithmetic, so if Collect
    /// ever changes how it measures or skips, a canary could pass while a tail rule really is selected —
    /// or fail while none is. It also makes the test read whatever the platform reads. The branch name
    /// decides only WHICH tail rules are drawn; the tier is fixed on every branch.
    /// <c>TwoBranches_SeeDifferentTails</c> in RuleOrderTests covers the branch-sensitivity of the ORDER.
    /// </remarks>
    private static RuleBundle CollectTheRealCorpus(string branch, out string corpus) =>
        CollectTheRealCorpus(branch, RuleFiles.DefaultBudgetBytes, out corpus);

    /// <summary>The same, at a budget the caller names — the measuring test needs no budget in the way.</summary>
    private static RuleBundle CollectTheRealCorpus(string branch, int budget, out string corpus)
    {
        corpus = RequireTheMount("the rotated-tail canary");
        var repoRoot = Directory.GetParent(corpus)!.Parent!.FullName;

        return RuleFiles.Collect(repoRoot, budget, RuleOrder.ForBranch(branch));
    }

    /// <summary>
    /// "Nothing more fits after the tier" says nothing until the tier really was selected — and a bundle
    /// holding one tail rule and no tier rule would have satisfied a bare NotBeEmpty. The expectation is
    /// derived from the production table rather than retyped.
    /// </summary>
    private static void AssertTheWholeTierWasShown(RuleBundle bundle, string corpus) =>
        MountRulesOf(bundle).Where(RuleOrder.IsTier).Should().BeEquivalentTo(
            RuleOrder.TierRules.Where(entry => File.Exists(Path.Combine(corpus, Native(entry)))),
            "the tier is what spends the budget, so every tier rule present in the corpus must be shown");

    /// <summary>
    /// The same corpus reaches the same reviewers, whichever line endings the checkout has.
    /// </summary>
    /// <remarks>
    /// <para><b>It did not, and the cost was a rule nobody received.</b> The budget was spent on
    /// <c>text.Length</c> over what the file holds on disk, and a CRLF checkout carries one extra
    /// character per line — about 1 KB across the tier alone, against a margin of a few hundred
    /// bytes. So a Windows machine's reviewers were handed SEVEN tier rules where a Linux
    /// machine's were handed eight, for the same commit.</para>
    /// <para><b>Why it went unseen.</b> Every job in <c>ci.yml</c> is <c>ubuntu-latest</c>; the only
    /// Windows legs are the release matrix, so the one check that could see this ran on a tag and
    /// nowhere else. The commit that last moved this cliff said as much — <i>"this worktree is CRLF
    /// … so a local run cannot answer the question"</i> — and measured the LF delta alone. It was a
    /// fair reading of what CI showed and the question was being asked in the one place nothing
    /// reported.</para>
    /// <para>Asserted as an EQUALITY between two corpora rather than a count, because a count would
    /// pass the day both sides lose the same rule.</para>
    /// </remarks>
    [Fact]
    public void ACrlfCheckout_ReachesTheSameRulesAsAnLfOne()
    {
        // Sized to STRADDLE the cliff, because that is the only place the difference shows:
        // 400 lines of 19 characters is 8 000 bytes under LF and 8 400 under CRLF, so a budget
        // of 16 400 fits two rules one way and one the other. A fixture whose sizes do not
        // straddle it passes against the defect - the first version of this one did.
        var tier = RuleOrder.TierRules;
        WriteMount();
        foreach (var entry in tier)
        {
            Write($".agents/conventions/{entry}", Lines(400, "\n"));
        }
        var underLf = MountRulesOf(RuleFiles.Collect(_repo, 16_400, RuleOrder.Walk));

        foreach (var entry in tier)
        {
            Write($".agents/conventions/{entry}", Lines(400, "\r\n"));
        }
        var underCrlf = MountRulesOf(RuleFiles.Collect(_repo, 16_400, RuleOrder.Walk));

        underLf.Should().HaveCount(2,
            "the fixture is only meaningful while it straddles the cliff, and this is the half "
            + "that says so: if the LF side stops fitting two rules the sizes above need re-deriving");

        underCrlf.Should().BeEquivalentTo(underLf,
            "the same rules must reach a reviewer whichever line endings the checkout has — a budget "
            + "spent on carriage returns hands a Windows machine fewer rules than a Linux one for the "
            + "same commit, and the only check that can see it runs on a release");
    }

    /// <summary>Lines of a fixed width, with the endings a caller asks for.</summary>
    private static string Lines(int count, string ending) =>
        string.Concat(Enumerable.Repeat(new string('x', 19) + ending, count));

    /// <summary>
    /// Given room, the collector DOES reach past the tier — so the canary above measures a budget,
    /// not a broken mechanism.
    /// </summary>
    /// <remarks>
    /// The other half of the pair a code round asked for. Without this, a green
    /// <c>AtTheDefaultBudget_TheRotatedTail_FitsAtMostThreeRules</c> is equally consistent with "the tail is out of
    /// budget" and "the tail is never collected at all", and only the first is true.
    /// </remarks>
    [Fact]
    public void GivenRoom_TheCollectorDoesReachPastTheTier()
    {
        WriteMount();
        Write(".agents/conventions/common/security.md", Filler("security", 1_000));
        Write(".agents/conventions/common/git-workflow.md", Filler("git-workflow", 1_000));
        Write(".agents/conventions/common/pull-requests.md", Filler("pull-requests", 1_000));

        var paths = RuleFiles.Collect(_repo, 200_000, RuleOrder.ForBranch("feat/room")).Files
            .Select(file => file.WithinMount).ToList();

        paths.Should().Contain("common/security.md", "the tier rule leads");
        paths.Should().Contain(["common/git-workflow.md", "common/pull-requests.md"],
            "and the rules outside the tier follow it when the budget allows");
    }

    /// <summary>The rules a bundle took from a MOUNT, whatever their depth.</summary>
    /// <remarks>
    /// <c>RuleFiles.Candidate</c> strips the mount prefix and leaves a non-mounted file's
    /// <c>WithinMount</c> equal to its <c>Path</c>, so the difference between the two IS the
    /// production answer to "did this come from the mount". A code round caught the earlier
    /// version matching exactly two path segments against four hard-coded directory names: the
    /// walk sets <c>RecurseSubdirectories</c>, so splitting a rule into <c>common/testing/*.md</c>
    /// — the whole point of the modularization follow-up — produced selected files this dropped
    /// before counting, which would have kept the canary green on the very day it must fire.
    /// </remarks>
    private static IEnumerable<string> MountRulesOf(RuleBundle bundle) =>
        bundle.Files
            .Where(file => !file.Path.Equals(file.WithinMount, StringComparison.Ordinal))
            .Select(file => file.WithinMount);

    /// <summary>Those of them the ORDER does not rank as tier — the rotated tail, as selected.</summary>
    private static IReadOnlyList<string> TailOf(RuleBundle bundle) =>
        [.. MountRulesOf(bundle).Where(within => !RuleOrder.IsTier(within))];

    /// <summary>
    /// A mounted rule in a SUBDIRECTORY is still a tail rule the canary must count.
    /// </summary>
    /// <remarks>
    /// The case rule modularization creates, and the one the canary exists to notice:
    /// <c>RuleFiles</c> walks the mount with <c>RecurseSubdirectories = true</c>, so splitting
    /// <c>common/testing.md</c> into <c>common/testing/*.md</c> yields selected files three
    /// segments deep. A classifier that matches exactly two segments drops them before counting,
    /// so the day capacity finally appears the canary stays green and the record stays stale.
    /// </remarks>
    [Fact]
    public void TheTailCount_IncludesAMountedRuleAtAnyDepth()
    {
        WriteMount();
        Write(".agents/conventions/common/security.md", Filler("security", 1_000));
        Write(".agents/conventions/common/reliability/retries.md", Filler("retries", 1_000));

        var bundle = RuleFiles.Collect(_repo, 200_000, RuleOrder.ForBranch("feat/deep"));

        bundle.Files.Select(file => file.WithinMount).Should().Contain("common/reliability/retries.md",
            "the collector recurses, so the nested rule IS selected");
        TailOf(bundle).Should().Contain("common/reliability/retries.md",
            "and a rule outside the tier is tail however deep it sits");
    }

    /// <summary>
    /// The tail is everything the ORDER does not rank as tier — not what another stage's tier names.
    /// </summary>
    /// <remarks>
    /// The canary collects with <see cref="RuleOrder.ForBranch"/>, whose tier is
    /// <c>RuleOrder</c>'s own table. <c>StageRules.Plan</c> is the PLAN stage's tier and names
    /// rules that table does not — <c>development-workflow.md</c>, <c>planning-docs.md</c>. Counting
    /// with the wrong list makes the canary lenient in the one direction that matters: a genuinely
    /// reachable tail rule is read as tier and never counted.
    /// </remarks>
    [Fact]
    public void TheTailCount_UsesTheOrdersOwnTier_NotThePlanStages()
    {
        WriteMount();
        Write(".agents/conventions/common/security.md", Filler("security", 1_000));
        Write(".agents/conventions/common/development-workflow.md", Filler("workflow", 1_000));

        var bundle = RuleFiles.Collect(_repo, 200_000, RuleOrder.ForBranch("feat/tier"));

        TailOf(bundle).Should().Contain("common/development-workflow.md",
            "the branch order does not rank it as tier, so it is tail however the plan stage reads it");
        TailOf(bundle).Should().NotContain("common/security.md", "which the order DOES rank as tier");
    }

    /// <summary>
    /// An oversized rule is SKIPPED and the walk goes on — so a smaller rule behind it is still shown.
    /// </summary>
    /// <remarks>
    /// <para>The property the canary's conclusion rests on and nothing was proving. It says
    /// <c>Collect</c> tries all 24 tail rules and omits them; if <c>Collect</c> ever stopped at the
    /// first file that does not fit instead of skipping it, the canary would still pass with zero tail
    /// rules and <see cref="GivenRoom_TheCollectorDoesReachPastTheTier"/> would still pass too, because
    /// its budget is large enough that nothing in it is ever oversized. A code round found that gap.</para>
    /// <para>It matters beyond the canary: after rule modularization an eligible small rule will sit
    /// behind a large one in the order, and stop-at-first would make it permanently unreachable while
    /// every test stayed green. <see cref="RuleOrder.Walk"/> is used so the order is the alphabet and
    /// the fixture reads as it runs.</para>
    /// </remarks>
    [Fact]
    public void AnOversizedRule_DoesNotStopTheWalk()
    {
        WriteMount();
        Write(".agents/conventions/common/security.md", Filler("security", 1_000));
        Write(".agents/conventions/common/aaa-oversized.md", Filler("aaa", 40_000));
        Write(".agents/conventions/common/zzz-small.md", Filler("zzz", 1_000));

        // Room for the tier rule and the small one, nowhere near room for the big one between them.
        var bundle = RuleFiles.Collect(_repo, 5_000, RuleOrder.Walk);

        bundle.Omitted.Should().Contain(".agents/conventions/common/aaa-oversized.md",
            "it does not fit, so it is omitted rather than truncated");
        TailOf(bundle).Should().Contain("common/zzz-small.md",
            "and the walk continues past it to a rule that does fit");
    }

    /// <summary>
    /// A missing mount is a developer's offline checkout — and in CI it is a broken job, not a skip.
    /// </summary>
    /// <remarks>
    /// A code round asked, twice and in two stages, whether this skip could hide the canary behind a
    /// green CI run. In this repository it cannot: <c>.github/workflows/ci.yml</c> initialises
    /// <c>.agents/conventions</c> before the test step, and the two steps between them —
    /// <c>npm ci --prefix .agents/conventions</c> and <c>rules.mjs check</c> — both fail outright on
    /// an empty mount, so the job dies before the suite starts. But that is the WORKFLOW's guarantee,
    /// not this test's, and a guarantee a test leans on silently is one a workflow edit can remove in
    /// silence. So the skip is scoped to a machine that is not CI; where <c>CI</c> is set, the same
    /// condition fails and names the step to run.
    /// </remarks>
    private static string RequireTheMount(string what)
    {
        const string Setup = "git submodule update --init --depth 1 .agents/conventions";

        if (MountedCorpus() is { } corpus)
        {
            return corpus;
        }

        if (Environment.GetEnvironmentVariable("CI") is { Length: > 0 } ci
            && !ci.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Fail($"{what} needs the conventions mount and CI is the authoritative run: {Setup}");
        }

        Assert.Skip($"{what} needs the conventions mount, absent in this checkout: {Setup}");

        // Unreachable: both branches above throw. Assert.Fail and Assert.Skip are not annotated
        // as such, so the compiler still wants a value.
        return string.Empty;
    }

    /// <summary>A mount-relative rule name as this platform spells a path.</summary>
    private static string Native(string withinMount) =>
        withinMount.Replace('/', Path.DirectorySeparatorChar);

    /// <summary>The mounted conventions of THIS repository, or nothing when it is not populated.</summary>
    private static string? MountedCorpus()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var mount = Path.Combine(dir.FullName, ".agents", "conventions");
            if (Directory.Exists(Path.Combine(mount, "common")))
            {
                return mount;
            }
        }

        return null;
    }

    /// <summary>
    /// A tier that matches nothing is a mount that contributes nothing — not an empty round.
    /// </summary>
    [Fact]
    public void ATierThatMatchesNothing_StillShowsTheRepositorysOwnRules()
    {
        WriteMount();
        Write("AGENTS.md", "How to work here");
        Write(".agents/rules/common/vendor-routing.md", "Which CLI runs which model");
        Write(".agents/conventions/common/something-else-entirely.md", Filler("other", 1_000));

        var bundle = RuleFiles.Collect(_repo, 200_000, RuleOrder.Staged(StageRules.Plan));

        bundle.Files.Select(file => file.Path).Should().Equal([
            "AGENTS.md", ".agents/rules/common/vendor-routing.md",
        ]);
    }

    /// <summary>
    /// One tier entry names one rule, not every file that ends with the same words.
    /// </summary>
    [Fact]
    public void ATierEntry_DoesNotMatchTheSameNameInAnotherDirectory()
    {
        WriteMount();
        Write(".agents/conventions/common/security.md", Filler("real", 1_000));
        Write(".agents/conventions/common/legacy/common/security.md", Filler("impostor", 1_000));

        var paths = RuleFiles.Collect(_repo, 200_000, RuleOrder.Staged(StageRules.Plan))
            .Files.Select(file => file.Path);

        paths.Should().Equal([".agents/conventions/common/security.md"]);
    }

    /// <summary>
    /// A tier rule the budget dropped is NOT counted as one the reviewer was shown.
    /// </summary>
    /// <remarks>
    /// The coverage sentence answers "how much of my tier am I holding", so it is counted from what
    /// was RENDERED. Counted from what the tree contains, a prompt would say "all seven are below"
    /// over a section showing two — and a reviewer's silence about the other five would read as
    /// compliance, which is the exact failure the sentence exists to prevent. (codex, code round.)
    /// </remarks>
    [Fact]
    public void ATierRuleTheBudgetDropped_IsNotCountedAsShown()
    {
        WriteTier(StageRules.Plan);

        // Room for two of the seven.
        var bundle = RuleFiles.Collect(_repo, 21_000, RuleOrder.Staged(StageRules.Plan));

        bundle.MatchedCount(StageRules.Plan).Should().Be(2);
        bundle.TierCoverage(StageRules.Plan).Should().Contain($"2 of the {StageRules.Plan.Length}");
        bundle.TierCoverage(StageRules.Plan).Should().NotContain("All ");
    }

    /// <summary>
    /// The tier is an order because the budget is real: the first entries fit, the rest are named.
    /// </summary>
    [Fact]
    public void TheTierOrder_DecidesWhoFitsTheBudget_NotTheAlphabet()
    {
        WriteTier(StageRules.Plan);

        var bundle = RuleFiles.Collect(_repo, 21_000, RuleOrder.Staged(StageRules.Plan));

        // Tier order takes security and reuse-first; the alphabet would have taken coding-style and
        // development-workflow, which is how `security.md` came to be shown to nobody before 2026-09-06.
        bundle.Files.Select(file => file.Path).Should().Equal([
            ".agents/conventions/common/security.md",
            ".agents/conventions/common/reuse-first.md",
        ]);
        bundle.Omitted.Should().Contain(".agents/conventions/common/coding-style.md");
    }

    /// <summary>
    /// The instruction files and this repository's own rules are not free.
    /// </summary>
    /// <remarks>
    /// They lead every order and are never dropped, which reads as "they always fit" — but they are
    /// collected under the SAME budget, so a large one leaves less for the tier. The plan round called
    /// that contradiction out; this pins the real behaviour, and it is the honest one: whole files, in
    /// order, and everything past the budget named rather than silently absent.
    /// </remarks>
    [Fact]
    public void TheBaseContextCountsTowardTheBudget_AndTheTierIsTruncatedWithItsOmissionsNamed()
    {
        WriteTier(StageRules.Plan);
        Write("AGENTS.md", Filler("agents", 15_000));
        Write(".agents/rules/common/vendor-routing.md", Filler("routing", 5_000));

        var bundle = RuleFiles.Collect(_repo, 31_000, RuleOrder.Staged(StageRules.Plan));

        bundle.Files.Select(file => file.Path).Should().Equal([
            "AGENTS.md",
            ".agents/rules/common/vendor-routing.md",
            ".agents/conventions/common/security.md",
        ]);
        bundle.Omitted.Should().Contain(".agents/conventions/common/reuse-first.md");
        bundle.Bytes.Should().BeLessThanOrEqualTo(31_000);
    }
}
