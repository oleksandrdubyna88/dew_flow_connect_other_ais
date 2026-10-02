using System.Text.Json;
using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Context;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog.Core;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The door to the person with the gate in front of it (<c>todo/PLAN_question_consultant.md</c>, S3 acceptance 1 and 3):
/// the phase inferred from the session and the stores, the consultId VERIFIED, a production risk writing the card
/// at once with the consultants running beside it, and the wait ending in <c>no_answer_yet</c> with the file marked
/// <c>expired</c> — over a scripted launcher, with real git for the checkout and a budget nobody meets.
/// </summary>
public sealed class AskHumanServiceTests : IAsyncLifetime
{
    private const string Question = "Which retry shape fits a flaky vendor?";

    private readonly ProcessLauncher _real = new();
    private readonly string _data = Directory.CreateTempSubdirectory("coai-askhuman-").FullName;
    private TempGitRepo _repo = null!;
    private string _caller = string.Empty;

    public async ValueTask InitializeAsync()
    {
        _repo = await TempGitRepo.InitAsync(_real, "coai-askhuman-repo-");
        await _repo.WriteAsync("Parser.cs", "int Count() => 3;\n");
        await _repo.CommitAsync("base");
        _caller = ConsultationService.CallerOf(_ => null, _repo.Path);
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

    // ---------- the harness ----------

    private sealed record Harness(
        AskHumanService Service,
        QuestionConsultService Questions,
        Escalations Escalations,
        SessionStore Sessions,
        QuestionPhaseStore Phase,
        CadenceStore Cadence,
        ScriptedLauncher Launcher);

    private static readonly QuestionRow ClaudeRow =
        new("sonnet", "claude", "claude", "sonnet", string.Empty, string.Empty, string.Empty, "question-opinion", Enabled: true, Acknowledged: true);

    private static Task<ScriptedAnswer?> Answers(ScriptedLaunch launch) => Task.FromResult<ScriptedAnswer?>(
        launch.Request.Executable == "claude" ? ScriptedAnswer.Claude("A ladder: 5, 30, 60, 120 seconds.") : null);

    private Harness Build(
        string mode = QuestionConsultSettings.Modes.Require,
        TimeSpan? budget = null,
        IReadOnlyList<QuestionRow>? rows = null,
        bool enabled = true,
        Func<ScriptedLaunch, Task<ScriptedAnswer?>>? script = null)
    {
        var settings = new PanelSettings
        {
            DataDir = _data,
            Providers = [],
            EscalationBudget = budget ?? TimeSpan.FromMilliseconds(300),
            QuestionConsult = new QuestionConsultSettings
            {
                Enabled = enabled,
                Mode = mode,
                Rows = rows ?? [ClaudeRow],
                RowBudget = TimeSpan.FromSeconds(30),
            },
        };
        var launcher = new ScriptedLauncher(_real, script ?? Answers);
        var questions = new QuestionConsultService(
            settings, launcher, new ReviewerExecutor(launcher), new ContextAssembler(launcher), new RolePrompts(_data),
            new UsageLedger(_data), VaultKeys.None("no vault in tests"), Logger.None, _ => null, Noticing.None);
        var escalations = new Escalations(_data, pollInterval: TimeSpan.FromMilliseconds(20));
        var sessions = new SessionStore(_data);
        var phase = new QuestionPhaseStore(_data);
        var cadence = new CadenceStore(_data);
        var desk = new AskGateDesk(questions, cadence, phase, escalations, _ => null, Logger.None);
        var service = new AskHumanService(
            settings, sessions, escalations, Logger.None, Noticing.None,
            (_, branch, _, _) => new SessionAddress(branch, string.Empty, string.Empty), desk);

        return new Harness(service, questions, escalations, sessions, phase, cadence, launcher);
    }

    /// <summary>A session whose plan has proceeded — the phase every question after the plan round is asked in.</summary>
    private void Proceeded(Harness h, string plan = "", string epic = "", Stage stage = Stage.CodeReview) =>
        h.Sessions.Save(new PersistedSession(
            new SessionState("s-1", _repo.Path, "main", PanelConfig.Uniform(3, 2)) { PlanProceeded = true, Stage = stage }, [])
        {
            Plan = plan,
            Epic = epic,
            CadenceRepoId = plan.Length > 0 ? "repo-id" : string.Empty,
        });

    private Task<JsonElement> Ask(Harness h, string consultId = "", bool productionRisk = false, string riskReason = "") =>
        Ask(h, Question, consultId, productionRisk, riskReason);

    private async Task<JsonElement> Ask(Harness h, string question, string consultId, bool productionRisk, string riskReason) =>
        JsonDocument.Parse(await h.Service.AskHumanAsync(
            _repo.Path, "main", question, consultId: consultId, productionRisk: productionRisk, riskReason: riskReason,
            ct: TestContext.Current.CancellationToken)).RootElement;

    private int Batches(Harness h) => h.Phase.Observe(new QuestionPhaseKey.Caller(_caller), released: false, DateTime.UtcNow).Batches;

    private async Task<string> Consulted(Harness h)
    {
        var reply = JsonDocument.Parse(await h.Questions.AskAsync(
            _repo.Path, Question, "I tried a fixed wait; the options are a ladder or a breaker.", string.Empty, string.Empty,
            (_, _, _, _) => "s-1", TestContext.Current.CancellationToken)).RootElement;
        reply.GetProperty("status").GetString().Should().Be("complete", reply.ToString());

        return reply.GetProperty("consultId").GetString()!;
    }

    private EscalationQuestion TheCard(Harness h)
    {
        var path = Directory.GetFiles(h.Escalations.Directory, "*.json").Should().ContainSingle(f => !f.EndsWith(".answer.json")).Subject;

        return h.Escalations.Read(Path.GetFileNameWithoutExtension(path))!;
    }

    // ---------- the phase ----------

    [Fact]
    public async Task InThePlanStage_TheQuestionGoesToThePerson_AndNothingIsCounted()
    {
        var h = Build();

        var reply = await Ask(h);

        reply.GetProperty("status").GetString().Should().Be("no_answer_yet");
        reply.GetProperty("note").GetString().Should().BeEmpty("the person is the right door while the plan is formed");
        Batches(h).Should().Be(0, "batches are counted from the plan's proceed");
        Directory.GetFiles(h.Escalations.Directory, "*.json").Should().ContainSingle("the card was written");
    }

    [Fact]
    public async Task AfterProceed_TheFirstTwoBatchesGoToThePerson_AndTheThirdIsRefusedNamingAskConsultants()
    {
        var h = Build();
        Proceeded(h);

        var first = await Ask(h);
        var second = await Ask(h, Question + " (again)");
        var third = await Ask(h, Question + " (a third time)");

        first.GetProperty("status").GetString().Should().Be("no_answer_yet");
        first.GetProperty("note").GetString().Should().Contain("free batch 1 of 2");
        second.GetProperty("note").GetString().Should().Contain("free batch 2 of 2");
        Batches(h).Should().Be(2, "both reached the person");
        third.GetProperty("error").GetString().Should().Contain("ask_consultants", "D7: the third batch goes through the consultant first")
            .And.Contain("productionRisk");
        Directory.GetFiles(h.Escalations.Directory, "*.json").Should().HaveCount(2, "a refused question writes no card");
    }

    [Fact]
    public async Task AVerifiedConsultId_OpensTheDoor_MarksTheRecord_AndIsSpentOnce()
    {
        var h = Build();
        Proceeded(h);
        await Ask(h);
        await Ask(h);
        var consultId = await Consulted(h);

        var opened = await Ask(h, consultId: consultId);

        opened.GetProperty("status").GetString().Should().Be("no_answer_yet", opened.ToString());
        var record = h.Questions.Store.Read(consultId)!;
        record.Outcome.Should().Be(QuestionOutcomes.PersonAsked, "the log says the person was asked after the consultants");
        record.EscalationId.Should().NotBeEmpty("and which card it became");
        TheCards(h).Should().Contain(card => card.Id == record.EscalationId && card.ConsultId == consultId,
            "the card names the consultation it followed, so the sidebar can fold the answers under it");

        var again = await Ask(h, Question + " (reused)", consultId, false, string.Empty);
        again.GetProperty("error").GetString().Should().Contain("already", "D14 (a): single use");
    }

    private IReadOnlyList<EscalationQuestion> TheCards(Harness h) =>
        [.. Directory.GetFiles(h.Escalations.Directory, "*.json").Where(f => !f.EndsWith(".answer.json"))
            .Select(f => h.Escalations.Read(Path.GetFileNameWithoutExtension(f))!)];

    [Fact]
    public async Task AFabricatedConsultId_IsRefusedAsIfNoneWereGiven_AndToldWhy()
    {
        var h = Build();
        Proceeded(h);
        await Ask(h);
        await Ask(h);

        var refused = await Ask(h, consultId: "0123456789abcdef0123456789abcdef");

        refused.GetProperty("error").GetString().Should().Contain("ask_consultants").And.Contain("0123456789abcdef0123456789abcdef")
            .And.ContainEquivalentOf("not a question this caller", "a fabricated id is the bypass D14 (a) closes");
    }

    [Fact]
    public async Task AConsultIdThatIsAnotherCallers_AnotherRepositorys_StillConsulting_OrTooOld_DoesNotCount()
    {
        var h = Build();
        Proceeded(h);
        await Ask(h);
        await Ask(h);
        var genuine = h.Questions.Store.Read(await Consulted(h))!;
        var stamp = QuestionConsultStore.Stamp(DateTime.UtcNow);
        var cases = new (string Name, QuestionConsultRecord Record, string Why)[]
        {
            ("another caller", genuine with { Id = QuestionConsultStore.NewId(), Caller = "somebody-else" }, "caller"),
            ("another repository", genuine with { Id = QuestionConsultStore.NewId(), RepoPath = Path.GetTempPath() }, "repositor"),
            ("still consulting", genuine with { Id = QuestionConsultStore.NewId(), Status = QuestionConsultStatuses.Consulting, EndedUtc = string.Empty }, "still"),
            ("too old", genuine with { Id = QuestionConsultStore.NewId(), EndedUtc = QuestionConsultStore.Stamp(DateTime.UtcNow - QuestionPolicy.ConsultProofAge - TimeSpan.FromMinutes(1)) }, "minutes"),
        };

        foreach (var (name, record, why) in cases)
        {
            h.Questions.Store.Write(record with { UpdatedUtc = stamp });
            var refused = await Ask(h, Question + " " + name, record.Id, false, string.Empty);

            refused.TryGetProperty("error", out var error).Should().BeTrue($"{name}: {refused}");
            error.GetString().Should().ContainEquivalentOf(why, name);
        }
    }

    [Fact]
    public async Task InRemind_TheThirdBatchIsAllowed_WithTheNote()
    {
        var h = Build(mode: QuestionConsultSettings.Modes.Remind);
        Proceeded(h);
        await Ask(h);
        await Ask(h);

        var third = await Ask(h, Question + " (third)", string.Empty, false, string.Empty);

        third.GetProperty("status").GetString().Should().Be("no_answer_yet");
        third.GetProperty("note").GetString().Should().Contain("ask_consultants");
        Batches(h).Should().Be(3);
    }

    [Fact]
    public async Task InOff_NothingIsSaid_AndNothingIsCounted()
    {
        var h = Build(mode: QuestionConsultSettings.Modes.Off);
        Proceeded(h);

        var reply = await Ask(h);

        reply.GetProperty("status").GetString().Should().Be("no_answer_yet");
        reply.GetProperty("note").GetString().Should().BeEmpty();
        Batches(h).Should().Be(0);
    }

    [Fact]
    public async Task WhenNoRowIsOn_TheGateStandsDown_WithTheNote()
    {
        var h = Build(rows: []);
        Proceeded(h);
        await Ask(h);
        await Ask(h);

        var third = await Ask(h, Question + " (third)", string.Empty, false, string.Empty);

        third.GetProperty("status").GetString().Should().Be("no_answer_yet", third.ToString());
        third.GetProperty("note").GetString().Should().Contain("stood down").And.Contain("no question-consultant row", "D9: never a deadlock");
    }

    [Fact]
    public async Task WhenTheConsultantIsSwitchedOff_TheGateStandsDownToo()
    {
        var h = Build(enabled: false);
        Proceeded(h);
        await Ask(h);
        await Ask(h);

        var third = await Ask(h, Question + " (third)", string.Empty, false, string.Empty);

        third.GetProperty("status").GetString().Should().Be("no_answer_yet", third.ToString());
        third.GetProperty("note").GetString().Should().Contain("stood down").And.Contain(QuestionConsultKeys.Enabled);
    }

    [Fact]
    public async Task AfterTheRelease_QuestionsGoToThePersonAgain()
    {
        var h = Build();
        Proceeded(h, plan: "todo/PLAN_x.md", epic: "2/2");
        h.Cadence.Update("repo-id", "todo/PLAN_x.md", state => state.WithClosed(1, "proceed", "t").WithClosed(2, "proceed", "t"));

        var reply = await Ask(h);

        reply.GetProperty("status").GetString().Should().Be("no_answer_yet", reply.ToString());
        reply.GetProperty("note").GetString().Should().BeEmpty();
        h.Phase.Observe(new QuestionPhaseKey.Plan("repo-id", "plan_x.md"), released: true, DateTime.UtcNow).Batches
            .Should().Be(0, "after the release nothing is counted");
    }

    [Fact]
    public async Task APlanWithAnEpicStillOpen_IsNotReleased_AndCountsUnderThePlan()
    {
        var h = Build();
        Proceeded(h, plan: "todo/PLAN_x.md", epic: "2/2");
        h.Cadence.Update("repo-id", "todo/PLAN_x.md", state => state.WithClosed(1, "proceed", "t"));

        await Ask(h);

        h.Phase.Observe(new QuestionPhaseKey.Plan("repo-id", "plan_x.md"), released: false, DateTime.UtcNow).Batches
            .Should().Be(1, "D14 (b): keyed by the plan, not the caller, when a plan is known");
        Batches(h).Should().Be(0, "and not under the caller");
    }

    [Fact]
    public async Task AnUnreadablePhaseRecord_Allows_AndSaysSo()
    {
        var h = Build();
        Proceeded(h);
        Directory.CreateDirectory(h.Phase.Directory);
        File.WriteAllText(h.Phase.FileFor(new QuestionPhaseKey.Caller(_caller)), "{ torn");

        var reply = await Ask(h);

        reply.GetProperty("status").GetString().Should().Be("no_answer_yet", reply.ToString());
        reply.GetProperty("note").GetString().Should().Contain("could not be read");
    }

    // ---------- the bypass (D8) ----------

    [Fact]
    public async Task ProductionRiskWithoutAReason_IsRefused()
    {
        var h = Build();
        Proceeded(h);

        var refused = await Ask(h, productionRisk: true);

        refused.GetProperty("error").GetString().Should().Contain("riskReason");
        Directory.Exists(h.Escalations.Directory).Should().BeFalse("nothing was written");
    }

    [Fact]
    public async Task AProductionRisk_WritesTheCardBeforeAnyLaunch_AndTheConsultantsAnswersArriveUnderIt()
    {
        var cardWasThereAtLaunch = false;
        var h = Build(budget: TimeSpan.FromSeconds(2), script: launch =>
        {
            if (launch.Request.Executable == "claude")
            {
                cardWasThereAtLaunch = Directory.Exists(Path.Combine(_data, "escalations"))
                    && Directory.GetFiles(Path.Combine(_data, "escalations"), "*.json").Any(f => !f.EndsWith(".answer.json"));

                return Task.FromResult<ScriptedAnswer?>(ScriptedAnswer.Claude("Back the column up first.") with { Wait = TimeSpan.FromMilliseconds(300) });
            }

            return Task.FromResult<ScriptedAnswer?>(null);
        });
        Proceeded(h);
        await Ask(h);
        await Ask(h);

        var reply = await Ask(h, Question + " (risk)", string.Empty, true, "the migration drops a column nothing can restore");

        reply.GetProperty("status").GetString().Should().Be("no_answer_yet", reply.ToString());
        reply.GetProperty("note").GetString().Should().ContainEquivalentOf("production risk");

        // The beside run is DETACHED: git and the outline come before its first launch, and under a loaded machine
        // that outlasts the two-second budget — the full suite showed `Vendors` empty at the instant ask_human
        // returned. The guarantee is the ORDER (the card before any launch, caught by the flag at launch time) and
        // the answers arriving under the card; neither is a promise about the budget, so both are asserted after
        // the wait.
        var card = await Eventually(() => TheCards(h).FirstOrDefault(c => c.ProductionRisk && c.ConsultantAnswers.Count > 0), TimeSpan.FromSeconds(20));
        card.Should().NotBeNull("the answers are folded under the card as the rows settle");
        h.Launcher.Vendors.Should().NotBeEmpty("the consultants ran");
        cardWasThereAtLaunch.Should().BeTrue("D8: the card is written AT ONCE, before any consultant is launched");
        card!.RiskReason.Should().Contain("drops a column");
        card.ConsultantAnswers.Single().Advice.Should().Be("Back the column up first.");
        card.ConsultantAnswers.Single().Vendor.Should().Be("claude");
        var record = h.Questions.Store.All().Single(r => r.ProductionRisk);
        record.Outcome.Should().Be(QuestionOutcomes.ProductionRisk);
        record.EscalationId.Should().Be(card.Id);
    }

    private static async Task<T?> Eventually<T>(Func<T?> read, TimeSpan within)
    {
        var deadline = DateTime.UtcNow + within;
        while (DateTime.UtcNow < deadline)
        {
            if (read() is { } found)
            {
                return found;
            }

            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        return read();
    }

    // ---------- the wait (A10) ----------

    [Fact]
    public async Task AtTheBudget_NoAnswerYet_AndTheFileReadsExpired_KeptForTheLog()
    {
        var h = Build(budget: TimeSpan.FromMilliseconds(300));

        var reply = await Ask(h);

        reply.GetProperty("status").GetString().Should().Be("no_answer_yet");
        reply.GetProperty("instruction").GetString().Should().Contain("ask the person directly").And.Contain("expired");
        var card = TheCard(h);
        card.Status.Should().Be(EscalationStatuses.Expired, "out of the active set");
        card.ExpiredUtc.Should().NotBeEmpty();
        File.Exists(h.Escalations.QuestionPath(card.Id)).Should().BeTrue("kept for the log; the retention takes it in seven days");
    }

    [Fact]
    public async Task AnAnsweredQuestion_IsNeverMarkedExpired()
    {
        var h = Build(budget: TimeSpan.FromSeconds(10));
        var asking = Ask(h);
        var id = (await Eventually(() => Directory.Exists(h.Escalations.Directory)
            ? Directory.GetFiles(h.Escalations.Directory, "*.json").Select(Path.GetFileNameWithoutExtension).FirstOrDefault()
            : null, TimeSpan.FromSeconds(5)))!;
        File.WriteAllText(h.Escalations.AnswerPath(id), JsonSerializer.Serialize(new { id, answer = "a ladder", answeredUtc = DateTime.UtcNow.ToString("O") }));

        var reply = await asking;

        reply.GetProperty("status").GetString().Should().Be("answered");
        reply.GetProperty("answer").GetString().Should().Be("a ladder");
        h.Escalations.Read(id)!.Status.Should().BeEmpty();
    }
}
