using System.Diagnostics;
using System.Text.Json;
using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Context;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog;
using Serilog.Core;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// <c>ask_human</c> under policy auto never waits and never writes a card (<c>research/PLAN_auto_never_asks_a_person_E1.md</c> § 9.2 and N3, owner
/// decision O3): the AI's own question is answered <c>consult_first</c>; with the consultants' id, or when none can be had, it reaches the
/// person in the conversation as today; a held gate is answered at once — the person's hold names its notice, a hold from before policy auto
/// whose release could not be written yet says to call again; a feature review's request window says the server decides.
/// </summary>
/// <remarks>
/// The settings say policy auto (no policy named — C2); the sessions are saved the way the real code saves them, with a policy NAMED
/// (the marker is <c>false</c>: a hold this build raised) or with no marker at all (a hold from before policy auto). The harness is
/// <see cref="AskHumanServiceTests"/>'s, with a flag for the policy and a logger of the test's.
/// </remarks>
public sealed class AskHumanUnderAutoTests : IAsyncLifetime
{
    private const string Question = "Which retry shape fits a flaky vendor?";

    private const string Plan = "todo/PLAN_feature_review.md";

    private readonly ProcessLauncher _real = new();
    private readonly string _data = TempDir.For("coai-askauto-").Path;
    private readonly ListSink _sink = new();
    private TempGitRepo _repo = null!;
    private string _caller = string.Empty;

    public async ValueTask InitializeAsync()
    {
        _repo = await TempGitRepo.InitAsync(_real, "coai-askauto-repo-");
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
        AskHumanService Service, QuestionConsultService Questions, Escalations Escalations, SessionStore Sessions, QuestionPhaseStore Phase, ScriptedLauncher Launcher);

    private static readonly QuestionRow ClaudeRow =
        new("sonnet", "claude", "claude", "sonnet", string.Empty, string.Empty, string.Empty, "question-opinion", Enabled: true);

    private static Task<ScriptedAnswer?> Answers(ScriptedLaunch launch) => Task.FromResult<ScriptedAnswer?>(
        launch.Request.Executable == "claude" ? ScriptedAnswer.Claude("A ladder: 5, 30, 60, 120 seconds.") : null);

    private Harness Build(bool autonomous = true, TimeSpan? budget = null, bool enabled = true, bool policyUnread = false)
    {
        var settings = new PanelSettings
        {
            // No policy named is policy auto (C2); a named one never is.
            Rounds = autonomous ? new PanelConfig() : new PanelConfig(OnExhausted: StagePolicy.Human),
            PolicyUnread = policyUnread,
            DataDir = _data,
            Providers = [],
            EscalationBudget = budget ?? TimeSpan.FromMilliseconds(300),
            QuestionConsult = new QuestionConsultSettings
            {
                Enabled = enabled,
                Mode = QuestionConsultSettings.Modes.Require,
                Rows = [ClaudeRow],
                Roots = [],
                RowBudget = TimeSpan.FromSeconds(30),
            },
        };
        var log = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(_sink).CreateLogger();
        var launcher = new ScriptedLauncher(_real, Answers);
        var questions = new QuestionConsultService(
            settings, launcher, new ReviewerExecutor(launcher), new ContextAssembler(launcher), new RolePrompts(_data),
            new UsageLedger(_data), VaultKeys.None("no vault in tests"), Logger.None, _ => null, Noticing.None);
        var escalations = new Escalations(_data, pollInterval: TimeSpan.FromMilliseconds(20));
        var sessions = new SessionStore(_data);
        var phase = new QuestionPhaseStore(_data);
        var desk = new AskGateDesk(questions, new CadenceStore(_data), phase, escalations, _ => null, Logger.None);
        var policy = new SessionPolicy(settings, sessions, escalations, new CoaiMcp.Store.Projection(_data, Logger.None), Logger.None);
        var service = new AskHumanService(
            settings, sessions, escalations, log, Noticing.None,
            (_, branch, document, feature) => feature.Length > 0
                ? new SessionAddress(SessionKey.FeatureBranch, string.Empty, feature)
                : new SessionAddress(branch, document, string.Empty),
            desk, policy);

        return new Harness(service, questions, escalations, sessions, phase, launcher);
    }

