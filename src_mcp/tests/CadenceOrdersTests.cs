using System.Text.RegularExpressions;
using CoaiMcp.Core.Cadence;
using CoaiMcp.Core.Commands;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The orders that make a caller consult on a cadence (<c>todo/PLAN_consult_on_a_cadence.md</c>, story 1.4).
/// </summary>
/// <remarks>
/// <para>Measured before any of this existed: ~1 260 gate rounds against 18 consultations, none after
/// 2026-09-22, and zero across fourteen epics of one real product. Every trigger shipped before was
/// reactive; these are the first that fire because of where the work IS.</para>
/// <para>The caller has usually never loaded <c>consult</c>'s schema — Claude Code defers MCP tool
/// schemas — so the order carries the whole call, not a reference to it.</para>
/// </remarks>
public sealed class CadenceOrdersTests
{
    private const string Plan = "todo/PLAN_x.md";
    private const string Repo = "D:/work/repo";

    private static string PlanWithEpics(int count, int first = 1)
    {
        var text = new System.Text.StringBuilder("# PLAN — generated\n\n");
        for (var epic = first; epic < first + count; epic++)
        {
            text.Append(System.Globalization.CultureInfo.InvariantCulture, $"### Epic {epic} — piece {epic}\n\ntext\n\n");
        }

        return text.ToString();
    }

    private static CadenceFacts Facts(CadenceMode mode, int epic, int epics = 6, params EpicGroup[] satisfied) => new()
    {
        Mode = mode,
        RepoPath = Repo,
        Plan = Plan,
        Epic = epic,
        Outline = PlanOutlineReader.Of(PlanWithEpics(epics)),
        Satisfied = satisfied,
        RiskAnswered = true,
    };

    private static IReadOnlyList<string> CodeRound(CadenceFacts facts) =>
        GateCommands.For(new CommandContext() { Cadence = facts });

    [Fact]
    public void Off_SaysNothing_WhateverTheFactsAre() =>
        CodeRound(Facts(CadenceMode.Off, epic: 1)).Should().BeEmpty("a switch nobody set must change nothing");

    [Fact]
    public void TheFirstEpicOfAnUnconsultedGroup_OrdersTheConsultation()
    {
        var order = CodeRound(Facts(CadenceMode.Remind, epic: 4)).Should().ContainSingle().Subject;

        order.Should().StartWith(CadenceOrders.GroupMarker);
        order.Should().Contain("\"epics\": \"4-6\"").And.Contain("piece 4").And.Contain("piece 6");
    }

    [Fact]
    public void AMiddleEpicOfAnUnconsultedGroup_IsOrderedToo()
    {
        // The epic-1-3 consultation, point 3: a first call of 2/6 with no consultation is still owed one.
        CodeRound(Facts(CadenceMode.Remind, epic: 2)).Should().ContainSingle()
            .Which.Should().Contain("\"epics\": \"1-3\"");
    }

    [Fact]
    public void AnEpicOfAConsultedGroup_OrdersNothing() =>
        CodeRound(Facts(CadenceMode.Require, epic: 5, satisfied: new EpicGroup(4, 6))).Should().BeEmpty();

    [Fact]
    public void TheConsultOrder_NamesEveryFieldTheCallNeeds()
    {
        var order = CodeRound(Facts(CadenceMode.Remind, epic: 1))[0];

        order.Should().Contain("ToolSearch").And.Contain("select:mcp__coai__consult,mcp__coai__close_consult");
        order.Should().Contain("mcp__coai__consult(");
        foreach (var field in new[] { "\"repoPath\": \"D:/work/repo\"", "\"kind\": \"cadence\"", $"\"plan\": \"{Plan}\"", "\"epics\": \"1-3\"", "\"problem\": \"" })
        {
            order.Should().Contain(field);
        }
        order.Should().Contain("mcp__coai__close_consult").And.Contain("solved").And.Contain("not_solved").And.Contain("abandoned");
    }

