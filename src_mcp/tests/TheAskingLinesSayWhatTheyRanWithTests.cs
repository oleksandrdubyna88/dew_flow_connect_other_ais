using System.Collections.Concurrent;
using System.Text.Json;
using CoaiMcp.Core.Catalog;
using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog.Core;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The ledger lines a consultation, a consultant check and a question row write say what their launch RAN WITH — the CLI,
/// the release when one was already asked, the effort and the tier each with its source — E3.S1 of
/// <c>todo/PLAN_the_round_counter_tail_E3.md</c>. Story 3.4 of the promoted plan labelled the reviewers' lines and left these
/// blank, so a consult line could not say which CLI had answered or at which tier.
/// </summary>
/// <remarks>
/// <para>Every test steers the fake CLI and <c>CODEX_HOME</c> through the process environment (or, for a question row, whose child
/// is started on a minimal environment, the steering FILE), so the class runs in the collection that does not run in parallel.
/// The probe's own twin is <c>ProbeApiModeTests.AProbeLine_SaysWhatItRanWith</c>.</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class TheAskingLinesSayWhatTheyRanWithTests : ConsultScenarioBase
{
    private const string Release = "codex-cli 0.131.0";

    /// <summary>A codex configuration that names an effort and nothing else — what a person's own file does.</summary>
    private const string CodexConfig = "model = \"gpt-6-luna\"\nmodel_reasoning_effort = \"xhigh\"\n";

    private static string Text(JsonElement line, string name) =>
        line.TryGetProperty(name, out var value) ? value.GetString() ?? string.Empty : string.Empty;

    /// <summary>The one ledger line this test's data directory holds, of the kind asked for.</summary>
    private JsonElement OnlyLine(string kind)
    {
        var lines = File.ReadAllLines(Path.Combine(_data, "usage.jsonl")).Select(l => JsonDocument.Parse(l).RootElement.Clone())
            .Where(l => Text(l, "kind") == kind).ToList();

        return lines.Should().ContainSingle().Subject;
    }

    /// <summary>Process-wide variables set for one test and put back (or cleared) afterwards.</summary>
    private sealed class Environ(TempDir? owned = null) : IDisposable
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

            owned?.Dispose();
        }
    }

    /// <summary>A codex home holding <paramref name="config"/>, made the process's for the test.</summary>
    private static Environ CodexHome(string config)
    {
        var home = TempDir.For("coai-asking-home-");
        File.WriteAllText(home.At("config.toml"), config);

        return new Environ(home).Set("CODEX_HOME", home.Path);
    }

    private static void SaysItIsCodex(JsonElement line)
    {
        Text(line, "cli").Should().Be("codex");
        Text(line, "cliVersion").Should().Be(Release, "the version is the tier probe's answer, which this launch asked before it started: " + line);
        (Text(line, "tier"), Text(line, "tierFrom")).Should().Be(("default", OptionSources.Row), "an Off row on a release that takes it is told the standard tier");
        (Text(line, "effort"), Text(line, "effortFrom")).Should().Be(("xhigh", OptionSources.CliConfig), "nothing was sent, and the fake config.toml names xhigh");
    }

    [Fact]
    public async Task AConsultationLine_SaysWhatItRanWith()
    {
        using var calling = CallingAs("CLAUDE_CODE_SESSION_ID");
        using var env = CodexHome(CodexConfig).Set("FAKECLI_VERSION_STDOUT", Release + "\n");

        var reply = await Consult(Service(), "the parser returns 3 where 4 is expected");

        reply.TryGetProperty("error", out _).Should().BeFalse(reply.ToString());
        SaysItIsCodex(OnlyLine(UsageKinds.Consult));
    }

    [Fact]
    public async Task AFailedConsultationLine_SaysWhatItRanWith()
    {
        using var calling = CallingAs("CLAUDE_CODE_SESSION_ID");
        // The stand-in's bare variables also answer its `--version`, so the probe's own exit is pinned: the launch fails, the probe does not.
        using var env = CodexHome(CodexConfig).Set("FAKECLI_VERSION_STDOUT", Release + "\n").Set("FAKECLI_VERSION_EXIT", "0")
            .Set("FAKECLI_EXIT", "1").Set("FAKECLI_STDERR", "codex: the model is overloaded");

        var reply = await Consult(Service(), "the parser returns 3 where 4 is expected");

        reply.ToString().Should().Contain("failure", "the stand-in exited non-zero, so the turn failed");
        var line = OnlyLine(UsageKinds.Consult);
        Text(line, "outcome").Should().NotBe("ok");
        SaysItIsCodex(line);
    }

    [Fact]
    public async Task AConsultantCheckLine_SaysWhatItRanWith()
    {
        using var scratch = TempDir.For("coai-asking-check-");
        using var env = CodexHome(CodexConfig)
            .Set("FAKECLI_VERSION_STDOUT", Release + "\n")
            .Set("FAKECLI_OUTFILE_TEXT", "marker: {{cwd-file:CHECK.md}}\ncanary: CANNOT");
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
        var line = OnlyLine(UsageKinds.Consult);
        Text(line, "role").Should().Be(ConsultantRoles.Check);
        SaysItIsCodex(line);
    }

    /// <summary>
    /// A check whose caller cancels AFTER its launch landed is billed for that launch, so its line says what the launch ran with — a refusal
    /// before any launch is the only line that may be blank. The first launch answers nothing (so a follow-up is offered and the tree is
    /// asked), and the caller's token fires the moment that launch returns; the cancellation reaches the check from the tree snapshot.
    /// </summary>
    [Fact]
    public async Task ACheckCancelledAfterItsLaunch_StillRecordsWhatTheLaunchRanWith()
    {
        using var scratch = TempDir.For("coai-asking-check-cancelled-");
        using var env = CodexHome(CodexConfig)
            .Set("FAKECLI_VERSION_STDOUT", Release + "\n")
            .Set("FAKECLI_STDOUT", """{"type":"thread.started","thread_id":"0198f2c1-check"}""" + "\n");
        using var caller = new CancellationTokenSource();
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
            settings, ["--check-consultant", "--caller", "claude"], new CancelsAfterTheLaunch(_launcher, caller), scratch.Path, _ => { }, Noticing.None, caller.Token);

        code.Should().Be(0, stdout);
        var line = OnlyLine(UsageKinds.Consult);
        Text(line, "role").Should().Be(ConsultantRoles.Check);
        Text(line, "outcome").Should().Be("cancelled", "the caller took the call back after the launch landed");
        SaysItIsCodex(line);
    }

    /// <summary>The real launcher, firing the caller's token the moment the consultant's own child (not its <c>--version</c>) has returned.</summary>
    private sealed class CancelsAfterTheLaunch(IProcessLauncher inner, CancellationTokenSource caller) : IProcessLauncher
    {
        public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            var result = await inner.RunAsync(request, ct);
            if (string.Equals(request.Executable, FakeCliExe, StringComparison.OrdinalIgnoreCase) && request.Arguments is not ["--version"])
            {
                await caller.CancelAsync();
            }

            return result;
        }
    }

    [Fact]
    public async Task AQuestionRowLine_SaysWhatItRanWith()
    {
        // The tier probe asks with the SERVER's environment, which the base class's FAKECLI_MODE keeps from reading the steering file.
        using var env = CodexHome(CodexConfig).Set("FAKECLI_VERSION_STDOUT", Release + "\n");
        using var steering = FakeCliSteering.Write(new Dictionary<string, string>
        {
            ["FAKECLI_STDOUT"] = "{\"type\":\"thread.started\",\"thread_id\":\"0198f2c1-first\"}\n",
            ["FAKECLI_OUTFILE_TEXT"] = Advice,
            ["FAKECLI_VERSION_STDOUT"] = Release + "\n",
        });
        var row = new QuestionRow("astra", "codex", "codex", "gpt-6-astra", string.Empty, FakeCliExe, string.Empty, "question-opinion", Enabled: true);
        var service = new PanelService(
            new PanelSettings
            {
                Providers = [],
                Rounds = PanelConfig.Uniform(3, 2, StagePolicy.Human),
                DataDir = _data,
                ReviewerTimeout = TimeSpan.FromSeconds(30),
                QuestionConsult = new QuestionConsultSettings { Rows = [row], RowBudget = TimeSpan.FromSeconds(30) },
            },
            VaultKeys.None("no vault in tests"), default, _launcher, Logger.None, Noticing.None);

        var reply = JsonDocument.Parse(await service.AskConsultantsAsync(
            _repo, "Which retry shape fits a flaky vendor?", "I tried a fixed wait.", ct: TestContext.Current.CancellationToken)).RootElement;

        reply.GetProperty("status").GetString().Should().Be("complete", reply.ToString());
        SaysItIsCodex(OnlyLine(UsageKinds.Question));
    }

    [Fact]
    public void ACodexConsultantsLaunch_SaysTheTierItSent()
    {
        var row = new ProviderSettings("codex") { Runtime = "codex", Fast = FastMode.Off };
        var settings = ConsultantTurnInputs.Settings(row, row.Model, TimeSpan.FromMinutes(5), new PanelSettings { DataDir = _data }, VaultKeys.None("t"));

        var built = ConsultantResolution.For(row.Identity())!
            .Build(new ConsultantLaunch("D:/rsd/some-checkout", "help me", string.Empty, "D:/answers", settings, "D:/answers/schema.json"));

        string.Join(' ', built.Request.Arguments).Should().Contain("service_tier=default", "the argv carried the standard tier");
        built.Sent.Tier.Should().Be("default", "a launch that put a tier on its argv says so, or its line reads 'not sent'");
    }

    [Fact]
    public void AClaudeConsultantsLaunch_SaysTheEffortAndTierItSent()
    {
        var row = new ProviderSettings("claude") { Runtime = "claude", Model = "claude-opus-5", CliEffort = "high", Fast = FastMode.On };
        var settings = ConsultantTurnInputs.Settings(row, row.Model, TimeSpan.FromMinutes(5), new PanelSettings { DataDir = _data }, VaultKeys.None("t"))
            with
        { ClaudeCli = ClaudeCapability.WithRestricted };

        var built = ConsultantResolution.For(row.Identity())!
            .Build(new ConsultantLaunch("D:/rsd/some-checkout", "help me", string.Empty, "D:/answers", settings, "D:/answers/schema.json"));

        built.Request.Arguments.Should().ContainInConsecutiveOrder("--effort", "high").And.Contain("--settings");
        built.Sent.Should().Be(new SentOptions("high", "fast"), "the claude consultant sends an effort and a fast-mode file, and its line must say so");
    }

    [Fact]
    public async Task ALineWhoseCliWasNeverAsked_HasAnEmptyVersion_AndSpawnsNothing()
    {
        using var calling = CallingAs("CLAUDE_CODE_SESSION_ID");
        var help = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "claude", "help-2.1.258-windows.txt"));
        var asked = new ConcurrentQueue<ProcessRequest>();
        var launcher = new Counting(asked, new TurnLauncher(_launcher)
        {
            Consultant = (request, _) => Task.FromResult(request.Arguments is ["--help"]
                ? new ProcessResult(0, help, string.Empty, TimedOut: false)
                : new ProcessResult(0, """{"type":"result","subtype":"success","is_error":false,"result":"Check the separator.","session_id":"67289235-65f7-40b5-9532-e63515d90f30"}""", string.Empty, TimedOut: false)),
        });
        var claude = new Dictionary<string, ConsultantChoice>
        {
            [CallerIdentity.Claude] = new("claude", Runtime: "claude", ExecutablePath: FakeCliExe),
        };

        var reply = await Consult(Service(providers: [], consultants: claude, launcher: launcher), "why is the count wrong");

        reply.TryGetProperty("error", out _).Should().BeFalse(reply.ToString());
        var line = OnlyLine(UsageKinds.Consult);
        Text(line, "cliVersion").Should().BeEmpty("no version was asked for a claude consultation, and none may be invented");
        asked.Where(request => request.Arguments is ["--version"]).Should().BeEmpty("a label is never bought with a spawn per launch");
    }

    /// <summary>Every request the service made, kept — so a plant that asks <c>--version</c> per launch is seen.</summary>
    private sealed class Counting(ConcurrentQueue<ProcessRequest> asked, IProcessLauncher inner) : IProcessLauncher
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            asked.Enqueue(request);

            return inner.RunAsync(request, ct);
        }
    }
}
