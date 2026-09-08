using Xunit;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;

namespace CoaiMcp.Tests;

/// <summary>
/// The order of the work list is the order reviewers actually start.
/// </summary>
/// <remarks>
/// <para><b>This was an assumption, and shuffling the providers rests on it.</b> Two reviewers on
/// the plan round said so from opposite directions: one asked whether reordering could matter at
/// all if the only gate were per-provider, and one pointed out that .NET does not promise a
/// semaphore hands slots out in the order tasks were created.</para>
/// <para>It holds, for a reason worth writing down rather than trusting. `RunAllAsync` builds its
/// tasks with `work.Select(async w =&gt; ...)`; an async lambda runs synchronously until its first
/// await, which here is the GLOBAL semaphore, and `Task.WhenAll` enumerates the sequence in list
/// order. So every reviewer reaches the machine's gate in list order, and `SemaphoreSlim` releases
/// its waiters first-in-first-out. The machine's cap being taken BEFORE the per-provider one is the
/// other half: with a cap smaller than the round, list order decides who gets the machine.</para>
/// <para>Measured with the cap at one, which turns the fan-out into exactly the sequence under
/// test. A test that allowed two at once would be asserting a race.</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class SubmissionOrderIsTheDispatchOrderTests
{
    private readonly ReviewerExecutor _executor = new(new ProcessLauncher());
    private readonly string _dir = Directory.CreateTempSubdirectory("coai-order-").FullName;

    [Fact]
    public async Task WithOneSlot_ReviewersStartInTheOrderTheyWereListed()
    {
        var busyDir = Directory.CreateDirectory(Path.Combine(_dir, "seq")).FullName;
        string[] vendors = ["delta", "alpha", "charlie", "bravo"];
        var work = vendors
            .Select(v => new ReviewerWork(FakeCliInvocations.Invoke(v, ["busy", busyDir, "40"])))
            .ToList();
        var scheduler = new BoundedScheduler(globalCap: 1, perProviderCap: 1);

        var started = new List<string>();
        await scheduler.RunAllAsync(work, _executor, TestContext.Current.CancellationToken, progress =>
        {
            if (progress.Status == "running")
            {
                lock (started)
                {
                    // "running" is reported once when the slot is taken and again later with an
                    // elapsed note while a long reviewer holds it; only the first is a start.
                    if (!started.Contains(progress.Provider))
                    {
                        started.Add(progress.Provider);
                    }
                }
            }
        });

        started.Should().Equal(vendors,
            "the list's order is the order reviewers reach a Team server, which is what makes "
            + "shuffling the providers change anything at all");
    }
}
