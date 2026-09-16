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

        foreach (var entry in StageRules.Plan.Concat(StageRules.Document).Distinct(StringComparer.Ordinal))
        {
            File.Exists(Path.Combine(corpus, entry.Replace('/', Path.DirectorySeparatorChar)))
                .Should().BeTrue($"{entry} is named by a stage tier and must exist in the pinned corpus");
        }
    }

    /// <summary>
    /// The ROTATED TAIL fits at most one rule once the tier has taken its share of the budget.
    /// </summary>
    /// <remarks>
    /// <para><b>A canary over the real mounted corpus, measured 2026-09-16 — and the answer depends on
    /// the checkout's line endings.</b> The instruction files, this repository's own rules and all eight
    /// tier rules come to 78 855 of the 80 000-byte budget on a CRLF checkout and 77 562 under LF,
    /// leaving 1 145 or 2 438 bytes; the smallest rule outside the tier is
    /// <c>common/durable-status.md</c> at 2 247 or 2 213. So NOTHING in the tail fits on Windows and
    /// exactly ONE rule fits on Linux — and it is that same rule on every branch, being the only one
    /// small enough to be eligible. <c>Collect</c> skips an oversized file and keeps walking, so it
    /// tries all 24 and omits the rest.</para>
    /// <para>That is why this asserts a BOUND and not an equality: an equality would have to be
    /// written per platform, and a canary that passes on the author's machine and fails in CI is the
    /// defect it is here to catch. The branch rotation orders a queue at most one rule is ever taken
    /// from, so the claim it was added to support — that different branches read different parts of
    /// the corpus — is true of the MECHANISM and false of this repository today.</para>
    /// <para>It asserts the FACT, not an aspiration. A failure here is news, not a defect, and the
    /// assertion message carries the re-baselining steps for whoever trips it.</para>
    /// </remarks>
    [Fact]
    public void TheRotatedTail_CurrentlyFitsAtMostOneRule()
    {
        var corpus = RequireTheMount("the rotated-tail canary");
        var repoRoot = Directory.GetParent(corpus)!.Parent!.FullName;

        // Through the PRODUCTION collector, not a byte-count beside it. Three reviewers made the same
        // point and they were right: a parallel calculation certifies its own arithmetic, so if Collect
        // ever changes how it measures or skips, this could pass while a tail rule really is selected —
        // or fail while none is. It also makes the test read whatever the platform reads, which matters
        // more here than anywhere: the leftover straddles the smallest tail rule between CRLF and LF.
        // The branch name is arbitrary and the result does not depend on it: the tier is fixed on every
        // branch, and only ONE rule is small enough to be eligible for what is left, so every branch
        // reaches the same answer. TwoBranches_SeeDifferentTails in RuleOrderTests is what covers the
        // branch-sensitivity of the ORDER; this covers the budget.
        var bundle = RuleFiles.Collect(
            repoRoot, RuleFiles.DefaultBudgetBytes, RuleOrder.ForBranch("canary/the-tail"));

        // "Nothing fits after the tier" says nothing until the tier really was selected — and a bundle
        // holding one tail rule and no tier rule would have satisfied the NotBeEmpty this replaces.
        // A code round asked for exactly this, and the expectation is derived from the production
        // table rather than retyped.
        MountRulesOf(bundle).Where(RuleOrder.IsTier).Should().BeEquivalentTo(
            RuleOrder.TierRules.Where(entry => File.Exists(Path.Combine(corpus, Native(entry)))),
            "the tier is what spends the budget, so every tier rule present in the corpus must be shown");

        var reachedTail = TailOf(bundle);

        reachedTail.Should().HaveCountLessThanOrEqualTo(1,
            $"the tail reached [{string.Join(", ", reachedTail)}] and at most one rule can fit today. "
            + "WHEN THIS FAILS the tail has become genuinely reachable, which is GOOD NEWS and not a "
            + "defect. Re-baseline in the same change: (1) re-measure "
            + "research/RESULTS_rules_selection_budget.md, (2) update the claim in RuleOrder.ForBranch "
            + "and research/module_runners.md, (3) raise this bound. Why a bound and not an equality: "
            + "the base and the eight tier rules spend nearly the whole 80 000-byte budget and every "
            + "rule outside the tier is larger than what is left — 0 of 24 fit under CRLF, 1 of 24 "
            + "under LF");
    }

    /// <summary>
    /// Given room, the collector DOES reach past the tier — so the canary above measures a budget,
    /// not a broken mechanism.
    /// </summary>
    /// <remarks>
    /// The other half of the pair a code round asked for. Without this, a green
    /// <c>TheRotatedTail_CurrentlyFitsAtMostOneRule</c> is equally consistent with "the tail is out of
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
    /// - the whole point of the modularization follow-up - produced selected files this dropped
    /// before counting, which would have kept the canary green on the very day it must fire.
    /// </remarks>
    private static IEnumerable<string> MountRulesOf(RuleBundle bundle) =>
        bundle.Files
            .Where(file => !file.Path.Equals(file.WithinMount, StringComparison.Ordinal))
            .Select(file => file.WithinMount);

    /// <summary>Those of them the ORDER does not rank as tier - the rotated tail, as selected.</summary>
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
    /// The tail is everything the ORDER does not rank as tier - not what another stage's tier names.
    /// </summary>
    /// <remarks>
    /// The canary collects with <see cref="RuleOrder.ForBranch"/>, whose tier is
    /// <c>RuleOrder</c>'s own table. <c>StageRules.Plan</c> is the PLAN stage's tier and names
    /// rules that table does not - <c>development-workflow.md</c>, <c>planning-docs.md</c>. Counting
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
    /// An oversized rule is SKIPPED and the walk goes on - so a smaller rule behind it is still shown.
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
