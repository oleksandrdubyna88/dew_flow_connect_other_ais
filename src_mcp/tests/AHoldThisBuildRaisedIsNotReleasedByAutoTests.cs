using System.Text.Json.Nodes;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A hold THIS build raised is the person's whatever the policy becomes: policy auto releases only a hold from before it
/// (<c>todo/PLAN_auto_never_asks_a_person_E1_S1_spec.md</c>, § S1.10.7).
/// </summary>
/// <remarks>
/// The fixture is <c>HeldAsync</c> with no strip. Its file says <c>"autonomous": false</c>; the positive beside these negatives is the same hold,
/// stripped, released by every entry in <see cref="AHoldFromBeforeAutoIsReleasedTests"/>.
/// </remarks>
[Collection("fakecli-env")]
public sealed class AHoldThisBuildRaisedIsNotReleasedByAutoTests : HoldFromBeforeAutoFixture
{
    private static readonly Hold PlanHold = new(Kind.Plan, Branch, string.Empty, string.Empty);

    /// <summary>The hold, raised by this build under human; its file carries the marker.</summary>
    private async Task<PanelService> RaisedAsync()
    {
        var human = await HeldAsync();
        SessionOf(PlanHold).State.Config.Autonomous.Should().BeFalse("a hold this build raises carries the marker");
        JsonNode.Parse(SessionBytes(PlanHold))!["state"]!["config"]!["autonomous"]!.GetValue<bool>().Should().BeFalse();

        return human;
    }

    private void Kept(string id)
    {
        var state = SessionOf(PlanHold).State;
        state.HumanGate.Should().BeTrue();
        state.HoldQuestions.Should().Contain(id);
        CardStatus(id).Should().Be(EscalationStatuses.Open);
        Said("auto released").Should().BeEmpty();
    }

    [Theory]
    [InlineData("a review entry")]
    [InlineData("resolve")]
    [InlineData("status")]
    [InlineData("ask_human")]
    public async Task NoEntryReleasesIt(string entry)
    {
        var human = await RaisedAsync();
        var id = NoticeId();
        ErrorOf(await human.ResolveAsync(_repo, Branch, RejectIt)).Should().BeEmpty("the round is resolved; the hold stays");
        var auto = AutoService();

        await EnterAsync(auto, entry, resolveWith: "[]");

        Kept(id);
    }

