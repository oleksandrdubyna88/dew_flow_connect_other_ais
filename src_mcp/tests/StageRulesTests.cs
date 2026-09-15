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
    /// `git submodule update --init`, not a broken table.
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
