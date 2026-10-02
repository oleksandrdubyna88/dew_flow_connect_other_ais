using CoaiMcp.Core.Commands;
using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Through a real <c>review_plan</c>: a reworded order is the one the caller reads, and a command a person
/// added reaches the caller and the rounds database — or is named when it has no text (issue #467).
/// </summary>
[Collection("fakecli-env")]
public sealed class ThePersonsCommandsReachTheCallerTests : FakeCliRoundTests
{
    private const string Plan = "# PLAN — one change\n\nChange `app.cs`.\n";

    private string PromptFile(string id) => Path.Combine(_data, "prompts", id + ".md");

    private void Write(string id, string text)
    {
        Directory.CreateDirectory(Path.Combine(_data, "prompts"));
        File.WriteAllText(PromptFile(id), text);
    }

    /// <summary>
    /// The question consultant's facts as the engine reads them off THESE settings (S3 of the question consultant):
    /// since S3 the autonomy switch also hands back the question-consult order under the shipped mode, so the
    /// expected context is derived from the settings the service is given rather than built bare.
    /// </summary>
    private static QuestionConsultFacts FactsOf(PanelSettings settings) =>
        settings.QuestionConsult.Enabled
            ? new QuestionConsultFacts(QuestionModes.Of(settings.QuestionConsult.Mode), settings.QuestionConsult.FreeBatches)
            : QuestionConsultFacts.Off;

    private static CommandContext AutonomousUnder(PanelSettings settings) =>
        new(Autonomous: true) { QuestionConsult = FactsOf(settings) };

    [Fact]
    public async Task AnOverrideFile_RewordsTheOrder_AndDeletingItRestoresTheShippedOne()
    {
        var settings = Defaults() with { Autonomous = true };
        Write(CommandTexts.Autonomy, "Work without asking, and {scope} before you do ask.\n");
        Write(CommandTexts.Preamble, "Orders from the panel.\n");

        var answer = await PlanRound(ServiceFor(settings), "feature", Plan);

        CommandsOf(answer).Should().Equal(
            "Work AUTONOMOUSLY. Work without asking, and re-read the whole plan before you do ask.",
            QuestionConsultOrder.For(FactsOf(settings), CommandTexts.Shipped));
        answer.GetProperty("commandsPreamble").GetString().Should().Be("Orders from the panel.");

        File.Delete(PromptFile(CommandTexts.Autonomy));
        var again = await PlanRound(ServiceFor(settings), "epic-1", Plan);

        CommandsOf(again).Should().Equal(GateCommands.For(AutonomousUnder(settings)),
            "deleting the file is how a reworded order is put back");
    }

    [Fact]
    public async Task AnEnabledCustomCommand_ReachesTheCaller_AndTheRoundsDatabase_AfterTheBuiltInOrders()
    {
        Write("command-docs", "Update the module docs with every change.\n");
        var settings = Defaults() with
        {
            Autonomous = true,
            CustomCommands =
            [
                new("command-docs", "Docs", true, CommandStage.Any),
                new("command-off", "Off", false, CommandStage.Any),
                new("command-code", "Code only", true, CommandStage.Code),
            ],
        };
        Write("command-off", "never given");
        Write("command-code", "never given in a plan round");

        var answer = await PlanRound(ServiceFor(settings), "feature", Plan);

        var commands = CommandsOf(answer);
        commands.Should().HaveCount(3, "the two built-in orders the autonomy switch gives since S3, then the person's");
        commands[0].Should().StartWith(GateCommands.AutonomyMarker);
        commands[1].Should().StartWith(GateCommands.QuestionConsultMarker);
        commands[2].Should().Be("Update the module docs with every change.");
        ListOf(answer, "commandsSkipped").Should().BeEmpty();
        var round = RoundsQuery.Read(_data).Rounds.Should().ContainSingle().Subject;
        RoundsQuery.FindingsOf(_data, round.SessionId, round.Stage, round.Number).Orders!.Commands
            .Should().Equal(commands, "the rounds database records every order the caller was given");
    }

    [Fact]
    public async Task AnOverrideThatCannotBeRead_CostsTheRoundNothing_AndTheShippedOrderIsGiven()
    {
        // The texts are read AFTER the reviewers have answered and been paid for. A file another process
        // holds at that moment — the extension restoring a default, a scanner, a NAS blip — failed the
        // whole round, so its findings were never saved and the caller paid for it again. (our own
        // reviewer, the code round.)
        Write(CommandTexts.Autonomy, "Reworded.");
        using var held = new FileStream(PromptFile(CommandTexts.Autonomy), FileMode.Open, FileAccess.Read, FileShare.None);

        var settings = Defaults() with { Autonomous = true };
        var answer = await PlanRound(ServiceFor(settings), "feature", Plan);

        answer.GetProperty("verdict").GetString().Should().NotBe("error");
        CommandsOf(answer).Should().Equal(GateCommands.For(AutonomousUnder(settings)),
            "a text that cannot be read is the shipped one this round");
    }

    [Fact]
    public async Task ACustomCommandWithNoText_IsNotGiven_AndTheReplySaysWhichFileToWrite()
    {
        var settings = Defaults() with { CustomCommands = [new("command-empty", "Say something", true, CommandStage.Any)] };

        var answer = await PlanRound(ServiceFor(settings), "feature", Plan);

        CommandsOf(answer).Should().BeEmpty();
        ListOf(answer, "commandsSkipped").Should().Equal(
            $"custom command 'Say something' has no text — write it in {PromptFile("command-empty")}");
    }
}
