using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Toolchain;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E3 s7 t5 — no check unit sees the coder folder or another job's folder (judgement S1, S2; ids K1 and K2). The E2 follow-ups already built the property list (<c>BoundaryUnit.JobView</c>: an empty read-only tmpfs over the folder that holds the job folders, the check's own
/// folders bound one by one), the K1 row (<c>coder-folder-hidden</c>) and the unit tests of the coder's config; this class adds what they did not hold: the K2 row for another job's folder, in both network states, red through the seam with a control that shows the old bind reads it;
/// and a step with the HOST network that cannot read a stand-in sign-in copy in the coder's home or write a stand-in guard folder.
/// </summary>
public sealed class ACheckUnitHidesTheCoderFolderTests
{
    private static void NeedAUserManager() => Assert.SkipUnless(SystemdUser.Works(out var why), why);

    private static Func<IReadOnlyList<string>, IReadOnlyList<string>> OldBind(BoundaryBed bed) => arguments => [.. arguments.Select(argument => argument == $"TemporaryFileSystem={bed.Job.JobCover}:ro" ? $"BindPaths={bed.Job.JobCover}" : argument)];

    [Theory]
    [InlineData(CheckNetwork.Private)]
    [InlineData(CheckNetwork.Host)]
    public async Task K2_TheRowIsAsked_InBothNetworkStates_AndTheStandInIsGoneWhenThePreflightEnds(CheckNetwork network)
    {
        NeedAUserManager();
        await using var bed = await BoundaryBed.CreateAsync();
        bed.UseJobsFolderWithAnotherJob();
        bed.Job.JobCover.Should().Be(Path.GetDirectoryName(bed.Job.JobRoot), "a holding folder that can be covered: the row has a stand-in to look for");

        var admission = await bed.Boundary().AdmitAsync(bed.Job, [network], TestContext.Current.CancellationToken);

        var admitted = admission.Should().BeOfType<BoundaryAdmission.Admitted>(admission.ToString()).Subject;
        var row = admitted.Records.Single().Value.Preflight.Single(one => one.Item == BoundaryPreflight.OtherJobFolderHidden);
        row.Held.Should().BeTrue(row.Detail);
        row.Detail.Should().Contain("another job");
        Directory.GetFileSystemEntries(bed.Job.JobCover).Select(Path.GetFileName).Should().NotContain(name => name!.StartsWith("coai-preflight-other-", StringComparison.Ordinal), "the stand-in is removed when the preflight ends");
    }

    [Fact]
    public async Task K2_AJobFolderThatIsItsOwnCover_HasNoOtherJobBesideIt_AndTheRowSaysSo()
    {
        NeedAUserManager();
        await using var bed = await BoundaryBed.CreateAsync();
        bed.Job.JobCover.Should().Be(bed.Job.JobRoot);

        var admission = await bed.Boundary().AdmitAsync(bed.Job, [CheckNetwork.Private], TestContext.Current.CancellationToken);

        var row = admission.Should().BeOfType<BoundaryAdmission.Admitted>(admission.ToString()).Subject.Records.Single().Value.Preflight.Single(one => one.Item == BoundaryPreflight.OtherJobFolderHidden);
        row.Held.Should().BeTrue();
        row.Detail.Should().Contain("itself is the cover");
    }

    [Theory]
    [InlineData(CheckNetwork.Private)]
    [InlineData(CheckNetwork.Host)]
    public async Task K2_WhenTheCoverIsDropped_TheRowGoesRed_AndTheJobIsRefusedBeforeAnyStep(CheckNetwork network)
    {
        NeedAUserManager();
        await using var bed = await BoundaryBed.CreateAsync();
        bed.UseJobsFolderWithAnotherJob();

        var admission = await bed.Boundary(OldBind(bed)).AdmitAsync(bed.Job, [network], TestContext.Current.CancellationToken);

        var refused = admission.Should().BeOfType<BoundaryAdmission.Refused>(admission.ToString()).Subject;
        refused.Record.Preflight.Single(one => one.Item == BoundaryPreflight.OtherJobFolderHidden).Held.Should().BeFalse("the row that watches another job's folder says no when the folder that holds the job folders is bound again");
        refused.Sentence.Should().Contain("refused before any check ran");
    }

    [Fact]
    public async Task K2_AControlUnitWithTheOldBind_ReadsAnotherJobsFile_AndTheRealUnitDoesNot()
    {
        NeedAUserManager();
        await using var bed = await BoundaryBed.CreateAsync();
        var others = bed.UseJobsFolderWithAnotherJob();
        var script = $"if cat {others} >/dev/null 2>&1; then echo reads-other-job; else echo hidden-other-job; fi";

        var real = await bed.ShellAsync(script);
        var control = await new ProcessLauncher().RunAsync(new ProcessRequest(bed.Host.SystemdRun, BoundaryUnit.Arguments(bed.Host, bed.Job, $"coai-check-{bed.Job.JobId}-ctl", CheckNetwork.Private, TimeSpan.FromMinutes(2), new Dictionary<string, string?>(), ["/bin/sh", "-c", script], OldBind(bed)), bed.Worktree)
        {
            Timeout = TimeSpan.FromMinutes(1),
            InheritsEnvironment = false,
            Passthrough = UnitControl.Client,
        }, TestContext.Current.CancellationToken);

        real.Result.StdOut.Should().Contain("hidden-other-job");
        control.StdOut.Should().Contain("reads-other-job", "the control: with the old bind the same read works, so what the real unit shows is the cover's doing");
    }

    [Fact]
    public async Task TheHostNetworkStep_CannotReadAStandInSignInCopyInTheCodersHome_NorWriteAStandInGuardFolder()
    {
        NeedAUserManager();
        await using var bed = await BoundaryBed.CreateAsync();
        var copy = Path.Combine(bed.Job.CoderHomeFolder, ".claude", ".credentials.json");
        var guard = Path.Combine(bed.Job.CoderFolder, "guard");
        Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
        Directory.CreateDirectory(guard);
        await File.WriteAllTextAsync(copy, "a stand-in sign-in copy\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(guard, "hooks.json"), "the guard's own file\n", TestContext.Current.CancellationToken);
        var script = $$"""
            if cat {{copy}} >/dev/null 2>&1; then echo reads-copy; else echo unreadable-copy; fi
            if { true > {{guard}}/hooks.json; } 2>/dev/null; then echo wrote-guard; else echo refused-guard; fi
            if { true > {{guard}}/planted.json; } 2>/dev/null; then echo planted-in-guard; else echo refused-planting; fi
            """;

        var run = await bed.ShellAsync(script, network: CheckNetwork.Host);

        run.Result.ExitCode.Should().Be(0, run.Result.StdErr);
        run.Result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries).Should().Equal("unreadable-copy", "refused-guard", "refused-planting");
        (await File.ReadAllTextAsync(Path.Combine(guard, "hooks.json"), TestContext.Current.CancellationToken)).Should().Be("the guard's own file\n", "the guard's file was not touched");
        File.Exists(Path.Combine(guard, "planted.json")).Should().BeFalse();
    }
}
