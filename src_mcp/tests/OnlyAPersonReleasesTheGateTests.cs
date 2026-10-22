using System.Text.Json;
using System.Text.Json.Nodes;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A held gate is released by the answer written to the hold's own NOTICE and by nothing the calling AI passes
/// (<c>todo/PLAN_only_a_person_releases_the_gate.md</c>).
/// </summary>
/// <remarks>
/// <para><b>The defect.</b> <c>resolve(humanDecision: "proceed")</c> released a held gate from the tool argument
/// alone: the gated party was the only party that could speak the word that releases the gate. These are
/// real <c>review_plan</c> rounds over the fake CLI in a throwaway repository — the whole road, review to
/// <c>call_human</c> to the notice file to the person's answer file to <c>resolve</c> — so each of them is a
/// scenario of the product and not a unit of the machine (the unit halves are in
/// <c>OnlyAPersonReleasesTheGateMachineTests</c>).</para>
/// <para>What is claimed is the PROTOCOL road: the argument no longer releases a gate and only a written answer
/// to the hold's notice does. The answer FILE is writable by anything running as the user, and nothing here says
/// otherwise (plan §8 R1, §11.1).</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class OnlyAPersonReleasesTheGateTests : FakeCliRoundTests
{
    private const string Branch = "epic-1";

    private const string Plan = "# PLAN — held\n\n## Build order\n\n1. one\n2. two\n";

    private const string OneBlockingFinding = """
        {"findings": [
          {"severity": "blocking", "category": "reliability", "file": "", "line": 0,
           "title": "the poll has no timeout", "why": "a hung peer hangs the loop", "fix": "add one"}
        ]}
        """;

    private const string RejectIt = """[{"finding":0,"action":"reject","reason":"the timeout lives in the caller"}]""";

    private SessionStore Store => new(_data, Defaults().Rounds.Catalog);

    private PersistedSession Session() => Store.Load(_repo, Branch)!;

    /// <summary>One round, one reviewer, one blocking finding that gates: the rounds are spent at once and a person is called.</summary>
    private PanelService HoldingService() =>
        ServiceFor(Defaults() with
        {
            Rounds = PanelConfig.Uniform(1, 0, StagePolicy.Human),
            CadenceMode = Core.Cadence.CadenceMode.Off,
            SplitPlan = false,
        });

    /// <summary>The gate, held: <c>call_human</c>, the notice on disk, the finding pending.</summary>
    private async Task<PanelService> HeldAsync()
    {
        var service = HoldingService();
        await service.OpenAsync(_repo, Branch);
        Script(OneBlockingFinding);
        var verdict = Parse(await service.ReviewPlanAsync(_repo, Branch, Plan, TestContext.Current.CancellationToken));
        verdict.GetProperty("verdict").GetString().Should().Be("call_human", verdict.ToString());
        Session().State.HumanGate.Should().BeTrue();

        return service;
    }

    private string NoticeId() => Session().State.HoldQuestions.Should().ContainSingle("the round raised one notice").Subject;

    private string NoticeFile(string id) => Path.Combine(_data, "escalations", $"{id}.json");

    /// <summary>The person's answer to a question, written exactly as the panel writes it, beside the question.</summary>
    private void ThePersonAnswers(string id, string decision, string words = "Proceed despite the findings")
    {
        var answer = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["id"] = id,
            ["answer"] = words,
            ["decision"] = decision,
            ["answeredUtc"] = DateTime.UtcNow.ToString("O"),
        });
        File.WriteAllText(Path.Combine(_data, "escalations", $"{id}.answer.json"), answer);
    }

    private Task<string> ResolveAsync(PanelService service, string decisions, bool callerClaimsProceed = false) =>
        service.ResolveAsync(_repo, Branch, decisions, callerClaimsProceed);

    private static string ErrorOf(string reply) => Parse(reply).TryGetProperty("error", out var e) ? e.GetString()! : string.Empty;

    // ---------- the attack ----------

    [Fact]
    public async Task AnAgentPassingHumanDecisionProceed_WithNoPersonsAnswer_IsRefused_AndTheGateStaysHeld()
    {
        var service = await HeldAsync();
        var before = Session();

        // What the agent actually sends: its decisions, and the word the protocol text used to tell it to relay.
        var reply = await ResolveAsync(service, RejectIt, callerClaimsProceed: true);

        Parse(reply).TryGetProperty("error", out _).Should().BeTrue($"the argument must not release the gate, but the reply was {reply}");
        ErrorOf(reply).Should().Contain("Proceed despite the findings").And.Contain("ask_human",
            "the refusal names the card and the road to it");
        var after = Session();
        after.State.HumanGate.Should().BeTrue();
        after.State.Stage.Should().Be(before.State.Stage);
        after.State.HoldQuestions.Should().Equal(before.State.HoldQuestions);
        after.State.PlanProceeded.Should().BeFalse();
        after.Pending.Should().HaveCount(before.Pending.Count, "a refusal records nothing");
    }

    [Fact]
    public async Task AnAgentPassingHumanDecisionProceed_WithNoDecisionsAtAll_IsRefusedWithTheSameSentence()
    {
        var service = await HeldAsync();

        var reply = await ResolveAsync(service, "[]", callerClaimsProceed: true);

        ErrorOf(reply).Should().Contain("Proceed despite the findings",
            "the claim is judged BEFORE the findings that still await a decision are counted");
        Session().State.HumanGate.Should().BeTrue();
    }

    [Fact]
    public async Task TheArgumentIsStillRefused_BeforeCallHuman()
    {
        // Rounds remain: the gate decides, not the flag. The refusal the machine always gave.
        var service = ServiceFor(Defaults() with
        {
            Rounds = PanelConfig.Uniform(3, 0, StagePolicy.Human),
            CadenceMode = Core.Cadence.CadenceMode.Off,
            SplitPlan = false,
        });
        await service.OpenAsync(_repo, Branch);
        Script(OneBlockingFinding);
        Parse(await service.ReviewPlanAsync(_repo, Branch, Plan, TestContext.Current.CancellationToken))
            .GetProperty("verdict").GetString().Should().Be("revise");

        var reply = await ResolveAsync(service, RejectIt, callerClaimsProceed: true);

        ErrorOf(reply).Should().Contain("call_human");
        Session().State.Stage.Should().Be(Stage.PlanReview);
    }

    // ---------- the person's road ----------

    [Fact]
    public async Task APersonsProceedOnAHeldGate_AdvancesTheRealStage()
    {
        // The scenario of plan §11.4: review -> call_human -> the notice file -> an answer file with proceed -> resolve.
        var service = await HeldAsync();
        File.Exists(NoticeFile(NoticeId())).Should().BeTrue("the round wrote the person's card");
        ThePersonAnswers(NoticeId(), "proceed");

        var reply = Parse(await ResolveAsync(service, RejectIt));

        reply.TryGetProperty("error", out var error).Should().BeFalse($"the person's Proceed releases the hold: {error}");
        reply.GetProperty("stage").GetString().Should().Be("CodeReview");
        var after = Session();
        after.State.HumanGate.Should().BeFalse();
        after.State.HoldQuestions.Should().BeEmpty();
        after.State.Stage.Should().Be(Stage.CodeReview);
        after.State.PlanProceeded.Should().BeTrue();
    }

    [Fact]
    public async Task APersonsProceedOnTheNotice_ReleasesTheHold_WithNoDecisionsAndNoArgument()
    {
        var service = await HeldAsync();
        ThePersonAnswers(NoticeId(), "proceed");

        var reply = Parse(await ResolveAsync(service, "[]"));

        reply.TryGetProperty("error", out var error).Should().BeFalse($"nothing but the person's answer was needed: {error}");
        Session().State.Stage.Should().Be(Stage.CodeReview);
        Session().State.HumanGate.Should().BeFalse();
    }

    [Fact]
    public async Task TheArgumentIsIgnored_WhenThePersonAlreadyProceeded()
    {
        var service = await HeldAsync();
        ThePersonAnswers(NoticeId(), "proceed");

        var reply = Parse(await ResolveAsync(service, RejectIt, callerClaimsProceed: true));

        reply.TryGetProperty("error", out var error).Should().BeFalse($"a redundant claim is ignored, not refused: {error}");
        Session().State.Stage.Should().Be(Stage.CodeReview);
    }

    [Fact]
    public async Task AProceedOverUndecidedFindings_RecordsThemAsProceededOver()
    {
        var service = await HeldAsync();
        ThePersonAnswers(NoticeId(), "proceed");
        var sessionId = Session().State.SessionId;

        Parse(await ResolveAsync(service, "[]")).TryGetProperty("error", out _).Should().BeFalse();

        var after = Session();
        after.State.Rejections.Should().ContainSingle().Which.Reason.Should().Be("proceeded over by a person");
        var round = RoundsQuery.Read(_data).Rounds.Should().ContainSingle().Subject;
        var recorded = RoundsQuery.FindingsOf(_data, sessionId, round.Stage, round.Number).Findings.Should().ContainSingle().Subject;
        recorded.Reason.Should().Be("proceeded over by a person", "a decision record, not a silent drop");
    }

    [Fact]
    public async Task ADecisionTheAiRecordedItself_IsKept_WhenThePersonProceeds()
    {
        var service = await HeldAsync();
        ThePersonAnswers(NoticeId(), "proceed");

        await ResolveAsync(service, RejectIt);

        Session().State.Rejections.Should().ContainSingle().Which.Reason.Should().Be("the timeout lives in the caller");
    }

    [Fact]
    public async Task ResolveWithNoDecisions_OnAHeldResolvedGate_AppliesThePersonsProceed()
    {
        // G6: the person decides AFTER the AI resolved the round. No call carried it before.
        var service = await HeldAsync();
        Parse(await ResolveAsync(service, RejectIt)).TryGetProperty("error", out _).Should().BeFalse();
        Session().State.AwaitingResolve.Should().BeFalse();
        Session().State.HumanGate.Should().BeTrue("recording decisions is not a person deciding");
        ThePersonAnswers(NoticeId(), "proceed");

        var reply = Parse(await ResolveAsync(service, "[]"));

        reply.TryGetProperty("error", out var error).Should().BeFalse($"the late Proceed is applied: {error}");
        Session().State.Stage.Should().Be(Stage.CodeReview);
        Session().State.HumanGate.Should().BeFalse();
    }

    [Fact]
    public async Task ResolveWithNoDecisions_OnAHeldResolvedGate_WithoutAProceed_IsStillRefused()
    {
        var service = await HeldAsync();
        await ResolveAsync(service, RejectIt);

        var reply = await ResolveAsync(service, "[]");

        ErrorOf(reply).Should().Contain("no completed round awaiting decisions");
        Session().State.HumanGate.Should().BeTrue();
    }

    [Fact]
    public async Task TheArgument_OnAHeldResolvedGate_IsRefusedWithTheCardSentence()
    {
        var service = await HeldAsync();
        await ResolveAsync(service, RejectIt);

        var reply = await ResolveAsync(service, "[]", callerClaimsProceed: true);

        ErrorOf(reply).Should().Contain("Proceed despite the findings");
        Session().State.HumanGate.Should().BeTrue();
    }

    [Fact]
    public async Task AWithdrawalHold_IsNotReleasedByTheArgument()
    {
        // D6: the cancel plan raises the SAME hold — HumanGate, AwaitingResolve, landed findings pending — after a
        // round that was not call_human. The hold is person-only whatever raised it.
        var service = await HeldAsync();
        var held = Session();
        Store.Save(held with { Rounds = [.. held.Rounds.Select(r => r with { Verdict = "withdrawn" })] });

        var reply = await ResolveAsync(service, RejectIt, callerClaimsProceed: true);

        ErrorOf(reply).Should().Contain("Proceed despite the findings");
        Session().State.HumanGate.Should().BeTrue();
        Session().State.Stage.Should().Be(Stage.PlanReview);
    }

    // ---------- what the notice offers ----------

    [Fact]
    public async Task TheNoticeListsTheDecisionsItOffers()
    {
        await HeldAsync();

        var notice = JsonNode.Parse(File.ReadAllText(NoticeFile(NoticeId())))!;

        notice["decisions"].Should().NotBeNull("the notice says which decisions it offers (G5)");
        notice["decisions"]!.AsArray().Select(d => d!.GetValue<string>())
            .Should().Equal("continue", "fix", "discuss", "proceed");
        notice["kind"]!.GetValue<string>().Should().Be("notice");
    }

    [Fact]
    public async Task AHoldFromAnOlderServer_IsOfferedProceedAfterTheUpgrade()
    {
        // §11.3: a session held before the upgrade has a notice with no `decisions`. It is rewritten in place — same
        // id, no second card — and the answer file, if any, is untouched.
        var service = await HeldAsync();
        var id = NoticeId();
        var older = JsonNode.Parse(File.ReadAllText(NoticeFile(id)))!.AsObject();
        older.Remove("decisions");
        File.WriteAllText(NoticeFile(id), older.ToJsonString());
        ThePersonAnswers(id, "discuss", "let us talk");
        var answerBefore = File.ReadAllText(Path.Combine(_data, "escalations", $"{id}.answer.json"));

        await service.StatusAsync(_repo, Branch, string.Empty, string.Empty);

        var notice = JsonNode.Parse(File.ReadAllText(NoticeFile(id)))!;
        notice["decisions"].Should().NotBeNull("the standing notice was refreshed in place");
        notice["decisions"]!.AsArray().Select(d => d!.GetValue<string>())
            .Should().Equal("continue", "fix", "discuss", "proceed");
        notice["id"]!.GetValue<string>().Should().Be(id);
        Directory.GetFiles(Path.Combine(_data, "escalations"), "*.json").Where(f => !f.EndsWith(".answer.json", StringComparison.Ordinal))
            .Should().ContainSingle("the card is refreshed, never duplicated");
        File.ReadAllText(Path.Combine(_data, "escalations", $"{id}.answer.json")).Should().Be(answerBefore);
        Session().State.HoldQuestions.Should().Equal(id);
    }

    // ---------- the agent learns of the Proceed ----------

    [Fact]
    public async Task Status_ReportsAPersonsProceed_AndSaysToResolve()
    {
        var service = await HeldAsync();
        ThePersonAnswers(NoticeId(), "proceed");

        var status = Parse(await service.StatusAsync(_repo, Branch, string.Empty, string.Empty));

        status.GetProperty("humanDecision").GetString().Should().Be("proceed");
        status.GetProperty("humanNote").GetString().Should().Be(
            "a person pressed Proceed on the held gate — call resolve to advance");
    }

    [Fact]
    public async Task Status_SaysNothingOfAProceed_WhenNoneIsOnFile()
    {
        var service = await HeldAsync();

        var status = Parse(await service.StatusAsync(_repo, Branch, string.Empty, string.Empty));

        status.GetProperty("humanDecision").GetString().Should().BeEmpty();
        status.GetProperty("humanNote").GetString().Should().BeEmpty();
    }

    [Fact]
    public async Task TheResolveReply_NamesTheDecisionAsThePersons()
    {
        var service = await HeldAsync();
        ThePersonAnswers(NoticeId(), "proceed");

        var reply = Parse(await ResolveAsync(service, RejectIt));

        reply.GetProperty("instruction").GetString().Should().Contain("a person pressed Proceed");
    }

    [Fact]
    public async Task EveryReviewEntry_AnswersTheSameSentence_OnceAProceedIsOnFile_AndGateHeldBefore()
    {
        var service = await HeldAsync();
        var held = Session();
        var sentence = "a person pressed Proceed on the held gate — call resolve to advance";

        // No person has answered: the entries refuse as they always did — never with the Proceed sentence.
        var codeBefore = ErrorOf(await service.ReviewCodeAsync(_repo, Branch, "main", Plan, again: false, ct: TestContext.Current.CancellationToken));
        codeBefore.Should().NotContain("a person pressed Proceed");

        ThePersonAnswers(NoticeId(), "proceed");
        var plan = ErrorOf(await service.ReviewPlanAsync(_repo, Branch, Plan, TestContext.Current.CancellationToken));
        var code = ErrorOf(await service.ReviewCodeAsync(_repo, Branch, "main", Plan, again: false, ct: TestContext.Current.CancellationToken));

        plan.Should().Contain(sentence);
        code.Should().Contain(sentence);
        Session().State.HumanGate.Should().BeTrue("a refusal changes nothing");
        Session().State.HoldQuestions.Should().Equal(held.State.HoldQuestions);
        Session().State.Stage.Should().Be(held.State.Stage);
    }
}
