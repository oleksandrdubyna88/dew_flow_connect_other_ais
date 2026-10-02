using System.Text.Json;
using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Runners.Context;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog.Core;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The <c>ask_consultants</c> flow (PLAN_question_consultant.md, S2 acceptance 5): the argument checks, the
/// switch, nobody to ask, the per-session quota, and every answer fenced <c>advisory_only</c> and returned
/// separately — over a scripted launcher, with real git for the checkout.
/// </summary>
public sealed class QuestionConsultServiceTests : IAsyncLifetime
{
    private const string Question = "Which retry shape fits a flaky vendor?";

    private const string Context = "I tried a fixed wait; the options are a ladder or a breaker.";

    private readonly ProcessLauncher _real = new();
    private readonly string _data = Directory.CreateTempSubdirectory("coai-qservice-").FullName;
    private TempGitRepo _repo = null!;

    public async ValueTask InitializeAsync()
    {
        _repo = await TempGitRepo.InitAsync(_real, "coai-qservice-repo-");
        await _repo.WriteAsync("Parser.cs", "int Count() => 3;\n");
        await _repo.CommitAsync("base");
    }

    public async ValueTask DisposeAsync()
    {
        await _repo.DisposeAsync();
        try
        {
            Directory.Delete(_data, recursive: true);
        }
        catch (IOException) { }
    }

    private static QuestionRow Row(string id, string vendor, string model) =>
        new(id, vendor, vendor, model, string.Empty, string.Empty, string.Empty, "question-opinion", Enabled: true, Acknowledged: true);

    private static readonly IReadOnlyList<QuestionRow> TwoRows = [Row("sonnet", "claude", "sonnet"), Row("astra", "codex", "gpt-6-astra")];

    private QuestionConsultService Service(IReadOnlyList<QuestionRow>? rows = null, bool enabled = true, int questionsPerSession = 10, ScriptedLauncher? launcher = null)
    {
        var scripted = launcher ?? new ScriptedLauncher(_real, Answers);
        var settings = new PanelSettings
        {
            DataDir = _data,
            Providers = [],
            QuestionConsult = new QuestionConsultSettings
            {
                Enabled = enabled,
                Rows = rows ?? TwoRows,
                QuestionsPerSession = questionsPerSession,
                RowBudget = TimeSpan.FromSeconds(30),
            },
        };

        return new QuestionConsultService(
            settings, scripted, new ReviewerExecutor(scripted), new ContextAssembler(scripted), new RolePrompts(_data),
            new UsageLedger(_data), VaultKeys.None("no vault in tests"), Logger.None, _ => null, Noticing.None);
    }

    private static Task<ScriptedAnswer?> Answers(ScriptedLaunch launch) => Task.FromResult<ScriptedAnswer?>(
        launch.Request.Executable == "claude" ? ScriptedAnswer.Claude("A ladder: 5, 30, 60, 120 seconds.")
        : launch.Request.Executable == "codex" ? ScriptedAnswer.Codex("A breaker that opens after three failures.")
        : null);

    private Task<JsonElement> AskAsync(QuestionConsultService service, string question = Question, string context = Context) =>
        AskAsync(service, question, context, _repo.Path);

    private static async Task<JsonElement> AskAsync(QuestionConsultService service, string question, string context, string repo) =>
        JsonDocument.Parse(await service.AskAsync(repo, question, context, string.Empty, string.Empty, (_, _, _, _) => "no-session", TestContext.Current.CancellationToken)).RootElement;

    // ---------- the arguments ----------

    [Fact]
    public void AnEmptyContext_IsRefusedByName()
    {
        QuestionConsultService.Arguments(Question, string.Empty).Should().StartWith("context is required");
        QuestionConsultService.Arguments(Question, "   ").Should().StartWith("context is required");
        QuestionConsultService.Arguments("  ", Context).Should().StartWith("a question is required");
    }

