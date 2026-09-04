using Xunit;
using FluentAssertions;
using CoaiMcp.Core.Commands;

namespace CoaiMcp.Tests;

/// <summary>
/// The orders a round hands back, and the switches that produce them.
/// </summary>
/// <remarks>
/// Every switch is off by default, and an empty list is exactly what every release before this one
/// returned — which is the first test here, because a feature that changes behaviour when nobody
/// asked for it is the one that gets reverted.
/// </remarks>
public sealed class GateCommandsTests
{
    private const string SmallPlan = "# PLAN — a nit\n\nOne paragraph. Change `panelView.ts`.\n";

    private static string PlanOf(int lines, int steps, int files, int areas)
    {
        var text = new System.Text.StringBuilder("# PLAN — generated\n\n## Build order\n\n");
        for (var i = 1; i <= steps; i++)
        {
            text.Append(System.Globalization.CultureInfo.InvariantCulture, $"{i}. do the {i}th thing\n");
        }
        text.Append("\n## Notes\n\n");
        for (var i = 0; i < files; i++)
        {
            text.Append(System.Globalization.CultureInfo.InvariantCulture, $"- touches `file{i}.cs`\n");
        }
        foreach (var area in new[] { "src_mcp", "src_vs_code", ".github", "research", "prompts" }.Take(areas))
        {
            text.Append(System.Globalization.CultureInfo.InvariantCulture, $"- under `{area}/`\n");
        }
        while (text.ToString().Split('\n').Length < lines)
        {
            text.Append("filler\n");
        }

        return text.ToString();
    }

    [Fact]
    public void WithEverythingOff_ThereAreNoCommands() =>
        GateCommands.For(new CommandContext(PlanText: SmallPlan, PlanStage: true))
            .Should().BeEmpty("a switch nobody set must change nothing at all");

    [Fact]
    public void TheSplitSwitch_AddsExactlyItsOwnCommand()
    {
        var commands = GateCommands.For(new CommandContext(SplitPlan: true, PlanText: SmallPlan, PlanStage: true));

        commands.Should().ContainSingle();
        commands[0].Should().Contain("review_code").And.Contain("commit");
    }

    [Fact]
    public void TheSplitCommand_IsNotIssuedOnACodeRound()
    {
        // A code round has a diff and no plan; a split verdict computed from source would be a
        // number invented. Raised twice in this change's plan round.
        GateCommands.For(new CommandContext(SplitPlan: true, PlanText: "", PlanStage: false))
            .Should().BeEmpty();
    }

    [Fact]
    public void TheAutonomySwitch_SaysWhatToDoWithBothKindsOfQuestion()
    {
        var commands = GateCommands.For(new CommandContext(Autonomous: true, PlanStage: true));

        commands.Should().ContainSingle();
        commands[0].Should().Contain("does not block").And.Contain("END of your final summary");
        commands[0].Should().Contain("interrupted once", "one interruption with everything beats five with one each");
    }

    [Fact]
    public void TheAutonomyCommand_DoesNotTellYouToRereadEpicsThatDoNotExist()
    {
        var alone = GateCommands.For(new CommandContext(Autonomous: true, PlanStage: true))[0];
        var withSplit = GateCommands.For(
            new CommandContext(Autonomous: true, SplitPlan: true, PlanText: SmallPlan, PlanStage: true))[1];

        alone.Should().Contain("re-read the whole plan").And.NotContain("every epic");
        withSplit.Should().Contain("every epic");
    }

    [Fact]
    public void Fable_IsNeverNamedWhenItIsNotThere()
    {
        var without = GateCommands.For(new CommandContext(
            SplitPlan: true, SplitWithFable: true, FableAvailable: false, PlanText: SmallPlan, PlanStage: true));

        without.Should().ContainSingle("the split command, and nothing about a model this machine has not got");
        string.Join(' ', without).Should().NotContain("Fable");
    }

    [Fact]
    public void Fable_NamesWhatItIsForWhenItIsThere()
    {
        var commands = GateCommands.For(new CommandContext(
            SplitPlan: true, SplitWithFable: true, FableAvailable: true, PlanText: SmallPlan, PlanStage: true));

        commands.Should().HaveCount(2);
        commands[1].Should().Contain("Fable").And.Contain("Opus");
        commands[1].Should().Contain("payments").And.Contain("security");
    }

