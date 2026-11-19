using System.Collections.Immutable;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Gate;
using CoaiMcp.Core.Rounds;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The pure half of the hold's authority: which hold policy auto may release, and what the release leaves
/// (<c>research/PLAN_auto_never_asks_a_person_E1_S1_spec.md</c>, § S1.3).
/// </summary>
/// <remarks>
/// A hold from before policy auto is a held session whose config has no <c>autonomous</c> member. Pure: no store, no service, no clock.
/// </remarks>
public sealed class LegacyHoldTests
{
    private static readonly ReviewerSummary Nobody = new(2, 0, ["codex: exit 1", "gemini: exit 1"]);

    /// <summary>A session whose config is "as a build before policy auto wrote it".</summary>
    private static SessionState Before(Stage stage = Stage.PlanReview, bool held = true) =>
        new("s-1", "/repo", "feature/x", new PanelConfig(roles: null, autonomous: null))
        {
            Stage = stage,
            HumanGate = held,
            PlanProceeded = stage != Stage.PlanReview,
        };

    private static PanelConfig Mine(StagePolicy policy) => PanelConfig.Uniform(1, 0, policy);

    private static PanelConfig Auto => PanelConfig.Uniform(1, 0);

    private static Finding F(string title = "finding") =>
        new(Severity.Major, Category.Security, "src/A.cs", 1, title, "why it breaks", "fix", ["codex"]);

    public static TheoryData<Stage> EveryStage() => [Stage.PlanReview, Stage.CodeReview, Stage.DocumentReview, Stage.FeatureReview];

    /// <summary>A held session of the stage's own kind, the way the engine would have saved it.</summary>
    private static SessionState HeldOf(Stage stage, bool resolved)
    {
        var held = Before(stage) with
        {
            AwaitingResolve = !resolved,
            RoundsRunThisStage = stage == Stage.FeatureReview ? 2 : 1,
            HoldQuestions = ["n-1"],
        };

        return stage switch
        {
            Stage.DocumentReview => held with { Document = "docs/a.md" },
            Stage.FeatureReview => held with { Feature = "the feature" },
            _ => held,
        };
    }