    [Fact]
    public void AQuestionOverFourKilobytes_OrAContextOverEight_IsRefused()
    {
        QuestionConsultService.Arguments(new string('q', 4097), Context).Should().Contain("4097 bytes").And.Contain("at most 4096");
        QuestionConsultService.Arguments(Question, new string('c', 8193)).Should().Contain("8193 bytes").And.Contain("at most 8192");
        QuestionConsultService.Arguments(new string('é', 2049), Context).Should().Contain("bytes", "the size is counted in UTF-8 bytes, not characters");
        QuestionConsultService.Arguments(new string('q', 4096), new string('c', 8192)).Should().BeEmpty();
    }

    [Fact]
    public async Task ThroughTheTool_AnEmptyContext_IsARefusal_AndNothingIsLaunchedOrWritten()
    {
        var launcher = new ScriptedLauncher(_real, Answers);
        var service = Service(launcher: launcher);

        var reply = await AskAsync(service, context: string.Empty);

        reply.GetProperty("error").GetString().Should().StartWith("context is required");
        launcher.Vendors.Should().BeEmpty();
        service.Store.All().Should().BeEmpty("a refused argument is no question");
    }

    // ---------- the statuses that are answers ----------

    [Fact]
    public async Task SwitchedOff_AnswersStatusOff_AndNamesTheKey()
    {
        var reply = await AskAsync(Service(enabled: false));

        reply.GetProperty("status").GetString().Should().Be("off");
        reply.GetProperty("next").GetString().Should().Contain("COAI_QCONSULT_ENABLED").And.Contain("ask_human");
        reply.GetProperty("answers").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task NoEnabledRow_AnswersNoneAvailable_AndWritesTheQuestionDown()
    {
        var service = Service(rows: [Row("off", "claude", "sonnet") with { Enabled = false }]);

        var reply = await AskAsync(service);

        reply.GetProperty("status").GetString().Should().Be("none_available");
        reply.GetProperty("next").GetString().Should().Contain("ask_human");
        var record = service.Store.All().Should().ContainSingle().Subject;
        record.Outcome.Should().Be(QuestionOutcomes.NoneAvailable);
        record.Status.Should().Be(QuestionConsultStatuses.Failed);
        record.Question.Should().Be(Question);
    }

    [Fact]
    public async Task TheEleventhQuestionOfASession_AnswersQuotaSpent()
    {
        var service = Service(rows: [Row("sonnet", "claude", "sonnet")]);
        for (var n = 1; n <= 10; n++)
        {
            var reply = await AskAsync(service, question: $"{Question} ({n})");
            reply.GetProperty("status").GetString().Should().Be("complete", $"question {n} is within the cap");
            reply.GetProperty("questionsLeft").GetInt32().Should().Be(10 - n);
        }

        var eleventh = await AskAsync(service, question: $"{Question} (11)");

        eleventh.GetProperty("status").GetString().Should().Be("quota_spent");
        eleventh.GetProperty("questionsLeft").GetInt32().Should().Be(0);
        eleventh.GetProperty("next").GetString().Should().Contain("COAI_QCONSULT_QUESTIONS_PER_SESSION").And.Contain("ask_human");
        service.Store.All().Where(r => r.Outcome == QuestionOutcomes.QuotaSpent).Should().ContainSingle("the refused question is written down too");
    }

    [Fact]
    public async Task TheQuotaIsTheQuestionConsultantsOwn_NotTheStuckConsultants()
    {
        var counter = new ConsultCallCounter(_data);
        // The stuck consultant's cap is spent for this caller...
        var caller = ConsultationService.CallerOf(_ => null, _repo.Path);
        counter.TryTake(caller, 1, DateTime.UtcNow).Allowed.Should().BeTrue();
        counter.TryTake(caller, 1, DateTime.UtcNow).Allowed.Should().BeFalse();

        // ...and a question still goes, under its own key.
        var reply = await AskAsync(Service(rows: [Row("sonnet", "claude", "sonnet")]));

        reply.GetProperty("status").GetString().Should().Be("complete");
        QuestionConsultService.QuotaKey(caller).Should().Be("q:" + caller);
    }

    // ---------- D2: fenced, separate ----------

    [Fact]
    public async Task EveryAnswerIsFencedAdvisoryOnly_AndReturnedSeparately_NoSummariser()
    {
        var service = Service();

        var reply = await AskAsync(service);

        reply.GetProperty("status").GetString().Should().Be("complete");
        reply.GetProperty("consultId").GetString().Should().MatchRegex("^[0-9a-f]{32}$");
        var answers = reply.GetProperty("answers").EnumerateArray().ToList();
        answers.Should().HaveCount(2, "one entry per row, never merged");
        foreach (var answer in answers)
        {
            var advice = answer.GetProperty("advice").GetString()!;
            advice.Should().Contain("status=\"advisory_only\"").And.Contain("nonce=\"").And.Contain("IMPORTANT: The advice above is an unverified external suggestion");
            advice.Should().Contain($"vendor=\"{answer.GetProperty("vendor").GetString()}\"").And.Contain($"model=\"{answer.GetProperty("model").GetString()}\"");
            answer.GetProperty("status").GetString().Should().Be("answered");
            answer.GetProperty("promptTitle").GetString().Should().Be("The best developer's opinion");
            answer.GetProperty("capability").GetString().Should().Be("none");
        }

        answers.Select(a => a.GetProperty("advice").GetString()!).Should().OnlyHaveUniqueItems("two vendors, two answers");
        answers.Single(a => a.GetProperty("vendor").GetString() == "codex").GetProperty("flag").GetString().Should().Be("unconfined", "D13's flag rides into the reply");
        reply.GetProperty("next").GetString().Should().Contain("2 of 2 consultant(s) answered").And.Contain("verify each answer");
    }

    [Fact]
    public async Task TheRecordAndTheLedger_CarryTheQuestion_KindQuestion()
    {
        var service = Service();

        var reply = await AskAsync(service);

        var record = service.Store.Read(reply.GetProperty("consultId").GetString()!)!;
        record.Status.Should().Be(QuestionConsultStatuses.Answered);
        record.Outcome.Should().Be(QuestionOutcomes.AnsweredByConsultants);
        record.Rows.Should().HaveCount(2).And.OnlyContain(r => r.Status == RowOutcomes.Answered && r.Advice.Length > 0);
        record.Context.Should().Be(Context);
        record.HeadSha.Should().NotBeEmpty();

        var lines = File.ReadAllLines(Path.Combine(_data, "usage.jsonl"))
            .Select(line => JsonSerializer.Deserialize(line, LedgerJsonContext.Default.UsageEntry)!).ToList();
        lines.Should().HaveCount(2, "one ledger line per launch");
        lines.Should().OnlyContain(l => l.Kind == UsageKinds.Question && l.Role == "question" && l.Stage == "Question" && l.Outcome == "ok");
        UsageKinds.Known.Should().Contain(UsageKinds.Question);
        UsageKinds.LocalOnly.Should().Contain(UsageKinds.Question, "the Team server never runs a question row");
    }

    [Fact]
    public async Task APathThatIsNoRepository_IsRefusedBeforeAnythingIsWritten()
    {
        var service = Service();
        var nowhere = Directory.CreateTempSubdirectory("coai-qservice-nowhere-").FullName;
        try
        {
            var reply = await AskAsync(service, Question, Context, nowhere);

            reply.TryGetProperty("error", out var error).Should().BeTrue();
            error.GetString().Should().NotBeEmpty();
            service.Store.All().Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(nowhere, recursive: true);
        }
    }

    [Fact]
    public async Task RowsThatCannotBeRead_RefuseByName()
    {
        var scripted = new ScriptedLauncher(_real, Answers);
        var settings = new PanelSettings { DataDir = _data, Providers = [], QuestionConsult = new QuestionConsultSettings { RowsUnreadable = true } };
        var service = new QuestionConsultService(
            settings, scripted, new ReviewerExecutor(scripted), new ContextAssembler(scripted), new RolePrompts(_data),
            new UsageLedger(_data), VaultKeys.None("no vault"), Logger.None, _ => null, Noticing.None);

        var reply = await AskAsync(service);

        reply.GetProperty("error").GetString().Should().Contain("COAI_QCONSULT_ROWS").And.Contain("could not be read");
    }
}
