using CoaiMcp.Core.Rounds;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A round's deadline, derived rather than chosen.
/// </summary>
/// <remarks>
/// <para>The operator asked for a max-time-per-ROUND setting, and the obvious mistake would look
/// exactly like a bug in the gate: a default below what a healthy round takes would cancel reviewers
/// mid-answer, lose their findings, and produce a verdict nobody could explain.</para>
/// <para>The arithmetic is not subtle and that is the point. A round runs `vendors × roles`
/// reviewers through a machine-wide cap, so it takes as many WAVES as that division needs, and each
/// wave can legitimately last a whole reviewer timeout. At the shipped defaults — three vendors,
/// four code roles, a cap of three, ten minutes each — that is four waves: <b>forty minutes of
/// entirely healthy work</b>.</para>
/// <para>So the default is computed from the three numbers that decide it, and this file is the
/// guard that it can never fall below one full wave — the case where the bound would cancel a
/// reviewer that had not even finished its first attempt.</para>
/// </remarks>
public sealed class RoundBudgetTests
{
    private static readonly TimeSpan TenMinutes = TimeSpan.FromMinutes(10);

    [Fact]
    public void TheShippedShape_IsFourWaves()
    {
        // Three vendors, four code roles, a cap of three: twelve reviewers, four at a time... no —
        // three at a time, so four waves. Written out because this number is the whole reason the
        // setting is derived rather than typed.
        RoundBudget.For(TenMinutes, reviewers: 12, concurrency: 3)
            .Should().Be(TimeSpan.FromMinutes(40));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 1)]
    [InlineData(12, 3)]
    [InlineData(12, 5)]
    [InlineData(2, 8)]
    [InlineData(40, 3)]
    public void ItNeverFallsBelowOneFullWave(int reviewers, int concurrency)
    {
        // The trap, asserted over the shapes a real panel produces: however few reviewers there are
        // and however wide the machine is, a round must always be allowed at least one reviewer's
        // whole deadline. A bound under that cancels a reviewer that is still on its first attempt.
        RoundBudget.For(TenMinutes, reviewers, concurrency)
            .Should().BeGreaterThanOrEqualTo(TenMinutes);
    }

    [Fact]
    public void AWiderMachine_FinishesInFewerWaves()
    {
        // The property that makes it a derivation rather than a fudge factor: raising the machine's
        // cap really does shorten the round it allows.
        RoundBudget.For(TenMinutes, reviewers: 12, concurrency: 6)
            .Should().BeLessThan(RoundBudget.For(TenMinutes, reviewers: 12, concurrency: 3));
    }

    [Fact]
    public void NonsenseNumbers_DoNotProduceANonsenseDeadline()
    {
        // A zero or negative cap is a configuration mistake, not a way to make a round infinite or
        // instantaneous. Both floor at one reviewer, one wave.
        RoundBudget.For(TenMinutes, reviewers: 0, concurrency: 0).Should().Be(TenMinutes);
        RoundBudget.For(TenMinutes, reviewers: -4, concurrency: -1).Should().Be(TenMinutes);
    }

    /// <summary>
    /// A derivation has a ceiling, because a round nobody is waiting for is not a round.
    /// </summary>
    /// <remarks>
    /// An hour per reviewer at a concurrency of one and twenty reviewers derives twenty hours. The
    /// person who set those numbers did not ask for a day-long round, and one holds a worktree and a
    /// session while nobody watches — a reviewer on the plan round called it a zombie, which is the
    /// right word. The ceiling is generous on purpose: it bounds the absurd without arguing with the
    /// merely patient.
    /// </remarks>
    [Fact]
    public void ADerivationCannotExceedTheCeiling()
    {
        RoundBudget.For(TimeSpan.FromHours(1), reviewers: 20, concurrency: 1)
            .Should().Be(RoundBudget.Ceiling);
    }

    [Fact]
    public void TheCeilingDoesNotTouchAnOrdinaryRound()
    {
        // The guard on the other side: a ceiling that clipped real configurations would be a limit
        // pretending to be a safety net.
        RoundBudget.For(TenMinutes, reviewers: 12, concurrency: 3)
            .Should().BeLessThan(RoundBudget.Ceiling);
        RoundBudget.For(TimeSpan.FromMinutes(30), reviewers: 12, concurrency: 3)
            .Should().BeLessThan(RoundBudget.Ceiling);
    }

    [Fact]
    public void TheCeilingIsStillAtLeastOneWave_ForALongReviewerTimeout()
    {
        // The two bounds meeting: a reviewer timeout above the ceiling would floor and cap at once,
        // and the floor must win — cancelling a reviewer before its first attempt is the one
        // outcome neither bound is for.
        var patient = RoundBudget.Ceiling + TimeSpan.FromHours(1);

        RoundBudget.For(patient, reviewers: 1, concurrency: 1)
            .Should().Be(patient,
                "a single reviewer allowed longer than the ceiling is a deliberate setting, and "
                + "capping under it would cancel that reviewer before its first attempt");
    }

    /// <summary>
    /// The two paths a budget can come from, and the floor that belongs to only one of them.
    /// </summary>
    /// <remarks>
    /// The first draft floored EVERYTHING at one reviewer timeout, including a number a person had
    /// typed — so an explicit five minutes silently became ten, while the panel warned that
    /// reviewers would be cut off. A setting ignored and a warning that lied about the same number.
    /// Two reviewers on the code round caught it.
    /// </remarks>
    [Fact]
    public void TheFloorBelongsToTheDERIVATION_NotToANumberSomebodyTyped()
    {
        // The derivation cannot produce a budget too small to finish one reviewer...
        RoundBudget.For(TenMinutes, reviewers: 1, concurrency: 99)
            .Should().BeGreaterThanOrEqualTo(TenMinutes);

        // ...and there is no path through it that returns less than the reviewer timeout it was
        // given, which is the property the caller relies on when it stops flooring.
        foreach (var reviewers in (int[])[0, 1, 2, 12, 40])
        {
            RoundBudget.For(TenMinutes, reviewers, concurrency: 100)
                .Should().BeGreaterThanOrEqualTo(TenMinutes, $"{reviewers} reviewers on a wide machine");
        }
    }

    [Fact]
    public void APatientReviewerAndAWideRound_DoNotOverflowOnTheWayToTheCeiling()
    {
        // `TimeSpan.op_Multiply` throws on overflow, so the check has to happen BEFORE the
        // multiplication — otherwise the derivation crashes on its way to the ceiling that exists
        // to catch exactly this. Raised on the code round.
        var patient = TimeSpan.FromMinutes(int.MaxValue);

        var budget = RoundBudget.For(patient, reviewers: 1000, concurrency: 1);

        budget.Should().BeGreaterThan(TimeSpan.Zero, "it returns a number rather than throwing");
    }

    /// <summary>
    /// A budget no timer can express is not a longer deadline — it is a crash before the round.
    /// </summary>
    /// <remarks>
    /// `CancellationTokenSource` takes its delay in milliseconds as an int, so it refuses anything
    /// past about 24.8 days. `COAI_ROUND_TIMEOUT_MINUTES=80000` is 55 days, and it would have thrown
    /// before a single reviewer started: a configuration value crashing the round it was meant to
    /// bound. Raised on the code round.
    /// </remarks>
    [Fact]
    public void ABudgetPastWhatATimerHolds_IsBroughtInside()
    {
        RoundBudget.Expressible(TimeSpan.FromMinutes(80_000)).Should().Be(RoundBudget.TimerMaximum);
        RoundBudget.Expressible(TimeSpan.FromDays(365)).Should().Be(RoundBudget.TimerMaximum);
    }

    [Fact]
    public void AnOrdinaryBudget_PassesThroughUntouched()
    {
        // The guard on the other side: a clamp that moved real numbers would be a second policy
        // wearing a platform limit's clothes.
        RoundBudget.Expressible(TenMinutes).Should().Be(TenMinutes);
        RoundBudget.Expressible(RoundBudget.Ceiling).Should().Be(RoundBudget.Ceiling);
    }

    [Fact]
    public void TheTimerMaximum_IsSomethingACancellationTokenSourceAccepts()
    {
        // Asserted against the real type rather than against a number I believe about it: the
        // constant exists to keep this constructor from throwing, so this is the only check of it
        // that means anything.
        var act = () =>
        {
            using var source = new CancellationTokenSource(RoundBudget.TimerMaximum);
        };

        act.Should().NotThrow();
    }
}
