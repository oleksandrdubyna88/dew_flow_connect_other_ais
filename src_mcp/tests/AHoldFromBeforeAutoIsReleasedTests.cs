using System.Text.Json;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Whichever call reads a hold from before policy auto first releases it, and says so
/// (<c>todo/PLAN_auto_never_asks_a_person_E1_S1_spec.md</c>, § S1.10.6, § S1.10.9, § S1.10.11 and § S1.10.13).
/// </summary>
/// <remarks>
/// The fixture is a hold the REAL code raised under <c>human</c>, made a hold from before policy auto by editing its file
/// (<see cref="HoldFromBeforeAutoFixture"/>). Each scenario runs for a hold whose notice the session records and for one with no recorded question,
/// as an older build left it — which policy auto releases without re-issuing anything.
/// </remarks>
[Collection("fakecli-env")]
public sealed class AHoldFromBeforeAutoIsReleasedTests : HoldFromBeforeAutoFixture
{
    private async Task<(PanelService Auto, Hold Hold, string? Notice, int Cards)> StrippedPlanHoldAsync(bool recorded, bool resolved)
    {
        var human = await HeldAsync();
        if (resolved)
        {
            ErrorOf(await human.ResolveAsync(_repo, Branch, RejectIt)).Should().BeEmpty();
        }

        var hold = new Hold(Kind.Plan, Branch, string.Empty, string.Empty);
        var notice = NoticeId();
        StripTheMarker(hold, recorded);

        return (AutoService(), hold, recorded ? notice : null, Cards());
    }

