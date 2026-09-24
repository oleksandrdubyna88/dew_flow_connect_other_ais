using CoaiMcp.Core.Commands;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>The commands a person added, for one round (issue #467).</summary>
public sealed class CustomCommandsTests
{
    private static CommandTexts Written(params (string Id, string Text)[] files) =>
        new(files.ToDictionary(f => f.Id, f => f.Text));

    private static string FileOf(string id) => $"/data/prompts/{id}.md";

    [Fact]
    public void AnEnabledCommand_IsGivenWithItsText_InTheOrderTheyAreListed()
    {
        CustomCommand[] commands =
        [
            new("command-docs", "Docs", true, CommandStage.Any),
            new("command-lint", "Lint", true, CommandStage.Any),
        ];

        var orders = CustomCommands.For(commands, CommandStage.Plan,
            Written(("command-lint", "Run the linter.\n"), ("command-docs", "Update the docs.")), FileOf);

        orders.Orders.Should().Equal("Update the docs.", "Run the linter.");
        orders.Skipped.Should().BeEmpty();
    }

    [Fact]
    public void ADisabledCommand_AndOneForTheOtherStage_AreNotGiven()
    {
        CustomCommand[] commands =
        [
            new("command-off", "Off", false, CommandStage.Any),
            new("command-code", "Code only", true, CommandStage.Code),
            new("command-plan", "Plan only", true, CommandStage.Plan),
        ];
        var texts = Written(("command-off", "x"), ("command-code", "code"), ("command-plan", "plan"));

        CustomCommands.For(commands, CommandStage.Plan, texts, FileOf).Orders.Should().Equal("plan");
        CustomCommands.For(commands, CommandStage.Code, texts, FileOf).Orders.Should().Equal("code");
        CustomCommands.For(commands, CommandStage.Any, texts, FileOf).Orders.Should().BeEmpty(
            "a document round is neither a plan nor a code round");
    }

    [Fact]
    public void AnAnyCommand_IsGivenInEveryKindOfRound_DocumentsIncluded()
    {
        // The positive half of the test above, with a command that SHOULD be given. (gemini, the code round.)
        CustomCommand[] commands = [new("command-always", "Always", true, CommandStage.Any)];
        var texts = Written(("command-always", "Say what you changed."));

        foreach (var round in (CommandStage[])[CommandStage.Plan, CommandStage.Code, CommandStage.Any])
        {
            CustomCommands.For(commands, round, texts, FileOf).Orders.Should().Equal(["Say what you changed."], round.ToString());
        }
    }

    [Fact]
    public void ACommandWithNoText_IsLeftOut_AndNamedWithTheFileToWrite()
    {
        CustomCommand[] commands = [new("command-empty", "Say something", true, CommandStage.Any)];

        var orders = CustomCommands.For(commands, CommandStage.Code, Written(("command-empty", "  \n")), FileOf);

        orders.Orders.Should().BeEmpty("an empty order is worse than none: the caller is told to follow nothing");
        orders.Skipped.Should().Equal(
            "custom command 'Say something' has no text — write it in /data/prompts/command-empty.md");
    }
}
