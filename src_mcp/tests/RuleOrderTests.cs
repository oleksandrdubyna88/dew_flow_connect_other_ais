using CoaiMcp.Runners.Context;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Which rules survive a budget that cannot hold them all.
/// </summary>
/// <remarks>
/// <para>The family corpus is far larger than the 80 KB a round can carry, and selection is
/// whole-file — so the ORDER decides what a reviewer is judged against, and what it is never shown.
/// Measured 2026-09-06: in plain enumeration order the two longest files took a quarter of the
/// budget, and <c>testing.md</c>, <c>security.md</c>, <c>reuse-first.md</c> and all three language
/// doctrines reached no reviewer at all. The draw was installed against that, at the cost of a gate
/// whose answer changes between two rounds over one fix.</para>
/// <para>These tests pin the other cure: a priority that is written down, so the same tree gives the
/// same bundle and the rules a finding is most often written against are the ones that fit.</para>
/// </remarks>
public sealed class RuleOrderTests : IDisposable
{
    private readonly string _repo = Directory.CreateTempSubdirectory("coai-order-").FullName;

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

    /// <summary>
    /// The mount as it really is: the two longest rules, the two most-cited ones, two doctrines.
    /// </summary>
    /// <remarks>
    /// Sizes are the real ones, rounded — <c>development-workflow.md</c> 14 KB and
    /// <c>http-contracts.md</c> 11 KB are the pair that took a quarter of the budget by sorting
    /// first, and <c>testing.md</c> at 25 KB is the single largest rule in the corpus. A 45 000-byte
    /// budget holds the tiers exactly and refuses the alphabet, which is the whole point.
    /// </remarks>
    private void WriteMount() =>
        Write(".gitmodules", "[submodule \"conventions\"]\n path = .agents/conventions\n url = https://example.invalid/rules\n");

    private void WriteFamilyMount()
    {
        Write(".gitmodules", "[submodule \"conventions\"]\n path = .agents/conventions\n url = https://example.invalid/rules\n");
        Write(".agents/conventions/common/development-workflow.md", Filler("development-workflow", 14_000));
        Write(".agents/conventions/common/http-contracts.md", Filler("http-contracts", 11_000));
        Write(".agents/conventions/common/security.md", Filler("security", 8_500));
        Write(".agents/conventions/common/testing.md", Filler("testing", 25_000));
        Write(".agents/conventions/csharp/doctrine.md", Filler("csharp", 4_600));
        Write(".agents/conventions/rust/doctrine.md", Filler("rust", 4_450));
        Write(".agents/conventions/typescript/doctrine.md", Filler("typescript", 5_800));
    }

    [Fact]
    public void TheFallbackOrder_ShowsTheDoctrines_NotOnlyTheTwoLongestFiles()
    {
        WriteFamilyMount();

        var bundle = RuleFiles.Collect(_repo, 49_000, RuleOrder.Walk);

        bundle.Files.Select(file => file.Path).Should().Contain([
            ".agents/conventions/common/testing.md",
            ".agents/conventions/common/security.md",
            ".agents/conventions/csharp/doctrine.md",
            ".agents/conventions/typescript/doctrine.md",
        ]);
        bundle.Omitted.Should().Contain([
            ".agents/conventions/common/development-workflow.md",
            ".agents/conventions/common/http-contracts.md",
        ]);
    }

    /// <summary>
    /// Twenty rules, one branch, two rounds: the same bundle both times.
    /// </summary>
    /// <remarks>
    /// <para>The defect the whole plan was opened for. A developer pushes a fix, runs a second round,
    /// and is answered out of a different part of the rule book — because the mount was SHUFFLED per
    /// round. Asserted through the DEFAULT entry point, with no order argument, because the wiring is
    /// half the claim: an order that is deterministic in isolation proves nothing if `Collect` still
    /// reaches for the draw.</para>
    /// <para>Twenty equal-sized files against a budget holding four, so a shuffle is caught with
    /// probability about 1 − 1/4845 per run.</para>
    /// </remarks>
    [Fact]
    public void TwoRoundsOnOneBranch_ShowByteIdenticalRules()
    {
        WriteMount();
        for (var i = 0; i < 20; i++)
        {
            Write($".agents/conventions/common/rule-{i:00}.md", Filler($"rule{i:00}", 10_000));
        }

        var first = RuleFiles.Collect(_repo, 41_000, RuleOrder.ForBranch("fix/the-composer-stays-put"));
        var second = RuleFiles.Collect(_repo, 41_000, RuleOrder.ForBranch("fix/the-composer-stays-put"));

        second.Files.Select(file => file.Path).Should().Equal(first.Files.Select(file => file.Path));
        second.Omitted.Should().Equal(first.Omitted);

        // And through the DEFAULT overload, which is what a caller that names no order gets. A code
        // round asked for this: an order deterministic in isolation proves nothing about the wiring,
        // and a silent return to a per-round draw would leave every assertion above still green.
        var byDefault = RuleFiles.Collect(_repo, 41_000);
        RuleFiles.Collect(_repo, 41_000).Files.Select(file => file.Path)
            .Should().Equal(byDefault.Files.Select(file => file.Path));
    }

