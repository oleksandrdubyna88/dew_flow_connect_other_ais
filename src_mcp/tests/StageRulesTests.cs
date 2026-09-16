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
        if (MountedCorpus() is not { } corpus)
        {
            // Assert.Skip throws, but it is not annotated as such — the return is for the compiler.
            Assert.Skip("the conventions submodule is not populated in this checkout");
            return;
        }

        foreach (var entry in StageRules.Plan.Concat(StageRules.Document).Distinct(StringComparer.Ordinal))
        {
            File.Exists(Path.Combine(corpus, entry.Replace('/', Path.DirectorySeparatorChar)))
                .Should().BeTrue($"{entry} is named by a stage tier and must exist in the pinned corpus");
        }
    }

    /// <summary>
    /// How much of the budget is left for the ROTATED TAIL once the tier has taken its share.
    /// </summary>
    /// <remarks>
    /// <para><b>A canary, and it is currently red in the honest sense: the answer is zero.</b> Measured
    /// against the real mounted corpus, 2026-09-16 — the instruction files, this repository's own rules
    /// and all eight tier rules come to about 78 900 of the 80 000-byte budget, leaving roughly 1 100
    /// bytes, while the SMALLEST rule outside the tier is <c>common/durable-status.md</c> at 2 247. So
    /// nothing in the tail fits, `Collect` skips each oversized file and keeps walking, and all 24 are
    /// omitted every round.</para>
    /// <para>The branch rotation therefore orders a queue nothing is ever taken from. It is not wrong
    /// and it costs nothing, but it is INERT at this corpus size — and the claim it was added to
    /// support, that different branches read different parts of the corpus, is true of the mechanism
    /// and false of this repository today. This test exists so that stops being invisible: if rule
    /// modularization or a smaller base ever makes room, it fails and somebody re-reads the record.</para>
    /// <para>It asserts the FACT, not an aspiration. A failure here is news, not a defect.</para>
    /// </remarks>
    [Fact]
    public void TheRotatedTail_CurrentlyFitsNothing_AndSaysSoOutLoud()
    {
        if (MountedCorpus() is not { } corpus)
        {
            Assert.Skip("the conventions submodule is not populated in this checkout");
            return;
        }

        var tier = StageRules.Plan.Concat(StageRules.Document)
            .Concat((string[])["csharp/doctrine.md", "rust/doctrine.md", "typescript/doctrine.md"])
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var repoRoot = Directory.GetParent(corpus)!.Parent!.FullName;

        // Through the PRODUCTION collector, not a byte-count beside it. Three reviewers made the same
        // point and they were right: a parallel calculation certifies its own arithmetic, so if Collect
        // ever changes how it measures or skips, this could pass while a tail rule really is selected —
        // or fail while none is. It also makes the test read whatever the platform reads, which matters
        // more here than anywhere: the leftover is ~1.1 KB under CRLF and ~2.4 KB under LF, and the
        // smallest tail rule sits between the two.
        var mountRules = RuleFiles
            .Collect(repoRoot, RuleFiles.DefaultBudgetBytes, RuleOrder.ForBranch("canary/the-tail"))
            .Files
            .Select(file => file.WithinMount)
            .Where(within => within.Split('/') is [("common" or "csharp" or "rust" or "typescript"), _])
            .ToList();

        mountRules.Should().NotBeEmpty("the tier itself is reachable — if this fails, something bigger broke");

        var reachedTail = mountRules.Where(within => !tier.Contains(within, StringComparer.OrdinalIgnoreCase)).ToList();

        reachedTail.Should().HaveCountLessThanOrEqualTo(1,
            "the rotated tail is effectively inert at this corpus size: the base and the eight tier "
            + "rules spend nearly the whole 80 000-byte budget, and every rule outside the tier is "
            + "larger than what is left — 0 of 24 fit under CRLF, 1 of 24 under LF, which is why this "
            + "is a bound and not an equality. WHEN THIS FAILS the tail has become genuinely reachable, "
            + "which is GOOD NEWS and not a defect: re-measure "
            + "research/RESULTS_rules_selection_budget.md, update the coverage claim in "
            + "RuleOrder.ForBranch and research/module_runners.md, and raise this bound in the same "
            + $"change. Reached this run: [{string.Join(", ", reachedTail)}]");
    }

    /// <summary>
    /// Given room, the collector DOES reach past the tier — so the canary above measures a budget,
    /// not a broken mechanism.
    /// </summary>
    /// <remarks>
    /// The other half of the pair a code round asked for. Without this, a green
    /// <c>TheRotatedTail_CurrentlyFitsNothing</c> is equally consistent with "the tail is out of
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