    [Fact]
    public async Task AFreshDocumentReview_DoesNotReleaseIt()
    {
        var hold = await RaiseAsync(Kind.Document, resolved: true);
        var id = NoticeIdOf(hold);
        SessionOf(hold).State.Config.Autonomous.Should().BeFalse();
        var auto = AutoService(Kind.Document);

        var reply = await auto.ReviewDocumentAsync(
            _repo, Branch, DocumentPurpose, documentPath: Path.Combine(_repo, "docs/spec.md"), newReview: true);

        ErrorOf(reply).Should().Contain("a person has to decide");
        SessionOf(hold).State.HumanGate.Should().BeTrue();
        SessionOf(hold).State.HoldQuestions.Should().Contain(id);
        CardStatus(id).Should().Be(EscalationStatuses.Open);
        Said("auto released").Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveWithHumanDecisionProceed_IsStillRefused_UnderAuto()
    {
        await RaisedAsync();
        var id = NoticeId();
        var auto = AutoService();

        foreach (var decisions in new[] { RejectIt, "[]" })
        {
            ErrorOf(await auto.ResolveAsync(_repo, Branch, decisions, callerClaimsProceed: true)).Should().Contain("Proceed despite the findings");
            Kept(id);
            SessionOf(PlanHold).State.Stage.Should().Be(Stage.PlanReview);
        }
    }

    [Fact]
    public async Task ThePersonsProceed_StillReleasesIt_UnderAuto()
    {
        await RaisedAsync();
        ThePersonAnswers(NoticeId(), "proceed");

        var reply = await AutoService().ResolveAsync(_repo, Branch, RejectIt);

        StageOf(reply).Should().Be("CodeReview", reply);
        Said("auto released").Should().BeEmpty();
    }

    // ---------- T43: however the policy became auto ----------

    private static Func<string, string?> With(string value) => name => name == "COAI_ON_EXHAUSTED" ? value : null;

    public static TheoryData<string, bool> HowAutoCameAbout()
    {
        var rows = new TheoryData<string, bool>();
        foreach (var how in new[] { "the word auto", "no key", "an empty value", "no policy named in code" })
        {
            rows.Add(how, true);
            rows.Add(how, false);
        }

        return rows;
    }

    private static PanelConfig RoundsFor(string how) => how switch
    {
        "the word auto" => PanelSettings.FromEnvironment(With("auto")).Rounds,
        "no key" => PanelSettings.FromEnvironment(_ => null).Rounds,
        "an empty value" => PanelSettings.FromEnvironment(With(string.Empty)).Rounds,
        _ => PanelConfig.Uniform(1, 0),
    };

    [Theory]
    [MemberData(nameof(HowAutoCameAbout))]
    public async Task WhateverMakesThePolicyAuto_ReleasesOnlyAHoldFromBeforeAuto(string how, bool stripped)
    {
        await RaisedAsync();
        var id = NoticeId();
        var hold = PlanHold;
        if (stripped)
        {
            StripTheMarker(hold, recorded: true);
        }

        var cards = Cards();
        var auto = new PanelService(
            SettingsOf(Kind.Plan, human: false) with { Rounds = RoundsFor(how) }, VaultKeys.None("no vault in tests"), default, _launcher, AuditLog, Noticing.None);

        await StatusAsync(auto, hold);

        if (stripped)
        {
            AssertReleased(hold, recorded: true, id, cards);
        }
        else
        {
            Kept(id);
        }
    }

    // ---------- T44–T46: a hold read under human keeps its marker ----------

    [Theory]
    [InlineData("a review entry")]
    [InlineData("status")]
    [InlineData("resolve")]
    [InlineData("ask_human")]
    public async Task AHoldFromBeforeAuto_ReadUnderHuman_KeepsItsMarker_AndAutoReleasesItLater(string entry)
    {
        var human = await HeldAsync();
        var id = NoticeId();
        if (entry != "resolve")
        {
            ErrorOf(await human.ResolveAsync(_repo, Branch, RejectIt)).Should().BeEmpty();
        }

        StripTheMarker(PlanHold, recorded: true);
        var cards = Cards();

        await EnterAsync(human, entry, resolveWith: RejectIt);

        SessionOf(PlanHold).State.HumanGate.Should().BeTrue();
        SessionOf(PlanHold).State.Config.PredatesAuto.Should().BeTrue("reading it under human did not write the marker");
        JsonNode.Parse(SessionBytes(PlanHold))!["state"]!["config"]!.AsObject().ContainsKey("autonomous").Should().BeFalse();

        await StatusAsync(AutoService(), PlanHold);

        AssertReleased(PlanHold, recorded: true, id, cards);
    }

    /// <summary>One of the entries that load a hold, called on the plan stage's hold through <paramref name="service"/>.</summary>
    private async Task EnterAsync(PanelService service, string entry, string resolveWith)
    {
        var reply = await (entry switch
        {
            "a review entry" => ReviewEntryAsync(service, PlanHold),
            "status" => StatusAsync(service, PlanHold),
            "resolve" => service.ResolveAsync(_repo, Branch, resolveWith),
            _ => service.AskHumanAsync(_repo, Branch, "May I ship?"),
        });

        if (entry == "a review entry")
        {
            ErrorOf(reply).Should().Be(RoundMachine.GateHeld);
        }
    }

    [Fact]
    public async Task AHoldRaisedAfterAPersonReleasedTheOldOne_IsThisBuilds()
    {
        var human = await HeldAsync();
        var old = NoticeId();
        ErrorOf(await human.ResolveAsync(_repo, Branch, """[{"finding":0,"action":"accept"}]""")).Should().BeEmpty();
        StripTheMarker(PlanHold, recorded: true);
        ThePersonAnswers(old, "continue");
        Script(OneBlockingFinding);

        var round = Parse(await human.ReviewPlanAsync(_repo, Branch, Plan, TestContext.Current.CancellationToken));

        round.GetProperty("verdict").GetString().Should().Be("call_human", round.ToString());
        var fresh = NoticeId();
        fresh.Should().NotBe(old, "a new hold has a new notice");
        SessionOf(PlanHold).State.Config.Autonomous.Should().BeFalse("the begin stamped the session after the person's decision was applied");
        JsonNode.Parse(SessionBytes(PlanHold))!["state"]!["config"]!["autonomous"]!.GetValue<bool>().Should().BeFalse();

        await StatusAsync(AutoService(), PlanHold);

        Kept(fresh);
    }

    [Fact]
    public async Task AHoldFromBeforeAutoWithNoQuestion_IsReissuedUnderHuman_AndAutoWithdrawsThatNotice()
    {
        var human = await HeldAsync();
        var old = NoticeId();
        ErrorOf(await human.ResolveAsync(_repo, Branch, RejectIt)).Should().BeEmpty();
        StripTheMarker(PlanHold, recorded: false);
        var cards = Cards();

        ErrorOf(await ReviewEntryAsync(human, PlanHold)).Should().Be(RoundMachine.GateHeld);

        var reissued = NoticeId();
        reissued.Should().NotBe(old, "one new notice is on the hold");
        Cards().Should().Be(cards + 1);
        SessionOf(PlanHold).State.Config.PredatesAuto.Should().BeTrue("the re-issue wrote no marker");

        await StatusAsync(AutoService(), PlanHold);

        AssertReleased(PlanHold, recorded: true, reissued, cards + 1);
    }
}
