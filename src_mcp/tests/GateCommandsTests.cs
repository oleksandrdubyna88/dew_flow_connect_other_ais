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
    public void TheCommandsSayTheyMustBeFollowed() =>
        GateCommands.Preamble.Should().Contain("outrank").And.Contain("operator");
}
