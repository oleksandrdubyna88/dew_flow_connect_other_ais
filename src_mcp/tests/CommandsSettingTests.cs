using CoaiMcp.Core.Commands;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary><c>COAI_COMMANDS</c> — the commands a person added, as the server reads them (issue #467).</summary>
public sealed class CommandsSettingTests
{
    [Fact]
    public void NoValue_IsNoCommands_AndNothingToSay()
    {
        CommandsSetting.Parse(null).Should().BeEquivalentTo(new CommandsSetting([], []));
        CommandsSetting.Parse("  ").Should().BeEquivalentTo(new CommandsSetting([], []));
    }

    [Fact]
    public void ARow_IsReadWithTheCommandPrefixAdded_AndItsDefaults()
    {
        var read = CommandsSetting.Parse("""
            [{"id": "review-docs", "title": "Review the docs", "enabled": true, "stage": "code"},
             {"id": "lint"}]
            """);

        read.Complaints.Should().BeEmpty();
        read.Commands.Should().Equal(
            new CustomCommand("command-review-docs", "Review the docs", true, CommandStage.Code),
            // Off until switched on, like a role a person adds; titled by its id; given in every round.
            new CustomCommand("command-lint", "lint", false, CommandStage.Any));
    }

    [Fact]
    public void AValueThatIsNotAList_IsNoCommands_AndSaysSo()
    {
        foreach (var value in (string[])["{not json", "null", """{"id": "x"}"""])
        {
            var read = CommandsSetting.Parse(value);

            read.Commands.Should().BeEmpty(value);
            read.Complaints.Should().ContainSingle().Which.Should().StartWith("COAI_COMMANDS could not be read (", value);
        }
    }

    [Fact]
    public void TheSettingsRead_CarriesTheCommands_AndPutsTheirComplaintsOnThePanel()
    {
        var good = PanelSettings.FromEnvironment(key => key == CommandsSetting.Key
            ? """[{"id": "docs", "title": "Docs", "enabled": true}]"""
            : null);
        var bad = PanelSettings.FromEnvironment(key => key == CommandsSetting.Key ? "{ not json" : null);

        good.CustomCommands.Should().Equal(new CustomCommand("command-docs", "Docs", true, CommandStage.Any));
        good.UnrecognisedSettings.Should().NotContain(u => u.Key == CommandsSetting.Key);
        bad.CustomCommands.Should().BeEmpty();
        bad.UnrecognisedSettings.Should().ContainSingle(u => u.Key == CommandsSetting.Key);
    }

    [Fact]
    public void ARowThatCannotBeUsed_IsDropped_ByName_AndTheRestAreKept()
    {
        var read = CommandsSetting.Parse("""
            [{"id": "Bad Name"}, {"id": "../escape"}, {"id": "autonomy"}, {"id": "ok"},
             {"id": "ok"}, {"id": "odd", "stage": "sometimes"}, {"id": "numbered", "stage": "1"}, {}, null]
            """);

        read.Commands.Select(c => c.Id).Should().Equal("command-ok");
        read.Complaints.Should().BeEquivalentTo(
            "COAI_COMMANDS: 'Bad Name' is not a command name — lower-case letters, digits and hyphens",
            "COAI_COMMANDS: '../escape' is not a command name — lower-case letters, digits and hyphens",
            "COAI_COMMANDS: 'autonomy' is the name of a shipped command — override its text instead",
            "COAI_COMMANDS: 'ok' is listed twice — the first is kept",
            "COAI_COMMANDS: 'odd' has the stage 'sometimes' — plan, code or any",
            // A number is not a stage name, whatever the enum underneath would make of it. (codex and our
            // own reviewer, the code round.)
            "COAI_COMMANDS: 'numbered' has the stage '1' — plan, code or any",
            "COAI_COMMANDS: a row has no id",
            "COAI_COMMANDS: a row has no id");
    }
}
