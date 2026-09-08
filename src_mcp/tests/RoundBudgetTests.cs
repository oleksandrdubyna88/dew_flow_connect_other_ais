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
}
