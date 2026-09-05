using Xunit;
using FluentAssertions;
using CoaiBench.Judging;
using CoaiBench.Model;

namespace CoaiBench.Tests;

/// <summary>
/// What each provider found that nobody else found.
/// </summary>
/// <remarks>
/// The per-arm table says how much of a provider's output was worth having, and that is not the
/// number a second provider is bought on. `local` scoring 14 % of its own output is one thing;
/// `local` finding nothing codex did not already find is a different thing, and only this pass can
/// tell them apart. Asked for on 2026-09-05, after exactly that misreading.
/// </remarks>
public sealed class OverlapTests
{
    private static readonly Case Work = new("split-once", "artifacts/bench/plan-B.md", "7133c2f", "4a27a17");

    private static Finding Found(string provider, string file, int line, string title, string useful = "unjudged") =>
        new(File: file, Line: line, Title: title, Providers: [provider]) { Useful = useful };

    private static RunRecord Run(string arm, params Finding[] findings) =>
        new(Work, arm, 1, 1)
        {
            Stages = [new StageResult("CodeReview", 10.0) { Findings = findings }],
        };

    // ---------- what counts as the same finding ----------

    [Fact]
    public void TwoProvidersNamingTheSameLineAndTheSameProblem_FoundOneThing()
    {
        Overlap.SameThing(
            Found("codex", "src/Panel.cs", 40, "session file opened without FileShare"),
            Found("gemini", "src/Panel.cs", 44, "FileShare missing when the session file is opened"))
            .Should().BeTrue("the same place and the same subject, differently worded");
    }

    [Fact]
    public void TheSameWordsAboutADifferentFile_AreTwoThings()
    {
        Overlap.SameThing(
            Found("codex", "src/Panel.cs", 40, "session file opened without FileShare"),
            Found("gemini", "src/Rounds.cs", 40, "session file opened without FileShare"))
            .Should().BeFalse();
    }

    [Fact]
    public void TheSamePlaceButADifferentSubject_AreTwoThings()
    {
        Overlap.SameThing(
            Found("codex", "src/Panel.cs", 40, "session file opened without FileShare"),
            Found("gemini", "src/Panel.cs", 42, "magic number should become a named constant"))
            .Should().BeFalse("one file can hold two problems");
    }

    [Fact]
    public void AFindingThatCitesNoLine_CannotDisagreeAboutWhere()
    {
        Overlap.SameThing(
            Found("codex", "src/Panel.cs", 0, "session file opened without FileShare"),
            Found("local", "src/Panel.cs", 900, "the session file is opened without FileShare"))
            .Should().BeTrue();
    }

    [Fact]
    public void FindingsWithNoFileAtAll_AreHeldToAHigherBarOnWording()
    {
        Overlap.SameThing(
            Found("codex", "", 0, "the plan never says what happens when the round is cancelled"),
            Found("gemini", "", 0, "the plan never says what happens when the round is cancelled"))
            .Should().BeTrue();

        Overlap.SameThing(
            Found("codex", "", 0, "the plan never says what happens when a round is cancelled"),
            Found("gemini", "", 0, "documentation would benefit from a diagram"))
            .Should().BeFalse();
    }

    // ---------- the counts ----------

    [Fact]
    public void AProviderThatOnlyEverRepeatsAnother_HasFoundNothingOfItsOwn()
    {
        var overlaps = Overlap.Across(
        [
            Run("codex", Found("codex", "src/Panel.cs", 40, "session file opened without FileShare", "yes")),
            Run("local", Found("local", "src/Panel.cs", 41, "the session file is opened without FileShare")),
        ]);

        var local = overlaps.Single(o => o.Provider == "local");
        local.Distinct.Should().Be(1);
        local.Only.Should().Be(0, "codex named it too");
        local.Shared.Should().Be(1);
    }

    [Fact]
    public void TheHeadlineNumberIsWhatOnlyThisProviderFoundAndTheJudgeKept()
    {
        var overlaps = Overlap.Across(
        [
            Run("codex",
                Found("codex", "src/Panel.cs", 40, "session file opened without FileShare", "yes"),
                Found("codex", "src/Gate.cs", 12, "the retry loop never gives up", "yes")),
            Run("local",
                Found("local", "src/Panel.cs", 41, "the session file is opened without FileShare", "yes"),
                Found("local", "src/Log.cs", 8, "this variable could be named better", "no"),
                Found("local", "src/Log.cs", 60, "an off-by-one closes the file too early", "yes")),
        ]);

        var codex = overlaps.Single(o => o.Provider == "codex");
        var local = overlaps.Single(o => o.Provider == "local");

        codex.Only.Should().Be(1, "the retry loop");
        codex.OnlyUseful.Should().Be(1);
        local.Only.Should().Be(2, "the naming nit and the off-by-one");
        local.OnlyUseful.Should().Be(1, "only the off-by-one was worth having");
        local.Useful.Should().Be(2, "the shared one counts towards its hit rate, not towards its uniqueness");
    }

    [Fact]
    public void OneProviderSayingTheSameThingInFourRunsSaidOneThing()
    {
        // Pooled across arms and repeats on purpose: a provider is measured on the union of its
        // attempts. Four runs of one arm must not make one insight look like four.
        var same = () => Found("codex", "src/Panel.cs", 40, "session file opened without FileShare");
        var overlaps = Overlap.Across([Run("codex", same()), Run("codex,gemini", same()), Run("codex,local", same())]);

        overlaps.Single(o => o.Provider == "codex").Raw.Should().Be(3);
        overlaps.Single(o => o.Provider == "codex").Distinct.Should().Be(1);
    }

    [Fact]
    public void FindingsAboutDifferentCasesNeverCluster()
    {
        var other = new Case("engine-lease", "research/PLAN_engine_lease.md");
        var overlaps = Overlap.Across(
        [
            Run("codex", Found("codex", "src/Panel.cs", 40, "session file opened without FileShare")),
            new RunRecord(other, "gemini", 1, 1)
            {
                Stages =
                [
                    new StageResult("CodeReview", 10.0)
                    {
                        Findings = [Found("gemini", "src/Panel.cs", 40, "session file opened without FileShare")],
                    },
                ],
            },
        ]);

        overlaps.Should().OnlyContain(o => o.Only == 1, "two reviews of two different changes are not agreement");
    }
}