    /// <summary>A session whose plan has proceeded, saved the way a build that names its policy saves it (<c>autonomous: false</c>).</summary>
    private void Proceeded(Harness h) =>
        h.Sessions.Save(new PersistedSession(
            new SessionState("s-1", _repo.Path, "main", PanelConfig.Uniform(3, 2, StagePolicy.Human)) { PlanProceeded = true, Stage = Stage.CodeReview }, []));

    /// <summary>A gate held by <c>call_human</c>, with its notices recorded; <paramref name="config"/> says who raised it.</summary>
    private void HeldBy(Harness h, PanelConfig config, params string[] notices) =>
        h.Sessions.Save(new PersistedSession(
            new SessionState("s-1", _repo.Path, "main", config) { PlanProceeded = true, HumanGate = true, Stage = Stage.CodeReview, HoldQuestions = notices }, []));

    /// <summary>A hold this build raised under <c>human</c>: the policy is named, so the marker is <c>false</c> and the hold is the person's.</summary>
    private void HeldByThisBuild(Harness h, params string[] notices) => HeldBy(h, PanelConfig.Uniform(3, 2, StagePolicy.Human), notices);

    /// <summary>A hold a build before policy auto raised: the config has no <c>autonomous</c> member (as the real loader reads such a file).</summary>
    private void HeldBeforeAuto(Harness h, params string[] notices)
    {
        var config = new PanelConfig(roles: null, autonomous: null, onExhausted: StagePolicy.Human);
        config.PredatesAuto.Should().BeTrue("the fixture is a config from before policy auto");
        HeldBy(h, config, notices);
    }

    private void Notice(Harness h, string id) =>
        h.Escalations.Notify(new EscalationQuestion(id, "s-1", _repo.Path, "main", "The rounds are spent.", "The rounds are spent.", "en", string.Empty, [], DateTime.UtcNow.ToString("O"))
        {
            Kind = EscalationKinds.Notice,
        });

    private static int Cards(Harness h) =>
        Directory.Exists(h.Escalations.Directory)
            ? Directory.GetFiles(h.Escalations.Directory, "*.json").Count(f => !f.EndsWith(".answer.json", StringComparison.Ordinal))
            : 0;

    private int Batches(Harness h) => h.Phase.Observe(new QuestionPhaseKey.Caller(_caller), released: false, DateTime.UtcNow).Batches;

    private Task<JsonElement> Ask(Harness h, string consultId = "", bool productionRisk = false, string riskReason = "", string question = Question, string feature = "") =>
        AskAsync(h, question, consultId, productionRisk, riskReason, feature);

    private async Task<JsonElement> AskAsync(Harness h, string question, string consultId, bool productionRisk, string riskReason, string feature) =>
        JsonDocument.Parse(await h.Service.AskHumanAsync(
            _repo.Path, "main", question, feature: feature, consultId: consultId, productionRisk: productionRisk, riskReason: riskReason,
            ct: TestContext.Current.CancellationToken)).RootElement;

    private async Task<string> Consulted(Harness h)
    {
        var reply = JsonDocument.Parse(await h.Questions.AskAsync(
            _repo.Path, Question, "I tried a fixed wait; the options are a ladder or a breaker.", string.Empty, string.Empty,
            (_, _, _, _) => "s-1", TestContext.Current.CancellationToken)).RootElement;
        reply.GetProperty("status").GetString().Should().Be("complete", reply.ToString());

        return reply.GetProperty("consultId").GetString()!;
    }

    private static string StatusOf(JsonElement reply) => reply.GetProperty("status").GetString()!;

    private static string InstructionOf(JsonElement reply) => reply.GetProperty("instruction").GetString()!;

    private static string StageWords => RoundSubject.StageName(Stage.CodeReview.ToString());

    // ---------- the AI's own question ----------

