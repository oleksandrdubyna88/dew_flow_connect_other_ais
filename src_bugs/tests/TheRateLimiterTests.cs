using System.Globalization;
using FluentAssertions;
using Xunit;

namespace CoaiBugs.Tests;

/// <summary>
/// The sliding window: per key, exactly the limit inside a minute, and safe under a real race.
/// </summary>
/// <remarks>
/// <para>A frozen clock, so "a minute later" is an assignment and not a wait. The two racing tests
/// use THREADS released by a barrier, not tasks — a race a scheduler may serialise proves
/// nothing, and the finding this answers was eleven simultaneous requests all reading one window
/// and all passing a limit of ten.</para>
/// <para>Their teeth are proved by replacing the limiter's compare-and-swap with a plain write:
/// with the lost update back in, the first race over-admits and goes red.</para>
/// </remarks>
public sealed class TheRateLimiterTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 17, 10, 0, 0, TimeSpan.Zero);

    private static readonly LimiterSubject Key = LimiterSubject.Contributor(new KeyId("key-1"));

    private static RatePerMinute Rate(int perMinute) =>
        ((RatePerMinute.Parsed.Rate)RatePerMinute.Parse(perMinute.ToString(CultureInfo.InvariantCulture))).Value;

    private static (RateLimiter Limiter, FrozenClock Clock) At(int limit)
    {
        var clock = new FrozenClock(T0);

        return (new RateLimiter(Rate(limit), clock), clock);
    }

    [Fact]
    public void TheLimitAdmitsExactlyThatManyInsideAMinute()
    {
        var (limiter, _) = At(3);

        for (var n = 0; n < 3; n++)
        {
            limiter.Admit(Key).Admitted.Should().BeTrue($"request {n + 1} of 3 is inside the limit");
        }

        var fourth = limiter.Admit(Key);

        fourth.Admitted.Should().BeFalse();
        fourth.RetryAfterSeconds.Should().Be(60, "every stamp is at T0, and the first leaves the window a minute later");
    }

    [Fact]
    public void ASlotOpensWhenTheOldestStampLeavesTheWindow()
    {
        var (limiter, clock) = At(2);
        limiter.Admit(Key).Admitted.Should().BeTrue();
        clock.Advance(TimeSpan.FromSeconds(30));
        limiter.Admit(Key).Admitted.Should().BeTrue();

        clock.Set(T0 + TimeSpan.FromSeconds(59));
        var still = limiter.Admit(Key);
        still.Admitted.Should().BeFalse("both stamps are inside the window");
        still.RetryAfterSeconds.Should().Be(1, "the first stamp leaves in one second, rounded up");

        clock.Set(T0 + TimeSpan.FromSeconds(60) + TimeSpan.FromMilliseconds(1));
        limiter.Admit(Key).Admitted.Should().BeTrue("the first stamp has left the window");

        var again = limiter.Admit(Key);
        again.Admitted.Should().BeFalse("the T0+30 stamp and the new one fill it");
        again.RetryAfterSeconds.Should().Be(30, "the T0+30 stamp leaves at T0+90, thirty seconds on");
    }

    /// <summary>The window SLIDES: a fixed bucket would admit twice the limit across its boundary.</summary>
    [Fact]
    public void TheWindowSlidesRatherThanResettingOnTheMinute()
    {
        var (limiter, clock) = At(2);
        clock.Set(T0 + TimeSpan.FromSeconds(59));
        limiter.Admit(Key).Admitted.Should().BeTrue();
        limiter.Admit(Key).Admitted.Should().BeTrue();

        clock.Set(T0 + TimeSpan.FromSeconds(61));

        limiter.Admit(Key).Admitted.Should().BeFalse(
            "two seconds on, both stamps are still inside the sliding minute; a bucket keyed on the "
            + "calendar minute would have started afresh at T0+60");
    }

    [Fact]
    public void KeysAreIndependent()
    {
        var (limiter, _) = At(1);

        limiter.Admit(LimiterSubject.Contributor(new KeyId("a"))).Admitted.Should().BeTrue();
        limiter.Admit(LimiterSubject.Contributor(new KeyId("b"))).Admitted.Should().BeTrue();
        limiter.Admit(LimiterSubject.Contributor(new KeyId("a"))).Admitted.Should().BeFalse();
        limiter.Tracked.Should().Be(2);
    }

    /// <summary>An administrator and a contributor never share a bucket, whatever their ids.</summary>
    /// <remarks>
    /// The identity an admin route hands the limiter is defined here, in story 1, so story 2 cannot
    /// land its routes in the contributor bucket by default. A contributor whose id happens to spell
    /// `admin-…` still lands in its own.
    /// </remarks>
    [Fact]
    public void AnAdministratorAndAContributorNeverShareABucket()
    {
        var hash = Corpus.HashOf("an-admin-key", "secret");
        var admin = LimiterSubject.Administrator(hash);
        var lookalike = LimiterSubject.Contributor(new KeyId(AdminId.Of(hash).Value));
        var (limiter, _) = At(1);

        admin.Key.Should().Be(AdminId.Of(hash).Value).And.StartWith("admin-");
        lookalike.Key.Should().NotBe(admin.Key, "the contributor prefix keeps even a spoofed id apart");

        limiter.Admit(admin).Admitted.Should().BeTrue();
        limiter.Admit(lookalike).Admitted.Should().BeTrue();
        limiter.Admit(admin).Admitted.Should().BeFalse();
        limiter.Admit(lookalike).Admitted.Should().BeFalse();
    }

    /// <summary>Zero switches the limit off, and keeps nothing.</summary>
    [Fact]
    public void ZeroDisablesTheLimitAndTracksNobody()
    {
        var (limiter, _) = At(0);

        for (var n = 0; n < 1_000; n++)
        {
            limiter.Admit(Key).Admitted.Should().BeTrue();
        }

        limiter.Tracked.Should().Be(0, "a disabled limiter must not grow with traffic either");
    }

    [Fact]
    public void TheSweepDropsIdleWindowsAndKeepsLiveOnes()
    {
        var (limiter, clock) = At(5);
        limiter.Admit(LimiterSubject.Contributor(new KeyId("idle"))).Admitted.Should().BeTrue();
        limiter.Admit(LimiterSubject.Contributor(new KeyId("busy"))).Admitted.Should().BeTrue();

        clock.Advance(TimeSpan.FromSeconds(61));
        limiter.Admit(LimiterSubject.Contributor(new KeyId("busy"))).Admitted.Should().BeTrue();

        limiter.Sweep().Should().Be(1, "only the window with no stamp inside the minute is dropped");
        limiter.Tracked.Should().Be(1);
        limiter.StampsOf(LimiterSubject.Contributor(new KeyId("busy"))).Should().Be(1, "the expired stamp left with the admit that replaced it");
    }

    [Fact]
    public void ARefusalNamesTheWaitRoundedUpAndNeverBelowOneSecond()
    {
        var (limiter, clock) = At(1);
        limiter.Admit(Key).Admitted.Should().BeTrue();

        clock.Set(T0 + TimeSpan.FromSeconds(59) + TimeSpan.FromMilliseconds(200));
        limiter.Admit(Key).RetryAfterSeconds.Should().Be(1, "0.8 s rounds up");

        clock.Set(T0 + TimeSpan.FromSeconds(59) + TimeSpan.FromMilliseconds(999));
        limiter.Admit(Key).RetryAfterSeconds.Should().Be(1);

        Admission.Refused(0).RetryAfterSeconds.Should().Be(1, "a 429 with a zero is a client that retries at once");
    }

    /// <summary>
    /// N requests racing for L slots: exactly L are admitted, every time.
    /// </summary>
    /// <remarks>
    /// Sixty-four threads released together against a limit of ten, twenty-five rounds. A lost
    /// update — two requests reading one window and both installing their own — would admit more
    /// than ten on some round, which is the defect the compare-and-swap exists to prevent.
    /// </remarks>
    [Fact]
    public void NRequestsRacingForLSlots_ExactlyLAreAdmitted()
    {
        const int Limit = 10;
        const int Racers = 64;
        const int Rounds = 25;
        var (limiter, clock) = At(Limit);

        for (var round = 0; round < Rounds; round++)
        {
            var admitted = Race(Racers, () => limiter.Admit(Key).Admitted);

            admitted.Should().Be(
                Limit, $"round {round}: {Racers} simultaneous requests against a limit of {Limit}");
            limiter.StampsOf(Key).Should().Be(Limit, "and the window holds exactly the stamps it granted");

            clock.Advance(TimeSpan.FromSeconds(61));
            limiter.Sweep().Should().Be(1);
        }
    }

    /// <summary>
    /// An admit racing the sweep keeps every stamp it was granted: eviction of an idle window can
    /// never take a stamp that landed on it meanwhile.
    /// </summary>
    /// <remarks>
    /// The window is made idle first (one stamp, then a minute passes), so the sweep WANTS to evict
    /// it at the same moment thirty-two admits arrive. The sweep removes only the exact instance it
    /// judged idle; an admit that swapped a stamp in keeps its window. Afterwards the window exists,
    /// holds every admitted stamp, and nothing was double-counted.
    /// </remarks>
    [Fact]
    public void AnAdmitRacingTheSweepKeepsEveryStampItWasGranted()
    {
        const int Racers = 32;
        const int Rounds = 25;
        var (limiter, clock) = At(Racers);

        for (var round = 0; round < Rounds; round++)
        {
            // Everything the last round granted has left the window; one seed stamp goes in, and a
            // minute later it is the idle window the sweep wants to evict.
            clock.Advance(TimeSpan.FromSeconds(61));
            limiter.Admit(Key).Admitted.Should().BeTrue($"round {round}: the window is empty again");
            clock.Advance(TimeSpan.FromSeconds(61));

            var admitted = Race(
                Racers,
                () => limiter.Admit(Key).Admitted,
                alongside: () =>
                {
                    for (var sweeps = 0; sweeps < 50; sweeps++)
                    {
                        limiter.Sweep();
                    }
                });

            admitted.Should().Be(Racers, $"round {round}: the window had room for every one of them");
            limiter.Tracked.Should().Be(1, "the window survived the sweep, or was re-created with its stamps");
            limiter.StampsOf(Key).Should().Be(
                Racers, $"round {round}: every admitted request left its stamp in the window that survived");
        }
    }

    /// <summary>Runs <paramref name="attempt"/> on that many threads released at once; answers how many said yes.</summary>
    private static int Race(int threads, Func<bool> attempt, Action? alongside = null)
    {
        var admitted = 0;
        using var gate = new Barrier(threads + (alongside is null ? 0 : 1));
        var racers = Enumerable.Range(0, threads).Select(_ => new Thread(() =>
        {
            gate.SignalAndWait();
            if (attempt())
            {
                Interlocked.Increment(ref admitted);
            }
        })).ToList();
        if (alongside is not null)
        {
            racers.Add(new Thread(() =>
            {
                gate.SignalAndWait();
                alongside();
            }));
        }

        racers.ForEach(thread => thread.Start());
        racers.ForEach(thread => thread.Join());

        return admitted;
    }
}
