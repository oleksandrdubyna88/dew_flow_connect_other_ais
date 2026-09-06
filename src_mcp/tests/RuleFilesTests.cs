using CoaiMcp.Runners.Context;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The reviewers are shown the project's OWN conventions.
/// </summary>
/// <remarks>
/// <para>Every repository this gate reviews carries written rules — `CLAUDE.md`, `AGENTS.md`,
/// `GEMINI.md`, `.claude/rules/**` — and the reviewers had never been shown a line of them. So the
/// gate could call a change well written by the reviewer's own standards while it broke four rules
/// the project wrote down and enforces on humans. Measured on this product's own rounds: not one
/// finding in three rounds referenced a project rule, because no rule was in the prompt.</para>
/// <para>A reviewer cannot flag what it was never told, and its silence reads as approval.</para>
/// </remarks>
public sealed class RuleFilesTests : IDisposable
{
    private readonly string _repo = Directory.CreateTempSubdirectory("coai-rules-").FullName;

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

    [Fact]
    public void TheFourInstructionFilesTheMajorClisRead_AreAllFound()
    {
        Write("CLAUDE.md", "claude rules");
        Write("AGENTS.md", "codex rules");
        Write("GEMINI.md", "gemini rules");
        Write(".github/copilot-instructions.md", "copilot rules");

        var bundle = RuleFiles.Collect(_repo);

        bundle.Files.Select(f => f.Path).Should().BeEquivalentTo(
            ["CLAUDE.md", "AGENTS.md", "GEMINI.md", ".github/copilot-instructions.md"]);
    }

    [Fact]
    public void RuleFolders_AreFollowed_BecauseThatIsWhereTheRulesActuallyLive()
    {
        Write("CLAUDE.md", "the entry point");
        Write(".claude/rules/common/coding-style.md", "immutability");
        Write(".claude/rules/csharp/blazor.md", "split the component");
        Write(".cursor/rules/naming.mdc", "name things");

        var bundle = RuleFiles.Collect(_repo);

        bundle.Files.Select(f => f.Path).Should().Contain(
            [".claude/rules/common/coding-style.md", ".claude/rules/csharp/blazor.md", ".cursor/rules/naming.mdc"]);
    }

    [Fact]
    public void TheInstructionFilesComeFirst_BecauseTheyPointAtTheRest()
    {
        Write(".claude/rules/common/style.md", "style");
        Write("CLAUDE.md", "the entry point");

        RuleFiles.Collect(_repo).Files[0].Path.Should().Be("CLAUDE.md");
    }

    [Fact]
    public void ARepoWithNoRules_ReturnsNothing_RatherThanInventingStandards()
    {
        Write("src/app.cs", "code");

        var bundle = RuleFiles.Collect(_repo);

        bundle.Files.Should().BeEmpty();
        bundle.HasRules.Should().BeFalse();
    }

    // ---------- the budget, and saying what it cut ----------

    [Fact]
    public void PastTheBudget_TheOmissionsAreNamed_NeverSilent()
    {
        // A silent truncation lets a reviewer report compliance with rules it never saw, which is
        // worse than showing it none: it turns an absence of evidence into a clean bill of health.
        Write("CLAUDE.md", Filler("entry", 3_000));
        Write(".claude/rules/a.md", Filler("first", 3_000));
        Write(".claude/rules/b.md", Filler("second", 3_000));
        Write(".claude/rules/c.md", Filler("third", 3_000));

        var bundle = RuleFiles.Collect(_repo, budgetBytes: 7_000);

        bundle.Bytes.Should().BeLessThanOrEqualTo(7_000);
        bundle.Omitted.Should().NotBeEmpty();
        bundle.Render().Should().Contain("omitted for length").And.Contain(bundle.Omitted[0]);
    }