    [Fact]
    public async Task TheOwnQuestion_IsConsultFirst_AndNothingIsSpent()
    {
        var h = Build();
        Proceeded(h);

        var reply = await Ask(h);

        StatusOf(reply).Should().Be("consult_first", reply.ToString());
        InstructionOf(reply).Should().Contain("ask_consultants").And.Contain("consultId");
        Cards(h).Should().Be(0);
        Batches(h).Should().Be(0, "a question that never reached the person is not one of its batches");
        h.Launcher.Vendors.Should().BeEmpty("no consultant is run by the reply that sends the caller to them");
    }

    [Fact]
    public async Task BeforeAnySession_TooItIsConsultFirst()
    {
        var h = Build();

        var reply = await Ask(h);

        StatusOf(reply).Should().Be("consult_first", reply.ToString());
        Cards(h).Should().Be(0);
    }

    [Fact]
    public async Task ARejectedConsultId_IsConsultFirst_AndSaysWhy()
    {
        var h = Build();
        Proceeded(h);

        var reply = await Ask(h, consultId: "no-such-consultation");

        StatusOf(reply).Should().Be("consult_first", reply.ToString());
        reply.GetProperty("note").GetString().Should().Contain("no-such-consultation").And.Contain("did not count");
    }

    [Fact]
    public async Task TheConsultantsId_ReachesThePerson()
    {
        var h = Build();
        Proceeded(h);
        var consultId = await Consulted(h);

        var reply = await Ask(h, consultId: consultId);

        StatusOf(reply).Should().Be("ask_in_conversation", reply.ToString());
        h.Questions.Store.Read(consultId)!.EscalationId.Should().NotBeEmpty("the proof is spent under the question's id");
        Cards(h).Should().Be(0);
        Batches(h).Should().Be(1, "the question reached the person, so the batch is a record of it");
    }

    [Fact]
    public async Task NoConsultant_StandsDownToThePerson()
    {
        var h = Build(enabled: false);
        Proceeded(h);

        var reply = await Ask(h);

        StatusOf(reply).Should().Be("ask_in_conversation", reply.ToString());
        reply.GetProperty("note").GetString().Should().Contain("switched off").And.NotContain("required here");
    }

    [Fact]
    public async Task AProductionRisk_AsksTheConsultantsFirst_AsToday()
    {
        var h = Build();
        Proceeded(h);

        var reply = await Ask(h, productionRisk: true, riskReason: "a wrong retry floods the vendor in production");

        StatusOf(reply).Should().Be("ask_in_conversation", reply.ToString());
        reply.GetProperty("consultantAnswers").GetArrayLength().Should().BeGreaterThan(0, "the consultants ran first and their answers come with the reply");
        Cards(h).Should().Be(0);
    }

    // ---------- the gate's question ----------

