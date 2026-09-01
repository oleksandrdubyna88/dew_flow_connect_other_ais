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
}