    /// <summary>
    /// Two branches read different parts of the corpus — the MECHANISM, on a fixture with room for it.
    /// </summary>
    /// <remarks>
    /// <para>The cost of a FIXED order, measured in
    /// <c>research/RESULTS_rules_selection_budget.md</c>: the tier fills the budget, so the other 24
    /// rules would never be shown to any code round. Ordering the tail by the branch answers that
    /// without the draw's instability, because a branch is what a round is about and it does not change
    /// between the rounds of one fix.</para>
    /// <para><b>What this does NOT say.</b> The fixture below writes twenty 10 KB rules under a
    /// 41 000-byte budget, so its tail has room. The real corpus does not: measured 2026-09-16, the
    /// base and the tier leave 1 145 bytes (2 438 under LF) against a smallest non-tier rule of 2 247
    /// (2 213), so the rotation reaches one rule on Linux and none on Windows. This test asserts the
    /// mechanism; <c>StageRulesTests.TheRotatedTail_CurrentlyFitsAtMostOneRule</c> asserts
    /// the fact.</para>
    /// </remarks>
    [Fact]
    public void TwoBranches_SeeDifferentTails_OnAFixtureWithRoomForThem()
    {
        WriteMount();
        for (var i = 0; i < 20; i++)
        {
            Write($".agents/conventions/common/rule-{i:00}.md", Filler($"rule{i:00}", 10_000));
        }

        var one = RuleFiles.Collect(_repo, 41_000, RuleOrder.ForBranch("fix/one"));
        var other = RuleFiles.Collect(_repo, 41_000, RuleOrder.ForBranch("feat/another"));

        other.Files.Select(file => file.Path)
            .Should().NotEqual(one.Files.Select(file => file.Path));
    }

    /// <summary>
    /// The tier is the tier on every branch — only the tail rotates.
    /// </summary>
    [Fact]
    public void TheTier_IsTheSameWhateverTheBranch()
    {
        WriteFamilyMount();

        var one = RuleFiles.Collect(_repo, 49_000, RuleOrder.ForBranch("fix/one"));
        var other = RuleFiles.Collect(_repo, 49_000, RuleOrder.ForBranch("feat/another"));

        foreach (var bundle in (RuleBundle[])[one, other])
        {
            bundle.Files.Select(file => file.Path).Should().Contain([
                ".agents/conventions/common/testing.md",
                ".agents/conventions/common/security.md",
                ".agents/conventions/csharp/doctrine.md",
                ".agents/conventions/rust/doctrine.md",
                ".agents/conventions/typescript/doctrine.md",
            ]);
        }
    }

    /// <summary>
    /// The tail order is the same on every machine and in every process.
    /// </summary>
    /// <remarks>
    /// This is why the order is a SHA-256 of the branch and the rule name rather than
    /// <see cref="string.GetHashCode()"/>: .NET randomises string hashing per PROCESS, so a
    /// GetHashCode-ordered tail would differ between two rounds of one fix on one machine — the exact
    /// defect this epic removes, reintroduced by the fix for it. The expected sequence is written
    /// down, so a change of hash cannot pass unnoticed.
    /// </remarks>
    [Fact]
    public void TheTailOrder_IsPinned_SoItCannotDriftBetweenProcesses()
    {
        WriteMount();
        foreach (var name in (string[])["alpha", "beta", "gamma", "delta"])
        {
            Write($".agents/conventions/common/{name}.md", Filler(name, 100));
        }

        var paths = RuleFiles.Collect(_repo, 200_000, RuleOrder.ForBranch("fix/the-draw-dies")).Files
            .Select(file => file.Path).ToList();

        // Deliberately NOT alphabetical. The first branch name tried here produced alpha-beta-delta-
        // gamma, which is exactly the order an implementation that ignored the key would give — a
        // test that cannot tell the two apart is worth nothing, so the fixture uses a branch whose
        // order only the hash can produce.
        paths.Should().Equal([
            ".agents/conventions/common/alpha.md",
            ".agents/conventions/common/gamma.md",
            ".agents/conventions/common/beta.md",
            ".agents/conventions/common/delta.md",
        ]);
    }

