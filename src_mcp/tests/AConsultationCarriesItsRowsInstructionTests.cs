using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A consultation sends the consultant its row's system prompt, and nothing the consultant says back carries that text
/// into a reply or a record (the cadence consultation for epics 1–3 of research/PLAN_one_model_catalog.md, finding C2, C2c).
/// </summary>
/// <remarks>
/// The shipped map borrows the codex reviewer row, so a system prompt set on that row is the consultant's too — the same
/// rule a definition's own row follows. A CLI echoes what it is given (<c>codex exec</c> prints its prompt on stderr), so
/// the text is redacted from the child's output as a reviewer's is.
/// </remarks>
[Collection("fakecli-env")]
public sealed class AConsultationCarriesItsRowsInstructionTests : ConsultScenarioBase
{
    private const string Instruction = "Answer in plain English, in five lines.";

    private PanelService WithInstruction(IProcessLauncher launcher) =>
        Service(providers: [new("codex") { ExecutablePath = FakeCliExe, SystemPrompt = Instruction }], launcher: launcher);

    [Fact]
    public async Task TheConsultantIsSentItsRowsInstruction()
    {
        string sent = string.Empty;
        var launcher = new TurnLauncher(_launcher)
        {
            Consultant = (request, ct) =>
            {
                sent = request.StdIn ?? string.Empty;
                return _launcher.RunAsync(request, ct);
            },
        };

        var reply = await Consult(WithInstruction(launcher), "The parser returns 3 where 4 is expected.");

        reply.TryGetProperty("error", out _).Should().BeFalse(reply.ToString());
        sent.Should().Contain("## What the person asked of this consultant").And.Contain(Instruction);
    }

    [Fact]
    public async Task AnInstructionEditedLater_NeverReachesAConversationOpenedBefore()
    {
        List<string> sent = [];
        var launcher = new TurnLauncher(_launcher)
        {
            Consultant = (request, ct) =>
            {
                sent.Add(request.StdIn ?? string.Empty);
                return _launcher.RunAsync(request, ct);
            },
        };
        var opened = await Consult(WithInstruction(launcher), "The parser returns 3 where 4 is expected.");
        var id = opened.GetProperty("consultationId").GetString()!;

        // The person edits the row's instruction while the conversation is open.
        var edited = Service(providers: [new("codex") { ExecutablePath = FakeCliExe, SystemPrompt = "Answer only in haiku." }], launcher: launcher);
        var followed = await Consult(edited, "I checked the separator; it is counted once.", id: id);

        followed.TryGetProperty("error", out _).Should().BeFalse(followed.ToString());
        sent.Should().HaveCount(2);
        sent[1].Should().Contain(Instruction, "the instruction is frozen with the model when the conversation opens")
            .And.NotContain("Answer only in haiku.");
    }

    [Fact]
    public async Task AnInstructionWithSpacesAroundIt_IsRedactedAsItWasSent()
    {
        // The epic 4 code round: the composer TRIMS the instruction before sending it, so a CLI echoes the trimmed text —
        // and a redaction of the stored, untrimmed text never matched it.
        Environment.SetEnvironmentVariable("FAKECLI_EXIT", "1");
        Environment.SetEnvironmentVariable("FAKECLI_STDERR", "error: the prompt was: " + Instruction);

        var service = Service(providers: [new("codex") { ExecutablePath = FakeCliExe, SystemPrompt = "  " + Instruction + "\n\n" }], launcher: new TurnLauncher(_launcher));
        var reply = await Consult(service, "The parser returns 3 where 4 is expected.");

        reply.ToString().Should().NotContain(Instruction);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("\n\t")]
    public void AWhitespaceInstruction_RedactsNothing(string instruction)
    {
        // Redacting whitespace would rewrite every gap in the child's output.
        ConsultantTurnInputs.Redacted(instruction).Should().BeEmpty();
    }

    [Fact]
    public async Task AConsultantThatEchoesTheInstruction_NeverCarriesItIntoTheReply()
    {
        Environment.SetEnvironmentVariable("FAKECLI_EXIT", "1");
        Environment.SetEnvironmentVariable("FAKECLI_STDERR", "error: the prompt was: " + Instruction);

        var reply = await Consult(WithInstruction(new TurnLauncher(_launcher)), "The parser returns 3 where 4 is expected.");

        reply.ToString().Should().NotContain(Instruction, "a CLI that echoes its prompt must not hand the row's system prompt back");
    }
}