    [Fact]
    public void TheEntryPointSurvivesTheBudget_EvenWhenTheFoldersDoNot()
    {
        Write("CLAUDE.md", Filler("entry", 2_000));
        Write(".claude/rules/huge.md", Filler("huge", 90_000));

        var bundle = RuleFiles.Collect(_repo, budgetBytes: 5_000);

        bundle.Files.Select(f => f.Path).Should().Contain("CLAUDE.md");
    }

    [Fact]
    public void TheRenderedBlock_NamesEachFile_SoAFindingCanCiteTheRule()
    {
        Write("CLAUDE.md", "Records for DTOs. Cyclomatic complexity of four.");

        var rendered = RuleFiles.Collect(_repo).Render();

        rendered.Should().Contain("CLAUDE.md").And.Contain("Cyclomatic complexity of four");
    }

    [Fact]
    public void BuildOutputAndDependencies_AreNotMistakenForRules()
    {
        Write("node_modules/pkg/CLAUDE.md", "somebody else's rules");
        Write("bin/Debug/AGENTS.md", "build output");
        Write("CLAUDE.md", "ours");

        RuleFiles.Collect(_repo).Files.Should().ContainSingle().Which.Path.Should().Be("CLAUDE.md");
    }

    // ---------- a rules repository mounted as a submodule ----------

    /// <summary>The family's shared rules arrive as a whole repository, and a repository has
    /// housekeeping: its own plans, its settings reference copy, its tooling. None of that is a
    /// rule the reviewed diff can break, and all of it competes for the same budget.</summary>
    [Fact]
    public void AMountedRulesRepository_ContributesItsRules_NotItsHousekeeping()
    {
        WriteMount(".claude/rules/shared");
        Write(".claude/rules/shared/common/git-workflow.md", "conventional commits");
        Write(".claude/rules/shared/csharp/doctrine.md", "records for DTOs");
        Write(".claude/rules/shared/todo/PLAN_something.md", "not a rule, an open task");
        Write(".claude/rules/shared/settings/settings.json.md", "a reference copy");
        Write(".claude/rules/shared/tools/fixtures/repo/POST_DEPLOY.md", "a test fixture");
        Write(".claude/rules/shared/README.md", "what this repository is");
        Write("CLAUDE.md", "the entry point");

        var paths = RuleFiles.Collect(_repo).Files.Select(f => f.Path).ToList();

        paths.Should().Contain([".claude/rules/shared/common/git-workflow.md", ".claude/rules/shared/csharp/doctrine.md"]);
        paths.Should().NotContain([
            ".claude/rules/shared/todo/PLAN_something.md",
            ".claude/rules/shared/settings/settings.json.md",
            ".claude/rules/shared/tools/fixtures/repo/POST_DEPLOY.md",
            ".claude/rules/shared/README.md",
        ]);
    }

    /// <summary>The exclusion belongs to the MOUNT, not to the words. A repository is entitled to
    /// its own <c>.claude/rules/todo/</c>, and it would be its own rule being thrown away.</summary>
    [Fact]
    public void TheRepositorysOwnDirectories_AreNotFilteredByTheMountsNames()
    {
        WriteMount(".claude/rules/shared");
        Write(".claude/rules/todo/security.md", "a rule of ours that happens to live in todo");
        Write("CLAUDE.md", "the entry point");

        RuleFiles.Collect(_repo).Files.Select(f => f.Path).Should().Contain(".claude/rules/todo/security.md");
    }

    /// <summary>Ordering used to be whatever the alphabet gave: a local directory sorting after
    /// <c>shared/</c> lost the budget race to 208 KB of family rules.</summary>
    [Fact]
    public void TheRepositorysOwnRulesOutrankTheMount_EvenWhenTheySortAfterIt()
    {
        WriteMount(".claude/rules/shared");
        Write(".claude/rules/shared/common/a-shared.md", Filler("shared", 3_000));
        Write(".claude/rules/zz-workflows/ours.md", Filler("ours", 1_500));
        Write("CLAUDE.md", "the entry point");

        var bundle = RuleFiles.Collect(_repo, budgetBytes: 4_000);

        bundle.Files.Select(f => f.Path).Should().Contain(".claude/rules/zz-workflows/ours.md",
            "the rules this diff can break come before the family's");
        bundle.Omitted.Should().Contain(".claude/rules/shared/common/a-shared.md");
    }

