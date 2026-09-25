using System.Text.Json;
using CoaiMcp.Core.Cadence;
using CoaiMcp.Core.Commands;
using CoaiMcp.Runners.Context;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The cadence REACHING a caller, and in <c>require</c> refusing it — real rounds over the fake CLI in a
/// throwaway repository (<c>todo/PLAN_consult_on_a_cadence.md</c>, epic 3).
/// </summary>
/// <remarks>
/// <para>Measured before any of this: ~1 260 gate rounds against 18 consultations, zero across fourteen
/// epics of one product. Prose had been tried and produced that number; these are the order and the
/// refusal that replace it.</para>
/// <para>Consultations are written straight into the store — closed, with an outcome, under the
/// repository's common dir — rather than run: <c>ConsultKindsScenarioTests</c> drives the consult flow
/// itself, and what these tests are about is what the GATE does with the evidence.</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class CadenceGateScenarioTests : FakeCliRoundTests
{
    private const string Plan = "todo/PLAN_x.md";
    private const string Scope =
        "Implements an epic of todo/PLAN_x.md: the parser change in app.cs, with its tests, as that epic describes. "
        + "Done when the parser counts two fields as two and the suite is green; constrained to app.cs and its tests, "
        + "with no change to the public shape the other epics build on.";

    private static string PlanWithEpics(int count, int first = 1) =>
        "# PLAN — generated\n\n> Status: plan only.\n\n"
        + string.Concat(Enumerable.Range(first, count).Select(epic => $"### Epic {epic} — piece {epic}\n\ntext\n\n"));

    private PanelService Service(CadenceMode mode, bool consultEnabled = true) =>
        ServiceFor(Defaults() with { CadenceMode = mode, ConsultEnabled = consultEnabled });

    private async Task CommitPlan(int epics = 6, int first = 1)
    {
        Directory.CreateDirectory(Path.Combine(_repo, "todo"));
        await File.WriteAllTextAsync(Path.Combine(_repo, "todo", "PLAN_x.md"), PlanWithEpics(epics, first));
        await Git("add", ".");
        await Git("commit", "-m", "the plan");
    }

    private async Task CommitWork(string text = "v2\n")
    {
        await File.WriteAllTextAsync(Path.Combine(_repo, "app.cs"), text);
        await Git("add", ".");
        await Git("commit", "-m", "work");
    }

    private static CadenceArgs At(string epic, string riskItems = "", string riskNote = "") => new(Plan, epic, riskItems, riskNote);

    /// <summary>Open, pass the plan stage clean, resolve — the code stage is next.</summary>
    private async Task<JsonElement> ThroughThePlan(PanelService service, string epic, string planText = "")
    {
        await service.OpenAsync(_repo, "epic-1");
        Script(Clean);
        var reply = Parse(await service.ReviewPlanAsync(_repo, "epic-1", planText.Length > 0 ? planText : Scope, At(epic)));
        await service.ResolveAsync(_repo, "epic-1", "[]");

        return reply;
    }

    private async Task<JsonElement> CodeRound(PanelService service, string epic, bool again = false)
    {
        Script(Clean);

        return Parse(await service.ReviewCodeAsync(_repo, "epic-1", "main", Scope, again, At(epic)));
    }

    private async Task Consulted(PanelService service, string kind, string epics)
    {
        var repoId = await new ContextAssembler(_launcher).CommonDirAsync(_repo, TestContext.Current.CancellationToken);
        service.Consultations.Write(new ConsultationRecord(
            ConsultationStore.NewId(), "caller", "claude", "no-session", _repo, "epic-1", "", "codex", "", "codex",
            ConsultationMemories.VendorRemembers, 5, "2026-09-25T10:00:00Z")
        {
            Status = ConsultationStatuses.Closed,
            Outcome = ConsultationOutcomes.Solved,
            EndedUtc = "2026-09-25T10:05:00Z",
            Kind = kind,
            Plan = Plan,
            Epics = epics,
            RepoId = repoId,
        });
    }

    private static string Refusal(JsonElement reply) =>
        reply.TryGetProperty("error", out var error) ? error.GetString()! : string.Empty;

    private static bool Reviewed(JsonElement reply) => reply.TryGetProperty("verdict", out _);

    [Fact]
    public async Task RequireMode_RefusesTheFirstCodeRoundOfAnUnconsultedGroup_WithTheCall()
    {
        await CommitPlan();
        var service = Service(CadenceMode.Require);
        await ThroughThePlan(service, "1/6");
        await CommitWork();

        var reply = await CodeRound(service, "1/6");

        Refusal(reply).Should().Contain("1-3").And.Contain("mcp__coai__consult({").And.Contain("Nothing was reviewed");
    }

    [Fact]
    public async Task AnyEpicOfAnUnconsultedGroup_IsRefused_NotOnlyItsFirst()
    {
        await CommitPlan();
        var service = Service(CadenceMode.Require);
        await ThroughThePlan(service, "2/6");
        await CommitWork();

        Refusal(await CodeRound(service, "2/6")).Should().Contain("1-3");
    }

    [Fact]
    public async Task AConsultationClosedWithAnOutcome_LetsTheGroupThrough()
    {
        await CommitPlan();
        var service = Service(CadenceMode.Require);
        await Consulted(service, "cadence", "1-3");
        await ThroughThePlan(service, "1/6");
        await CommitWork();

        var reply = await CodeRound(service, "1/6");

        Reviewed(reply).Should().BeTrue(reply.ToString());
    }

    [Fact]
    public async Task RemindMode_OrdersTheConsultation_ButNeverRefuses()
    {
        await CommitPlan();
        var service = Service(CadenceMode.Remind);
        await ThroughThePlan(service, "1/6");
        await CommitWork();

        var reply = await CodeRound(service, "1/6");

        Reviewed(reply).Should().BeTrue(reply.ToString());
        CommandsOf(reply).Should().Contain(order => order.StartsWith(CadenceOrders.GroupMarker, StringComparison.Ordinal));
    }

    [Fact]
    public async Task OffMode_SaysNothingAndRefusesNothing_WhateverIsDeclared()
    {
        await CommitPlan();
        var service = Service(CadenceMode.Off);
        var plan = await ThroughThePlan(service, "1/6", PlanWithEpics(6));
        await CommitWork();

        var reply = await CodeRound(service, "not-even-k/N");

        Reviewed(reply).Should().BeTrue(reply.ToString());
        CommandsOf(plan).Should().BeEmpty();
        CommandsOf(reply).Should().BeEmpty();
    }

    [Fact]
    public async Task ThePlanRound_ForecastsTheConsultations_OfAPlanThatNamesItsEpics()
    {
        await CommitPlan();

        var plan = await ThroughThePlan(Service(CadenceMode.Remind), "1/6", PlanWithEpics(6));

        CommandsOf(plan).Should().Contain(order => order.StartsWith(CadenceOrders.ForecastMarker, StringComparison.Ordinal)
            && order.Contains("owes 2 consultations", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ACheckpointForTheSameEpic_IsNotAskedAgain_ButTheNextGroupsEpicIs()
    {
        await CommitPlan();
        var service = Service(CadenceMode.Require);
        await Consulted(service, "cadence", "1-3");
        await ThroughThePlan(service, "1/6");
        await CommitWork();
        Reviewed(await CodeRound(service, "1/6")).Should().BeTrue();
        await service.ResolveAsync(_repo, "epic-1", "[]");

        // The evidence is gone, and a checkpoint of the SAME epic still goes through: the gate asked
        // once, on this epic's first code round, and does not ask again (the epic-1-3 consultation, point 2).
        foreach (var file in Directory.GetFiles(Path.Combine(_data, "consultations"), "*.json"))
        {
            File.Delete(file);
        }
        await CommitWork("v3\n");
        Reviewed(await CodeRound(service, "1/6", again: true)).Should().BeTrue();
        await service.ResolveAsync(_repo, "epic-1", "[]");

        // The first code round of epic 4 opens group 4-6, which nobody consulted on.
        await CommitWork("v4\n");
        Refusal(await CodeRound(service, "4/6", again: true)).Should().Contain("4-6");
    }

    [Fact]
    public async Task AnUnavailableConsultant_StandsTheRefusalDown_AndTheRoundSaysSo()
    {
        await CommitPlan();
        var service = Service(CadenceMode.Require, consultEnabled: false);
        await ThroughThePlan(service, "1/6");
        await CommitWork();

        var reply = await CodeRound(service, "1/6");

        Reviewed(reply).Should().BeTrue("a consultant that cannot be had never deadlocks the work: " + reply);
        var status = Parse(await service.StatusAsync(_repo, "epic-1"));
        status.GetProperty("rounds").EnumerateArray().Last().GetProperty("cadenceNote").GetString()
            .Should().Contain("stood down").And.Contain("switched off");
    }

    [Fact]
    public async Task ADeclaredCountThePlanDoesNotHave_IsRefusedNamingBoth()
    {
        await CommitPlan(epics: 6);
        var service = Service(CadenceMode.Remind);
        await ThroughThePlan(service, "1/6");
        await CommitWork();

        Refusal(await CodeRound(service, "1/5")).Should().Contain("1/5").And.Contain("1-6");
    }

    [Fact]
    public async Task APlanOfFifteenEpics_IsRefused_SplitItInTwo()
    {
        await CommitPlan(epics: 15);
        var service = Service(CadenceMode.Remind);
        await service.OpenAsync(_repo, "epic-1");
        Script(Clean);

        var reply = Parse(await service.ReviewPlanAsync(_repo, "epic-1", Scope, At("1/15")));

        Refusal(reply).Should().Contain("15 epics").And.Contain("two plans");
    }

    [Fact]
    public async Task AContinuingPlan_IsCountedByItsOwnEpics()
    {
        // email-service's second plan runs epics 5-14: ten epics, last number fourteen, not refused.
        await CommitPlan(epics: 10, first: 5);
        var service = Service(CadenceMode.Require);
        await Consulted(service, "cadence", "5-7");
        await ThroughThePlan(service, "5/14");
        await CommitWork();

        Reviewed(await CodeRound(service, "5/14")).Should().BeTrue();
    }

    [Fact]
    public async Task EpicsInThePlanButNoEpicDeclared_IsRefusedInRequire()
    {
        await CommitPlan();
        var service = Service(CadenceMode.Require);
        await service.OpenAsync(_repo, "epic-1");
        Script(Clean);
        await service.ReviewPlanAsync(_repo, "epic-1", PlanWithEpics(6));
        await service.ResolveAsync(_repo, "epic-1", "[]");
        await CommitWork();
        Script(Clean);

        var reply = Parse(await service.ReviewCodeAsync(_repo, "epic-1", "main", Scope));

        // And it names the plan's own epics, so the caller need not go and read the plan to answer
        // (epic 3's code round).
        Refusal(reply).Should().Contain("epic").And.Contain("\"k/N\"").And.Contain("1-6");
    }

    [Fact]
    public async Task APlanWithNoEpics_IsNeverAskedForOne()
    {
        var service = Service(CadenceMode.Require);
        await service.OpenAsync(_repo, "epic-1");
        Script(Clean);
        await service.ReviewPlanAsync(_repo, "epic-1", Scope);
        await service.ResolveAsync(_repo, "epic-1", "[]");
        await CommitWork();
        Script(Clean);

        Reviewed(Parse(await service.ReviewCodeAsync(_repo, "epic-1", "main", Scope))).Should().BeTrue();
    }

    [Theory]
    [InlineData("""[{"epic":1,"reason":"a"},{"epic":2,"reason":"b"},{"epic":3,"reason":"c"},{"epic":4,"reason":"d"}]""", "", "at most 3")]
    [InlineData("""[{"epic":2}]""", "", "reason")]
    [InlineData("[]", "", "riskNote")]
    [InlineData("""[{"epic":2,"story":"two","reason":"x"}]""", "", "'two'")]
    [InlineData("not json", "", "JSON")]
    public async Task ABadRiskAnswer_IsRefusedSayingWhatIsWrong(string items, string note, string named)
    {
        await CommitPlan();
        var service = Service(CadenceMode.Remind);
        await service.OpenAsync(_repo, "epic-1");
        Script(Clean);

        var reply = Parse(await service.ReviewPlanAsync(_repo, "epic-1", Scope, At("1/6", items, note)));

        Refusal(reply).Should().Contain(named);
    }

    [Fact]
    public async Task ARiskyEpic_IsRefusedUntilItsOwnConsultation()
    {
        await CommitPlan();
        var service = Service(CadenceMode.Require);
        await Consulted(service, "cadence", "1-3");
        await service.OpenAsync(_repo, "epic-1");
        Script(Clean);
        await service.ReviewPlanAsync(_repo, "epic-1", Scope, At("2/6", """[{"epic":2,"story":"2.1","reason":"moves the data"}]"""));
        await service.ResolveAsync(_repo, "epic-1", "[]");
        await CommitWork();

        Refusal(await CodeRound(service, "2/6")).Should().Contain("\"kind\": \"risk\"").And.Contain("2/2.1");

        await Consulted(service, "risk", "2/2.1");
        Reviewed(await CodeRound(service, "2/6")).Should().BeTrue();
    }

    [Fact]
    public async Task AnEmptyRiskAnswerWithAReason_StopsTheQuestion()
    {
        await CommitPlan();
        var service = Service(CadenceMode.Remind);
        await service.OpenAsync(_repo, "epic-1");
        Script(Clean);

        var reply = Parse(await service.ReviewPlanAsync(_repo, "epic-1", Scope, At("1/6", "[]", "nothing here moves money or data")));

        CommandsOf(reply).Should().NotContain(order => order.StartsWith(CadenceOrders.RiskQuestionMarker, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(Clean, true)]
    public async Task APassedCodeRound_ClosesTheEpic_AndStatusSaysSo(string answer, bool closes)
    {
        await CommitPlan();
        var service = Service(CadenceMode.Require);
        await Consulted(service, "cadence", "1-3");
        await ThroughThePlan(service, "1/6");
        await CommitWork();
        Script(answer);
        Reviewed(Parse(await service.ReviewCodeAsync(_repo, "epic-1", "main", Scope, false, At("1/6")))).Should().BeTrue();
        await service.ResolveAsync(_repo, "epic-1", "[]");

        var cadence = Parse(await service.StatusAsync(_repo, "epic-1", string.Empty, Plan)).GetProperty("cadence");

        cadence.GetProperty("epicsClosed").EnumerateArray().Select(e => e.GetInt32()).Contains(1).Should().Be(closes);
        cadence.GetProperty("epics").GetInt32().Should().Be(6);
        var groups = cadence.GetProperty("groups").EnumerateArray().ToList();
        groups.Select(g => g.GetProperty("range").GetString()).Should().Equal("1-3", "4-6");
        groups.Select(g => g.GetProperty("consulted").GetBoolean()).Should().Equal(true, false);
    }

    [Fact]
    public async Task StatusForAPlan_ReadsTheSameRecordFromAnotherBranch()
    {
        await CommitPlan();
        var service = Service(CadenceMode.Require);
        await Consulted(service, "cadence", "1-3");
        await ThroughThePlan(service, "1/6");
        await CommitWork();
        await CodeRound(service, "1/6");
        await service.ResolveAsync(_repo, "epic-1", "[]");
        await Git("checkout", "-b", "epic-2");
        await service.OpenAsync(_repo, "epic-2");

        var cadence = Parse(await service.StatusAsync(_repo, "epic-2", string.Empty, Plan)).GetProperty("cadence");

        cadence.GetProperty("epicsClosed").EnumerateArray().Select(e => e.GetInt32()).Should().Equal(1);
    }

    [Fact]
    public async Task APlanWithoutAnEpic_IsHeld_AndItsRiskAnswerKept()
    {
        // Epic 3's code round (codex): one plan gate for the whole task has no epic yet — and it is the
        // natural place to answer the risk question. The plan is held; the epic comes with the code rounds.
        await CommitPlan();
        var service = Service(CadenceMode.Remind);
        await service.OpenAsync(_repo, "epic-1");
        Script(Clean);

        var plan = Parse(await service.ReviewPlanAsync(_repo, "epic-1", Scope,
            new CadenceArgs(Plan, "", """[{"epic":5,"story":"5.1","reason":"the migration"}]""", "")));

        Reviewed(plan).Should().BeTrue(plan.ToString());
        var cadence = Parse(await service.StatusAsync(_repo, "epic-1")).GetProperty("cadence");
        cadence.GetProperty("plan").GetString().Should().Be(Plan, "the session holds it without being told again");
        cadence.GetProperty("risk").EnumerateArray().Select(r => r.GetProperty("key").GetString()).Should().Equal("5/5.1");
    }

    [Fact]
    public async Task AHugeRiskAnswer_IsRefusedWithoutBeingBuilt()
    {
        await CommitPlan();
        var service = Service(CadenceMode.Remind);
        await service.OpenAsync(_repo, "epic-1");
        Script(Clean);
        var many = "[" + string.Join(",", Enumerable.Range(1, 100).Select(i => $$"""{"epic":1,"reason":"r{{i}}"}""")) + "]";
        var huge = "[" + new string(' ', 20_000) + "]";

        Refusal(Parse(await service.ReviewPlanAsync(_repo, "epic-1", Scope, At("1/6", many)))).Should().Contain("at most 3");
        Refusal(Parse(await service.ReviewPlanAsync(_repo, "epic-1", Scope, At("1/6", huge)))).Should().Contain("too large");
    }

    [Fact]
    public async Task ARiskStoryOfAnotherEpic_IsRefused() =>
        await RiskRefused("""[{"epic":2,"story":"7.2","reason":"x"}]""", "'7.2'");

    private async Task RiskRefused(string items, string named)
    {
        await CommitPlan();
        var service = Service(CadenceMode.Remind);
        await service.OpenAsync(_repo, "epic-1");
        Script(Clean);

        Refusal(Parse(await service.ReviewPlanAsync(_repo, "epic-1", Scope, At("1/6", items)))).Should().Contain(named);
    }

    [Theory]
    [InlineData(CadenceMode.Remind, true)]
    [InlineData(CadenceMode.Require, false)]
    public async Task ARiskAnswerThatCannotBeRecorded_RefusesOnlyInRequire(CadenceMode mode, bool reviewed)
    {
        // Epic 3's code round (codex): remind never refuses the work over a record it could not write.
        await CommitPlan();
        var service = Service(mode);
        await service.OpenAsync(_repo, "epic-1");
        Script(Clean);
        var repoId = await new ContextAssembler(_launcher).CommonDirAsync(_repo, TestContext.Current.CancellationToken);
        var turn = SessionTurn.LockFileFor(new CadenceStore(_data).FileFor(repoId, Plan));
        Directory.CreateDirectory(Path.GetDirectoryName(turn)!);

        using (new FileStream(turn, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            var reply = Parse(await service.ReviewPlanAsync(_repo, "epic-1", Scope, At("1/6", "[]", "nothing here is risky")));

            Reviewed(reply).Should().Be(reviewed, reply.ToString());
        }
    }

    [Fact]
    public async Task AReconciliationThatCannotTakeItsTurn_DoesNotRefuseTheRound()
    {
        // Epic 3's code round: reconciling a missed close is catch-up work, and a busy record must not
        // turn it into a refusal of a round that owes nothing.
        await CommitPlan();
        var service = Service(CadenceMode.Require);
        await Consulted(service, "cadence", "1-3");
        await ThroughThePlan(service, "1/6");
        await CommitWork();
        await CodeRound(service, "1/6");
        var repoId = await new ContextAssembler(_launcher).CommonDirAsync(_repo, TestContext.Current.CancellationToken);
        var turn = SessionTurn.LockFileFor(new CadenceStore(_data).FileFor(repoId, Plan));
        Directory.CreateDirectory(Path.GetDirectoryName(turn)!);

        using (new FileStream(turn, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            await service.ResolveAsync(_repo, "epic-1", "[]");
            await CommitWork("v3\n");

            Reviewed(await CodeRound(service, "1/6", again: true)).Should().BeTrue();
        }
    }

    [Fact]
    public async Task AFailedCadenceWrite_DoesNotFailResolve_AndIsReconciledOnTheNextCall()
    {
        await CommitPlan();
        var service = Service(CadenceMode.Require);
        await Consulted(service, "cadence", "1-3");
        await ThroughThePlan(service, "1/6");
        await CommitWork();
        await CodeRound(service, "1/6");

        var repoId = await new ContextAssembler(_launcher).CommonDirAsync(_repo, TestContext.Current.CancellationToken);
        var turn = SessionTurn.LockFileFor(new CadenceStore(_data).FileFor(repoId, Plan));
        Directory.CreateDirectory(Path.GetDirectoryName(turn)!);
        using (new FileStream(turn, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            Parse(await service.ResolveAsync(_repo, "epic-1", "[]")).GetProperty("stage").GetString().Should().Be("Done");
        }

        var cadence = Parse(await service.StatusAsync(_repo, "epic-1", string.Empty, Plan)).GetProperty("cadence");
        cadence.GetProperty("epicsClosed").EnumerateArray().Select(e => e.GetInt32()).Should().Equal(1);
    }
}

/// <summary>The cadence's four settings, read the way every other one is (<c>todo/PLAN_consult_on_a_cadence.md</c>, story 3.1).</summary>
public sealed class CadenceSettingsTests
{
    private static PanelSettings From(params (string Key, string Value)[] vars)
    {
        var map = vars.ToDictionary(v => v.Key, v => v.Value);

        return PanelSettings.FromEnvironment(name => map.GetValueOrDefault(name));
    }

    [Fact]
    public void TheDefaults_AreRemindThreeFiveThree()
    {
        var settings = From();

        settings.CadenceMode.Should().Be(CadenceMode.Remind, "D4: a refusal by default would change every Marketplace user's gate");
        settings.CadenceEvery.Should().Be(3);
        settings.CadenceRiskThreshold.Should().Be(5);
        settings.CadenceRiskMax.Should().Be(3);
    }

    [Theory]
    [InlineData("off", CadenceMode.Off)]
    [InlineData("REQUIRE", CadenceMode.Require)]
    [InlineData(" remind ", CadenceMode.Remind)]
    public void TheModeIsReadWithoutCase(string value, CadenceMode mode) =>
        From(("COAI_CADENCE_MODE", value)).CadenceMode.Should().Be(mode);

    [Fact]
    public void AnUnknownMode_IsNamed_AndTheDefaultHolds()
    {
        var settings = From(("COAI_CADENCE_MODE", "strict"));

        settings.CadenceMode.Should().Be(CadenceMode.Remind);
        settings.UnrecognisedSettings.Should().Contain(u => u.Key == "COAI_CADENCE_MODE" && u.Sentence.Contains("'strict'"));
    }

    [Fact]
    public void TheNumbersAreRead_AndNothingBelowOneIsTaken()
    {
        var settings = From(("COAI_CADENCE_EVERY", "4"), ("COAI_CADENCE_RISK_THRESHOLD", "0"), ("COAI_CADENCE_RISK_MAX", "2"));

        settings.CadenceEvery.Should().Be(4);
        settings.CadenceRiskThreshold.Should().Be(5, "zero would ask about risk on a plan of no epics");
        settings.CadenceRiskMax.Should().Be(2);
    }
}
