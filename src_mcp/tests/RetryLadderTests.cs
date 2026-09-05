using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The ladder a rate-limited reviewer climbs, and the two things that stop it: a spent ladder and
/// a deadline it would outrun.
/// </summary>
/// <remarks>
/// Pure, so every property here is a table rather than a wall-clock observation — the jitter
/// especially, which is exactly the kind of thing a test would otherwise assert by sleeping and
/// then fail on a loaded runner.
/// </remarks>
public sealed class RetryLadderTests
{
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);

    [Fact]
    public void TheDefaultLadder_IsFiveThirtySixtyAndTwoMinutes()
    {
        RetryLadder.Default.Should().Equal(
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(60),
            TimeSpan.FromSeconds(120));
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(1, 30)]
    [InlineData(2, 60)]
    [InlineData(3, 120)]
    public void EachAttempt_TakesItsOwnStep(int attempt, int seconds)
    {
        // roll 0.5 is the middle of the jitter band, so the step arrives unmodified.
        RetryLadder.NextWait(attempt, RetryLadder.Default, roll: 0.5, elapsed: TimeSpan.Zero, budget: Hour)
            .Should().Be(TimeSpan.FromSeconds(seconds));
    }

    [Fact]
    public void AspentLadder_HasNoNextStep()
    {
        RetryLadder.NextWait(4, RetryLadder.Default, roll: 0.5, elapsed: TimeSpan.Zero, budget: Hour)
            .Should().BeNull("four steps mean four waits, and the fifth failure is the answer");
    }

    /// <summary>
    /// The whole point of the jitter: nine reviewers of one round hit the same limit at the same
    /// instant, and without it they would all retry at the same instant too.
    /// </summary>
    [Theory]
    [InlineData(0.0, 24)]     // the bottom of the band: 30s - 20%
    [InlineData(0.5, 30)]
    [InlineData(1.0, 36)]     // the top: 30s + 20%
    public void Jitter_SpreadsTheStepByAFifthEitherWay(double roll, int seconds)
    {
        RetryLadder.NextWait(1, RetryLadder.Default, roll, TimeSpan.Zero, Hour)
            .Should().Be(TimeSpan.FromSeconds(seconds));
    }

    [Fact]
    public void EveryRoll_StaysInsideTheBand()
    {
        for (var i = 0; i <= 100; i += 1)
        {
            var wait = RetryLadder.NextWait(1, RetryLadder.Default, i / 100.0, TimeSpan.Zero, Hour);

            // The band IS "the step, give or take a fifth" — six seconds either side of thirty.
            wait!.Value.Should().BeCloseTo(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(6));
        }
    }

    /// <summary>
    /// The budget is WALL CLOCK since the first launch, not the sum of the waits — the launches
    /// are most of it. A reviewer that has already spent its deadline failing does not then wait
    /// two minutes to fail once more.
    /// </summary>
    /// <remarks>
    /// Raised by codex, gemini and the local model on this change's plan round, each from a
    /// different angle: waits alone fit a 60 s budget while the attempts that produced them have
    /// already taken 95 s.
    /// </remarks>
    [Fact]
    public void AstepThatWouldOutrunTheDeadline_IsNotTaken()
    {
        RetryLadder.NextWait(
                attempt: 2,
                RetryLadder.Default,
                roll: 0.5,
                elapsed: TimeSpan.FromSeconds(50),
                budget: TimeSpan.FromSeconds(60))
            .Should().BeNull("50s gone of a 60s deadline leaves no room for a 60s wait");
    }

    [Fact]
    public void TimeAlreadySpentLaunching_CountsAgainstTheBudget()
    {
        var steps = new[] { TimeSpan.FromSeconds(5) };

        RetryLadder.NextWait(0, steps, 0.5, TimeSpan.FromSeconds(58), TimeSpan.FromSeconds(60))
            .Should().BeNull("two attempts of 29s each leave no room, however small the step");
        RetryLadder.NextWait(0, steps, 0.5, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(60))
            .Should().Be(TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData("5,30,60,120", new[] { 5, 30, 60, 120 })]
    [InlineData("5, 30", new[] { 5, 30 })]
    [InlineData("15", new[] { 15 })]
    public void Parse_ReadsTheStepsInSeconds(string csv, int[] expected)
    {
        RetryLadder.Parse(csv).Should().Equal(expected.Select(s => TimeSpan.FromSeconds(s)));
    }

    /// <summary>
    /// A half-parsed ladder is worse than no ladder: it would silently be a DIFFERENT policy from
    /// the one somebody wrote down, and nothing would say so. The caller falls back and reports it.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nonsense")]
    [InlineData("5,nonsense,60")]
    [InlineData("5,0,60")]
    [InlineData("5,-30")]
    public void Parse_RefusesAnythingItCannotReadWhole(string csv)
    {
        RetryLadder.Parse(csv).Should().BeEmpty();
    }
}