    [Fact]
    public void Stands_OnlyForAHeldSessionWhoseConfigPredatesAuto()
    {
        var configs = new Dictionary<string, PanelConfig>
        {
            ["absent"] = new(roles: null, autonomous: null),
            ["false"] = new(roles: null, autonomous: false),
            ["true"] = new(roles: null, autonomous: true, StagePolicy.GoodEnough),
            ["new()"] = new(),
            ["Uniform auto"] = PanelConfig.Uniform(3, 2),
        };
        foreach (var policy in Enum.GetValues<StagePolicy>())
        {
            configs[$"named {policy}"] = new PanelConfig(OnExhausted: policy);
        }

        foreach (var (label, config) in configs)
        {
            var held = new SessionState("s-1", "/repo", "feature/x", config) { HumanGate = true };
            var notHeld = held with { HumanGate = false };

            LegacyHold.Stands(held).Should().Be(label == "absent", $"held, config {label}");
            LegacyHold.Stands(notHeld).Should().BeFalse($"not held, config {label}");
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Released_LeavesTheSessionAsAnAutoVerdictWould(bool resolved)
    {
        var rejection = new PriorRejection(F("argued"), "the caller owns it");
        var held = Before() with
        {
            RoundsRunThisStage = 1,
            EscalationsUsed = 2,
            HoldQuestions = ["n-1", "q-2"],
            AwaitingResolve = !resolved,
            Rejections = [rejection],
        };

        var released = LegacyHold.Released(held, Auto);

        released.HumanGate.Should().BeFalse();
        released.HoldQuestions.Should().BeEmpty();
        released.AwaitingResolve.Should().BeTrue("the next resolve, and no other call, moves the stage — the resolved hold included");
        released.AdvanceOnResolve.Should().BeTrue();
        released.Config.OnExhausted.Should().Be(StagePolicy.GoodEnough);
        released.Config.Autonomous.Should().BeTrue();
        released.RoundsRunThisStage.Should().Be(1);
        released.EscalationsUsed.Should().Be(2);
        released.Rejections.Should().Equal(rejection);
        released.Stage.Should().Be(Stage.PlanReview, "the stage moves in resolve, through the one path that closes the cadence");
    }

    private static Transition BeginOf(SessionState s) => s.Stage switch
    {
        Stage.PlanReview => RoundMachine.BeginPlanRound(s),
        Stage.CodeReview => RoundMachine.BeginCodeRound(s),
        Stage.DocumentReview => RoundMachine.BeginDocumentRound(s),
        _ => RoundMachine.BeginFeatureRound(s),
    };

    [Theory]
    [MemberData(nameof(EveryStage))]
    public void AReleasedHold_ThenAnEmptyResolve_AdvancesTheStage(Stage stage)
    {
        foreach (var resolved in new[] { true, false })
        {
            var label = $"{stage}, resolved {resolved}";
            var released = LegacyHold.Released(HeldOf(stage, resolved), Auto);

            BeginOf(released).Should().BeOfType<Transition.Refused>(label)
                .Which.Sentence.Should().Be(RoundMachine.Unresolved, $"{label}: the machine still has a round to resolve");

            var first = RoundMachine.Resolve(released, []).Should().BeOfType<Transition.Moved>(label).Subject;
            first.State.Stage.Should().Be(Stages.Of(stage).AdvancesTo, label);
            first.State.AdvanceOnResolve.Should().BeFalse(label);
            first.State.AwaitingResolve.Should().BeFalse(label);
            first.State.HumanGate.Should().BeFalse(label);
            if (stage == Stage.PlanReview)
            {
                first.State.PlanProceeded.Should().BeTrue(label);
            }

            RoundMachine.Resolve(first.State, []).Should().BeOfType<Transition.Refused>($"{label}: the stage moves once, not twice");
        }
    }

    [Fact]
    public void TheNaiveRelease_ThatKeepsAwaitingResolveFalse_WouldLetALaterCallHumanRoundAdvance_AndThatIsThePoint()
    {
        // A refuted shape, asserted as the defect it is (the parent's entry 1.2): a resolved hold released without AwaitingResolve
        // leaves AdvanceOnResolve with no resolve to consume it, and the next call_human round advances on a plain resolve.
        var naive = Before() with { AwaitingResolve = false } with { HumanGate = false, HoldQuestions = [], AdvanceOnResolve = true };
        naive = naive with { Config = naive.Config.UnderPolicyOf(Mine(StagePolicy.Human)) };

        var completed = RoundMachine.CompleteRound(naive, GateResult.Empty, Nobody).Should().BeOfType<Transition.Ok>().Subject;
        completed.Verdict.Should().BeOfType<RoundVerdict.CallHuman>();
        completed.State.HumanGate.Should().BeTrue();

        var resolved = RoundMachine.Resolve(completed.State, []).Should().BeOfType<Transition.Moved>(
            "the naive release lets a plain resolve advance a stage whose hold a person has not answered").Subject;
        resolved.State.Stage.Should().Be(Stage.CodeReview);
        resolved.State.HumanGate.Should().BeTrue("and the hold is still there");
    }

    [Fact]
    public void Stamped_FollowsTheSettings_ExceptWhileAHoldFromBeforeAutoStands()
    {
        var notHeld = Before(held: false);
        var held = Before();
        var thisBuildsHold = new SessionState("s-1", "/repo", "feature/x", Mine(StagePolicy.Human)) { HumanGate = true };

        var one = LegacyHold.Stamped(notHeld, Auto);
        one.OnExhausted.Should().Be(StagePolicy.GoodEnough);
        one.Autonomous.Should().BeTrue();

        var two = LegacyHold.Stamped(notHeld, Mine(StagePolicy.Human));
        two.OnExhausted.Should().Be(StagePolicy.Human);
        two.Autonomous.Should().BeFalse();

        LegacyHold.Stamped(held, Auto).Should().BeSameAs(held.Config, "a hold from before auto keeps its config as loaded (F0)");
        LegacyHold.Stamped(held, Mine(StagePolicy.Human)).Should().BeSameAs(held.Config, "even under a policy that is not auto");

        var five = LegacyHold.Stamped(thisBuildsHold, Auto);
        five.OnExhausted.Should().Be(StagePolicy.GoodEnough);
        five.Autonomous.Should().BeTrue();

        var six = LegacyHold.Stamped(thisBuildsHold, Mine(StagePolicy.Escalate));
        six.OnExhausted.Should().Be(StagePolicy.Escalate);
        six.Autonomous.Should().BeFalse();
    }

    [Fact]
    public void ARoundNeverRunsOnAConfigWhoseMarkerIsAbsent()
    {
        var begins = new Dictionary<string, Transition>
        {
            ["plan"] = RoundMachine.BeginPlanRound(HeldOf(Stage.PlanReview, resolved: true)),
            ["code"] = RoundMachine.BeginCodeRound(HeldOf(Stage.CodeReview, resolved: true)),
            ["code again"] = RoundMachine.BeginCodeRoundAgain(HeldOf(Stage.CodeReview, resolved: true)),
            ["document"] = RoundMachine.BeginDocumentRound(HeldOf(Stage.DocumentReview, resolved: true)),
            ["feature"] = RoundMachine.BeginFeatureRound(HeldOf(Stage.FeatureReview, resolved: true)),
            ["feature again"] = RoundMachine.BeginFeatureRoundAgain(HeldOf(Stage.FeatureReview, resolved: true)),
        };
        foreach (var (label, begin) in begins)
        {
            begin.Should().BeOfType<Transition.Refused>($"a held {label} session begins nothing, so no round runs on an absent marker");
        }

        foreach (var stage in Enum.GetValues<Stage>().Where(s => s != Stage.Done))
        {
            var notHeld = Before(stage, held: false);
            LegacyHold.Stamped(notHeld, Mine(StagePolicy.Human)).PredatesAuto.Should().BeFalse($"{stage}: a session that is not held is stamped before its begin");
        }
    }
}