    [Fact]
    public void RequireMode_SaysTheGateWillRefuse_RemindModeDoesNot()
    {
        CodeRound(Facts(CadenceMode.Require, epic: 1))[0].Should().Contain("refuses");
        CodeRound(Facts(CadenceMode.Remind, epic: 1))[0].Should().NotContain("refuses");
    }

    [Fact]
    public void TheCallIsValidJson_EvenWhenATitleHasQuotesAndDashes()
    {
        var facts = Facts(CadenceMode.Remind, epic: 1) with
        {
            Outline = PlanOutlineReader.Of("### Epic 1 — the \"door\" — SMTP\n### Epic 2 — two\n"),
        };
        var order = CodeRound(facts)[0];
        var json = Regex.Match(order, @"mcp__coai__consult\((\{.*?\})\)", RegexOptions.Singleline, TimeSpan.FromSeconds(1)).Groups[1].Value;

        var parsed = System.Text.Json.JsonDocument.Parse(json).RootElement;
        parsed.GetProperty("problem").GetString().Should().Contain("the \"door\" — SMTP");
    }

    [Fact]
    public void TheCallIsValidJson_ForAWindowsPathAndAReasonWithANewline()
    {
        // Epic 1's code round claimed JsonEncodedText leaves backslashes and control characters
        // unescaped under the relaxed encoder. It does not; this is the evidence.
        var facts = Facts(CadenceMode.Remind, epic: 5, satisfied: new EpicGroup(4, 6)) with
        {
            RepoPath = @"D:\rsd\_wt\a ""quoted"" repo",
            RiskItems = [new RiskItem(5, "", "line one\nline two\t<b>&")],
        };
        var order = CodeRound(facts).Should().ContainSingle().Subject;
        var json = Regex.Match(order, @"mcp__coai__consult\((\{.*?\})\)", RegexOptions.Singleline, TimeSpan.FromSeconds(1)).Groups[1].Value;

        var parsed = System.Text.Json.JsonDocument.Parse(json).RootElement;
        parsed.GetProperty("repoPath").GetString().Should().Be(@"D:\rsd\_wt\a ""quoted"" repo");
        parsed.GetProperty("problem").GetString().Should().Contain("line one\nline two\t<b>&");
    }

    [Theory]
    [InlineData(CommandTexts.ConsultGroup, 1)]
    [InlineData(CommandTexts.ConsultRiskItem, 5)]
    public void AnOverrideThatDropsTheCall_StillCarriesIt(string id, int epic)
    {
        // Epic 1's code round (codex): a person who rewords the order without {call} would leave a
        // caller with a deferred schema nothing to call.
        var facts = Facts(CadenceMode.Remind, epic, satisfied: epic == 5 ? [new EpicGroup(4, 6)] : []) with
        {
            RiskItems = [new RiskItem(5, "", "why")],
        };
        var texts = new CommandTexts(new Dictionary<string, string> { [id] = "Consult first. That is all." });

        var order = GateCommands.For(new CommandContext() { Cadence = facts, Texts = texts }).Should().ContainSingle().Subject;

        order.Should().Contain("Consult first. That is all.").And.Contain("mcp__coai__consult({");
    }

    [Fact]
    public void AtTheThreshold_TheRiskQuestionIsAskedUntilAnswered()
    {
        var unanswered = Facts(CadenceMode.Remind, epic: 1, epics: 5, satisfied: new EpicGroup(1, 3)) with { RiskAnswered = false };

        CodeRound(unanswered).Should().ContainSingle().Which.Should().StartWith(CadenceOrders.RiskQuestionMarker)
            .And.Contain("riskItems").And.Contain("riskNote").And.Contain("at most 3");
        CodeRound(unanswered with { RiskAnswered = true }).Should().BeEmpty();
    }

    [Fact]
    public void BelowTheThreshold_NoRiskQuestion() =>
        CodeRound(Facts(CadenceMode.Remind, epic: 1, epics: 4, satisfied: new EpicGroup(1, 3)) with { RiskAnswered = false })
            .Should().BeEmpty();