    [Fact]
    public async Task AHoldThisBuildRaised_IsAnsweredAtOnce_PointingAtItsNotice()
    {
        var h = Build(budget: TimeSpan.FromMinutes(10));
        HeldByThisBuild(h, "n-1");
        var before = Cards(h);

        var clock = Stopwatch.StartNew();
        var reply = await Ask(h);
        clock.Stop();

        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5), "under policy auto nothing is waited for");
        StatusOf(reply).Should().Be("no_answer_yet", reply.ToString());
        InstructionOf(reply).Should().Contain("n-1").And.Contain("nothing was waited for").And.Contain("only their answer")
            .And.Contain("held by this server under a policy that asks a person");
        Cards(h).Should().Be(before, "no card is written");
        var session = h.Sessions.Load(_repo.Path, "main")!;
        session.State.HumanGate.Should().BeTrue();
        session.State.HoldQuestions.Should().Equal("n-1");
        session.State.RequestQuestions.Should().BeEmpty("nothing is recorded on the session");
    }

    [Fact]
    public async Task AHoldThisBuildRaised_WithAProductionRisk_IsAnsweredAtOnceToo()
    {
        var h = Build(budget: TimeSpan.FromMinutes(10));
        HeldByThisBuild(h, "n-1");

        var clock = Stopwatch.StartNew();
        var reply = await Ask(h, productionRisk: true, riskReason: "a wrong answer drops a column");
        clock.Stop();

        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
        StatusOf(reply).Should().Be("no_answer_yet", reply.ToString());
        InstructionOf(reply).Should().Contain("n-1");
        Cards(h).Should().Be(0);
        h.Launcher.Vendors.Should().BeEmpty("no consultant runs beside a card that is never written");
    }

    [Fact]
    public async Task AHeldGateWithNoRecordedNotice_IsAnsweredAtOnceToo_WithNoNoticeNamed()
    {
        var h = Build(budget: TimeSpan.FromMinutes(10));
        HeldByThisBuild(h);

        var reply = await Ask(h);

        StatusOf(reply).Should().Be("no_answer_yet", reply.ToString());
        InstructionOf(reply).Should().Contain("ConnectOtherAIs notice").And.NotContain("(escalation");
        Cards(h).Should().Be(0);
    }

    [Fact]
    public async Task AFeatureRequestWindow_IsToldTheServerDecides()
    {
        var h = Build(budget: TimeSpan.FromMinutes(10));
        h.Sessions.Save(new PersistedSession(
            new SessionState("s-1", _repo.Path, SessionKey.FeatureBranch, PanelConfig.Uniform(3, 2, StagePolicy.Human)) { Feature = Plan, Stage = Stage.Done, RoundsRunThisStage = 1 }, []));

        var clock = Stopwatch.StartNew();
        var reply = await Ask(h, feature: Plan);
        clock.Stop();

        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
        StatusOf(reply).Should().Be("no_answer_yet", reply.ToString());
        InstructionOf(reply).Should().Contain("runs only on a ground the server finds").And.Contain("nothing was waited for");
        Cards(h).Should().Be(0);
        h.Sessions.Load(_repo.Path, SessionKey.FeatureBranch, string.Empty, Plan)!.State.RequestQuestions.Should().BeEmpty("the question is not bound to a request that cannot be made");
    }

    // ---------- a hold from before policy auto ----------

    [Fact]
    public async Task AHoldFromBeforeAutoNotYetReleased_IsToldToCallAgain_AndNoCardIsWritten()
    {
        var h = Build(budget: TimeSpan.FromMinutes(10));
        HeldBeforeAuto(h, "n-1");
        Notice(h, "n-1");
        var before = Cards(h);

        using (SessionClaim.TryTake(_data, _repo.Path, "main"))
        {
            var clock = Stopwatch.StartNew();
            var reply = await Ask(h);
            clock.Stop();

            clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
            StatusOf(reply).Should().Be("no_answer_yet", reply.ToString());
            InstructionOf(reply).Should().Contain(LegacyHold.NotWrittenSentence(StageWords, LegacyHold.ClaimBusy)).And.Contain("call again")
                .And.NotContain("the person's").And.NotContain("only their answer");
            Cards(h).Should().Be(before, "policy auto writes no card for a hold from before it — not even while its release waits");
            var held = h.Sessions.Load(_repo.Path, "main")!;
            held.State.HumanGate.Should().BeTrue();
            held.State.HoldQuestions.Should().Equal("n-1");
            held.State.RequestQuestions.Should().BeEmpty();
        }

        var again = await Ask(h);

        StatusOf(again).Should().Be("consult_first", "the claim is free, so the call released the hold and the question is the AI's own again");
        h.Sessions.Load(_repo.Path, "main")!.State.HumanGate.Should().BeFalse();
    }

    [Fact]
    public async Task AHoldFromBeforeAutoWhoseSettingsCouldNotBeRead_IsToldToCallAgain_AndNoCardIsWritten()
    {
        var h = Build(budget: TimeSpan.FromMinutes(10), policyUnread: true);
        HeldBeforeAuto(h, "n-1");
        Notice(h, "n-1");
        var before = Cards(h);

        var reply = await Ask(h, productionRisk: true, riskReason: "a wrong answer drops a column");

        StatusOf(reply).Should().Be("no_answer_yet", reply.ToString());
        InstructionOf(reply).Should().Contain(LegacyHold.NotWrittenSentence(StageWords, LegacyHold.SettingsUnread)).And.Contain("call again");
        Cards(h).Should().Be(before);
        h.Sessions.Load(_repo.Path, "main")!.State.HumanGate.Should().BeTrue();
        h.Launcher.Vendors.Should().BeEmpty();
    }

    [Fact]
    public async Task AHoldFromBeforeAutoThePersonAnswered_IsThePersons()
    {
        var h = Build(budget: TimeSpan.FromMinutes(10));
        HeldBeforeAuto(h, "n-1");
        Notice(h, "n-1");
        File.WriteAllText(
            h.Escalations.AnswerPath("n-1"),
            JsonSerializer.Serialize(new { id = "n-1", answer = "why did round 3 fail?", answeredUtc = DateTime.UtcNow.ToString("O") }));
        var before = Cards(h);

        var clock = Stopwatch.StartNew();
        var reply = await Ask(h);
        clock.Stop();

        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
        StatusOf(reply).Should().Be("no_answer_yet", reply.ToString());
        InstructionOf(reply).Should().Contain("the person has answered its notice").And.Contain("n-1").And.NotContain("call again");
        Cards(h).Should().Be(before);
        h.Sessions.Load(_repo.Path, "main")!.State.HumanGate.Should().BeTrue("the person's answer wins: nothing was released");
    }

    // ---------- the log ----------

    public enum Road
    {
        AutoConsultFirst,
        AutoInTheConversation,
        HumanInTheConversation,
        HumanHeldCard,
    }

    /// <summary>An agent may paste a secret into a question: the text stays on the card and in the consult record, never in a log line.</summary>
    [Theory]
    [InlineData(Road.AutoConsultFirst)]
    [InlineData(Road.AutoInTheConversation)]
    [InlineData(Road.HumanInTheConversation)]
    [InlineData(Road.HumanHeldCard)]
    public async Task NoLogLineCarriesTheQuestion(Road road)
    {
        const string Secret = "SECRETSECRETSECRET";
        var question = $"Is sk-ant-api03-{Secret} the right key?";
        var h = Build(autonomous: road is Road.AutoConsultFirst or Road.AutoInTheConversation);
        var consultId = string.Empty;
        if (road == Road.HumanHeldCard)
        {
            HeldByThisBuild(h);
        }
        else
        {
            Proceeded(h);
            consultId = road == Road.AutoInTheConversation ? await Consulted(h) : string.Empty;
        }

        var reply = await Ask(h, consultId, question: question);

        StatusOf(reply).Should().Be(
            road switch
            {
                Road.AutoConsultFirst => "consult_first",
                Road.HumanHeldCard => "no_answer_yet",
                _ => "ask_in_conversation",
            },
            reply.ToString());
        _sink.Lines.Should().NotBeEmpty("the call said what it did");
        _sink.Lines.Should().OnlyContain(line => !line.Contains(Secret, StringComparison.Ordinal), "a question's text never enters a log");
        _sink.Lines.Should().OnlyContain(line => !line.Contains("right key", StringComparison.Ordinal));
        _sink.Lines.Any(line => line.Contains("question ", StringComparison.Ordinal) || line.Contains("escalating ", StringComparison.Ordinal))
            .Should().BeTrue("the line names the question by its id");
    }

    [Fact]
    public async Task EveryAnswerAtOnce_WritesOneInformationLine_WithTheStageAndNoText()
    {
        var h = Build();
        HeldByThisBuild(h, "n-1");

        await Ask(h, question: "the secret is SECRETSECRETSECRET");

        var lines = _sink.Lines.Where(l => l.Contains("under policy auto", StringComparison.Ordinal)).ToList();
        lines.Should().ContainSingle().Which.Should().Contain(StageWords).And.Contain("s-1").And.Contain("n-1").And.NotContain("SECRETSECRETSECRET");
    }
}