    [Fact]
    public void WithEverythingOn_TheOrderIsSplitThenModelThenAutonomy()
    {
        // The order is the order they are carried out in, and it is asserted whole because three
        // per-switch tests can all pass while the sequence is wrong.
        var commands = GateCommands.For(new CommandContext(
            Autonomous: true, SplitPlan: true, SplitWithFable: true, FableAvailable: true,
            PlanText: PlanOf(lines: 150, steps: 5, files: 3, areas: 2), PlanStage: true));

        commands.Should().HaveCount(3);
        commands[0].Should().Contain("STORIES");
        commands[1].Should().Contain("Fable");
        commands[2].Should().Contain("AUTONOMOUSLY");
    }

    [Theory]
    // The corpus's own numbers: the largest open plan, an ordinary one, and a small tail.
    [InlineData(554, 7, 8, 2, PlanShape.Split.Epics)]
    [InlineData(440, 0, 16, 5, PlanShape.Split.Epics)]
    [InlineData(230, 9, 10, 2, PlanShape.Split.Stories)]
    [InlineData(122, 7, 4, 2, PlanShape.Split.Stories)]
    [InlineData(85, 3, 3, 2, PlanShape.Split.AsItIs)]
    public void TheVerdict_IsTwoAxes_BecauseSizeAloneIsRefutedByTheCorpus(
        int lines, int steps, int files, int areas, PlanShape.Split expected) =>
        new PlanShape(lines, steps, files, areas).Verdict.Should().Be(expected);

    [Fact]
    public void ThePlanItselfIsMeasured_NotGuessedAt()
    {
        var shape = PlanShapeReader.Of(PlanOf(lines: 320, steps: 7, files: 6, areas: 4));

        shape.Steps.Should().Be(7);
        shape.Files.Should().Be(6);
        shape.Areas.Should().BeGreaterThanOrEqualTo(4);
        shape.Lines.Should().BeGreaterThanOrEqualTo(320);
        shape.Verdict.Should().Be(PlanShape.Split.Epics);
    }

    [Fact]
    public void APlanWithNoBuildOrder_IsStillMeasured()
    {
        // The layout this heuristic must not be fooled by: the one plan in this repository that
        // WAS split into epics has no build order at all.
        var text = "# PLAN\n\n" + string.Join("\n", Enumerable.Range(0, 400).Select(i => $"line {i} `f{i % 20}.cs` src_mcp src_vs_code .github research"));

        var shape = PlanShapeReader.Of(text);

        shape.Steps.Should().Be(0);
        shape.Verdict.Should().Be(PlanShape.Split.Epics, "big and broad, whatever shape it is written in");
    }

    [Fact]
    public void FilesAreCountedByPath_NotByName()
    {
        // `src/a.cs` and `tests/a.cs` are two files. Collapsing them by base name made a plan that
        // names fourteen look like one naming seven — and fourteen is the threshold the epics
        // verdict turns on. (codex, this change's code round.)
        var text = "# PLAN\n\n" + string.Join(
            "\n",
            Enumerable.Range(0, 7).SelectMany(i => new[] { $"- `src/a{i}.cs`", $"- `tests/a{i}.cs`" }));

        PlanShapeReader.Of(text).Files.Should().Be(14);
    }

    [Fact]
    public void AChecklistIsNotABuildOrder()
    {
        // With no build-order heading only the longest CONTIGUOUS run counts: four acceptance
        // criteria in one place and a numbered example in another are not six build steps.
        var text = "# PLAN\n\n## What must be true\n\n1. a\n2. b\n3. c\n4. d\n\n"
            + "Prose that breaks the run.\n\n## An example\n\n1. x\n2. y\n";

        PlanShapeReader.Of(text).Steps.Should().Be(4, "the longest run, not the sum of every list");
    }

    [Fact]
    public void TheCommandsSayTheyMustBeFollowed() =>
        GateCommands.Preamble.Should().Contain("outrank").And.Contain("operator");

