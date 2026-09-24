using CoaiMcp.Core.Commands;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The words of the gate's orders are data a person can override — and the markers are not (issue #467).
/// </summary>
public sealed class CommandTextsTests
{
    [Fact]
    public void EveryShippedId_HasItsText_AndEveryFileInSharedCommands_IsAShippedId()
    {
        // Both directions, because both failures are silent: an id with no file throws only when that
        // order is first given, and a file with no id is a text nobody can ever be shown.
        var files = Directory.EnumerateFiles(SharedFixtures.PathOf("commands"), "*.md")
            .Select(Path.GetFileNameWithoutExtension)
            .Order(StringComparer.Ordinal);

        files.Should().Equal(CommandTexts.ShippedIds.Order(StringComparer.Ordinal));
        foreach (var id in CommandTexts.ShippedIds)
        {
            CommandTexts.ShippedText(id).Should().NotBeNullOrWhiteSpace($"{id} is shipped");
            CommandTexts.ShippedText(id).Should().NotEndWith("\n", $"{id}'s file newline is not part of the order");
        }
    }

    [Fact]
    public void AnOverrideThatSaysSomething_Wins_AndABlankOne_IsNoOverride()
    {
        var texts = new CommandTexts(new Dictionary<string, string>
        {
            [CommandTexts.Preamble] = "Orders follow.\r\n\n",
            [CommandTexts.Autonomy] = "   \n",
        });

        texts.Text(CommandTexts.Preamble).Should().Be("Orders follow.", "the file's line ending is not part of the order");
        texts.Text(CommandTexts.Autonomy).Should().Be(CommandTexts.ShippedText(CommandTexts.Autonomy),
            "a file created before it was written must not hand the caller an empty order");
    }

    private static CommandContext Everything(string id, string text) => new(
        Autonomous: true, SplitPlan: true, SplitWithFable: true, PlanText: "# PLAN\n\nOne change.\n", PlanStage: true)
    {
        Texts = new CommandTexts(new Dictionary<string, string> { [id] = text }),
    };

    [Fact]
    public void AnOverriddenAutonomyOrder_StillOpensWithItsMarker_AndFillsItsScope()
    {
        var order = GateCommands.For(Everything(CommandTexts.Autonomy, "Be bold; before a question, {scope}."))[2];

        order.Should().Be("Work AUTONOMOUSLY. Be bold; before a question, re-read every epic and story you have written so far.");
    }

    [Fact]
    public void AnOverriddenModelOrder_StillOpensWithItsMarker_AndNamesTheModels()
    {
        var order = GateCommands.For(Everything(CommandTexts.Model, "{strongest} splits; {implementation} builds."))[1];

        order.Should().Be(GateCommands.ModelOrderMarker + "Fable splits; Opus builds.");
    }

    [Fact]
    public void AnOverriddenSplitOrder_StillCarriesTheGateMarker_WhateverItsWordsAre()
    {
        var context = Everything(CommandTexts.CadenceSingle, "once, at the end.") with { Autonomous = false };
        var split = GateCommands.For(context with
        {
            Texts = new CommandTexts(new Dictionary<string, string>
            {
                [CommandTexts.SplitNone] = "Build it.",
                [CommandTexts.SplitMeasured] = "[{numbers}; {verdict}]",
                [CommandTexts.CadenceSingle] = "once, at the end.",
                [CommandTexts.AnotherCodeRound] = "Again: true for another.",
            }),
        })[0];

        split.Should().StartWith("Build it. [")
            .And.Contain("; AsItIs]")
            .And.EndWith($" {GateCommands.GateOrderMarker} once, at the end. Again: true for another.");
    }

    [Fact]
    public void AnOverriddenAlreadySplitOrder_StillOpensWithItsMarker()
    {
        var context = Everything(CommandTexts.AlreadySplitEpic, " — carry on.") with { FirstPlanRound = false, Autonomous = false };

        GateCommands.For(context).Should().Equal(GateCommands.AlreadySplitMarker + " — carry on.");
    }

    [Fact]
    public void TheBenchsMarkers_AreTheOnesTheOrdersOpenWith()
    {
        // The bench holds its own copies (it references nothing of the server); these are the server's.
        GateCommands.AutonomyMarker.Should().StartWith("Work AUTONOMOUSLY");
        GateCommands.AlreadySplitMarker.Should().Contain("already under way");
    }

    [Fact]
    public void ThePreamble_CanBeReworded_AndIsTheShippedOneOtherwise()
    {
        GateCommands.PreambleFor(new CommandContext()).Should().Be(GateCommands.Preamble);
        GateCommands.PreambleFor(new CommandContext
        {
            Texts = new CommandTexts(new Dictionary<string, string> { [CommandTexts.Preamble] = "Mine." }),
        }).Should().Be("Mine.");
    }
}
