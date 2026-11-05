using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog.Core;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The deadline a round earns is <c>reviewerTimeout × (1 + follow-ups)</c> — pure arithmetic, so pinned without a process, a clock or a
/// round (E4 S5, C6).
/// </summary>
/// <remarks>
/// <para>This is the half of the old end-to-end test that was ever arithmetic. That test slept 700 ms per turn so that four turns of a 2 s
/// reviewer would overrun a round bounded by ONE turn — a claim about a number, paid for in wall time and lost to a loaded machine
/// (C6). Here the number is read straight off <see cref="RoundDeadline.For"/>: one vendor, one role, so one wave, and the derived
/// whole is exactly one reviewer's own scaled budget (<see cref="RoundBudget.For"/> with a single reviewer returns it unchanged).</para>
/// <para>What this does NOT prove is that the engine HANDS the follow-ups to the derivation — that is
/// <c>AFeatureIsReviewedEndToEndTests.TheRoundsDeadline_IsScaledByTheFollowUps_TheEngineHandsThemToTheDerivation</c>; and where a
/// reviewer's own conversation is cut is the scheduler-level test in <c>AReviewerThatAsksForSourceIsAskedAgainTests</c>.</para>
/// </remarks>
public sealed class RoundDeadlineTests
{
    private static readonly TimeSpan TwoSeconds = TimeSpan.FromSeconds(2);

    private static TimeSpan DeadlineOf(int followUps) => RoundDeadline.For(
        new PanelSettings
        {
            Providers = [new("codex") { Feature = true }],
            ReviewerTimeout = TwoSeconds,
        },
        Stage.FeatureReview, roles: 1, followUps, Logger.None);

    [Fact]
    public void ThreeFollowUps_EarnARoundFourTurnsLong_AndNoFollowUpsEarnOne()
    {
        var withFollowUps = DeadlineOf(followUps: 3);
        var single = DeadlineOf(followUps: 0);

        withFollowUps.Should().Be(TimeSpan.FromSeconds(8), "a 2 s reviewer asked again three times may take 2 s × (1 + 3)");
        single.Should().Be(TwoSeconds, "a single-turn reviewer is bounded by its own 2 s, nothing scaled");
        (withFollowUps / single).Should().Be(4, "the follow-ups multiply the whole by one plus their number");
    }
}