    // ---------- T34–T38: whichever call comes first ----------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AReviewEntry_ComesFirst(bool recorded)
    {
        var (auto, hold, notice, cards) = await StrippedPlanHoldAsync(recorded, resolved: true);

        var reply = await ReviewEntryAsync(auto, hold);

        var error = ErrorOf(reply);
        error.Should().Be(LegacyHold.ReleasedSentence(StageWords(SessionOf(hold))));
        error.Should().NotContain("call_human").And.NotContain("a person has to decide").And.NotContain("have not been resolved");
        AssertReleased(hold, recorded, notice, cards);

        StageOf(await auto.ResolveAsync(_repo, Branch, "[]")).Should().Be("CodeReview");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Resolve_ComesFirst(bool recorded)
    {
        var (auto, hold, notice, cards) = await StrippedPlanHoldAsync(recorded, resolved: false);

        var reply = await auto.ResolveAsync(_repo, Branch, RejectIt);

        StageOf(reply).Should().Be("CodeReview", reply);
        Session().State.PlanProceeded.Should().BeTrue();
        AssertReleased(hold, recorded, notice, cards, advanced: true);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Status_ComesFirst(bool recorded)
    {
        var (auto, hold, notice, cards) = await StrippedPlanHoldAsync(recorded, resolved: false);

        var reply = Parse(await StatusAsync(auto, hold));

        reply.TryGetProperty("error", out _).Should().BeFalse(reply.ToString());
        reply.GetProperty("awaitingResolve").GetBoolean().Should().BeTrue();
        AssertReleased(hold, recorded, notice, cards);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AskHuman_ComesFirst(bool recorded)
    {
        var (auto, hold, notice, cards) = await StrippedPlanHoldAsync(recorded, resolved: false);

        var reply = Parse(await auto.AskHumanAsync(_repo, Branch, "May I ship?"));

        (reply.TryGetProperty("status", out var status) ? status.GetString() : string.Empty).Should().NotBe("no_answer_yet", reply.ToString());
        AssertReleased(hold, recorded, notice, cards);
        Cards().Should().Be(cards, "no new question file: the gate no longer holds");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AFreshDocumentReview_ComesFirst(bool recorded)
    {
        var hold = await RaiseAsync(Kind.Document, resolved: true);
        var notice = NoticeIdOf(hold);
        StripTheMarker(hold, recorded);
        var cards = Cards();
        var auto = AutoService(Kind.Document);

        var reply = await ReviewFreshDocumentAsync(auto);

        var error = ErrorOf(reply);
        error.Should().Be(LegacyHold.ReleasedSentence(StageWords(SessionOf(hold))));
        error.Should().NotContain("a person has to decide").And.NotContain("nobody has decided");
        AssertReleased(hold, recorded, recorded ? notice : null, cards);

        ErrorOf(await ResolveAsync(auto, hold, "[]")).Should().BeEmpty();
        Script(DocumentAnswerClean);
        ErrorOf(await ReviewFreshDocumentAsync(auto)).Should().BeEmpty("after the resolve a fresh review starts");
    }

    private const string DocumentAnswerClean = """{"findings": [], "notes": "fine"}""";

    private Task<string> ReviewFreshDocumentAsync(PanelService service) =>
        service.ReviewDocumentAsync(
            _repo, Branch, DocumentPurpose,
            documentPath: Path.Combine(_repo, "docs/spec.md"), newReview: true);

    // ---------- T50: an answer that raced the release ----------

    [Theory]
    [InlineData("continue")]
    [InlineData("proceed")]
    [InlineData("discuss")]
    public async Task AnAnswerWrittenBetweenTheWithdrawalAndTheSecondRead_Wins(string decision)
    {
        var human = await HeldAsync();
        ErrorOf(await human.ResolveAsync(_repo, Branch, RejectIt)).Should().BeEmpty();
        var hold = new Hold(Kind.Plan, Branch, string.Empty, string.Empty);
        var id = NoticeId();
        StripTheMarker(hold, recorded: true);
        var before = JsonDocument.Parse(File.ReadAllText(NoticeFile(id))).RootElement.Clone();
        var bytes = SessionBytes(hold);
        var seen = new List<(string Path, string Text)>();
        var escalations = new Escalations(_data)
        {
            Writer = (path, text) =>
            {
                seen.Add((path, text));
                AtomicJson.Write(path, text);
                if (seen.Count == 1)
                {
                    ThePersonAnswers(id, decision);
                }
            },
        };
        var settings = SettingsOf(Kind.Plan, human: false);
        var policy = new SessionPolicy(settings, Store, escalations, new CoaiMcp.Store.Projection(_data, AuditLog), AuditLog);
        var session = Session();

        var released = policy.Released(session);

        released.Session.Should().BeSameAs(session);
        released.NotWritten.Should().BeEmpty();
        seen.Select(w => Path.GetFileName(w.Path)).Should().Equal([$"{id}.json", $"{id}.json"], "the notice's file was written exactly twice");
        StatusOf(seen[0].Text).Should().Be(EscalationStatuses.Expired);
        StatusOf(seen[1].Text).Should().BeEmpty("then it was put back");
        var after = JsonDocument.Parse(File.ReadAllText(NoticeFile(id))).RootElement;
        foreach (var member in new[] { "status", "expiredUtc", "question", "kind", "decisions" })
        {
            after.GetProperty(member).ToString().Should().Be(before.GetProperty(member).ToString(), $"the notice reads back as before the call ({member})");
        }

        SessionBytes(hold).Should().Be(bytes, "no marker and no note");
        Said("their answer stands").Should().ContainSingle().Which.Should().Contain("offered again");
        Said("auto released").Should().BeEmpty();

        var auto = AutoService();
        switch (decision)
        {
            case "continue":
                var reply = Parse(await auto.ReviewPlanAsync(_repo, Branch, Plan, TestContext.Current.CancellationToken));
                reply.TryGetProperty("verdict", out _).Should().BeTrue(reply.ToString());
                Said("a person chose").Should().NotBeEmpty();
                break;
            case "proceed":
                StageOf(await auto.ResolveAsync(_repo, Branch, "[]")).Should().Be("CodeReview");
                break;
            default:
                ErrorOf(await auto.ReviewPlanAsync(_repo, Branch, Plan, TestContext.Current.CancellationToken)).Should().Be(RoundMachine.GateHeld);
                SessionOf(hold).State.HumanGate.Should().BeTrue();
                CardStatus(id).Should().Be(EscalationStatuses.Open, "the person can answer again");
                ThePersonAnswers(id, "continue");
                Parse(await auto.ReviewPlanAsync(_repo, Branch, Plan, TestContext.Current.CancellationToken))
                    .TryGetProperty("verdict", out _).Should().BeTrue("a continue lets the next review run");
                break;
        }

        SessionOf(hold).Rounds.Should().OnlyContain(r => r.Note != LegacyHold.Note);
        Column("SELECT note FROM rounds WHERE note = '" + LegacyHold.Note + "'").Should().BeEmpty();
    }

    private static string StatusOf(string text) =>
        JsonDocument.Parse(text).RootElement.TryGetProperty("status", out var status) ? status.GetString() ?? string.Empty : string.Empty;

    // ---------- T54: what every review entry answers once the hold is released ----------

    public static TheoryData<Kind, bool> EntryRows()
    {
        var rows = new TheoryData<Kind, bool>();
        foreach (var kind in AllKinds)
        {
            rows.Add(kind, false);
            rows.Add(kind, true);
        }

        return rows;
    }

    [Theory]
    [MemberData(nameof(EntryRows))]
    public async Task AfterARelease_EveryReviewEntry_NamesTheReleaseAndResolve(Kind kind, bool releasedEarlier)
    {
        var hold = await RaiseAsync(kind, resolved: true);
        var notice = NoticeIdOf(hold);
        StripTheMarker(hold, recorded: true);
        var cards = Cards();
        var auto = AutoService(kind);
        if (releasedEarlier)
        {
            ErrorOf(await StatusAsync(auto, hold)).Should().BeEmpty();
        }

        var bytes = SessionBytes(hold);

        var error = ErrorOf(await ReviewEntryAsync(auto, hold));

        error.Should().Be(LegacyHold.ReleasedSentence(StageWords(SessionOf(hold))));
        error.Should().Contain("call resolve")
            .And.NotContain("have not been resolved").And.NotContain("call_human").And.NotContain("a person has to decide").And.NotContain("nobody has decided");
        AssertReleased(hold, recorded: true, notice, cards);
        if (releasedEarlier)
        {
            SessionBytes(hold).Should().Be(bytes, "the refusing call changes no byte of the session file");
        }

        ErrorOf(await ReviewEntryAsync(auto, hold)).Should().Be(error, "a second identical call answers the same");
    }

    [Fact]
    public async Task ARoundThatWasNeverHeld_StillAnswersTheMachinesUnresolved()
    {
        var service = ServiceFor(Defaults() with
        {
            Rounds = PanelConfig.Uniform(3, 0),
            CadenceMode = Core.Cadence.CadenceMode.Off,
            SplitPlan = false,
        });
        await service.OpenAsync(_repo, Branch);
        Script(OneBlockingFinding);
        Parse(await service.ReviewPlanAsync(_repo, Branch, Plan, TestContext.Current.CancellationToken))
            .GetProperty("verdict").GetString().Should().Be("revise");

        var error = ErrorOf(await service.ReviewPlanAsync(_repo, Branch, Plan, TestContext.Current.CancellationToken));

        error.Should().Be(RoundMachine.Unresolved);
    }

    [Fact]
    public async Task AHoldWithNoRounds_IsReleasedWithoutANote_AndAReviewEntryStillNamesTheRelease()
    {
        var human = await HeldAsync();
        ErrorOf(await human.ResolveAsync(_repo, Branch, RejectIt)).Should().BeEmpty();
        var hold = new Hold(Kind.Plan, Branch, string.Empty, string.Empty);
        StripTheMarker(hold, recorded: false);
        var file = SessionFileOf(hold);
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        json["rounds"] = new System.Text.Json.Nodes.JsonArray();
        File.WriteAllText(file, json.ToJsonString());
        SessionOf(hold).Rounds.Should().BeEmpty("a hold with no round is released without a note (D12)");
        var auto = AutoService();

        // The first call that reads it releases it, and its entry names the release.
        var error = ErrorOf(await ReviewEntryAsync(auto, hold));

        error.Should().Be(LegacyHold.ReleasedSentence(StageWords(SessionOf(hold))));
        error.Should().NotBe(RoundMachine.Unresolved);
        var released = SessionOf(hold);
        released.State.HumanGate.Should().BeFalse();
        released.Rounds.Should().BeEmpty();
        SessionPolicy.AwaitsItsResolve(released).Should().BeTrue("the release is read from the flags when there is no round to carry a note");
        ErrorOf(await ReviewEntryAsync(auto, hold)).Should().Be(error, "a second identical call answers the same");

        StageOf(await auto.ResolveAsync(_repo, Branch, "[]")).Should().Be("CodeReview", "the following resolve advances once");
        SessionPolicy.AwaitsItsResolve(SessionOf(hold)).Should().BeFalse("the resolve consumed both flags");
    }

    // ---------- T55: a release that could not be written ----------

    [Fact]
    public async Task AReleaseThatCouldNotBeWritten_IsSaidWithItsReason_AndNeverAsGateHeld_ANoticeThatCannotBeWithdrawn()
    {
        var (auto, hold, _, _) = await StrippedPlanHoldAsync(recorded: true, resolved: true);
        var id = NoticeId();
        var card = File.ReadAllText(NoticeFile(id));
        File.Delete(NoticeFile(id));
        Directory.CreateDirectory(NoticeFile(id));
        var cards = Cards();
        var bytes = SessionBytes(hold);

        var error = ErrorOf(await ReviewEntryAsync(auto, hold));

        error.Should().Be(LegacyHold.NotWrittenSentence(StageWords(SessionOf(hold)), LegacyHold.NoticeNotWithdrawn));
        error.Should().Contain("call again").And.NotContain("a person has to decide").And.NotContain("ask_human");
        SessionBytes(hold).Should().Be(bytes, "held, no marker, no note");
        Cards().Should().Be(cards, "no new file under escalations");

        Directory.Delete(NoticeFile(id));
        File.WriteAllText(NoticeFile(id), card);

        ErrorOf(await ReviewEntryAsync(auto, hold)).Should().Be(LegacyHold.ReleasedSentence(StageWords(SessionOf(hold))));
        AssertReleased(hold, recorded: true, id, cards);
    }

    [Fact]
    public async Task AReleaseThatCouldNotBeWritten_IsSaidWithItsReason_AndNeverAsGateHeld_AClaimAnotherCallHolds()
    {
        var hold = await RaiseAsync(Kind.Document, resolved: true);
        var id = NoticeIdOf(hold);
        StripTheMarker(hold, recorded: true);
        var cards = Cards();
        var auto = AutoService(Kind.Document);
        var bytes = SessionBytes(hold);

        using (var claim = SessionClaim.TryTake(_data, _repo, Branch, hold.Document))
        {
            claim.Should().NotBeNull();

            var error = ErrorOf(await ReviewFreshDocumentAsync(auto));

            error.Should().Be(LegacyHold.NotWrittenSentence(StageWords(SessionOf(hold)), LegacyHold.ClaimBusy));
            error.Should().Contain("call again").And.NotContain("a person has to decide").And.NotContain("ask_human");
            SessionBytes(hold).Should().Be(bytes);
            Cards().Should().Be(cards);
        }

        ErrorOf(await ReviewFreshDocumentAsync(auto)).Should().Be(LegacyHold.ReleasedSentence(StageWords(SessionOf(hold))));
        AssertReleased(hold, recorded: true, id, cards);
    }

    // ---------- T64, T65: a settings read that failed ----------

    private const string HumanFile = """{"COAI_ON_EXHAUSTED":"human"}""";

    private string SettingsPath => Path.Combine(_data, SettingsFile.Name);

    private string? OnlyTheDataFolder(string name) => name == "COAI_DATA_DIR" ? _data : null;

    private PanelServiceHost Host(Func<string, string>? readText = null) =>
        readText is null
            ? new PanelServiceHost(OnlyTheDataFolder, VaultKeys.None("no vault in tests"), default, _launcher, AuditLog, Noticing.None, CancellationToken.None)
            : new PanelServiceHost(OnlyTheDataFolder, VaultKeys.None("no vault in tests"), default, _launcher, AuditLog, Noticing.None, readText);

    private async Task<string> PlanEntryAsync(PanelServiceHost host) =>
        await host.Current.ReviewPlanAsync(_repo, Branch, Plan, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ABusyReadWithHumanBehindIt_ReleasesNothing_AndTheNextCallRunsHuman(bool recorded)
    {
        var (_, hold, _, _) = await StrippedPlanHoldAsync(recorded, resolved: true);
        var notice = recorded ? NoticeId() : null;
        var cards = Cards();
        File.WriteAllText(SettingsPath, HumanFile);
        var busy = true;
        var host = Host(path => busy ? throw new IOException("in use by another process", unchecked((int)0x80070020)) : File.ReadAllText(path));

        var first = ErrorOf(await PlanEntryAsync(host));

        first.Should().Be(LegacyHold.NotWrittenSentence(StageWords(SessionOf(hold)), LegacyHold.SettingsUnread));
        SessionOf(hold).State.HumanGate.Should().BeTrue();
        SessionOf(hold).State.Config.PredatesAuto.Should().BeTrue();
        if (recorded)
        {
            CardStatus(notice!).Should().Be(EscalationStatuses.Open);
        }
        else
        {
            Cards().Should().Be(cards, "nothing is re-issued while the read has failed");
        }

        busy = false;

        var second = ErrorOf(await PlanEntryAsync(host));

        second.Should().Be(RoundMachine.GateHeld, "the policy is the operator's human now");
        SessionOf(hold).State.Config.PredatesAuto.Should().BeTrue("a hold from before policy auto keeps its marker absent");
        Cards().Should().Be(recorded ? cards : cards + 1, "an unrecorded hold's notice is re-issued as today");
        Said("auto released").Should().BeEmpty();
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("removed")]
    [InlineData(HumanFile)]
    public async Task AMalformedFile_ReleasesNothing_UntilItIsRepairedOrRemoved(string repair)
    {
        var (_, hold, notice, cards) = await StrippedPlanHoldAsync(recorded: true, resolved: true);
        File.WriteAllText(SettingsPath, "not json");
        var host = Host();
        var sentence = LegacyHold.NotWrittenSentence(StageWords(SessionOf(hold)), LegacyHold.SettingsUnread);

        ErrorOf(await PlanEntryAsync(host)).Should().Be(sentence);
        ErrorOf(await PlanEntryAsync(host)).Should().Be(sentence);
        SessionOf(hold).State.HumanGate.Should().BeTrue();
        CardStatus(notice!).Should().Be(EscalationStatuses.Open);

        if (repair == "removed")
        {
            File.Delete(SettingsPath);
        }
        else
        {
            await Task.Delay(30, TestContext.Current.CancellationToken);
            File.WriteAllText(SettingsPath, repair);
        }

        Parse(await host.Current.StatusAsync(_repo, Branch, string.Empty, string.Empty)).TryGetProperty("error", out _).Should().BeFalse();
        var entry = ErrorOf(await PlanEntryAsync(host));
        if (repair == HumanFile)
        {
            entry.Should().Be(RoundMachine.GateHeld);
            SessionOf(hold).State.HumanGate.Should().BeTrue();
            CardStatus(notice!).Should().Be(EscalationStatuses.Open);
            Said("auto released").Should().BeEmpty();
        }
        else
        {
            entry.Should().Be(LegacyHold.ReleasedSentence(StageWords(SessionOf(hold))));
            AssertReleased(hold, recorded: true, notice, cards);
        }
    }
}