    /// <summary>
    /// The dangerous absence. An omitted file was seen and dropped; a mount that never materialised
    /// leaves zero files, zero omissions, and a bundle that looks like a repository with no rules.
    /// </summary>
    [Fact]
    public void ADeclaredRulesMountThatIsNotHere_IsNamedToTheReviewer()
    {
        WriteMount(".claude/rules/shared");
        Directory.CreateDirectory(Path.Combine(_repo, ".claude", "rules", "shared"));
        Write("CLAUDE.md", "the entry point");

        var bundle = RuleFiles.Collect(_repo);

        bundle.MissingMounts.Should().Equal([".claude/rules/shared"]);
        bundle.Render().Should().Contain(".claude/rules/shared")
            .And.Contain("Do not read their absence as compliance");
    }

    /// <summary>Once it is populated there is nothing to warn about.</summary>
    [Fact]
    public void APopulatedMount_IsNotReportedAsMissing()
    {
        WriteMount(".claude/rules/shared");
        Write(".claude/rules/shared/common/git-workflow.md", "conventional commits");
        Write("CLAUDE.md", "the entry point");

        RuleFiles.Collect(_repo).MissingMounts.Should().BeEmpty();
    }

    /// <summary>
    /// The mounted repository's OWN instruction files are about that repository. Collected as
    /// rules they would sit beside the consumer's, under a name nothing distinguishes, saying
    /// something else.
    /// </summary>
    [Fact]
    public void TheMountsOwnInstructionFiles_AreNotCollectedAsRulesHere()
    {
        WriteMount(".claude/rules/shared");
        Write(".claude/rules/shared/CLAUDE.md", "how to work on the rules repository itself");
        Write(".claude/rules/shared/AGENTS.md", "the same, for another CLI");
        Write(".claude/rules/shared/common/git-workflow.md", "a real shared rule");
        Write("CLAUDE.md", "ours");

        var paths = RuleFiles.Collect(_repo).Files.Select(f => f.Path).ToList();

        paths.Should().Contain([".claude/rules/shared/common/git-workflow.md", "CLAUDE.md"]);
        paths.Should().NotContain([".claude/rules/shared/CLAUDE.md", ".claude/rules/shared/AGENTS.md"]);
    }

    /// <summary>Two missing mounts read the same way whatever order the file listed them in.</summary>
    [Fact]
    public void MissingMounts_AreNamedInAStableOrder()
    {
        Write(".gitmodules",
            "[submodule \".claude/rules/zzz\"]\n\tpath = .claude/rules/zzz\n"
            + "[submodule \".claude/rules/aaa\"]\n\tpath = .claude/rules/aaa\n");
        Directory.CreateDirectory(Path.Combine(_repo, ".claude", "rules", "zzz"));
        Directory.CreateDirectory(Path.Combine(_repo, ".claude", "rules", "aaa"));
        Write("CLAUDE.md", "ours");

        RuleFiles.Collect(_repo).MissingMounts.Should().Equal([".claude/rules/aaa", ".claude/rules/zzz"]);
    }

    /// <summary>A submodule that is not a rules mount is somebody else's code, and not our business.</summary>
    [Fact]
    public void ACodeSubmodule_IsNotReportedAsMissingRules()
    {
        WriteMount("external/dew_flow_mcp");
        Directory.CreateDirectory(Path.Combine(_repo, "external", "dew_flow_mcp"));
        Write("CLAUDE.md", "the entry point");

        RuleFiles.Collect(_repo).MissingMounts.Should().BeEmpty();
    }

    /// <summary>Declares a submodule at <paramref name="path"/>, the way a consumer repository does.</summary>
    private void WriteMount(string path) =>
        Write(".gitmodules", $"[submodule \"{path}\"]\n\tpath = {path}\n\turl = https://example.invalid/rules.git\n");

