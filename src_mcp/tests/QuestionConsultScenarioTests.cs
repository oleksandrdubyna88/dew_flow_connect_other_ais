using System.Text.Json;
using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog.Core;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The <c>ask_consultants</c> flow through the public service entry the twelfth tool calls, with the fake CLI
/// standing in for codex as a REAL child process: the planned argv reaches a process, the answer file it
/// writes is read back, the reply is fenced, the record and the ledger are on disk.
/// </summary>
/// <remarks>
/// <para>In the <c>fakecli-env</c> collection for the reason every class that launches the fake CLI is: the
/// steering is process-wide. What this adds over <see cref="QuestionConsultServiceTests"/>' scripted launcher
/// is the one thing a script cannot prove — that the argv an adapter composes is one a process can be
/// started with and answers through.</para>
/// <para><b>The child is started with the MINIMAL environment</b> (S1 acceptance 6), so it sees no
/// <c>FAKECLI_*</c> variable — the first run of this test found exactly that: <c>exit 64: fake-cli: unknown
/// verb [exec …]</c>, the stand-in falling through to its verb mode. The fake CLI therefore also reads its
/// steering from a file in the temp directory, which IS on the minimal list, and this test writes that
/// file (<c>MinimalSteering</c>) rather than the environment.</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class QuestionConsultScenarioTests : IAsyncLifetime
{
    private const string Advice = "A circuit breaker: open after three failures, half-open after a minute.";

    private readonly ProcessLauncher _launcher = new();
    private string _repo = string.Empty;
    private string _data = string.Empty;
    private string _record = string.Empty;

    private static string FakeCliExe => Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli");

    private static string SteeringFile => Path.Combine(Path.GetTempPath(), "fakecli-minimal.json");

    public async ValueTask InitializeAsync()
    {
        _repo = Directory.CreateTempSubdirectory("coai-qscenario-repo-").FullName;
        _data = Directory.CreateTempSubdirectory("coai-qscenario-data-").FullName;
        _record = Directory.CreateTempSubdirectory("coai-qscenario-argv-").FullName;
        await Git("init", "-b", "main");
        await Git("config", "user.email", "t@example.com");
        await Git("config", "user.name", "t");
        await File.WriteAllTextAsync(Path.Combine(_repo, "Parser.cs"), "int Count() => 3;\n");
        await Git("add", ".");
        await Git("commit", "-m", "base");

        Steer(
            ("FAKECLI_STDOUT", "{\"type\":\"thread.started\",\"thread_id\":\"0198f2c1-first\"}\n"),
            ("FAKECLI_OUTFILE_TEXT", Advice),
            ("FAKECLI_RECORD_DIR", _record));
    }

    /// <summary>The fake CLI's steering, by FILE: a minimal-environment child never sees this process's variables.</summary>
    private static void Steer(params (string Name, string Value)[] pairs) =>
        File.WriteAllText(SteeringFile, JsonSerializer.Serialize(pairs.ToDictionary(p => p.Name, p => p.Value)));

    public ValueTask DisposeAsync()
    {
        try
        {
            File.Delete(SteeringFile);
        }
        catch (IOException) { }

        foreach (var dir in (string[])[_repo, _data, _record])
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }

        return ValueTask.CompletedTask;
    }

    private async Task Git(params string[] args)
    {
        var result = await _launcher.RunAsync(new ProcessRequest("git", args, _repo), TestContext.Current.CancellationToken);
        result.ExitCode.Should().Be(0, string.Join(' ', args) + ": " + result.StdErr);
    }

    private PanelService Service(params QuestionRow[] rows) => new(
        new PanelSettings
        {
            Providers = [],
            Rounds = PanelConfig.Uniform(3, 2, StagePolicy.Human),
            DataDir = _data,
            ReviewerTimeout = TimeSpan.FromSeconds(30),
            QuestionConsult = new QuestionConsultSettings { Rows = rows, RowBudget = TimeSpan.FromSeconds(30) },
        },
        VaultKeys.None("no vault in tests"),
        default,
        _launcher,
        Logger.None,
        Noticing.None);

    private static QuestionRow CodexRow(string prompt) =>
        new("astra", "codex", "codex", "gpt-6-astra", string.Empty, FakeCliExe, string.Empty, prompt, Enabled: true);

    /// <summary>
    /// A codex question row asks the installed codex its release before it launches (research/PLAN_codex_tier_floor.md): an Off
    /// row on 0.120.0 — which refuses <c>service_tier=default</c> at config load, and so failed every question row there —
    /// is sent no tier; one on 0.160.0 is sent exactly what it was before. The probe runs with the server's environment, so
    /// the steering FILE answers its <c>--version</c> too.
    /// </summary>
    [Theory]
    [InlineData("0.120.0", new string[0])]
    [InlineData("0.160.0", new[] { "service_tier=default" })]
    public async Task ACodexQuestionRow_IsToldOnlyWhatItsReleaseTakes(string release, string[] sent)
    {
        Steer(
            ("FAKECLI_STDOUT", "{\"type\":\"thread.started\",\"thread_id\":\"0198f2c1-first\"}\n"),
            ("FAKECLI_OUTFILE_TEXT", Advice),
            ("FAKECLI_RECORD_DIR", _record),
            ("FAKECLI_VERSION_STDOUT", $"codex-cli {release}\n"));

        var reply = JsonDocument.Parse(await Service(CodexRow("question-opinion")).AskConsultantsAsync(
            _repo, "Which retry shape fits a flaky vendor?", "I tried a fixed wait.", ct: TestContext.Current.CancellationToken)).RootElement;

        reply.GetProperty("status").GetString().Should().Be("complete", reply.ToString());
        var argv = LaunchRecords.Read(Directory.GetFiles(_record, "*.argv").Should().ContainSingle().Subject).Split('\0')[..^1];
        argv.Zip(argv.Skip(1)).Where(pair => pair.First == "-c" && pair.Second.StartsWith("service_tier=", StringComparison.Ordinal))
            .Select(pair => pair.Second).Should().Equal(sent, $"codex {release} with fast Off");
    }

    [Fact]
    public async Task AQuestionIsAnsweredByARealChild_FencedAndRecorded()
    {
        var service = Service(CodexRow("question-opinion"));

        var reply = JsonDocument.Parse(await service.AskConsultantsAsync(
            _repo, "Which retry shape fits a flaky vendor?", "I tried a fixed wait; the options are a ladder or a breaker.",
            ct: TestContext.Current.CancellationToken)).RootElement;

        reply.GetProperty("status").GetString().Should().Be("complete", reply.ToString());
        var answer = reply.GetProperty("answers").EnumerateArray().Should().ContainSingle().Subject;
        answer.GetProperty("vendor").GetString().Should().Be("codex");
        answer.GetProperty("advice").GetString().Should().Contain(Advice).And.Contain("status=\"advisory_only\"");
        answer.GetProperty("flag").GetString().Should().Be("unconfined");

        // The argv a real process was started with is the planner's — one shot, read-only, ephemeral, no --search.
        var argv = Directory.GetFiles(_record, "*.argv").Should().ContainSingle().Subject;
        var fields = LaunchRecords.Read(argv).Split('\0');
        fields.Should().ContainInOrder("exec", "-s", "read-only", "--ephemeral").And.NotContain("--search").And.NotContain("-C");
        fields[^1].Should().Contain("Which retry shape fits a flaky vendor?").And.Contain("ladder or a breaker", "the prompt reaches the child on stdin");

        var id = reply.GetProperty("consultId").GetString()!;
        var written = service.QuestionConsults.Read(id)!;
        written.Status.Should().Be(QuestionConsultStatuses.Answered);
        written.Rows.Single().Advice.Should().Be(Advice);
        File.ReadAllLines(Path.Combine(_data, "usage.jsonl")).Should().ContainSingle().Which.Should().Contain("\"kind\":\"question\"");
    }

    [Fact]
    public async Task ARowsCachedTokens_ReachTheLedger_NotOnlyItsInAndOut()
    {
        // The row's turn hands the ledger its whole Usage: a scalar call kept in, out and cost and dropped the cached
        // count (and the not-captured / no-price markers) codex reports (PR #692, CodeRabbit).
        Steer(
            ("FAKECLI_STDOUT", "{\"type\":\"thread.started\",\"thread_id\":\"0198f2c1-first\"}\n"
                + "{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":1200,\"cached_input_tokens\":800,\"output_tokens\":40}}\n"),
            ("FAKECLI_OUTFILE_TEXT", Advice),
            ("FAKECLI_RECORD_DIR", _record));

        var reply = JsonDocument.Parse(await Service(CodexRow("question-opinion")).AskConsultantsAsync(
            _repo, "Which retry shape fits a flaky vendor?", "I tried a fixed wait; the options are a ladder or a breaker.",
            ct: TestContext.Current.CancellationToken)).RootElement;

        reply.GetProperty("status").GetString().Should().Be("complete", reply.ToString());
        var billed = JsonDocument.Parse(File.ReadAllLines(Path.Combine(_data, "usage.jsonl")).Should().ContainSingle().Subject).RootElement;
        billed.GetProperty("tokensIn").GetInt64().Should().Be(1200);
        billed.GetProperty("tokensCached").GetInt64().Should().Be(800, "the cached count codex reported is part of the row's usage");
    }

    [Fact]
    public async Task AWebQuestionCarryingAPath_IsRefusedByClass_AndNoChildIsStarted()
    {
        var service = Service(CodexRow("question-web"));

        var reply = JsonDocument.Parse(await service.AskConsultantsAsync(
            _repo, "Why does src/Server/PanelService.cs grow?", "It is 1700 lines.", ct: TestContext.Current.CancellationToken)).RootElement;

        reply.GetProperty("status").GetString().Should().Be("none_available", "the only row was refused before any launch");
        var answer = reply.GetProperty("answers").EnumerateArray().Should().ContainSingle().Subject;
        answer.GetProperty("status").GetString().Should().Be(RowOutcomes.Refused);
        answer.GetProperty("reason").GetString().Should().Contain("(path)").And.Contain("describe the file");
        Directory.GetFiles(_record, "*.argv").Should().BeEmpty("nothing was launched");
    }

    [Fact]
    public async Task DocumentAndFeatureTogether_AreRefused_AsAskHumanRefusesThem()
    {
        var reply = JsonDocument.Parse(await Service(CodexRow("question-opinion")).AskConsultantsAsync(
            _repo, "q", "c", document: "docs/spec.md", feature: "todo/PLAN_x.md", ct: TestContext.Current.CancellationToken)).RootElement;

        reply.GetProperty("error").GetString().Should().Contain("document OR feature");
    }
}