    [Fact]
    public void ARiskyEpic_IsOrderedItsOwnConsultation_UntilItHasOne()
    {
        var facts = Facts(CadenceMode.Remind, epic: 5, satisfied: new EpicGroup(4, 6)) with
        {
            RiskItems = [new RiskItem(5, "5.2", "moves the data")],
        };

        var order = CodeRound(facts).Should().ContainSingle().Subject;
        order.Should().StartWith(CadenceOrders.RiskItemMarker);
        order.Should().Contain("\"kind\": \"risk\"").And.Contain("\"epics\": \"5/5.2\"").And.Contain("moves the data");

        CodeRound(facts with { SatisfiedRisk = ["5/5.2"] }).Should().BeEmpty();
        CodeRound(facts with { Epic = 6 }).Should().BeEmpty("the item is in epic 5");
    }

    [Fact]
    public void TheForecast_ComesOnTheFirstPlanRound_OfAPlanThatNamesEpics()
    {
        var context = new CommandContext(PlanText: PlanWithEpics(14), PlanStage: true, FirstPlanRound: true)
        {
            Cadence = CadenceFacts.Off with { Mode = CadenceMode.Remind },
        };

        var order = GateCommands.For(context).Should().ContainSingle().Subject;
        order.Should().StartWith(CadenceOrders.ForecastMarker);
        order.Should().Contain("5 consultations").And.Contain("1-3, 4-6, 7-9, 10-12, 13-14").And.Contain("\"k/N\"");

        GateCommands.For(context with { FirstPlanRound = false }).Should().BeEmpty("said once, like the split");
        GateCommands.For(context with { PlanText = "# PLAN\n\none paragraph\n" }).Should().BeEmpty("no epics, nothing owed");
    }

    [Fact]
    public void TheForecast_ComesWithASplitOrderForEpics_EvenBeforeTheyAreWritten()
    {
        var bigPlan = "# PLAN\n\n## Build order\n\n" + string.Concat(Enumerable.Range(1, 11).Select(i => $"{i}. step\n"));
        var context = new CommandContext(SplitPlan: true, PlanText: bigPlan, PlanStage: true)
        {
            Cadence = CadenceFacts.Off with { Mode = CadenceMode.Remind },
        };

        var orders = GateCommands.For(context);
        orders.Should().HaveCount(2);
        orders[0].Should().StartWith("Split this plan into ");
        orders[1].Should().StartWith(CadenceOrders.ForecastMarker).And.Contain("how many epics you split it into");
    }

    [Fact]
    public void CadenceOrdersComeAfterEveryExistingOrder_SoNoPositionShifts()
    {
        var context = new CommandContext(Autonomous: true)
        {
            Cadence = Facts(CadenceMode.Remind, epic: 1),
        };

        var orders = GateCommands.For(context);
        orders[0].Should().StartWith(GateCommands.AutonomyMarker);
        orders[^1].Should().StartWith(CadenceOrders.GroupMarker);
    }

    [Fact]
    public void NoCadenceOrder_EverSaysCritical()
    {
        // D2: `critical` is the canonical INVENTED severity — ReviewParser rejects it by name, and two
        // tests forbid it in what a reviewer reads. These orders reach the caller, who quotes them.
        var all = new[]
        {
            CodeRound(Facts(CadenceMode.Require, epic: 1)),
            CodeRound(Facts(CadenceMode.Require, epic: 1, epics: 9) with { RiskAnswered = false }),
            CodeRound(Facts(CadenceMode.Require, epic: 1) with { RiskItems = [new RiskItem(1, "", "why")] }),
            GateCommands.For(new CommandContext(PlanText: PlanWithEpics(9), PlanStage: true) { Cadence = CadenceFacts.Off with { Mode = CadenceMode.Require } }),
        }.SelectMany(orders => orders).ToList();

        all.Should().NotBeEmpty();
        all.Should().AllSatisfy(order => order.Should().NotContainEquivalentOf("critical"));
    }
}
