using System.Diagnostics;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Toolchain;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E2 follow-up, the security verify's M7: a coder unit whose process ignores the stop signal. With <c>TimeoutStopSec</c> below coai's own stop wait the manager's kill lands in time and the unit is gone; where it is not (the property taken away by the degrade seam, as an
/// old or odd manager would leave it) the unit is NOT gone, the launch is not read as a model that crashed, and the job ends <c>coder-boundary</c> with no next attempt beside a live writer. Real units on temp folders only; skipped, with the reason, where there is no user manager.
/// </summary>
[Collection("fakecli-env")]
public sealed class ACoderUnitThatCannotBeStoppedTests
{
    private static readonly string[] SonnetThenOpus = ["claude/sonnet", "claude/opus"];

    /// <summary>A process that ignores the stop signal, and so does everything it starts (an ignored signal is inherited through exec).</summary>
    private const string IgnoresTerm = "trap '' TERM; sleep 300";

    private static IReadOnlyList<string> WithoutStopTimeout(IReadOnlyList<string> arguments)
    {
        var list = arguments.ToList();
        var at = list.FindIndex(argument => argument.StartsWith("TimeoutStopSec=", StringComparison.Ordinal));
        if (at > 0)
        {
            list.RemoveRange(at - 1, 2);
        }

        return list;
    }

    private static async Task KillAsync(string unit, string folder) =>
        await new ProcessLauncher().RunAsync(new ProcessRequest("systemctl", ["--user", "kill", "--signal=SIGKILL", "--kill-whom=all", $"{unit}.service"], folder) { Timeout = TimeSpan.FromSeconds(20) });

    private static async Task WaitGoneAsync(Func<Task<string>> active)
    {
        var until = Stopwatch.StartNew();
        while (until.Elapsed < TimeSpan.FromSeconds(30) && (await active()).Length > 0)
        {
            await Task.Delay(200, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task TheUnitsStopTimeout_IsBelowCoaisWait_SoAProcessThatIgnoresTheStopSignalIsKilledAndTheUnitIsGone()
    {
        Assert.SkipUnless(SystemdUser.Works(out var why), why);
        await using var bed = await CoderBed.CreateAsync();

        var clock = Stopwatch.StartNew();
        var outcome = await bed.Turns().RunAsync(bed.Job, bed.Shell(IgnoresTerm) with { Timeout = TimeSpan.FromSeconds(3) }, "c1");
        var took = clock.Elapsed;
        await WaitGoneAsync(bed.Inner.ActiveUnitsAsync);

        CoderOsLayer.StopOf(outcome).Should().BeNull("the manager killed what ignored the signal inside coai's wait, so this is an ordinary timeout and not a unit that could not be stopped");
        outcome.Should().BeOfType<CoderTurnOutcome.Failed>(outcome.ToString()).Which.Run.Gone.Should().BeTrue();
        took.Should().BeLessThan(TimeSpan.FromSeconds(19), "TimeoutStopSec is 10 s, coai waits 20");
        (await bed.Inner.ActiveUnitsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task AUnitThatCannotBeStopped_IsNeverAModelThatCrashed_ItIsItsOwnOutcomeAndEndsTheJob()
    {
        Assert.SkipUnless(SystemdUser.Works(out var why), why);
        await using var bed = await CoderBed.CreateAsync();
        var unit = $"coai-coder-{bed.Job.JobId}-c1";
        try
        {
            var outcome = await bed.Turns(bed.Boundary(WithoutStopTimeout)).RunAsync(bed.Job, bed.Shell(IgnoresTerm) with { Timeout = TimeSpan.FromSeconds(3) }, "c1");

            var stop = CoderOsLayer.StopOf(outcome);
            stop.Should().NotBeNull("a unit that is still alive ends the job; it is not a crash for the failover to answer with the next model");
            stop!.Kind.Should().Be("unit-not-stopped");
            stop.Started.Should().BeTrue();
            stop.Sentence.Should().Contain(unit).And.Contain("could not be stopped");
        }
        finally
        {
            await KillAsync(unit, bed.Worktree);
            await WaitGoneAsync(bed.Inner.ActiveUnitsAsync);
        }
    }

    [Fact]
    public async Task AJobWhoseCodersUnitCannotBeStopped_EndsCoderBoundary_AndStartsNoSecondLaunch()
    {
        Assert.SkipUnless(SystemdUser.Works(out var why), why);
        await using var rig = await CoderRig.CreateAsync();
        rig.AttemptTimeout = TimeSpan.FromSeconds(3);
        rig.Degrade = arguments => arguments.Any(argument => argument.StartsWith("coai-coder-", StringComparison.Ordinal)) ? WithoutStopTimeout(arguments) : arguments;
        rig.Says("sonnet", new CoderRig.Launch(null, CoderRig.Report(), SleepMs: 300_000, IgnoreTerm: true)).Says("opus", new CoderRig.Launch(new Dictionary<string, string> { [CoderRig.StateFile] = CoderRig.Green + "\n" }, CoderRig.Report()));
        var job = rig.NewJob(route: SonnetThenOpus);
        try
        {
            var (_, ended) = await rig.RunAsync(job);

            (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, RoleStopWords.CoderBoundary), ended.Reason);
            rig.Launches(job).Should().ContainSingle("the next model is never started beside a unit that is still alive");
        }
        finally
        {
            foreach (var unit in await rig.UnitsOfAsync(job))
            {
                await KillAsync(unit.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0].Replace(".service", string.Empty, StringComparison.Ordinal), rig.Repo);
            }

            await WaitGoneAsync(async () => string.Join(' ', await rig.UnitsOfAsync(job)));
        }
    }
}
