using CoaiMcp.Core.Cadence;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Whether the work has been released to the stage environment, INFERRED by the server and never declared by
/// the AI (<c>todo/PLAN_question_consultant.md</c> D7, A7): every epic of the plan closed in the cadence state, or
/// — without epics — the session reached <c>Done</c> after its code round. Unknown is not released.
/// </summary>
public sealed class StageReleaseTests
{
    private static PersistedSession Session(bool proceeded = true, Stage stage = Stage.CodeReview, string plan = "", string epic = "") =>
        new(new SessionState("s-1", "D:/repo", "main", PanelConfig.Uniform(3, 2)) { PlanProceeded = proceeded, Stage = stage }, [])
        {
            Plan = plan,
            Epic = epic,
            CadenceRepoId = plan.Length > 0 ? "d:/repo/.git" : string.Empty,
        };

    private static CadenceState Closed(params int[] epics) =>
        epics.Aggregate(new CadenceState { Plan = "todo/PLAN_x.md" }, (state, epic) => state.WithClosed(epic, "proceed", "t"));

    private static Func<string, string, CadenceState?> Cadence(CadenceState? state) => (_, _) => state;

    [Fact]
    public void EveryEpicClosed_IsReleased() =>
        StageRelease.Of(Session(plan: "todo/PLAN_x.md", epic: "3/3"), Cadence(Closed(1, 2, 3))).Should().BeTrue();

    [Fact]
    public void AnEpicStillOpen_IsNotReleased() =>
        StageRelease.Of(Session(plan: "todo/PLAN_x.md", epic: "3/3"), Cadence(Closed(1, 3))).Should().BeFalse(
            "epic 2 has not been through its code gate; a plan is released when the whole of it has");

    [Fact]
    public void TheLastEpicStillOpen_IsNotReleased_HoweverManyBefore() =>
        StageRelease.Of(Session(plan: "todo/PLAN_x.md", epic: "3/3"), Cadence(Closed(1, 2))).Should().BeFalse();

    [Fact]
    public void APlanThatContinuesAnother_IsReleasedWhenItsOwnEpicsAreClosed() =>
        // Private repo A's second plan numbers its epics 5 to 14; its cadence record holds only its own closes.
        StageRelease.Of(Session(plan: "todo/PLAN_y.md", epic: "7/7"), Cadence(Closed(5, 6, 7))).Should().BeTrue();

    [Fact]
    public void NothingClosedYet_IsNotReleased() =>
        StageRelease.Of(Session(plan: "todo/PLAN_x.md", epic: "1/3"), Cadence(new CadenceState())).Should().BeFalse();

    [Fact]
    public void WithoutEpics_DoneAfterTheCodeRound_IsReleased() =>
        StageRelease.Of(Session(stage: Stage.Done), Cadence(null)).Should().BeTrue(
            "one gate for the whole task: its code round resolved is the work shipped");

    [Fact]
    public void WithoutEpics_StillInTheCodeStage_IsNotReleased() =>
        StageRelease.Of(Session(stage: Stage.CodeReview), Cadence(null)).Should().BeFalse();

    [Fact]
    public void APlanDeclaredWithoutAnEpic_FollowsTheSessionsStage()
    {
        // One plan gate for the whole task declares the plan and no epic (the cadence desk admits that): the
        // release is then the session's Done, and the cadence record — which holds no closes — is not asked.
        StageRelease.Of(Session(stage: Stage.Done, plan: "todo/PLAN_x.md"), Cadence(new CadenceState())).Should().BeTrue();
        StageRelease.Of(Session(stage: Stage.CodeReview, plan: "todo/PLAN_x.md"), Cadence(new CadenceState())).Should().BeFalse();
    }

    [Fact]
    public void NoSession_IsNotReleased() =>
        StageRelease.Of(null, Cadence(Closed(1, 2, 3))).Should().BeFalse("unknown is not released — erring toward the consultant");

    [Fact]
    public void ACadenceRecordThatCannotBeRead_IsNotReleased() =>
        StageRelease.Of(Session(plan: "todo/PLAN_x.md", epic: "3/3"), Cadence(null)).Should().BeFalse(
            "a record that could not be read is unknown, and unknown is not released");

    [Fact]
    public void ADoneSessionWhoseEpicsAreNotAllClosed_IsNotReleased() =>
        // One session per epic ends at Done when ITS epic closes; the plan is released when all of them have.
        StageRelease.Of(Session(stage: Stage.Done, plan: "todo/PLAN_x.md", epic: "2/3"), Cadence(Closed(1, 2))).Should().BeFalse();
}
