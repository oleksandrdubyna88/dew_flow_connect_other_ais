using System.Text;
using CoaiMcp.Core.Toolchain;
using CoaiMcp.Runners.Toolchain;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E2 s2 t4, DoD d4 (result half) and the road of a coder job through <see cref="CheckRunner"/>: exit code and output pass through, the result records the boundary, its properties and the network state of each step, a failed preflight
/// refuses the job before any step, and a coder job given a runner with no boundary is refused rather than run bare.
/// </summary>
/// <remarks>Real units. The steps are <c>/bin/sh -c</c> scripts, so the toolchain file is the one the real reader accepts. Skipped, with the reason, without a user manager.</remarks>
public sealed class ACheckRunnerBoundaryTests
{
    private static void NeedAUserManager() => Assert.SkipUnless(SystemdUser.Works(out var why), why);

    private static string Toolchain(string build, string test, string tail = "")
    {
        var text = new StringBuilder("version: 1\nprojects:\n  - path: \"app\"\n    adapter: dotnet\n");
        foreach (var (key, script) in new[] { ("build", build), ("test", test) })
        {
            text.Append($"    {key}:\n      - \"/bin/sh\"\n      - \"-c\"\n      - {ToolchainFixture.Quote(script)}\n");
        }

        return text.Append(tail).ToString();
    }

    private sealed record Run(CheckResult Result, TempDir Logs);

    private static async Task<Run> RunAsync(BoundaryBed bed, string build, string test, string tail = "", BoundaryJob? job = null, CheckRunner? runner = null)
    {
        var logs = TempDir.For("coai-cb-logs-");
        var request = new CheckRequest(bed.Worktree, logs.At("run"), 2) { Job = job ?? bed.Job };
        var result = await (runner ?? new CheckRunner(bed.Launcher, bed.Boundary())).RunAsync(ToolchainFixture.Read(Toolchain(build, test, tail)), request);

        return new Run(result, logs);
    }

    [Fact]
    public async Task ACoderJob_RunsEveryStepInsideTheBoundary_AndTheResultRecordsIt()
    {
        NeedAUserManager();
        await using var bed = await BoundaryBed.CreateAsync();

        var run = await RunAsync(bed, "echo built", "echo tested; echo oops >&2");
        using var logs = run.Logs;
        var result = run.Result;

        result.Green.Should().BeTrue(result.Note);
        result.Steps.Should().HaveCount(2);
        foreach (var step in result.Steps)
        {
            step.Boundary.Bounded.Should().BeTrue();
            step.Boundary.Boundary.Should().Be(BoundaryRecord.UnitBoundary);
            step.Boundary.Network.Should().Be("none");
            step.Boundary.NetworkReason.Should().Be("test");
            step.Boundary.Properties.Should().Contain(["ProtectSystem=strict", "ProtectHome=tmpfs", "TemporaryFileSystem=/run/user", "TemporaryFileSystem=/mnt", "InaccessiblePaths=-/run/WSL", $"BindPaths={bed.Worktree}", $"BindReadOnlyPaths={bed.Main}", $"BindReadOnlyPaths={bed.Worktree}/.git", "NoNewPrivileges=yes", "PrivateUsers=yes", "PrivateNetwork=yes", "MemorySwapMax=0", $"MemoryMax={bed.Job.MemoryMegabytes}M"]);
            step.Boundary.Properties.Should().Contain(property => property.StartsWith("RuntimeMaxSec=", StringComparison.Ordinal));
            step.Boundary.Preflight.Should().NotBeEmpty().And.OnlyContain(row => row.Held);
        }

        File.ReadAllText(result.Steps[1].LogPath).Should().Be("tested\n\n--- stderr ---\noops\n");
    }

    [Fact]
    public async Task ExitCode_AndOutput_PassThrough_AFailingTestIsRecordedAsFailedWithItsOwnExitCode()
    {
        NeedAUserManager();
        await using var bed = await BoundaryBed.CreateAsync();

        var run = await RunAsync(bed, "echo built", "echo 'Test run summary: Failed!'; echo boom >&2; exit 3");
        using var logs = run.Logs;

        run.Result.State.Should().Be(CheckState.Failed);
        run.Result.Steps.Should().HaveCount(2);
        run.Result.Steps[0].ExitCode.Should().Be(0);
        run.Result.Steps[1].ExitCode.Should().Be(3);
        run.Result.Steps[1].Started.Should().BeTrue();
        run.Result.Steps[1].TimedOut.Should().BeFalse();
        File.ReadAllText(run.Result.Steps[1].LogPath).Should().Contain("Test run summary: Failed!").And.Contain("boom");
        run.Result.Note.Should().Contain("exited 3");
    }

