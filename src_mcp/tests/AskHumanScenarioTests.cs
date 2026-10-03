using System.Text.Json;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The scenario S3 acceptance 6 names, against a REAL server build over real stdio: under <c>require</c>, after the
/// plan's proceed and the two free batches, <c>ask_human</c> is refused naming <c>ask_consultants</c>; the consultants
/// answer (the fake CLI as a codex row, a real child); and <c>ask_human</c> with the <c>consultId</c> is allowed. None of
/// these questions is the gate's — the session is not held — so every allowed one is answered at once with
/// <c>ask_in_conversation</c> and none becomes a card (<c>todo/PLAN_ask_human_is_for_the_gate.md</c>, G2).
/// </summary>
/// <remarks>
/// <para><c>ask_human</c> was <c>NotCovered</c> in <see cref="ScenarioCoverageTests"/> because the wait is the
/// behaviour and a fake surface would exercise the fake. The gate changed that: the refusal does not wait at all,
/// and since 2026-10-03 neither does the AI's own question, which is asked in the AI's conversation. The card and
/// its wait are covered over the wire by <see cref="McpContractTests"/> on a held session.</para>
/// <para>In the <c>fakecli-env</c> collection, as every class that starts the fake CLI is; the row's child runs on
/// the MINIMAL environment, so the fake CLI's steering is the file in the temp directory, as
/// <see cref="QuestionConsultScenarioTests"/> explains.</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class AskHumanScenarioTests : IAsyncLifetime
{
    private const string Advice = "A circuit breaker: open after three failures, half-open after a minute.";

    private readonly ProcessLauncher _launcher = new();
    private readonly string _data = Directory.CreateTempSubdirectory("coai-askscenario-data-").FullName;
    private TempGitRepo _repo = null!;

    private static string FakeCliExe => Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli");

    private static string SteeringFile => Path.Combine(Path.GetTempPath(), "fakecli-minimal.json");

    public async ValueTask InitializeAsync()
    {
        _repo = await TempGitRepo.InitAsync(_launcher, "coai-askscenario-repo-");
        await _repo.WriteAsync("Parser.cs", "int Count() => 3;\n");
        await _repo.CommitAsync("base");
        File.WriteAllText(SteeringFile, JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["FAKECLI_STDOUT"] = "{\"type\":\"thread.started\",\"thread_id\":\"0198f2c1-first\"}\n",
            ["FAKECLI_OUTFILE_TEXT"] = Advice,
        }));
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            File.Delete(SteeringFile);
        }
        catch (IOException) { }

        await _repo.DisposeAsync();
        try
        {
            Directory.Delete(_data, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private string Rows => JsonSerializer.Serialize(new[]
    {
        new { id = "astra", vendor = "codex", runtime = "codex", model = "gpt-6-astra", executablePath = FakeCliExe, prompt = "question-opinion", enabled = true, acknowledged = true },
    });

    private string Arguments(string question, string consultId = "") =>
        JsonSerializer.Serialize(new { repoPath = _repo.Path, branch = "main", question, consultId });

    [Fact]
    public async Task RequireRefusesTheThirdBatch_AskConsultantsAnswers_AndItsConsultIdOpensAskHuman()
    {
        // The plan has proceeded: the phase every question after the plan round is asked in.
        new SessionStore(_data).Save(new PersistedSession(
            new SessionState("s-scenario", _repo.Path, "main", PanelConfig.Uniform(3, 2)) { PlanProceeded = true, Stage = Stage.CodeReview }, []));
        using var server = StdioServer.Start(_data, "debug", 1,
            ("COAI_QCONSULT_ROWS", Rows), ("COAI_QCONSULT_MODE", "require"), ("COAI_QCONSULT_ROW_MINUTES", "1"));
        try
        {
            await StdioServer.InitializeAsync(server);

            var first = await StdioServer.Call(server, 2, "ask_human", Arguments("Ship the parser?"));
            var second = await StdioServer.Call(server, 3, "ask_human", Arguments("Ship the parser now?"));
            first.GetProperty("status").GetString().Should().Be("ask_in_conversation", first.ToString());
            first.GetProperty("note").GetString().Should().Contain("free batch 1 of 2");
            second.GetProperty("note").GetString().Should().Contain("free batch 2 of 2");

            var refused = await StdioServer.Call(server, 4, "ask_human", Arguments("Ship the parser, really?"));
            refused.GetProperty("error").GetString().Should().Contain("ask_consultants", "the third batch goes to the consultants first");

            var consulted = await StdioServer.Call(server, 5, "ask_consultants",
                JsonSerializer.Serialize(new { repoPath = _repo.Path, question = "Which retry shape fits a flaky vendor?", context = "I tried a fixed wait; the options are a ladder or a breaker." }));
            consulted.GetProperty("status").GetString().Should().Be("complete", consulted.ToString());
            consulted.GetProperty("answers")[0].GetProperty("advice").GetString().Should().Contain(Advice);
            var consultId = consulted.GetProperty("consultId").GetString()!;

            var opened = await StdioServer.Call(server, 6, "ask_human", Arguments("The consultants say a breaker. Agreed?", consultId));
            opened.GetProperty("status").GetString().Should().Be("ask_in_conversation", opened.ToString());
            opened.GetProperty("instruction").GetString().Should().Contain("this conversation");

            Directory.Exists(Path.Combine(_data, "escalations")).Should().BeFalse(
                "none of these questions is the gate's — the session is not held — so none became a card in VS Code");
            var record = new QuestionConsultStore(_data).Read(consultId)!;
            record.Outcome.Should().Be("person_asked_in_conversation");
            record.EscalationId.Should().NotBeEmpty("the proof was spent once");
        }
        finally
        {
            await StdioServer.StopAsync(server);
        }
    }
}
