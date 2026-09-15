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
/// budget, and <c>testing.md</c>, <c>security.md</c>, <c>reuse-first.md</c> and all four language
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
    private void WriteFamilyMount()
    {
        Write(".gitmodules", "[submodule \"conventions\"]\n path = .agents/conventions\n url = https://example.invalid/rules\n");
        Write(".agents/conventions/common/development-workflow.md", Filler("development-workflow", 14_000));
        Write(".agents/conventions/common/http-contracts.md", Filler("http-contracts", 11_000));
        Write(".agents/conventions/common/security.md", Filler("security", 8_500));
        Write(".agents/conventions/common/testing.md", Filler("testing", 25_000));
        Write(".agents/conventions/csharp/doctrine.md", Filler("csharp", 4_600));
        Write(".agents/conventions/typescript/doctrine.md", Filler("typescript", 5_800));
    }

    [Fact]
    public void TheFallbackOrder_ShowsTheDoctrines_NotOnlyTheTwoLongestFiles()
    {
        WriteFamilyMount();

        var bundle = RuleFiles.Collect(_repo, 45_000, RuleOrder.Walk);

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

    [Fact]
    public void TwoWalksOverTheSameTree_ShowByteIdenticalRules()
    {
        WriteFamilyMount();

        var first = RuleFiles.Collect(_repo, 45_000, RuleOrder.Walk);
        var second = RuleFiles.Collect(_repo, 45_000, RuleOrder.Walk);

        second.Files.Select(file => file.Path).Should().Equal(first.Files.Select(file => file.Path));
        second.Omitted.Should().Equal(first.Omitted);
        second.Bytes.Should().Be(first.Bytes);
    }

    [Fact]
    public void TheInstructionFilesAndTheRepositorysOwnRules_StillLeadEveryOrder()
    {
        WriteFamilyMount();
        Write("AGENTS.md", "How to work in this repository");
        Write(".agents/rules/common/vendor-routing.md", "Which CLI runs which model");

        var bundle = RuleFiles.Collect(_repo, 45_000, RuleOrder.Walk);

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

        var bundle = RuleFiles.Collect(_repo, 45_000, RuleOrder.Walk);

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

        var bundle = RuleFiles.Collect(_repo, 45_000, RuleOrder.Walk);

        bundle.Files.Select(file => file.Path).Concat(bundle.Omitted)
            .Should().Contain(".agents/conventions/common/a-rule-invented-tomorrow.md");
    }
}
