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
        new("sonnet", "claude", "claude", "sonnet", string.Empty, string.Empty, string.Empty, "question-opinion", Enabled: true);

    private static Task<ScriptedAnswer?> Answers(ScriptedLaunch launch) => Task.FromResult<ScriptedAnswer?>(
        launch.Request.Executable == "claude" ? ScriptedAnswer.Claude("A ladder: 5, 30, 60, 120 seconds.") : null);

    private Harness Build(
        string mode = QuestionConsultSettings.Modes.Require,
        TimeSpan? budget = null,
        IReadOnlyList<QuestionRow>? rows = null,
        bool enabled = true,
        Func<ScriptedLaunch, Task<ScriptedAnswer?>>? script = null,
        IReadOnlyList<string>? roots = null)
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
                Roots = roots ?? [],
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

    /// <summary>
    /// A session whose gate is HELD by <c>call_human</c> — where an <c>ask_human</c> question is the gate's own and becomes a
    /// card (<c>research/PLAN_ask_human_is_for_the_gate.md</c>, G1; the other such place, a feature review's request window, is
    /// the feature tests'). Every test about the card, the wait and the answers folded under it runs here.
    /// </summary>
    private void Held(Harness h) =>
        h.Sessions.Save(new PersistedSession(
            new SessionState("s-1", _repo.Path, "main", PanelConfig.Uniform(3, 2)) { PlanProceeded = true, HumanGate = true, Stage = Stage.CodeReview }, []));

    /// <summary>The cards in front of the person — none at all when the directory was never made.</summary>
    private static int Cards(Harness h) =>
        Directory.Exists(h.Escalations.Directory)
            ? Directory.GetFiles(h.Escalations.Directory, "*.json").Count(f => !f.EndsWith(".answer.json", StringComparison.Ordinal))
            : 0;

    /// <summary>The AI's own question: no card, the reply tells it to ask in its own conversation (G2).</summary>
    private static void AskedInTheConversation(JsonElement reply, Harness h)
    {
        reply.GetProperty("status").GetString().Should().Be("ask_in_conversation", reply.ToString());
        reply.GetProperty("instruction").GetString().Should().Contain("this conversation");
        Cards(h).Should().Be(0, "a question that is not the gate's is not shown in VS Code under 'a review is waiting on you'");
    }

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
    public async Task InThePlanStage_TheQuestionIsAskedInTheConversation_AndNothingIsCounted()
    {
        var h = Build();

        var reply = await Ask(h);

        AskedInTheConversation(reply, h);
        reply.GetProperty("note").GetString().Should().BeEmpty("the person is the right door while the plan is formed");
        Batches(h).Should().Be(0, "batches are counted from the plan's proceed");
    }

    [Fact]
    public async Task AfterProceed_TheFirstTwoBatchesGoToThePerson_AndTheThirdIsRefusedNamingAskConsultants()
    {
        var h = Build();
        Proceeded(h);

        var first = await Ask(h);
        var second = await Ask(h, Question + " (again)");
        var third = await Ask(h, Question + " (a third time)");

        AskedInTheConversation(first, h);
        first.GetProperty("note").GetString().Should().Contain("free batch 1 of 2");
        second.GetProperty("note").GetString().Should().Contain("free batch 2 of 2");
        Batches(h).Should().Be(2, "both reached the person — in the conversation, which is still the person");
        third.GetProperty("error").GetString().Should().Contain("ask_consultants", "D7: the third batch goes through the consultant first")
            .And.Contain("productionRisk");
        Cards(h).Should().Be(0);
    }

    /// <summary>
    /// The symptom of 2026-10-03: an AI's own A-or-B question became a card under "a review is waiting on you", and
    /// its Answer… offered the gate's three decisions. Outside a held gate there is no card at all.
    /// </summary>
    [Fact]
    public async Task AnAiQuestionOutsideTheGate_WritesNoCard_AndIsAskedInTheConversation()
    {
        var h = Build(budget: TimeSpan.FromSeconds(5));
        Proceeded(h);

        var started = DateTime.UtcNow;
        var reply = await Ask(h, "Should the api row read A) the working tree or B) the last commit?", string.Empty, false, string.Empty);

        AskedInTheConversation(reply, h);
        (DateTime.UtcNow - started).Should().BeLessThan(TimeSpan.FromSeconds(4), "nothing waits for a card nobody is shown");
        reply.GetProperty("instruction").GetString().Should().Contain("document").And.Contain("feature",
            "the one way to land here by mistake is a held document or feature review asked without naming it");
    }

    /// <summary>
    /// G7: a held gate's question is the gate asking the person, not the AI's own — the phase rule does not send it
    /// to the consultants, who cannot release a hold. It used to be refused under <c>require</c> past the free batches.
    /// </summary>
    [Fact]
    public async Task AGateQuestionPastTheFreeBatches_IsNotSentToTheConsultants()
    {
        var h = Build();
        Held(h);
        h.Phase.Count(new QuestionPhaseKey.Caller(_caller), "earlier-1", DateTime.UtcNow);
        h.Phase.Count(new QuestionPhaseKey.Caller(_caller), "earlier-2", DateTime.UtcNow);

        var reply = await Ask(h, "The rounds are spent — keep going, act on the findings, or talk?", string.Empty, false, string.Empty);

        reply.TryGetProperty("error", out _).Should().BeFalse($"the gate's own question is never sent to the consultants first: {reply}");
        reply.GetProperty("status").GetString().Should().Be("no_answer_yet", $"the gate's question reaches the person's card: {reply}");
        Cards(h).Should().Be(1);
        TheCards(h).Single().Kind.Should().Be(EscalationKinds.Question, "the card says it is an ask_human question, so its Answer… offers words first");
        Batches(h).Should().Be(2, "a gate question is not one of the AI's batches");
    }

    /// <summary>
    /// G4: a typed answer to a hold's question goes back to the AI that asked and releases NOTHING — only a decision
    /// opens a held gate (<see cref="CurrentAnswer"/>).
    /// </summary>
    [Fact]
    public async Task ATypedAnswerToAHoldQuestion_ReachesTheAsker_AndReleasesNothing()
    {
        var h = Build(budget: TimeSpan.FromSeconds(10));
        Held(h);
        var asking = Ask(h);
        var id = (await Eventually(() => Directory.Exists(h.Escalations.Directory)
            ? Directory.GetFiles(h.Escalations.Directory, "*.json").Select(Path.GetFileNameWithoutExtension).FirstOrDefault()
            : null, TimeSpan.FromSeconds(5)))!;
        File.WriteAllText(h.Escalations.AnswerPath(id), JsonSerializer.Serialize(new { id, answer = "why did round 3 fail?", answeredUtc = DateTime.UtcNow.ToString("O") }));

        var reply = await asking;

        reply.GetProperty("answer").GetString().Should().Be("why did round 3 fail?");
        var session = h.Sessions.Load(_repo.Path, "main")!;
        session.State.HoldQuestions.Should().Contain(id, "the question was asked for the hold");
        CurrentAnswer.DecisionFor(session, h.Escalations).Should().Be(HumanDecision.None, "words are not a decision");
        session.State.HumanGate.Should().BeTrue("the gate stays held until a decision is pressed");
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

        AskedInTheConversation(opened, h);
        var record = h.Questions.Store.Read(consultId)!;
        record.Outcome.Should().Be("person_asked_in_conversation",
            "the log says the person was asked after the consultants — in the conversation, not on a card");
        record.EscalationId.Should().NotBeEmpty("the proof is spent under the question's id");

        var again = await Ask(h, Question + " (reused)", consultId, false, string.Empty);
        again.GetProperty("error").GetString().Should().Contain("already", "D14 (a): single use");
    }

    /// <summary>
    /// S4b item 8: single use must hold for two calls IN FLIGHT at once, not only for one after another — on the AI's own
    /// question, the door a proof still opens (a held gate's question needs none since G7, and a production risk needs
    /// none either). Whichever interleaving happens, exactly one gets through: the other fails the verification ("it was
    /// already used by an earlier ask_human") or, past it, the atomic spend ("already used by another ask_human").
    /// </summary>
    /// <remarks>
    /// It used to FORCE both calls past their verification on a held session by holding its claim; the conversation path
    /// has no await between the verification and the spend, so the interleaving is no longer staged — the atomic spend
    /// stays the guard, and <c>QuestionConsultStore</c>'s own tests pin it.
    /// </remarks>
    [Fact]
    public async Task TwoAskHumanCallsInFlightWithOneConsultId_OnlyOneGoesThrough()
    {
        var h = Build();
        Proceeded(h);
        await Ask(h);
        await Ask(h);
        var consultId = await Consulted(h);

        var replies = await Task.WhenAll(
            Task.Run(() => Ask(h, Question + " (first)", consultId, false, string.Empty), TestContext.Current.CancellationToken),
            Task.Run(() => Ask(h, Question + " (second)", consultId, false, string.Empty), TestContext.Current.CancellationToken));

        replies.Count(r => r.TryGetProperty("status", out _)).Should().Be(1, $"one consultation opens the door ONCE: {string.Join(" | ", replies.Select(r => r.ToString()))}");
        replies.Count(r => r.TryGetProperty("error", out var e) && e.GetString()!.Contains("already", StringComparison.Ordinal)).Should().Be(1, "the other is refused as already used");
        var spent = h.Questions.Store.Read(consultId)!;
        spent.Outcome.Should().Be(QuestionOutcomes.PersonAskedInConversation);
        spent.EscalationId.Should().NotBeEmpty("the record names the question that spent it");
        Cards(h).Should().Be(0);
    }

    /// <summary>
    /// S4b item 8, the other half: the proof is spent BEFORE the card is posted, so a post that fails must give it back —
    /// otherwise a disk hiccup would burn a consultation the person never saw a card for.
    /// </summary>
    [Fact]
    public async Task AConsultIdWhoseCardCouldNotBePosted_IsGivenBack_AndOpensTheDoorAfterwards()
    {
        var h = Build();
        Held(h);
        await Ask(h);
        await Ask(h);
        var consultId = await Consulted(h);
        var cards = h.Escalations.Directory;
        Directory.Move(cards, cards + ".aside");
        File.WriteAllText(cards, "a file where the cards' directory must be: the card cannot be written");

        var lost = async () => await Ask(h, Question + " (lost)", consultId, false, string.Empty);

        await lost.Should().ThrowAsync<IOException>("the post failed, and the caller is told");
        var record = h.Questions.Store.Read(consultId)!;
        record.EscalationId.Should().BeEmpty("a card never posted spent nothing");
        record.Outcome.Should().Be(QuestionOutcomes.AnsweredByConsultants, "the record is as it was before the spend");

        File.Delete(cards);
        Directory.Move(cards + ".aside", cards);
        var opened = await Ask(h, Question + " (again)", consultId, false, string.Empty);
        opened.GetProperty("status").GetString().Should().Be("no_answer_yet", opened.ToString());
        h.Questions.Store.Read(consultId)!.EscalationId.Should().NotBeEmpty("the proof opened the door the second time");
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

        third.GetProperty("status").GetString().Should().Be("ask_in_conversation");
        third.GetProperty("note").GetString().Should().Contain("ask_consultants");
        Batches(h).Should().Be(3);
    }

    [Fact]
    public async Task InOff_NothingIsSaid_AndNothingIsCounted()
    {
        var h = Build(mode: QuestionConsultSettings.Modes.Off);
        Proceeded(h);

        var reply = await Ask(h);

        reply.GetProperty("status").GetString().Should().Be("ask_in_conversation");
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

        third.GetProperty("status").GetString().Should().Be("ask_in_conversation", third.ToString());
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

        third.GetProperty("status").GetString().Should().Be("ask_in_conversation", third.ToString());
        third.GetProperty("note").GetString().Should().Contain("stood down").And.Contain(QuestionConsultKeys.Enabled);
    }

    [Fact]
    public async Task AfterTheRelease_QuestionsGoToThePersonAgain()
    {
        var h = Build();
        Proceeded(h, plan: "todo/PLAN_x.md", epic: "2/2");
        h.Cadence.Update("repo-id", "todo/PLAN_x.md", state => state.WithClosed(1, "proceed", "t").WithClosed(2, "proceed", "t"));

        var reply = await Ask(h);

        reply.GetProperty("status").GetString().Should().Be("ask_in_conversation", reply.ToString());
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

        reply.GetProperty("status").GetString().Should().Be("ask_in_conversation", reply.ToString());
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
    public async Task AProductionRiskOnAHeldGate_WritesTheCardBeforeAnyLaunch_AndTheConsultantsAnswersArriveUnderIt()
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
        Held(h);

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

    /// <summary>
    /// A person may be asked a long question; the consultants beside it are bounded by the tool's 4 KB. Before
    /// this, a question past it made <c>BesideAsync</c> answer null and the consultants silently did not run.
    /// </summary>
    [Fact]
    public async Task AProductionRiskQuestionPastFourKilobytes_StillRunsTheConsultants_WithTheQuestionCutAndSaidSo()
    {
        var h = Build(budget: TimeSpan.FromSeconds(2), script: launch => Task.FromResult<ScriptedAnswer?>(
            launch.Request.Executable == "claude" ? ScriptedAnswer.Claude("Back the column up first.") : null));
        Held(h);
        var longQuestion = "Is it safe to drop the legacy column now? " + string.Join(' ', Enumerable.Repeat("The table holds every invoice since 2019.", 150));
        System.Text.Encoding.UTF8.GetByteCount(longQuestion).Should().BeGreaterThan(QuestionConsultService.MaxQuestionBytes, "the fixture is past the limit");

        await Ask(h, longQuestion, string.Empty, true, "the migration drops a column nothing can restore");

        var card = await Eventually(() => TheCards(h).FirstOrDefault(c => c.ProductionRisk && c.ConsultantAnswers.Count > 0), TimeSpan.FromSeconds(20));
        card.Should().NotBeNull("the consultants ran beside the card although the question was past 4 KB");
        card!.Question.Should().Be(longQuestion, "the PERSON is asked the whole question; only the consultants get the cut one");
        var prompt = h.Launcher.Vendors.Single().Prompt;
        prompt.Should().Contain("Is it safe to drop the legacy column now?").And.Contain("[…truncated]").And.NotContain(longQuestion);
        var record = h.Questions.Store.All().Single(r => r.ProductionRisk);
        System.Text.Encoding.UTF8.GetByteCount(record.Question).Should().BeLessThanOrEqualTo(QuestionConsultService.MaxQuestionBytes);
        record.Question.Should().EndWith("[…truncated]");
        record.Truncated.Should().Contain("question").And.Contain(System.Text.Encoding.UTF8.GetByteCount(longQuestion).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// S4b item 1, the beside run: the person is asked the question as written, and the consultants beside the card
    /// are not handed a secret in it — the row is refused naming the class, and the card says so under it.
    /// </summary>
    [Fact]
    public async Task AProductionRiskQuestionCarryingASecret_ReachesThePerson_ButNoConsultant()
    {
        var h = Build(budget: TimeSpan.FromSeconds(2), script: launch => Task.FromResult<ScriptedAnswer?>(
            launch.Request.Executable == "claude" ? ScriptedAnswer.Claude("Back the column up first.") : null));
        Held(h);
        const string leaking = "The deploy key sk-live-0123456789abcdefghijklmnop is in the migration — may it run against production now?";

        await Ask(h, leaking, string.Empty, true, "the migration drops a column nothing can restore");

        var card = await Eventually(() => TheCards(h).FirstOrDefault(c => c.ProductionRisk && c.ConsultantAnswers.Count > 0), TimeSpan.FromSeconds(20));
        card.Should().NotBeNull("the beside run still settles, and says why under the card");
        card!.Question.Should().Be(leaking, "the PERSON is asked what the caller wrote");
        var row = card.ConsultantAnswers.Single();
        row.Status.Should().Be(RowOutcomes.Refused);
        row.Reason.Should().Contain("question was not sent").And.Contain("vendor-key").And.NotContain("sk-live");
        h.Launcher.Vendors.Should().BeEmpty("no consultant was handed the secret");
    }

    /// <summary>S4b item 5, under the card: a disk row's root that no invariant watched is said beside its answer there too.</summary>
    [Fact]
    public async Task ARiskCardsDiskAnswer_CarriesTheNoteThatItsRootWasNotWatched()
    {
        var plain = Directory.CreateTempSubdirectory("coai-askhuman-plain-").FullName;
        try
        {
            var diskRow = new QuestionRow("astra-disk", "codex", "codex", "gpt-6-astra", string.Empty, string.Empty, string.Empty, "question-disk", Enabled: true);
            var h = Build(budget: TimeSpan.FromSeconds(2), rows: [diskRow], roots: [plain], script: launch => Task.FromResult<ScriptedAnswer?>(
                launch.Request.Executable == "codex" ? ScriptedAnswer.Codex("Back the column up first.") : null));
            Held(h);

            await Ask(h, Question, string.Empty, true, "the migration drops a column nothing can restore");

            var card = await Eventually(() => TheCards(h).FirstOrDefault(c => c.ProductionRisk && c.ConsultantAnswers.Count > 0), TimeSpan.FromSeconds(20));
            card.Should().NotBeNull();
            card!.ConsultantAnswers.Single().Note.Should().Contain($"root {plain} is not a git checkout: changes there are not watched");
        }
        finally
        {
            Directory.Delete(plain, recursive: true);
        }
    }

    // ---------- a production risk OUTSIDE the gate (G3) ----------

    /// <summary>
    /// G3, the operator's choice of 2026-10-03: outside a held gate there is no card to fold answers under, so the
    /// consultants are asked FIRST and their answers come back in the reply — fenced, one per row — for the AI to show
    /// the person beside its question.
    /// </summary>
    [Fact]
    public async Task AProductionRiskOutsideTheGate_AsksTheConsultantsFirst_AndReturnsTheirAnswers()
    {
        var h = Build(script: launch => Task.FromResult<ScriptedAnswer?>(
            launch.Request.Executable == "claude" ? ScriptedAnswer.Claude("Back the column up first.") : null));
        Proceeded(h);

        var reply = await Ask(h, Question + " (risk)", string.Empty, true, "the migration drops a column nothing can restore");

        AskedInTheConversation(reply, h);
        h.Launcher.Vendors.Should().NotBeEmpty("the consultants ran BEFORE the reply, not beside it");
        var answers = reply.GetProperty("consultantAnswers");
        answers.GetArrayLength().Should().Be(1, reply.ToString());
        answers[0].GetProperty("advice").GetString().Should().Contain("Back the column up first.").And.Contain("advisory_only",
            "another model's advice is fenced exactly as ask_consultants fences it");
        reply.GetProperty("note").GetString().Should().ContainEquivalentOf("production risk").And.Contain("first");
        var record = h.Questions.Store.All().Single(r => r.ProductionRisk);
        record.Outcome.Should().Be("production_risk_consulted_first", "the log must not say the person was asked at once");
        reply.GetProperty("consultId").GetString().Should().Be(record.Id);
        Batches(h).Should().Be(1, "the person is asked — in the conversation");
    }

    /// <summary>
    /// A client that gives up — the person presses Esc, or the client's own tool timeout fires before a five-minute row
    /// budget — must not leave the proof spent and the batch counted for answers nobody receives. The token never reached
    /// the service before (the own review of 2026-10-03): Tools.cs passed none.
    /// </summary>
    [Fact]
    public async Task ACancelledProductionRiskOutsideTheGate_SpendsNothing_AndCountsNothing()
    {
        var launches = 0;
        var h = Build(script: launch => Task.FromResult<ScriptedAnswer?>(launch.Request.Executable == "claude"
            ? ScriptedAnswer.Claude("A ladder.") with { Wait = Interlocked.Increment(ref launches) == 1 ? TimeSpan.Zero : TimeSpan.FromSeconds(20) }
            : null));
        Proceeded(h);
        var consultId = await Consulted(h);
        using var giveUp = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        giveUp.CancelAfter(TimeSpan.FromMilliseconds(500));

        var asking = async () => await h.Service.AskHumanAsync(
            _repo.Path, "main", Question + " (risk)", consultId: consultId, productionRisk: true,
            riskReason: "a wrong retry floods the vendor in production", ct: giveUp.Token);

        await asking.Should().ThrowAsync<OperationCanceledException>("the caller gave up, and the call says so");
        h.Questions.Store.Read(consultId)!.EscalationId.Should().BeEmpty("the proof was given back — it opens the door again");
        Batches(h).Should().Be(0, "nothing reached the person, so nothing is counted");
    }

    /// <summary>S4b item 1 on the conversation path: the consultants are still never handed a secret, and the reply says why the row is empty.</summary>
    [Fact]
    public async Task AProductionRiskOutsideTheGateCarryingASecret_ReachesNoConsultant()
    {
        var h = Build(script: launch => Task.FromResult<ScriptedAnswer?>(
            launch.Request.Executable == "claude" ? ScriptedAnswer.Claude("Back the column up first.") : null));
        Proceeded(h);
        const string leaking = "The deploy key sk-live-0123456789abcdefghijklmnop is in the migration — may it run against production now?";

        var reply = await Ask(h, leaking, string.Empty, true, "the migration drops a column nothing can restore");

        AskedInTheConversation(reply, h);
        h.Launcher.Vendors.Should().BeEmpty("no consultant was handed the secret");
        var row = reply.GetProperty("consultantAnswers")[0];
        row.GetProperty("status").GetString().Should().Be(RowOutcomes.Refused);
        row.GetProperty("reason").GetString().Should().Contain("question was not sent").And.NotContain("sk-live");
        reply.GetProperty("note").GetString().Should().Contain("none answered",
            "a refused row is no answer — the reply must not say the consultants' answers come with it");
        reply.GetProperty("instruction").GetString().Should().NotContain("show the person what each said");
    }

    /// <summary>When the consultant is switched off nobody can be asked first — the reply says so rather than going quiet.</summary>
    [Fact]
    public async Task AProductionRiskOutsideTheGate_WithTheConsultantOff_SaysNobodyCouldBeAsked()
    {
        var h = Build(enabled: false);
        Proceeded(h);

        var reply = await Ask(h, Question + " (risk)", string.Empty, true, "the migration drops a column nothing can restore");

        AskedInTheConversation(reply, h);
        reply.GetProperty("consultantAnswers").GetArrayLength().Should().Be(0);
        reply.GetProperty("note").GetString().Should().ContainEquivalentOf("no consultant");
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
        Held(h);

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
        Held(h);
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
