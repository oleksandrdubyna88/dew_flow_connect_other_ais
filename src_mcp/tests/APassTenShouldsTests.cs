using CoaiMcp.Core.Roles;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Toolchain;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;
using static CoaiMcp.Tests.CodexScript;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E3, pass 10, the shoulds before the merge toward main: S72 (the sentence of the look before the round says what is known), S73 (a check that makes the toolchain file unreadable ends the job with a stop word, not <c>runner-error</c>), S74 (the look after the round, and the look before it, have a test of their own, and
/// <c>noFollow</c> of the job's copy), S26, R-S7 and S54 (the refresh unit hides the key pool and every real sign-in file, and asks the home rule). The rig cases go through the product's road with real units; made-up folders only.
/// </summary>
[Collection("fakecli-env")]
public sealed class APassTenShouldsTests
{
    private const string ToolchainName = ".coai/toolchain.yaml";

    // ---------- S26, R-S7, S54: the refresh unit ----------

    [Fact]
    public void S26_TheRefreshUnit_HidesTheKeyPool_AndEveryRealSignInFile_AtTheirRealPaths()
    {
        using var dir = TempDir.For("s26-");
        var home = Path.Combine(dir.Path, "home");
        var keys = Path.Combine(dir.Path, "data", "keys");
        Directory.CreateDirectory(home);
        Directory.CreateDirectory(keys);
        var job = new BoundaryJob("job-x", new CoaiMcp.Runners.Worktrees.CoderTree(Path.Combine(dir.Path, "wt"), Path.Combine(dir.Path, "main"), string.Empty), Path.Combine(dir.Path, "jobs", "job-x"), NetworkChoice.None("t"), NetworkChoice.None("t")) { KeysFolder = keys };
        var host = BoundaryHost.Current() with { RealHome = home };

        var properties = SignInRefresh.Properties(host, job, job.RefreshFolder, new CoderProgram("/usr/bin/true", string.Empty, string.Empty));

        properties.Should().Contain($"InaccessiblePaths=-{job.KeysFolder}", "the key pool is in no unit of the family (KEY_POOL_SPEC)");
        foreach (var relative in BoundaryUnit.RealSignInFiles)
        {
            properties.Should().Contain($"InaccessiblePaths=-{BoundaryJob.RealOrSpelled(Path.Combine(host.ResolvedHome, relative))}", $"the real file {Path.GetFileName(relative)} is read by no unit — the scratch home holds the one copy this unit needs");
        }
    }

    [Fact]
    public async Task S54_ARefreshOfAProgramThatLiesDirectlyInTheHome_IsRefusedBeforeAnyUnitStarts()
    {
        await using var unit = await AgyUnit.CreateAsync(realExpiry: DateTime.UtcNow.AddMinutes(3));
        var inHome = Path.Combine(unit.Bed.RealHome, "agy-program");
        File.Copy("/usr/bin/true", inHome);
        var boundary = new CoderBoundary(unit.Bed.Launcher, unit.Bed.Host) { Vendor = unit.Vendor };
        var before = unit.Bed.Launcher.Requests.Count;

        var said = await boundary.PrepareSignInAsync(unit.Bed.Job, inHome, TimeSpan.FromMinutes(46), new SignInJobs(), DateTime.UtcNow, TestContext.Current.CancellationToken);

        said.Should().Contain("person's home", "the home rule refuses the program, for the refresh unit as for the launch: the sentence is the home rule's, not another refusal that came first");
        unit.Bed.Launcher.Requests.Count.Should().Be(before, "no refresh unit was started, so none ran with the owner's refresh token and the home bound back");
    }

    // ---------- S73, S72, S74: the two looks at the worktree's toolchain file ----------

    [Fact]
    public async Task S73_ACheckThatMakesTheToolchainFileUnreadable_EndsTheJobWithAStopWordAndAnEnvelope_NotRunnerError()
    {
        var toolchain = ToolchainFixture.Shell("chmod 000 .coai/toolchain.yaml; echo built", "echo tested");
        await using var world = await CodexRigWorld.CreateAsync(toolchain);
        var rig = world.Rig;
        rig.SaysRaw("terra", Turn(Writes(CoderRig.Green), Report()));
        var job = rig.NewJob(route: ["codex/terra"]);

        var (_, ended) = await rig.RunAsync(job).WaitAsync(TimeSpan.FromSeconds(90), TestContext.Current.CancellationToken);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, "checks-changed-tree"), ended.Reason);
        File.Exists(Path.Combine(rig.World.Store.JobsRoot, job.JobId, "result.json")).Should().BeTrue("the job has an envelope, and an attempt in the record");
        ended.Attempts.Should().NotBeEmpty();
    }

    [Fact]
    public async Task S74_ACheckThatMakesAFifoInARoundThatPasses_EndsTheJobNotGreen_ThePostRoundLookIsTheLineThatStopsIt()
    {
        // E4 joined: the head's own toolchain file runs (read from the commit), so it is NOT deleted here — a deleted file would run nothing. The check replaces the tracked file by a FIFO.
        var toolchain = ToolchainFixture.Shell("rm -f .coai/toolchain.yaml && /usr/bin/mkfifo .coai/toolchain.yaml; echo built", "echo tested");
        await using var world = await CodexRigWorld.CreateAsync(toolchain);
        var rig = world.Rig;
        var turn = (Dictionary<string, object?>)Turn(Writes(CoderRig.Green), Report());
        rig.SaysRaw("terra", turn);
        var job = rig.NewJob(route: ["codex/terra"]);

        var (_, ended) = await rig.RunAsync(job).WaitAsync(TimeSpan.FromSeconds(90), TestContext.Current.CancellationToken);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, "checks-changed-tree"), $"every step passed, and the job is still not green: {ended.Reason}");
        ended.Reason.Should().Contain("not trusted").And.Contain("this round", "the look after the round says that a check of this round left it");
    }

    [Fact]
    public async Task S72_TheLookBeforeTheRound_SaysThatTheCodersTurnLeftIt_NoCheckRanAfterTheTurn_AndRunsNoStep()
    {
        await using var world = await CodexRigWorld.CreateAsync(string.Empty);
        var rig = world.Rig;
        var turn = (Dictionary<string, object?>)Turn(Writes(CoderRig.Green), Report());
        turn["cwdFifos"] = new[] { ToolchainName };
        rig.SaysRaw("terra", turn);
        var job = rig.NewJob(route: ["codex/terra"]);

        var (_, ended) = await rig.RunAsync(job).WaitAsync(TimeSpan.FromSeconds(90), TestContext.Current.CancellationToken);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, "checks-changed-tree"), ended.Reason);
        ended.Reason.Should().Contain("nothing was run").And.Contain("turn").And.NotContain("or a check", "no check ran between the end of the turn and this look, so the sentence does not name one");
        var logs = Path.Combine(rig.World.Store.JobsRoot, job.JobId, "checks", "round-0");
        (Directory.Exists(logs) ? Directory.GetFileSystemEntries(logs) : []).Should().BeEmpty("no step was run in the round");
    }

    [Fact]
    public void S74_TheJobsCopy_IsReadWithNoFollow_AndTheFollowingReadStaysForTheRealFile()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "a symbolic link needs a privilege on Windows");
        using var temp = TempDir.For("s74-");
        var real = Path.Combine(temp.Path, "real.json");
        File.WriteAllText(real, AgyCoderBed.SignInFile(DateTime.UtcNow.AddHours(1)));
        var link = Path.Combine(temp.Path, "copy.json");
        File.CreateSymbolicLink(link, real);
        var recipe = (CoderSignInRecipe.CopyForJob)AgyCoderBed.Vendor().SignIn;

        SignInCopy.ExpiryOfFile(recipe, link, noFollow: true).Should().BeNull("the job's copy is in a folder a unit can write: a link there is never followed");
        SignInCopy.ExpiryOfFile(recipe, link).Should().NotBeNull("the person's own file may be a link they made, and is followed as before");
    }
}