    [Fact]
    public void TwoWalksOverTheSameTree_ShowByteIdenticalRules()
    {
        WriteFamilyMount();

        var first = RuleFiles.Collect(_repo, 49_000, RuleOrder.Walk);
        var second = RuleFiles.Collect(_repo, 49_000, RuleOrder.Walk);

        second.Files.Select(file => file.Path).Should().Equal(first.Files.Select(file => file.Path));
        second.Omitted.Should().Equal(first.Omitted);

        // And through the DEFAULT overload, which is what a caller that names no order gets. A code
        // round asked for this: an order deterministic in isolation proves nothing about the wiring,
        // and a silent return to a per-round draw would leave every assertion above still green.
        var byDefault = RuleFiles.Collect(_repo, 41_000);
        RuleFiles.Collect(_repo, 41_000).Files.Select(file => file.Path)
            .Should().Equal(byDefault.Files.Select(file => file.Path));
        second.Bytes.Should().Be(first.Bytes);
    }

    [Fact]
    public void TheInstructionFilesAndTheRepositorysOwnRules_StillLeadEveryOrder()
    {
        WriteFamilyMount();
        Write("AGENTS.md", "How to work in this repository");
        Write(".agents/rules/common/vendor-routing.md", "Which CLI runs which model");

        var bundle = RuleFiles.Collect(_repo, 49_000, RuleOrder.Walk);

        bundle.Files.Select(file => file.Path).Take(2).Should().Equal([
            "AGENTS.md", ".agents/rules/common/vendor-routing.md",
        ]);
    }

    /// <summary>
    /// The tier names a language doctrine, not any file that happens to be called one.
    /// </summary>
    /// <remarks>
    /// A doctrine is promoted because it is about the code in front of the reviewer. A
    /// <c>common/doctrine.md</c> is not that, and promoting it ahead of <c>security.md</c> on the
    /// strength of its filename would spend the budget the tier table exists to protect.
    /// </remarks>
    [Fact]
    public void ADoctrineOutsideALanguageDirectory_IsNotPromotedAboveSecurity()
    {
        WriteFamilyMount();
        Write(".agents/conventions/common/doctrine.md", Filler("common-doctrine", 30_000));

        var bundle = RuleFiles.Collect(_repo, 49_000, RuleOrder.Walk);

        bundle.Files.Select(file => file.Path).Should().Contain([
            ".agents/conventions/common/security.md",
            ".agents/conventions/common/testing.md",
        ]);
        bundle.Omitted.Should().Contain(".agents/conventions/common/doctrine.md");
    }

    /// <summary>
    /// The tier table is matched the way the rest of the pipeline compares paths.
    /// </summary>
    /// <remarks>
    /// <c>FolderFiles</c> de-duplicates and sorts its candidates with
    /// <see cref="StringComparer.OrdinalIgnoreCase"/>, so a case-sensitive tier match would be a
    /// second comparison rule inside one pipeline — and the rule it would starve is whichever one
    /// somebody committed with a capital letter.
    /// </remarks>
    [Fact]
    public void ATierNameInAnotherCase_StillMatchesItsTier()
    {
        // The competitor leads with a capital too, and sorts before `Testing.md` under BOTH
        // comparers — so only the tier can put `Testing.md` first, and a case-sensitive tier match
        // fails this test instead of passing it on an accident of ASCII, where every capital sorts
        // ahead of every lowercase letter.
        Write(".gitmodules", "[submodule \"conventions\"]\n path = .agents/conventions\n url = https://example.invalid/rules\n");
        Write(".agents/conventions/common/Alpha-rule.md", Filler("alpha", 25_000));
        Write(".agents/conventions/common/Testing.md", Filler("testing", 25_000));

        var bundle = RuleFiles.Collect(_repo, 26_000, RuleOrder.Walk);

        bundle.Files.Select(file => file.Path).Should().Contain(".agents/conventions/common/Testing.md");
        bundle.Omitted.Should().Contain(".agents/conventions/common/Alpha-rule.md");
    }

    /// <summary>A name the tier table has never heard of is ordered, not dropped.</summary>
    [Fact]
    public void ARuleTheTierTableDoesNotName_IsStillOffered()
    {
        WriteFamilyMount();
        Write(".agents/conventions/common/a-rule-invented-tomorrow.md", Filler("tomorrow", 200));

        var bundle = RuleFiles.Collect(_repo, 49_000, RuleOrder.Walk);

        bundle.Files.Select(file => file.Path).Concat(bundle.Omitted)
            .Should().Contain(".agents/conventions/common/a-rule-invented-tomorrow.md");
    }
}
