using System.Text.Json;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog.Core;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A consult line names the CONSULTATION its turn belongs to — E3.S4 of <c>todo/PLAN_the_round_counter_tail_E3.md</c>, the
/// <c>coai-mcp</c> half: the extension divides consultation money by consultations, and a line that cannot say which
/// consultation it is part of can only be counted as a turn of its own.
/// </summary>
/// <remarks>
/// <para>Decision D8: a consultant CHECK has no consultation behind it, and neither has a new consultation refused before its launch —
/// its record was never written, and an id invented for the line would merge unrelated turns. Both carry none.</para>
/// <para>Every test steers the fake CLI through the process environment, so the class runs in the collection that does not run in parallel.</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class AConsultLineCarriesItsConsultationTests : ConsultScenarioBase
{
    private static string Text(JsonElement line, string name) =>
        line.TryGetProperty(name, out var value) ? value.GetString() ?? string.Empty : string.Empty;

    private List<JsonElement> Lines() =>
        [.. File.ReadAllLines(Path.Combine(_data, "usage.jsonl")).Select(l => JsonDocument.Parse(l).RootElement.Clone())];

    private sealed class Environ : IDisposable
    {
        private readonly List<(string Name, string? Before)> _saved = [];

        public Environ Set(string name, string value)
        {
            _saved.Add((name, Environment.GetEnvironmentVariable(name)));
            Environment.SetEnvironmentVariable(name, value);

            return this;
        }

        public void Dispose()
        {
            foreach (var (name, before) in _saved)
            {
                Environment.SetEnvironmentVariable(name, before);
            }
        }
    }

    [Fact]
    public async Task AConsultLine_CarriesItsConsultation()
    {
        using var calling = CallingAs("CLAUDE_CODE_SESSION_ID");
        var service = Service();
        var first = await Consult(service, "the parser returns 3 where 4 is expected");
        var id = first.GetProperty("consultationId").GetString()!;

        Answer("0198f2c1-first", Advice);
        await Consult(service, "I ran your check: with two fields it prints 3", id);
        // A third turn that FAILS, in the same conversation: the failure path bills through its own writer.
        using var failing = new Environ().Set("FAKECLI_VERSION_EXIT", "0").Set("FAKECLI_EXIT", "1").Set("FAKECLI_STDERR", "codex: the model is overloaded");
        var failed = await Consult(service, "and with three fields it prints 4", id);

        failed.ToString().Should().Contain("failure", "the stand-in exited non-zero, so the third turn failed");
        var lines = Lines();
        lines.Should().HaveCount(3);
        lines.Select(l => Text(l, "consultation")).Should().OnlyContain(c => c == id,
            "an answered first turn, an answered follow-up and a failed turn all belong to the consultation the record names");
    }

    [Fact]
    public async Task ACheckLineAndARefusedNewConsultation_CarryNone()
    {
        using var calling = CallingAs("CLAUDE_CODE_SESSION_ID");

        // A new consultation refused before its launch: claude's --help never answers, so nothing is launched and no record is written.
        var refusedHelp = new ProcessResult(1, string.Empty, "help did not answer", TimedOut: false);
        var launcher = new TurnLauncher(_launcher) { Consultant = (_, _) => Task.FromResult(refusedHelp) };
        var claude = new Dictionary<string, ConsultantChoice>
        {
            [CallerIdentity.Claude] = new("claude", Runtime: "claude", ExecutablePath: FakeCliExe),
        };
        var reply = await Consult(Service(providers: [], consultants: claude, launcher: launcher), "why is the count wrong");
        reply.ToString().Should().Contain("vendor-refused");
        Directory.EnumerateFiles(Path.Combine(_data, "consultations"), "*.json").Should().BeEmpty("nothing was sent, so no consultation was opened");

        // A consultant check: a paid turn nobody asked a consultation for.
        using var scratch = TempDir.For("coai-consultation-check-");
        using var env = new Environ().Set("FAKECLI_OUTFILE_TEXT", "marker: {{cwd-file:CHECK.md}}\ncanary: CANNOT");
        var settings = new PanelSettings
        {
            Providers = [],
            DataDir = _data,
            ReviewerTimeout = TimeSpan.FromSeconds(60),
            ConsultEnabled = true,
            Consultants = new Dictionary<string, ConsultantChoice>
            {
                [CallerIdentity.Claude] = new("codex", Runtime: "codex", ExecutablePath: FakeCliExe),
            },
        };
        var (code, stdout, _) = await ConsultantCheckMode.AnswerAsync(
            settings, ["--check-consultant", "--caller", "claude"], _launcher, scratch.Path, _ => { }, Noticing.None, TestContext.Current.CancellationToken);
        code.Should().Be(0, stdout);

        var lines = Lines();
        lines.Select(l => Text(l, "role")).Should().BeEquivalentTo([ConsultantRoles.Consult, ConsultantRoles.Check]);
        lines.Select(l => Text(l, "consultation")).Should().OnlyContain(c => c.Length == 0,
            "neither a check nor a refusal before the launch has a consultation behind it, and an invented id would merge unrelated turns");
    }
}
