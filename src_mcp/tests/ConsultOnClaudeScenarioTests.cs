using System.Collections.Concurrent;
using System.Text.Json;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A claude consultation asks the INSTALLED claude whether it knows <c>--restricted</c> before every turn, and the
/// turn's argv follows the answer — and a claude that refuses the flag anyway is told to update.
/// </summary>
/// <remarks>
/// <para>E3.2 of PLAN_the_consultant_works_on_every_vendor.md. Measured 2026-10-02
/// (research/RESULTS_claude_consultant_confinement.md): claude 2.1.258 is confined by <c>--restricted</c> 9 of 9, and
/// claude 2.1.197 refuses any launch that carries it (<c>error: unknown option '--restricted'</c>, exit 1). A server
/// that always sent it would lose every consultation on the older CLI; one that never sent it would leave the newer
/// one unconfined.</para>
/// <para>The whole <c>consult</c> flow through the real service and the real launcher; only the claude CLI's own
/// processes are answered by the test (<see cref="ConsultScenarioBase.TurnLauncher"/>), with the REAL help texts of
/// both versions (<c>fixtures/claude/</c>). git runs for real.</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class ConsultOnClaudeScenarioTests : ConsultScenarioBase
{
    private const string Session = "67289235-65f7-40b5-9532-e63515d90f30";

    private static readonly IReadOnlyDictionary<string, ConsultantChoice> OnClaude = new Dictionary<string, ConsultantChoice>
    {
        [CallerIdentity.Claude] = new("claude", Runtime: "claude", ExecutablePath: FakeCliExe),
    };

    private static string Help(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "claude", name));

    private static ProcessResult Answered(string advice) =>
        new(0, $$"""{"type":"result","subtype":"success","is_error":false,"result":"{{advice}}","session_id":"{{Session}}"}""", string.Empty, TimedOut: false);

    private static bool IsHelp(ProcessRequest request) => request.Arguments is ["--help"];

    /// <summary>The claude CLI as the test plays it: <paramref name="help"/> for <c>--help</c>, <paramref name="turn"/> for a turn.</summary>
    private sealed class PlayedClaude(string help, ProcessResult turn)
    {
        public ConcurrentQueue<ProcessRequest> Asked { get; } = new();

        public string Help { get; set; } = help;

        /// <summary>The exit code <c>--help</c> answers with — non-zero is a help that did not come back.</summary>
        public int HelpExit { get; set; }

        public IReadOnlyList<ProcessRequest> Turns => [.. Asked.Where(request => !IsHelp(request))];

        public int HelpsAsked => Asked.Count(IsHelp);

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct)
        {
            Asked.Enqueue(request);

            return Task.FromResult(IsHelp(request) ? new ProcessResult(HelpExit, Help, string.Empty, TimedOut: false) : turn);
        }
    }

    private PanelService OnThis(PlayedClaude claude, Serilog.ILogger? log = null) =>
        Service(providers: [], consultants: OnClaude, launcher: new TurnLauncher(_launcher) { Consultant = claude.RunAsync }, log: log);

    private JsonElement RecordFile() =>
        JsonDocument.Parse(File.ReadAllText(Directory.EnumerateFiles(Path.Combine(_data, "consultations"), "*.json").Single())).RootElement;

    private JsonElement HealthFile(string name) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(_data, "consultations", "health", name))).RootElement;

    private static string Field(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.GetString() ?? string.Empty : string.Empty;

    [Theory]
    [InlineData("help-2.1.258-windows.txt", "restricted")]
    [InlineData("help-2.1.197-wsl.txt", "no-restricted")]
    public async Task TheInstalledClaudesHelp_DecidesTheArgv_AndTheTurnRecordsWhatWasSent(string help, string sent)
    {
        using var calling = CallingAs("CLAUDE_CODE_SESSION_ID");
        var sink = new ListSink();
        var claude = new PlayedClaude(Help(help), Answered(Advice));

        var reply = await Consult(OnThis(claude, new Serilog.LoggerConfiguration().WriteTo.Sink(sink).CreateLogger()), "why is the count wrong");

        reply.TryGetProperty("error", out var error).Should().BeFalse(error.ToString());
        Advise(reply).Should().Contain("separator");
        var argv = claude.Turns.Should().ContainSingle().Subject.Arguments;
        argv.Contains("--restricted").Should().Be(sent == "restricted",
            sent == "restricted" ? "claude 2.1.258 declares --restricted, and it is what confined the consultant 9 of 9"
                                 : "claude 2.1.197 refuses any launch carrying --restricted");
        argv.Should().ContainInOrder(["--tools", "Read,Glob,Grep"], "the allowlist rides either way");

        // What was SENT is what is recorded — on the turn, the record and the caller kind's health file — so a
        // later reader shows what ran, not what a fresh probe says now (epic 3's code round).
        var record = RecordFile();
        Field(record, "confinement").Should().Be(sent);
        Field(record.GetProperty("turns")[0], "confinement").Should().Be(sent);
        Field(HealthFile("claude.answer.json"), "confinement").Should().Be(sent);
        sink.Lines.Should().Contain(line => line.Contains($"launches {sent}:", StringComparison.Ordinal),
            "every turn's capability and the reason for it are logged");
    }

    [Fact]
    public async Task AClaudeWhoseHelpNeverAnswers_IsAskedTwice_ThenREFUSED_AndNothingIsLaunched()
    {
        // The security half of epic 3's code round: a --help that does not come back says nothing about the flag.
        // The first version launched without --restricted on it — unconfined, on a claude that may well have the
        // flag. Now: asked once more, then the turn is refused, classified, and told why.
        using var calling = CallingAs("CLAUDE_CODE_SESSION_ID");
        var sink = new ListSink();
        var claude = new PlayedClaude(Help("help-2.1.258-windows.txt"), Answered(Advice)) { HelpExit = 1 };

        var reply = await Consult(OnThis(claude, new Serilog.LoggerConfiguration().WriteTo.Sink(sink).CreateLogger()), "why is the count wrong");

        claude.Turns.Select(turn => string.Join(' ', turn.Arguments)).Should().BeEmpty("nothing may be launched when its confinement cannot be told");
        claude.HelpsAsked.Should().Be(2, "asked once more before giving up");
        var refused = Refusal(reply);
        refused.Should().Contain("(failure: vendor-refused)");
        refused.Should().Contain("will not launch it unconfined").And.Contain("claude --help");
        // Nothing was launched, so no consultation was opened (the whole-branch review, E) — the health file says it.
        Directory.EnumerateFiles(Path.Combine(_data, "consultations"), "*.json").Should().BeEmpty("a consultation nothing was sent to was never opened");
        Field(HealthFile("claude.failure.json"), "kind").Should().Be("vendor-refused");
        Field(HealthFile("claude.failure.json"), "confinement").Should().BeEmpty("nothing was sent");
        sink.Events.Select(e => e.Level).Zip(sink.Lines).Should().Contain(
            said => said.First == Serilog.Events.LogEventLevel.Warning && said.Second.Contains("not launched", StringComparison.Ordinal),
            "a refused turn is a warning in the log");
    }

    [Fact]
    public async Task ARefusalBeforeTheLaunch_DoesNotEndTheConsultation_SoTheNextTurnIsStillAccepted()
    {
        // The whole-branch review, E: nothing was launched, so nothing about the CONVERSATION went wrong — a --help
        // that timed out once ended a consultation two turns deep, and the caller's next follow-up was refused for a
        // record the refusal itself had closed.
        using var calling = CallingAs("CLAUDE_CODE_SESSION_ID");
        var claude = new PlayedClaude(Help("help-2.1.258-windows.txt"), Answered(Advice));
        var service = OnThis(claude);
        var id = (await Consult(service, "why is the count wrong")).GetProperty("consultationId").GetString()!;
        await Consult(service, "I ran your check: with two fields it prints 3", id);
        claude.HelpExit = 1;

        var refused = Refusal(await Consult(service, "and with three fields it prints 4", id));

        refused.Should().Contain("(failure: vendor-refused)", "the answer still says what happened and its cure");
        new ConsultationStore(_data).Read(id)!.Status.Should().Be(ConsultationStatuses.Open, "nothing was launched, so the consultation is as it was");
        Field(HealthFile("claude.failure.json"), "kind").Should().Be("vendor-refused", "the health file still records it");
        claude.HelpExit = 0;
        var again = await Consult(service, "and with three fields it prints 4", id);
        again.TryGetProperty("error", out var error).Should().BeFalse(error.ToString());
        again.GetProperty("turnIndex").GetInt32().Should().Be(3, "the refused turn was never counted");
    }

    [Fact]
    public async Task ARefusalBeforeTheFirstLaunch_LeavesNoConsultationRecord()
    {
        using var calling = CallingAs("CLAUDE_CODE_SESSION_ID");
        var claude = new PlayedClaude(Help("help-2.1.258-windows.txt"), Answered(Advice)) { HelpExit = 1 };

        Refusal(await Consult(OnThis(claude), "why is the count wrong")).Should().Contain("(failure: vendor-refused)");

        new ConsultationStore(_data).All().Should().BeEmpty("a consultation nothing was ever launched for was never opened");
    }

    [Fact]
    public async Task TheCliIsAskedBeforeEVERYTurn_SoAnUpgradeBetweenTurnsIsSeen()
    {
        // Risk consultation 264fbcf2, 2026-10-03: no cache. A claude upgraded in place between two turns of one
        // consultation is launched with the flags IT knows, not the ones its predecessor knew.
        using var calling = CallingAs("CLAUDE_CODE_SESSION_ID");
        var claude = new PlayedClaude(Help("help-2.1.197-wsl.txt"), Answered(Advice));
        var service = OnThis(claude);

        var first = await Consult(service, "why is the count wrong");
        claude.Help = Help("help-2.1.258-windows.txt");
        await Consult(service, "I ran your check: with two fields it prints 3", first.GetProperty("consultationId").GetString()!);

        claude.HelpsAsked.Should().Be(2, "one --help per turn");
        claude.Turns.Select(turn => turn.Arguments.Contains("--restricted")).Should().Equal([false, true],
            "turn 1 on 2.1.197, turn 2 on the upgraded 2.1.258");
        claude.Turns[1].Arguments.Should().ContainInOrder(["--resume", Session], "the resumed turn keeps its conversation");
    }

    [Fact]
    public async Task AClaudeThatRefusesRestricted_IsVendorRefused_AndToldToUpdateTheCli()
    {
        // The shape claude 2.1.197 answered on 2026-10-02 — here from a CLI whose help DID declare the flag, which
        // is what a shim that resolves to another install, or a downgrade between the probe and the launch, looks
        // like. Whatever the cause, the person's cure is the CLI's own update, not a sharper question.
        using var calling = CallingAs("CLAUDE_CODE_SESSION_ID");
        var claude = new PlayedClaude(
            Help("help-2.1.258-windows.txt"),
            new ProcessResult(1, string.Empty, "error: unknown option '--restricted'", TimedOut: false));

        var refused = Refusal(await Consult(OnThis(claude), "why is the count wrong"));

        claude.Turns.Should().ContainSingle().Which.Arguments.Should().Contain("--restricted");
        refused.Should().Contain("(failure: vendor-refused)");
        refused.Should().Contain("update the CLI");
    }

    [Fact]
    public void ARecordWrittenBeforeTheConfinementWasRecorded_ReadsAsEmpty_NotNull()
    {
        // Doctrine 4a: the source-generated deserializer runs no initialiser and binds an omitted positional
        // parameter to null — and every record already on disk lacks both fields.
        const string old = """
            {"id":"c1","status":"open","turns":[{"utc":"2026-10-01T00:00:00Z","problem":"p","advice":"a","seconds":1,"tokensIn":1,"tokensOut":1}]}
            """;

        var record = JsonSerializer.Deserialize(old, ConsultationJsonContext.Default.ConsultationRecord)!;

        record.Confinement.Should().BeEmpty();
        record.Turns[0].Confinement.Should().BeEmpty();
    }
}
