using CoaiMcp.Core.Cadence;
using CoaiMcp.Core.Commands;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Where a caller says it is in a plan, and what that plan owes the consultant
/// (<c>todo/PLAN_consult_on_a_cadence.md</c>, epic 1).
/// </summary>
/// <remarks>
/// Pure arithmetic, so every rule the operator gave on 2026-09-25 is a test rather than a sentence:
/// one consultation per group of three epics, the risk question at five, nothing past fourteen.
/// </remarks>
public sealed class CadenceArithmeticTests
{
    private const string Plan = "todo/PLAN_x.md";

    [Fact]
    public void NoEpicAndNoPlan_IsNowhere() =>
        EpicRef.Parse("", "").Should().BeOfType<EpicRef.None>("a caller outside a split plan owes nothing");

    [Theory]
    [InlineData("5/14", 5, 14)]
    [InlineData(" 7 / 9 ", 7, 9)]
    [InlineData("1/1", 1, 1)]
    public void AnEpicReadsAsItsNumberAndThePlansLast(string epic, int number, int last)
    {
        var parsed = EpicRef.Parse(epic, Plan).Should().BeOfType<EpicRef.Some>().Subject;

        parsed.Number.Should().Be(number);
        parsed.Last.Should().Be(last);
        parsed.Plan.Should().Be(Plan);
    }

    [Theory]
    [InlineData("5-14")]
    [InlineData("five of fourteen")]
    [InlineData("5/")]
    [InlineData("/14")]
    public void AMalformedEpic_IsRefusedNamingTheShape(string epic) =>
        EpicRef.Parse(epic, Plan).Should().BeOfType<EpicRef.Refused>()
            .Which.Sentence.Should().Contain("'k/N'").And.Contain($"'{epic}'");

    [Theory]
    // Epic 1's code round: .NET's \d is every Unicode decimal digit, so these passed the pattern and
    // then threw inside int.Parse — an exception where a refusal was owed.
    [InlineData("٥/١٤")]
    [InlineData("５/１４")]
    public void NonAsciiDigits_AreRefused_NotThrown(string epic) =>
        EpicRef.Parse(epic, Plan).Should().BeOfType<EpicRef.Refused>().Which.Sentence.Should().Contain("'k/N'");

    [Theory]
    [InlineData("5/14", "")]
    [InlineData("", Plan)]
    public void APlanWithoutAnEpic_OrTheOtherWayRound_IsRefused(string epic, string plan) =>
        EpicRef.Parse(epic, plan).Should().BeOfType<EpicRef.Refused>()
            .Which.Sentence.Should().Contain("together");

    [Theory]
    [InlineData("0/5")]
    [InlineData("6/5")]
    public void EpicZeroOrPastTheLast_IsRefused(string epic) =>
        EpicRef.Parse(epic, Plan).Should().BeOfType<EpicRef.Refused>()
            .Which.Sentence.Should().Contain("between 1 and 5");

    [Fact]
    public void FifteenEpics_IsRefusedWithSplitThePlan()
    {
        var refused = CadenceRule.RefuseIfTooMany(epicCount: 15, Plan);

        refused.Should().Contain("15 epics").And.Contain("two plans").And.Contain(Plan);
        CadenceRule.RefuseIfTooMany(epicCount: 14, Plan).Should().BeEmpty("fourteen is the most, not one too many");
    }

    [Theory]
    [InlineData(1, 3, 1, 3)]
    [InlineData(3, 3, 1, 3)]
    [InlineData(4, 3, 4, 6)]
    [InlineData(7, 3, 7, 9)]
    [InlineData(7, 4, 5, 8)]
    [InlineData(5, 1, 5, 5)]
    public void AnEpicBelongsToTheGroupOfEveryEpicsItFallsIn(int epic, int every, int first, int last) =>
        CadenceRule.GroupOf(epic, every).Should().Be(new EpicGroup(first, last));

    [Theory]
    [InlineData(2, 1)]
    [InlineData(3, 1)]
    [InlineData(4, 2)]
    [InlineData(6, 2)]
    [InlineData(14, 5)]
    public void APlanNumberedOneToN_OwesOneConsultationPerGroup(int epics, int owed) =>
        CadenceRule.GroupsOwed(Enumerable.Range(1, epics), every: 3).Should().HaveCount(owed);

    [Fact]
    public void APlanThatContinuesAnother_IsGroupedFromItsOwnFirstEpic()
    {
        // email-service's PLAN_first_application_live numbers its epics 5-14, continuing the plan
        // before it. Its groups start where IT starts (epic 1's plan round, codex): a group 4-6 would
        // ask the consultant about an epic that is in another file.
        var groups = CadenceRule.GroupsOwed(Enumerable.Range(5, 10), every: 3);

        groups.Should().Equal(new EpicGroup(5, 7), new EpicGroup(8, 10), new EpicGroup(11, 13), new EpicGroup(14, 14));
    }