    [Fact]
    public async Task TheNetworkStateIsRecordedForEachStep_WithItsReason()
    {
        NeedAUserManager();
        await using var bed = await BoundaryBed.CreateAsync();
        var job = bed.Job with { BuildNetwork = NetworkChoice.OnHost("the restore of packages needs the network"), TestNetwork = NetworkChoice.None("the suite passed with only a loopback device") };

        var run = await RunAsync(bed, "echo built", "ls /sys/class/net", job: job);
        using var logs = run.Logs;

        run.Result.Green.Should().BeTrue(run.Result.Note);
        run.Result.Steps[0].Boundary.Network.Should().Be("host");
        run.Result.Steps[0].Boundary.NetworkReason.Should().Be("the restore of packages needs the network");
        run.Result.Steps[0].Boundary.Properties.Should().Contain("PrivateNetwork=no");
        run.Result.Steps[1].Boundary.Network.Should().Be("none");
        run.Result.Steps[1].Boundary.NetworkReason.Should().Be("the suite passed with only a loopback device");
        File.ReadAllText(run.Result.Steps[1].LogPath).Trim().Should().Be("lo", "the test step really had a loopback device and nothing else");
    }

    [Fact]
    public async Task TheMeasuredDefaults_AreTheHostNetworkForTheBuild_AndNoneForTheTests_EachRecordedWithItsReason()
    {
        NeedAUserManager();
        await using var bed = await BoundaryBed.CreateAsync();
        var job = BoundaryJob.ForCoder(bed.JobId, bed.Tree, bed.JobRoot);

        var run = await RunAsync(bed, "echo built", "ls /sys/class/net", job: job);
        using var logs = run.Logs;

        run.Result.Green.Should().BeTrue(run.Result.Note);
        run.Result.Steps[0].Boundary.Network.Should().Be("host");
        run.Result.Steps[0].Boundary.NetworkReason.Should().Contain("NU1900", "the reason is the measured one");
        run.Result.Steps[1].Boundary.Network.Should().Be("none");
        run.Result.Steps[1].Boundary.NetworkReason.Should().Contain("passed with only a loopback device");
        File.ReadAllText(run.Result.Steps[1].LogPath).Trim().Should().Be("lo");
    }

    [Fact]
    public async Task APreflightThatSaysNo_RefusesTheJob_BeforeAnyStep_WithTheRowsRecorded()
    {
        NeedAUserManager();
        await using var bed = await BoundaryBed.CreateAsync();
        var broken = new CheckRunner(bed.Launcher, bed.Boundary(arguments => [.. arguments.Select(argument => argument == $"BindReadOnlyPaths={bed.Main}" ? $"BindPaths={bed.Main}" : argument)]));
        var marker = Path.Combine(bed.Worktree, "step-ran");

        var run = await RunAsync(bed, $"echo x > {marker}", "echo y", runner: broken);
        using var logs = run.Logs;

        run.Result.State.Should().Be(CheckState.BoundaryRefused);
        run.Result.State.Wire().Should().Be("boundary_refused");
        run.Result.Green.Should().BeFalse();
        run.Result.Steps.Should().BeEmpty();
        run.Result.Note.Should().Contain("main-readonly").And.Contain("refused before any check ran");
        run.Result.Boundary.Preflight.Single(row => row.Item == "main-readonly").Held.Should().BeFalse();
        File.Exists(marker).Should().BeFalse("the build script never ran");
        Directory.GetFiles(logs.At("run")).Should().BeEmpty("no step, no log");
        bed.Launcher.Requests.Select(request => string.Join(' ', request.Arguments)).Should().NotContain(arguments => arguments.Contains($"coai-check-{bed.JobId}-s1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ACoderJob_GivenARunnerWithNoBoundary_IsRefused_NotRunBare()
    {
        NeedAUserManager();
        await using var bed = await BoundaryBed.CreateAsync();
        var marker = Path.Combine(bed.Worktree, "step-ran");

        var run = await RunAsync(bed, $"echo x > {marker}", "echo y", runner: new CheckRunner(bed.Launcher));
        using var logs = run.Logs;

        run.Result.State.Should().Be(CheckState.BoundaryRefused);
        run.Result.Note.Should().Contain("no OS boundary");
        File.Exists(marker).Should().BeFalse("a coder job is never run without the boundary");
        run.Result.Steps.Should().BeEmpty();
    }

    [Fact]
    public async Task AStepOverItsTimeout_IsStopped_WithItsUnit_AndTheLogSaysSo()
    {
        NeedAUserManager();
        await using var bed = await BoundaryBed.CreateAsync();

        var run = await RunAsync(bed, "sleep 7314", "echo never", "timeouts:\n  build: 3s\n");
        using var logs = run.Logs;

        run.Result.State.Should().Be(CheckState.Failed);
        run.Result.Steps.Should().ContainSingle().Which.TimedOut.Should().BeTrue();
        run.Result.Steps[0].Passed.Should().BeFalse();
        run.Result.Note.Should().Contain("timeout");
        File.ReadAllText(run.Result.Steps[0].LogPath).Should().Contain($"coai-check-{bed.JobId}-s1 was stopped");
        (await bed.ActiveUnitsAsync()).Should().BeEmpty();
    }
}