    // ---------- the loop, and its floor ----------

    [Fact]
    public void AnEpicComingBack_IsToldItIsAPiece_NotToSplitAgain()
    {
        // The operator's question, and the whole reason FirstPlanRound exists: a plan is split into
        // epics, each epic comes back for its own plan review, and a gate with no memory tells each
        // one to split into epics. Epics of epics, for ever.
        var again = GateCommands.For(new CommandContext(
            SplitPlan: true, PlanText: PlanOf(lines: 554, steps: 7, files: 8, areas: 2),
            PlanStage: true, FirstPlanRound: false));

        again.Should().ContainSingle();
        again[0].Should().Contain("do NOT split it again").And.Contain("as one unit");
        again[0].Should().NotContain("EPICS").And.NotContain("STORIES");
    }

    [Fact]
    public void TheAlreadySplitOrder_StillSaysWhatToDoWithEachPiece()
    {
        // "Do not split" must not read as "do not review": the per-unit loop is the valuable half
        // of the order and it applies to a piece exactly as it applies to a story.
        var again = GateCommands.For(new CommandContext(
            SplitPlan: true, PlanText: SmallPlan, PlanStage: true, FirstPlanRound: false))[0];

        again.Should().Contain("review its diff").And.Contain("commit");
        again.Should().Contain("say so in your summary", "an oversized piece is reported, not silently built");
    }

    [Fact]
    public void AnEpicComingBack_IsNeverToldToSplitItWithFable()
    {
        // The Fable order is about the SPLIT — "do the splitting with Fable". Handed to a piece
        // that must not be split, it is an instruction with nothing to apply to.
        var again = GateCommands.For(new CommandContext(
            SplitPlan: true, SplitWithFable: true, FableAvailable: true,
            PlanText: SmallPlan, PlanStage: true, FirstPlanRound: false));

        again.Should().ContainSingle();
        string.Join(' ', again).Should().NotContain("Fable");
    }

    [Fact]
    public void TheAutonomyOrder_SurvivesTheEpic()
    {
        // Autonomy is not part of the split and has no reason to stop when the split does — a
        // second interruption policy halfway through a task is the opposite of what was asked for.
        var again = GateCommands.For(new CommandContext(
            Autonomous: true, SplitPlan: true, PlanText: SmallPlan, PlanStage: true, FirstPlanRound: false));

        again.Should().HaveCount(2);
        again[1].Should().Contain("AUTONOMOUSLY");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WithTheSplitSwitchOff_NeitherOrderIsGiven(bool firstRound)
    {
        // The operator's first question: with the box unticked, nothing goes — not the split order,
        // and not the "you are a piece" order either, which would be a command about a feature
        // nobody switched on.
        GateCommands.For(new CommandContext(
            SplitPlan: false, SplitWithFable: true, FableAvailable: true,
            PlanText: SmallPlan, PlanStage: true, FirstPlanRound: firstRound))
            .Should().BeEmpty();
    }

    [Theory]
    [InlineData(true, true, true, true)]     // the one case that orders a split
    [InlineData(true, true, false, false)]   // an epic coming back
    [InlineData(true, false, true, false)]   // a code round
    [InlineData(false, true, true, false)]   // the switch is off
    public void OrdersSplit_AnswersExactlyWhatForActuallyDid(
        bool splitPlan, bool planStage, bool firstRound, bool expected)
    {
        // The server records the order it gave, and it must ask ONE question to know it gave one.
        // Two copies of the same condition is how the surface-name check ended up with three.
        var context = new CommandContext(
            SplitPlan: splitPlan, PlanText: SmallPlan, PlanStage: planStage, FirstPlanRound: firstRound);

        GateCommands.OrdersSplit(context).Should().Be(expected);
        // "After EVERY story" rather than "Split this plan": a plan small enough to build as it
        // stands is still a split ORDER — it says so, and the per-story loop is the half that
        // always applies. Caught by this test on its first run.
        GateCommands.For(context).Any(c => c.Contains("After EVERY story", StringComparison.Ordinal))
            .Should().Be(expected, "what it says and what it reports must be the same event");
    }
}
