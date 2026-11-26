using System.Diagnostics;
using CoaiMcp.Core.Toolchain;
using CoaiMcp.Runners.Toolchain;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E2 s2 t4, DoD d4 (stop half): a timeout and a cancel stop the UNIT (<c>systemctl --user stop</c>) and no process of the step stays. Killing the <c>systemd-run --wait</c> client is not enough — measured here: the tree belongs to the user
/// manager — so each case writes the pids the step started and looks at the kernel afterwards.
/// </summary>
/// <remarks>Real units, real <c>sleep</c> processes with an odd duration, the pids read back from a file in the worktree. Skipped, with the reason, without a user manager. Every unit is stopped by the code under test or the bed fails the test.</remarks>
public sealed class ACheckBoundaryStopTests
{
    private const string TwoSleepers = "sleep 7311 & echo $! > pids; (sleep 7312 & echo $! >> pids; wait) & wait";

    private static void NeedAUserManager() => Assert.SkipUnless(SystemdUser.Works(out var why), why);

    private static async Task<IReadOnlyList<int>> PidsAsync(BoundaryBed bed)
    {
        var pids = Path.Combine(bed.Worktree, "pids");
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(30))
        {
            var lines = File.Exists(pids) ? File.ReadAllLines(pids).Where(line => line.Length > 0).ToList() : [];
            if (lines.Count == 2)
            {
                return [.. lines.Select(int.Parse)];
            }

            await Task.Delay(50);
        }

        throw new TimeoutException("the step did not write its two pids");
    }

    private static bool Alive(int pid) => Directory.Exists($"/proc/{pid}") && !File.ReadAllText($"/proc/{pid}/stat").Contains(") Z ", StringComparison.Ordinal);

    private static async Task<bool> AllGoneAsync(IReadOnlyList<int> pids)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(5))
        {
            if (!pids.Any(Alive))
            {
                return true;
            }

            await Task.Delay(50);
        }

        return false;
    }

    [Fact]
    public async Task ATimeout_StopsTheUnit_AndNoProcessOfTheStepStays()
    {
        NeedAUserManager();
        await using var bed = await BoundaryBed.CreateAsync();
        var watch = Stopwatch.StartNew();

        var running = bed.ShellAsync(TwoSleepers, timeout: TimeSpan.FromSeconds(5));
        var pids = await PidsAsync(bed);
        pids.Should().OnlyContain(pid => Alive(pid), "POSITIVE CONTROL: both processes are running before the deadline");
        var shown = await bed.Launcher.RunAsync(new(("systemctl"), ["--user", "show", $"coai-check-{bed.JobId}-s1.service", "--property=RuntimeMaxUSec", "--value"], bed.Worktree));
        var run = await running;

        shown.StdOut.Trim().Should().Be("1min 5s", "the unit's own RuntimeMaxSec is a minute past the step's deadline: the backstop for a coai that dies first");
        run.Result.TimedOut.Should().BeTrue();
        run.Result.Cancelled.Should().BeFalse();
        run.Gone.Should().BeTrue("the unit was asked after and is not active");
        (await bed.ActiveUnitsAsync()).Should().BeEmpty();
        (await AllGoneAsync(pids)).Should().BeTrue("no process of the step stays: sleep 7311 and its child's sleep 7312 are gone");
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(40));
    }

    [Fact]
    public async Task ACancel_StopsTheUnit_AndNoProcessOfTheStepStays()
    {
        NeedAUserManager();
        await using var bed = await BoundaryBed.CreateAsync();
        using var cancel = new CancellationTokenSource();

        var running = bed.ShellAsync(TwoSleepers, timeout: TimeSpan.FromMinutes(5), ct: cancel.Token);
        var pids = await PidsAsync(bed);
        pids.Should().OnlyContain(pid => Alive(pid), "POSITIVE CONTROL: both processes are running when the cancel comes");
        await cancel.CancelAsync();
        var run = await running;

        run.Result.Cancelled.Should().BeTrue();
        run.Result.TimedOut.Should().BeTrue("the launcher reports a killed tree as timed out and says WHY in Cancelled");
        run.Gone.Should().BeTrue();
        (await bed.ActiveUnitsAsync()).Should().BeEmpty();
        (await AllGoneAsync(pids)).Should().BeTrue("a cancel leaves no process of the step");
    }

    [Fact]
    public async Task AStepThatEndsOnItsOwn_LeavesNoUnit_AndNoProcess_EvenWhenItLeftABackgroundChildBehind()
    {
        NeedAUserManager();
        await using var bed = await BoundaryBed.CreateAsync();

        var run = await bed.ShellAsync("sleep 7313 & echo $! > pids; echo 7313 >> pids; exit 0");

        run.Result.ExitCode.Should().Be(0);
        run.Result.TimedOut.Should().BeFalse();
        (await bed.ActiveUnitsAsync()).Should().BeEmpty("a unit with a child still in it is stopped when the step ends: nothing the step started outlives it");
        (await AllGoneAsync([int.Parse(File.ReadAllLines(Path.Combine(bed.Worktree, "pids"))[0])])).Should().BeTrue();
    }
}
