using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Which reviewer a round submits FIRST, over many rounds.
/// </summary>
/// <remarks>
/// The defect is not visible in one round: any single order is as good as any other. It appears
/// across CLIENTS — everybody's vendor list is built the same way, so everybody's first submission
/// lands on the same vendor's single shared account while the last vendor's sits idle. The unit
/// under test is therefore a DISTRIBUTION, and the assertions below are about flatness rather than
/// about "the first one varies", which a badly skewed shuffle would also satisfy.
/// </remarks>
public sealed class SubmissionOrderTests
{
    private const int Rounds = 3_000;

    private static ReviewerWork Remote(string vendor) => Work(vendor, jobFile: $"{vendor}.job");

    private static ReviewerWork Local(string vendor) => Work(vendor, jobFile: string.Empty);

    private static ReviewerWork Work(string vendor, string jobFile) =>
        new(new ReviewerInvocation(
            vendor,
            ReviewRole.PlanCritique,
            new ProcessRequest("x", [], "."),
            JobFile: jobFile));

    /// <summary>Seeded, so a failure here is a real regression and never a bad afternoon.</summary>
    private static Func<double> Rolls(int seed)
    {
        var random = new Random(seed);

        return random.NextDouble;
    }

    [Fact]
    public void EachTeamServerVendorLeadsRoughlyAsOftenAsTheOthers()
    {
        var work = new[] { Remote("server-codex"), Remote("server-antigravity"), Remote("server-claude") };
        var roll = Rolls(20260908);
        var led = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var i = 0; i < Rounds; i++)
        {
            var first = work[SubmissionOrder.For(work, roll)[0]].Invocation.Provider;
            led[first] = led.GetValueOrDefault(first) + 1;
        }

        // A tenth of the fair share either way. The point of the tolerance being explicit is that
        // "not always the same vendor" would pass a shuffle that still sent 80% of first
        // submissions to one account — which is the defect, not the fix. (CodeRabbit, PR 93.)
        var fair = Rounds / work.Length;
        led.Should().HaveCount(work.Length, "every vendor must lead sometimes");
        foreach (var (vendor, count) in led)
        {
            count.Should().BeCloseTo(fair, (uint)(fair / 10), $"{vendor} should lead about a third of the time");
        }
    }

    [Fact]
    public void ALocalReviewerKeepsItsPlace()
    {
        // Remote, local, remote — the local one is in the middle and must stay there however the
        // two around it are ordered.
        var work = new[] { Remote("server-codex"), Local("local"), Remote("server-claude") };
        var roll = Rolls(7);

        for (var i = 0; i < 200; i++)
        {
            var order = SubmissionOrder.For(work, roll);

            work[order[1]].Invocation.Provider.Should().Be("local");
        }
    }

    [Fact]
    public void EveryReviewerIsDispatchedExactlyOnce()
    {
        var work = new[]
        {
            Remote("a"), Local("b"), Remote("c"), Remote("d"), Local("e"), Remote("f"),
        };
        var roll = Rolls(11);

        for (var i = 0; i < 200; i++)
        {
            SubmissionOrder.For(work, roll).Should().BeEquivalentTo(Enumerable.Range(0, work.Length));
        }
    }

    /// <summary>A round with no Team-server reviewer is not touched at all.</summary>
    [Fact]
    public void ARoundOfLocalReviewersIsLeftExactlyAsItWas()
    {
        var work = new[] { Local("codex"), Local("gemini"), Local("local") };

        SubmissionOrder.For(work, Rolls(3)).Should().Equal(0, 1, 2);
    }

    /// <summary>One remote reviewer has nothing to be shuffled against.</summary>
    [Fact]
    public void ASingleTeamServerReviewerIsLeftWhereItIs()
    {
        var work = new[] { Local("codex"), Remote("server-claude"), Local("local") };

        SubmissionOrder.For(work, Rolls(3)).Should().Equal(0, 1, 2);
    }

    /// <summary>
    /// A roll that returns its upper bound must not index past the end.
    /// </summary>
    /// <remarks>
    /// `roll` is a parameter, so its range is a promise rather than a guarantee — and the one caller
    /// that would break it is a test pinning it to a constant.
    /// </remarks>
    [Fact]
    public void ARollAtItsUpperBoundIsStillAValidOrder()
    {
        var work = new[] { Remote("a"), Remote("b"), Remote("c") };

        SubmissionOrder.For(work, () => 1.0).Should().BeEquivalentTo(Enumerable.Range(0, 3));
    }
}

/// <summary>
/// The scheduler's end of it: reviewers are STARTED in a varying order, and reported in a fixed one.
/// </summary>
/// <remarks>
/// The unit above proves the permutation is flat. This proves the scheduler actually uses it — and,
/// just as importantly, that a person watching a round does not see their reviewers shuffle between
/// runs. Those two requirements pull in opposite directions, which is exactly why both are pinned.
/// </remarks>
[Collection("fakecli-env")]
public sealed class SubmissionOrderSchedulerTests
{
    private readonly ReviewerExecutor _executor = new(new ProcessLauncher());
    private readonly string _dir = Directory.CreateTempSubdirectory("coai-order-").FullName;

    private ReviewerWork Remote(string vendor) =>
        new(FakeCliInvocations.Invoke(vendor, ["ok", Path.Combine(_dir, $"{vendor}.json")]) with
        {
            JobFile = Path.Combine(_dir, $"{vendor}.job"),
        });

    /// <summary>Serial, so "which started first" is a fact rather than a race.</summary>
    private static BoundedScheduler Serial() => new(globalCap: 1, perProviderCap: 1);

    [Fact]
    public async Task TeamServerReviewersDoNotAlwaysStartInTheOrderTheyWereListed()
    {
        var work = new[] { Remote("s-a"), Remote("s-b"), Remote("s-c") };
        var firsts = new HashSet<string>(StringComparer.Ordinal);

        for (var run = 0; run < 40 && firsts.Count < 2; run++)
        {
            var started = new List<string>();
            await Serial().RunAllAsync(
                work,
                _executor,
                TestContext.Current.CancellationToken,
                p =>
                {
                    if (p.Status == "running")
                    {
                        lock (started)
                        {
                            started.Add(p.Provider);
                        }
                    }
                });

            firsts.Add(started[0]);
        }

        firsts.Should().HaveCountGreaterThan(1,
            "everybody's list is built the same way, so a fixed dispatch order puts everybody's "
            + "first review on the same shared account while the last vendor's sits idle");
    }

    [Fact]
    public async Task TheRoundStillReportsItsReviewersInTheOrderItWasGivenThem()
    {
        var work = new[] { Remote("s-a"), Remote("s-b"), Remote("s-c") };

        for (var run = 0; run < 10; run++)
        {
            var results = await Serial().RunAllAsync(work, _executor, TestContext.Current.CancellationToken);

            results.Select(r => r.Invocation.Provider).Should().Equal(
                ["s-a", "s-b", "s-c"],
                "a person watching must not see their reviewers move between runs");
        }
    }
}