    [Theory]
    [InlineData(5, 5, 7)]
    [InlineData(6, 5, 7)]
    [InlineData(8, 8, 10)]
    [InlineData(14, 14, 16)]
    public void InAContinuingPlan_AnEpicsGroupIsCountedFromThePlansFirst(int epic, int first, int last) =>
        CadenceRule.GroupOf(epic, every: 3, firstEpic: 5).Should().Be(new EpicGroup(first, last));

    [Theory]
    // Epic 1's code round (gemini): integer division truncates toward zero, so an epic BEFORE the
    // plan's first got a group that did not hold it.
    [InlineData(4, 2, 4)]
    [InlineData(2, 2, 4)]
    [InlineData(1, -1, 1)]
    public void AnEpicBeforeThePlansFirst_StillFallsInItsOwnGroup(int epic, int first, int last)
    {
        var group = CadenceRule.GroupOf(epic, every: 3, firstEpic: 5);

        group.Should().Be(new EpicGroup(first, last));
        group.Holds(epic).Should().BeTrue();
    }

    [Fact]
    public void AGroupIsCutAtThePlansLastEpic() =>
        CadenceRule.GroupsOwed([1, 2, 3, 4], every: 3).Should().Equal(new EpicGroup(1, 3), new EpicGroup(4, 4));

    [Fact]
    public void EveryIsNeverBelowOne() =>
        CadenceRule.GroupOf(5, every: 0).Should().Be(new EpicGroup(5, 5), "a group of zero epics would owe nothing ever");

    [Theory]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(14, true)]
    public void TheRiskQuestionIsAskedFromTheThreshold(int epics, bool asked) =>
        CadenceRule.AsksForRisk(epics, threshold: 5).Should().Be(asked);

    [Theory]
    [InlineData(1, 1, "1")]
    [InlineData(4, 6, "4-6")]
    public void AGroupSaysItsRangeTheWayConsultTakesIt(int first, int last, string range) =>
        new EpicGroup(first, last).Range.Should().Be(range);

    [Fact]
    public void ARecordWithoutRiskItems_ReadsAsAnEmptyList()
    {
        // A record written before the field existed, or with an explicit null, must not throw in the
        // gate that reads it.
        var state = new CadenceState { RiskItems = null!, Closed = null! };

        state.RiskItems.Should().BeEmpty();
        state.Closed.Should().BeEmpty();
        state.RiskAnswered.Should().BeFalse();
    }

    [Fact]
    public void ClosingAnEpicTwice_RecordsItOnce_WithTheFirstVerdict()
    {
        // Reconciliation calls this on every review_code and status (the epic-1-3 consultation, point 5),
        // so it must be idempotent — and a checkpoint that closes the epic early is not overwritten.
        var once = new CadenceState().WithClosed(4, "proceed", "2026-09-25T10:00:00Z");
        var twice = once.WithClosed(4, "good_enough", "2026-09-25T11:00:00Z");

        twice.Closed.Should().ContainSingle().Which.Should().Be(new ClosedEpic(4, "proceed", "2026-09-25T10:00:00Z"));
        twice.IsClosed(4).Should().BeTrue();
        twice.IsClosed(5).Should().BeFalse();
    }

    [Fact]
    public void ClosedEpicsStayInNumberOrder() =>
        new CadenceState().WithClosed(7, "proceed", "t").WithClosed(2, "proceed", "t")
            .Closed.Select(c => c.Number).Should().Equal(2, 7);

    [Fact]
    public void ARiskItemIsKeyedByItsEpicAndStory()
    {
        new RiskItem(7, "7.2", "moves money").Key.Should().Be("7/7.2");
        new RiskItem(7, "", "the migration").Key.Should().Be("7");
    }

    [Fact]
    public void ThePlanKeyIsTheFileName_SoPromotionKeepsIt()
    {
        // Gate round 1 (gemini): a plan moves from todo/ to research/ when it is finished, and an
        // unfinished tail may still be gated after it.
        EpicRef.PlanKey("todo/PLAN_x.md").Should().Be(EpicRef.PlanKey("research\\PLAN_x.md"));
        EpicRef.PlanKey("todo/PLAN_X.md").Should().Be(EpicRef.PlanKey("todo/plan_x.md"), "Windows paths are case-blind");
        EpicRef.PlanKey("todo/PLAN_x.md").Should().NotBe(EpicRef.PlanKey("todo/PLAN_y.md"));
    }
}
