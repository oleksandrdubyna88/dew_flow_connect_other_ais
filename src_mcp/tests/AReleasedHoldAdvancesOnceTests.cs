using CoaiMcp.Core.Cadence;
using CoaiMcp.Core.Commands;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Context;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// One <c>resolve</c> after a release advances the stage exactly once, in each of the four stages, with the decisions kept and — for a code stage —
/// the cadence closed once (<c>todo/PLAN_auto_never_asks_a_person_E1_S1_spec.md</c>, § S1.10.12; D10, J11).
/// </summary>
/// <remarks>
/// <c>PanelService</c> level; every hold is raised by the real code under <c>human</c> (<see cref="HoldFromBeforeAutoFixture.RaiseAsync"/>).
/// <i>Resolved</i> means the caller decided the held round before the strip (a reasoned reject; the hold stays and no round awaits a resolve);
/// <i>not resolved</i> means its finding is still pending, so the one resolve must pass its decisions — <c>PanelService</c> refuses an empty one.
/// </remarks>
[Collection("fakecli-env")]
public sealed class AReleasedHoldAdvancesOnceTests : HoldFromBeforeAutoFixture
{
    public static TheoryData<Kind, bool> Rows()
    {
        var rows = new TheoryData<Kind, bool>();
        foreach (var kind in AllKinds)
        {
            rows.Add(kind, true);
            rows.Add(kind, false);
        }

        return rows;
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task OneResolveAfterARelease_AdvancesTheStageExactlyOnce(Kind kind, bool resolved)
    {
        var hold = await RaiseAsync(kind, resolved);
        var held = SessionOf(hold).Rounds[^1];
        StripTheMarker(hold, recorded: true);
        var auto = AutoService(kind);
        ErrorOf(await StatusAsync(auto, hold)).Should().BeEmpty();
        SessionOf(hold).State.AwaitingResolve.Should().BeTrue("the status call released it");

        var reply = await TheOneResolveAsync(auto, hold, resolved);

        ErrorOf(reply).Should().BeEmpty(reply);
        var expected = kind == Kind.Plan ? Stage.CodeReview : Stage.Done;
        var after = SessionOf(hold);
        after.State.Stage.Should().Be(expected);
        after.State.HumanGate.Should().BeFalse();
        after.State.AwaitingResolve.Should().BeFalse();
        after.State.AdvanceOnResolve.Should().BeFalse();
        after.Pending.Should().BeEmpty();
        after.State.Rejections.Should().HaveCount(1, "the rejection made before the release, or in the one resolve, is kept");
        after.Rounds[^1].Verdict.Should().Be("call_human");
        after.Rounds[^1].Note.Should().Be(LegacyHold.Note);
        var where = $"session_id = '{after.State.SessionId}' AND stage = '{held.Stage}' AND number = {held.Number}";
        Column($"SELECT rejected FROM rounds WHERE {where}").Should().Equal("1");
        Column($"SELECT resolution || '|' || reason FROM findings WHERE round_id = (SELECT id FROM rounds WHERE {where})")
            .Should().Equal("reject|the timeout lives in the caller");

        var again = ErrorOf(await ResolveAsync(auto, hold, "[]"));

        again.Should().Contain("no completed round awaiting decisions");
        SessionOf(hold).State.Stage.Should().Be(expected, "the stage moved once, not twice");

        await TheStagesOwnAfterwardsAsync(kind, auto, hold, after);
    }

    /// <summary>
    /// The one resolve of the row. Resolved: the caller decided the held round before the strip, so an empty list does it. Not resolved: its finding is
    /// still pending, so an empty list is refused with no byte changed, and the decisions come in the one that advances.
    /// </summary>
    private async Task<string> TheOneResolveAsync(PanelService auto, Hold hold, bool resolved)
    {
        if (resolved)
        {
            return await ResolveAsync(auto, hold, "[]");
        }

        var bytes = SessionBytes(hold);
        ErrorOf(await ResolveAsync(auto, hold, "[]")).Should().Contain("await a decision");
        SessionBytes(hold).Should().Be(bytes, "the refusal changes no byte");

        return await ResolveAsync(auto, hold, RejectIt);
    }

    /// <summary>What only one stage can show after its resolve: a plan opens the code gate, a feature review's third round is refused.</summary>
    private async Task TheStagesOwnAfterwardsAsync(Kind kind, PanelService auto, Hold hold, PersistedSession after)
    {
        if (kind == Kind.Plan)
        {
            after.State.PlanProceeded.Should().BeTrue();
            Script(Clean);
            ErrorOf(await auto.ReviewCodeAsync(_repo, hold.Branch, "main", CodeRoundHarness.Scope)).Should().NotContain("plan gate comes first");
        }

        if (kind == Kind.Feature)
        {
            after.State.RoundsRunThisStage.Should().Be(2);
            ErrorOf(await ReviewEntryAsync(auto, hold)).Should().NotBeEmpty("a third round is refused as today");
        }
    }

    // ---------- T57: the code stage closes its epic on that resolve, once ----------

    private const string CadencePlan = "todo/PLAN_x.md";

    private const string CadenceScope =
        "Implements an epic of todo/PLAN_x.md: the parser change in app.cs, with its tests, as that epic describes. "
        + "Done when the parser counts two fields as two and the suite is green; constrained to app.cs and its tests, "
        + "with no change to the public shape the other epics build on.";

    private static readonly CadenceArgs Epic = new(CadencePlan, "1/6", string.Empty, string.Empty);

    private PanelSettings Cadenced(bool human) => SettingsOf(Kind.Plan, human) with { CadenceMode = CadenceMode.Require, ConsultEnabled = true };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AReleasedCodeHold_ClosesItsEpicOnThatResolve_Once(bool resolved)
    {
        Directory.CreateDirectory(Path.Combine(_repo, "todo"));
        await File.WriteAllTextAsync(
            Path.Combine(_repo, "todo", "PLAN_x.md"),
            "# PLAN — generated\n\n> Status: plan only.\n\n" + string.Concat(Enumerable.Range(1, 6).Select(epic => $"### Epic {epic} — piece {epic}\n\ntext\n\n")));
        await Git("add", ".");
        await Git("commit", "-m", "the plan");
        var human = ServiceFor(Cadenced(human: true));
        var repoId = await new ContextAssembler(_launcher).CommonDirAsync(_repo, TestContext.Current.CancellationToken);
        human.Consultations.Write(new ConsultationRecord(
            ConsultationStore.NewId(), "caller", "claude", "no-session", _repo, Branch, "", "codex", "", "codex",
            ConsultationMemories.VendorRemembers, 5, "2026-09-25T10:00:00Z")
        {
            Status = ConsultationStatuses.Closed,
            Outcome = ConsultationOutcomes.Solved,
            EndedUtc = "2026-09-25T10:05:00Z",
            Kind = "cadence",
            Plan = CadencePlan,
            Epics = "1-3",
            RepoId = repoId,
        });
        await human.OpenAsync(_repo, Branch);
        Script(Clean);
        Parse(await human.ReviewPlanAsync(_repo, Branch, CadenceScope, Epic)).GetProperty("verdict").GetString().Should().Be("proceed");
        ErrorOf(await human.ResolveAsync(_repo, Branch, "[]")).Should().BeEmpty();
        Write("app.cs", "v2\n");
        await Git("add", "-A");
        await Git("commit", "-m", "work");
        Script(OneBlockingFinding);
        Verdict(await human.ReviewCodeAsync(_repo, Branch, "main", CadenceScope, false, Epic)).Should().Be("call_human");
        if (resolved)
        {
            ErrorOf(await human.ResolveAsync(_repo, Branch, RejectIt)).Should().BeEmpty();
        }

        var hold = new Hold(Kind.Code, Branch, string.Empty, string.Empty);
        StripTheMarker(hold, recorded: true);
        var auto = new PanelService(Cadenced(human: false), VaultKeys.None("no vault in tests"), default, _launcher, AuditLog, Noticing.None);

        var released = Parse(await auto.StatusAsync(_repo, Branch, string.Empty, CadencePlan));

        released.GetProperty("cadence").GetProperty("epicsClosed").EnumerateArray().Select(e => e.GetInt32()).Should().NotContain(1, "the stage has not advanced yet");

        ErrorOf(await auto.ResolveAsync(_repo, Branch, resolved ? "[]" : RejectIt)).Should().BeEmpty();

        var closed = Parse(await auto.StatusAsync(_repo, Branch, string.Empty, CadencePlan)).GetProperty("cadence").GetProperty("epicsClosed");
        closed.EnumerateArray().Select(e => e.GetInt32()).Should().Equal([1], "the epic is closed exactly once");
        var state = new CadenceStore(_data).Load(SessionOf(hold).CadenceRepoId, CadencePlan);
        state.Closed.Should().ContainSingle().Which.Verdict.Should().Be("call_human", "the held round's verdict is what the cadence records");
    }

    // ---------- T58: nothing is left behind for a later hold to advance on ----------

    [Fact]
    public async Task AfterTheOneResolve_ALaterCallHumanRound_DoesNotAdvanceOnAPlainResolve()
    {
        var hold = await RaiseAsync(Kind.Plan, resolved: true);
        var old = NoticeIdOf(hold);
        StripTheMarker(hold, recorded: true);
        var auto = AutoService();
        ErrorOf(await StatusAsync(auto, hold)).Should().BeEmpty();
        StageOf(await ResolveAsync(auto, hold, "[]")).Should().Be("CodeReview");
        Write("app.cs", "v2\n");
        await Git("add", "-A");
        await Git("commit", "-m", "the change");
        var human = HumanService();
        Script(CodeRoundHarness.OneMajor);
        Verdict(await human.ReviewCodeAsync(_repo, Branch, "main", CodeRoundHarness.Scope)).Should().Be("call_human");

        var reply = await human.ResolveAsync(_repo, Branch, RejectIt);

        ErrorOf(reply).Should().BeEmpty();
        var after = SessionOf(hold);
        after.State.Stage.Should().Be(Stage.CodeReview, "a plain resolve does not move a stage that is held");
        after.State.HumanGate.Should().BeTrue();
        var fresh = after.State.HoldQuestions.Should().ContainSingle().Subject;
        fresh.Should().NotBe(old);
        CardStatus(fresh).Should().Be(EscalationStatuses.Open);
    }

    private void Write(string path, string text)
    {
        var full = Path.Combine(_repo, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }
}
