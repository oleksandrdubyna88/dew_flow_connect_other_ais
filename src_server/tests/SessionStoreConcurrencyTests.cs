using System.Collections.Concurrent;
using FluentAssertions;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>
/// One <c>sessions/</c> directory under everything that touches it at once — sign-ins, the hourly
/// last-used stamps (a rename each), the sweep, and the roster's read — and the only two facts that
/// matter at the end: every live token still validates, and the read never threw.
/// </summary>
/// <remarks>
/// <para>Asked for by the risk consultation on story 2.1 (2026-10-10). Bounded by iteration counts,
/// not by time, and asserted on the FINAL state only, so it cannot flake on scheduling; the deadline
/// exists to turn a hang into a failure, not to be reached.</para>
/// <para>On Windows this is where the two repairs meet: a stamp's rename under a reader is refused and
/// must be non-fatal, and a sweep's read under a rename is refused and must keep the file — the
/// second of which, before 2026-10-10, deleted the live session.</para>
/// </remarks>
public sealed class SessionStoreConcurrencyTests
{
    /// <summary>Long enough to be sure the answer is not coming, short enough to fail fast.</summary>
    private static readonly TimeSpan Promptly = TimeSpan.FromSeconds(90);

    private const int Workers = 6;

    private const int Rounds = 120;

    [Fact]
    public async Task ConcurrentIssueValidateSweepAndActive_LeaveEveryLiveTokenValid_AndActiveNeverThrows()
    {
        var data = Directory.CreateTempSubdirectory("coai-stress-").FullName;
        var store = new SessionStore(data, TimeSpan.FromDays(7));
        var now = DateTimeOffset.UtcNow;
        var live = new ConcurrentBag<string>();
        for (var i = 0; i < 20; i++)
        {
            live.Add(store.Issue($"seed{i}@{TeamServer.Domain}", "", now).Token);
        }

        for (var i = 0; i < 10; i++)
        {
            store.Issue($"gone{i}@{TeamServer.Domain}", "", now.AddDays(-30));
        }

        var seeds = live.ToArray();
        var failures = new ConcurrentBag<Exception>();
        var workers = Enumerable.Range(0, Workers).Select(worker => Task.Run(() =>
        {
            for (var round = 0; round < Rounds; round++)
            {
                try
                {
                    Step(store, live, seeds, worker, round, now);
                }
                catch (Exception e)
                {
                    failures.Add(e);
                }
            }
        }));

        await Task.WhenAll(workers).WaitAsync(Promptly, TestContext.Current.CancellationToken);

        failures.Should().BeEmpty("none of the four operations may throw under the others");
        var tokens = live.ToArray();
        tokens.Where(token => store.Validate(token, now.AddHours(1)) is null)
            .Should().BeEmpty("no live session may be lost to a sweep or a stamp that met another hand on the file");
        store.Sweep(now);
        var sessions = Path.Combine(data, "sessions");
        Directory.GetFiles(sessions, "*.json").Should().HaveCount(tokens.Length, "the expired ten are gone and nothing live is");
        Directory.GetFiles(sessions, "*.tmp").Should().BeEmpty("no refused rename left its temporary behind");
        store.Active(now).Should().HaveCount(tokens.Length);
    }

    /// <summary>One operation per round, cycling through the four; the stamp moves an hour each time so every Validate writes.</summary>
    private static void Step(SessionStore store, ConcurrentBag<string> live, string[] seeds, int worker, int round, DateTimeOffset now)
    {
        switch (round % 4)
        {
            case 0:
                live.Add(store.Issue($"w{worker}-{round}@{TeamServer.Domain}", "", now).Token);
                break;
            case 1:
                store.Validate(seeds[(worker + round) % seeds.Length], now.AddHours(2 + round));
                break;
            case 2:
                _ = store.Active(now);
                break;
            default:
                store.Sweep(now);
                break;
        }
    }
}
