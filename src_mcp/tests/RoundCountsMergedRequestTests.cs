using System.Diagnostics;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The request <c>--round-counts-merged</c> reads off its arguments (E6.S1 of <c>todo/PLAN_the_round_counter_tail_E6.md</c>), without a
/// process: the cap on snapshot paths is a cap on what is READ from a possibly slow share, and it is enforced on what was GIVEN.
/// </summary>
public sealed class RoundCountsMergedRequestTests
{
    private static string Absolute(int n) => Path.Combine(Path.GetTempPath(), "testsToDelete-request", $"{n}.json");

    private static string[] Args(IEnumerable<string> paths) => ["--round-counts-merged", .. paths];

    private static readonly TimeProvider Clock = new FixedClock(new DateTimeOffset(2026, 10, 21, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public void TenThousandCopiesOfOnePath_AreRefusedBeforeTheyAreDeduplicated()
    {
        var outcome = RoundCountsMergedMode.RequestOf(Args(Enumerable.Repeat(Absolute(1), 10_000)), Clock);

        outcome.Ok.Should().BeFalse("the cap counts the arguments given, not what is left once the duplicates are folded away");
        outcome.Why.Should().Contain("10000 paths were given").And.Contain("at most 32");
    }

    [Fact]
    public void TenThousandDistinctPaths_AreRefusedQuickly_WithTheSameSentence()
    {
        var args = Args(Enumerable.Range(0, 10_000).Select(Absolute));
        var clock = Stopwatch.StartNew();

        var outcome = RoundCountsMergedMode.RequestOf(args, Clock);

        outcome.Ok.Should().BeFalse();
        outcome.Why.Should().Contain("10000 paths were given").And.Contain("at most 32");
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5), "nothing is materialised, deduplicated or checked for being absolute past the cap");
    }

    [Fact]
    public void ThirtyTwoPaths_AreAFineRequest_AndThirtyThreeAreNot()
    {
        RoundCountsMergedMode.RequestOf(Args(Enumerable.Range(0, 32).Select(Absolute)), Clock).Ok.Should().BeTrue();
        RoundCountsMergedMode.RequestOf(Args(Enumerable.Range(0, 33).Select(Absolute)), Clock).Ok.Should().BeFalse();
    }

    [Fact]
    public void APathGivenTwice_StillCountsOnceWithinTheCap()
    {
        var outcome = RoundCountsMergedMode.RequestOf(Args([Absolute(1), Absolute(1), Absolute(2)]), Clock);

        outcome.Value.Paths.Should().Equal(Absolute(1), Absolute(2));
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
