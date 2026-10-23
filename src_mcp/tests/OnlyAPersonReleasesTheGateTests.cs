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
            // A question asked on the held gate waits this long for a person, then returns with its card still on disk.
            EscalationBudget = TimeSpan.FromMilliseconds(300),
        });

    /// <summary>The gate, held:<c>call_human</c>, the notice on disk, the finding pending.</summary>
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
        after.State.Rejections.Should().BeEmpty("a person's Proceed authorises THIS stage transition only — it is not a standing rejection");
        var round = RoundsQuery.Read(_data).Rounds.Should().ContainSingle().Subject;
        var recorded = RoundsQuery.FindingsOf(_data, sessionId, round.Stage, round.Number).Findings.Should().ContainSingle().Subject;
        recorded.Resolution.Should().Be("proceeded", "its own audited disposition, neither accepted nor rejected");
        recorded.Reason.Should().Be("proceeded over by a person", "a decision record, not a silent drop");
        round.Rejected.Should().Be(0, "a proceeded-over finding is not a rejection");
        round.Accepted.Should().Be(0);
    }

    /// <summary>The verdict, or the whole reply when there is none — so a set-up error reads as itself, not as a missing key.</summary>
    private static string VerdictOf(JsonElement reply) =>
        reply.TryGetProperty("verdict", out var verdict) ? verdict.GetString()! : reply.ToString();

    /// <summary><c>review_code</c> reviews COMMITTED changes over the base only: one more commit on the branch.</summary>
    private async Task CommitAChange(string message)
    {
        await File.AppendAllTextAsync(Path.Combine(_repo, "app.cs"), message + "\n", TestContext.Current.CancellationToken);
        await Git("add", ".");
        await Git("commit", "-m", message);
    }

    private const string CodeScope = """
        # SCOPE

        Make the poll loop give up on a hung peer instead of waiting for it forever.
        When it is done: the loop returns after a timeout, and the caller sees which peer hung.
        Constraint: no new dependency.
        """;

    [Fact]
    public async Task AFindingAPersonProceededOver_IsStillGatingWhenAReviewerRaisesItAgainInALaterRound()
    {
        // A Proceed authorises one stage transition. It is not a standing waiver: the same defect, raised again in the
        // code round, gates at threshold zero exactly as it would had nobody proceeded.
        var service = await HeldAsync();
        ThePersonAnswers(NoticeId(), "proceed");
        Parse(await ResolveAsync(service, "[]")).TryGetProperty("error", out _).Should().BeFalse();
        Session().State.Stage.Should().Be(Stage.CodeReview);
        await CommitAChange("the loop gives up");

        Script(OneBlockingFinding);
        var verdict = Parse(await service.ReviewCodeAsync(_repo, Branch, "main", CodeScope, false, TestContext.Current.CancellationToken));

        VerdictOf(verdict).Should().Be("call_human",
            $"the proceeded-over finding is raised again and must still count: {verdict}");
    }

    [Fact]
    public async Task AFindingAPersonProceededOver_IsStillGatingInAReopenedCodeStage()
    {
        // The same, through `again: true`, which keeps the session's standing rejections (BeginCodeRoundAgain).
        var service = await HeldAsync();
        ThePersonAnswers(NoticeId(), "proceed");
        Parse(await ResolveAsync(service, "[]")).TryGetProperty("error", out _).Should().BeFalse();
        await CommitAChange("the loop gives up");
        Script(Clean);
        VerdictOf(Parse(await service.ReviewCodeAsync(_repo, Branch, "main", CodeScope, false, TestContext.Current.CancellationToken)))
            .Should().Be("proceed");
        Parse(await ResolveAsync(service, "[]")).TryGetProperty("error", out _).Should().BeFalse();
        Session().State.Stage.Should().Be(Stage.Done);
        await CommitAChange("the follow-up");

        Script(OneBlockingFinding);
        var verdict = Parse(await service.ReviewCodeAsync(_repo, Branch, "main", CodeScope, true, TestContext.Current.CancellationToken));

        VerdictOf(verdict).Should().Be("call_human",
            $"a Proceed over the plan's finding must not discount it in a reopened code stage: {verdict}");
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

    /// <summary>The card an <c>ask_human</c> question wrote — the one that is not the hold's notice.</summary>
    private JsonNode TheQuestionCard() =>
        Directory.GetFiles(Path.Combine(_data, "escalations"), "*.json")
            .Where(f => !f.EndsWith(".answer.json", StringComparison.Ordinal))
            .Select(f => JsonNode.Parse(File.ReadAllText(f))!)
            .Single(card => card["kind"]!.GetValue<string>() == "question");

    [Fact]
    public async Task AQuestionCardAskedDuringTheHold_ShowsTheFindingsTheHoldWasRaisedFor_AfterTheAiResolvedThem()
    {
        // The AI resolved the round's findings (no Proceed): Pending is empty and the hold stays. A question asked now is
        // still the gate's — its Continue / Fix release the hold (D5) — so the person must see what the hold is FOR.
        var service = await HeldAsync();
        Parse(await ResolveAsync(service, RejectIt)).TryGetProperty("error", out _).Should().BeFalse();
        Session().Pending.Should().BeEmpty("the round's decisions were recorded");
        Session().State.HumanGate.Should().BeTrue("recording decisions is not a person deciding");

        var reply = Parse(await service.AskHumanAsync(_repo, Branch, "Which log format would you rather have?", ct: TestContext.Current.CancellationToken));

        reply.GetProperty("status").GetString().Should().Be("no_answer_yet", reply.ToString());
        var card = TheQuestionCard();
        var findings = card["openFindings"]!.AsArray();
        findings.Should().ContainSingle("the card of a held gate lists the findings the hold was raised for");
        findings[0]!["title"]!.GetValue<string>().Should().Be("the poll has no timeout");
        card["decisions"]!.AsArray().Select(d => d!.GetValue<string>())
            .Should().Equal("continue", "fix", "discuss");
        Session().State.HoldQuestions.Should().HaveCount(2, "the card joined the hold");
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