    [Fact]
    public void ADozenRealRuleFiles_FitTheDefaultBudget()
    {
        // Measured on this repository on 2026-09-06: at the old 40 KB the bundle was 8 files with 19
        // omitted, and the omissions were the rules findings are actually written against - testing,
        // security, reuse-first, git-workflow, and all four language doctrines. The default is a
        // budget rather than a promise (the family set is ~199 KB, and what does not fit is NAMED),
        // but it must at least fit the shape of a real family repository rather than a third of it.
        // Seven rules of 10 KB plus the entry file is 75 KB - inside 80, outside 60.
        Write("CLAUDE.md", Filler("entry", 3_000));
        for (var n = 0; n < 7; n++)
        {
            Write($".claude/rules/shared/common/rule-{n}.md", Filler($"rule {n}", 10_000));
        }

        var bundle = RuleFiles.Collect(_repo);

        bundle.Files.Should().HaveCount(8, "75 KB of rules is not a big rule set");
        bundle.Omitted.Should().BeEmpty();
    }

    [Fact]
    public void TwoRoundsSeeTwoDifferentHalvesOfTheFamilyRules()
    {
        // The measurement that asked for this: 199 KB of family rules against an 80 KB budget, and in
        // enumeration order the same twelve files won every time - so testing.md, security.md,
        // reuse-first.md and all four language doctrines had never been shown to any reviewer.
        Write("CLAUDE.md", Filler("entry", 1_000));
        WriteMount(".claude/rules/shared");
        for (var n = 0; n < 20; n++)
        {
            Write($".claude/rules/shared/common/rule-{n:00}.md", Filler($"rule {n}", 10_000));
        }

        var first = RuleFiles.Collect(_repo, budgetBytes: 40_000, seed: 1);
        var second = RuleFiles.Collect(_repo, budgetBytes: 40_000, seed: 2);

        first.Files.Select(f => f.Path).Should().NotBeEquivalentTo(
            second.Files.Select(f => f.Path), "two rounds that show the same rules leave the rest unread for ever");
    }

    [Fact]
    public void AcrossEnoughRounds_EveryFamilyRuleGetsRead()
    {
        Write("CLAUDE.md", Filler("entry", 1_000));
        WriteMount(".claude/rules/shared");
        for (var n = 0; n < 20; n++)
        {
            Write($".claude/rules/shared/common/rule-{n:00}.md", Filler($"rule {n}", 10_000));
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var round = 0; round < 60; round++)
        {
            foreach (var file in RuleFiles.Collect(_repo, budgetBytes: 40_000, seed: round).Files)
            {
                seen.Add(file.Path);
            }
        }

        // Sixty FIXED seeds, so this is a deterministic statement about the draw rather than a
        // coin-flip that fails once a fortnight in somebody else's CI.
        seen.Should().HaveCount(21, "a rule that is never drawn is a rule that is never applied");
    }

    [Fact]
    public void TheEntryFileAndTheRepositorysOwnRules_AreNeverInTheDraw()
    {
        // They are few, they are the entry points, and they are the rules a diff in THIS repository
        // can break. A round that rolled them out of its own budget would be worse than one that
        // never varied at all.
        Write("CLAUDE.md", Filler("entry", 1_000));
        WriteMount(".claude/rules/shared");
        Write(".claude/rules/common/ours.md", Filler("ours", 2_000));
        for (var n = 0; n < 20; n++)
        {
            Write($".claude/rules/shared/common/rule-{n:00}.md", Filler($"rule {n}", 10_000));
        }

        for (var round = 0; round < 6; round++)
        {
            var paths = RuleFiles.Collect(_repo, budgetBytes: 25_000, seed: round).Files.Select(f => f.Path);

            paths.Should().Contain("CLAUDE.md").And.Contain(".claude/rules/common/ours.md");
        }
    }
}
