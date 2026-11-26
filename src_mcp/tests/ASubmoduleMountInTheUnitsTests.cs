using CoaiMcp.Core.Toolchain;
using CoaiMcp.Runners.Toolchain;
using CoaiMcp.Runners.Worktrees;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E2 s6, the recorded change: a submodule's content at the base commit is bound READ-ONLY at its worktree path in the units of a coder job. The boundary must not widen otherwise, so the first two tests compare the property list with the one E2 recorded
/// (<c>RESULTS_s2_check_boundary.md</c>: t4 for the checks, t5 for the coder), element by element: with no mount it is that list, with one it is that list and ONE more element. The rest read the effect off real units (skipped, with the reason, where there is no
/// user manager).
/// </summary>
public sealed class ASubmoduleMountInTheUnitsTests
{
    /// <summary>The systemd-run argument list without one property: the <c>-p</c> before it goes too, or the next argument would be read as its value.</summary>
    private static IReadOnlyList<string> WithoutProperty(IReadOnlyList<string> arguments, string startsWith)
    {
        var list = arguments.ToList();
        var at = list.FindIndex(argument => argument.StartsWith(startsWith, StringComparison.Ordinal));
        if (at > 0)
        {
            list.RemoveRange(at - 1, 2);
        }

        return list;
    }

    private static void NeedAUserManager() => Assert.SkipUnless(SystemdUser.Works(out var why), why);

    private const string W = "/w/coai-coder-j1";

    private const string M = "/w/main";

    private const string J = "/w/job";

    private const string TempName = "tmp";

    private static BoundaryHost NoHome() => new(BoundaryPlatform.Linux, "/nonexistent/home", string.Empty, string.Empty, "systemd-run", "systemctl", string.Empty, string.Empty);

    private static BoundaryJob Job(params ReadOnlyMount[] mounts) =>
        new("j1", new CoderTree(W, M, "gitdir: /w/main/.git/worktrees/j1\n"), J, NetworkChoice.None("t"), NetworkChoice.None("t"), 2000) { Mounts = mounts };

    private static readonly ReadOnlyMount OneMount = new("/w/job.mounts/m0", W + "/.agents/conventions");

    private static readonly string[] E2Isolation =
    [
        "ProtectSystem=strict", "ProtectHome=tmpfs", "TemporaryFileSystem=/run/user", "TemporaryFileSystem=/mnt", "TemporaryFileSystem=/dev/shm", "InaccessiblePaths=-/run/WSL",
    ];

    private static readonly string[] E2Hardening =
    [
        "NoNewPrivileges=yes", "PrivateUsers=yes", "RestrictSUIDSGID=yes", "LockPersonality=yes", "ProtectKernelTunables=yes", "ProtectControlGroups=yes", "Nice=10", "LimitCORE=0",
    ];

    private static readonly string[] E2Limits = ["RuntimeMaxSec=300", "MemoryMax=2000M", "MemorySwapMax=0"];

    /// <summary>What a check unit has of the job folder since the coder's folder was taken out of it: an empty read-only tmpfs over it, and the folders the checks use bound on top — the one change to the list E2 recorded.</summary>
    private static readonly string[] JobView =
    [
        $"TemporaryFileSystem={J}:ro", $"BindPaths={J}/home", $"BindPaths={J}/nuget-packages", $"BindPaths={J}/npm-cache", $"BindPaths={J}/dotnet-home",
    ];

    /// <summary>The person's sign-in file at the (stand-in) real home's path, hidden from a check unit: the one property a check unit got besides the job view and the pids.</summary>
    private static readonly string[] SignInHidden = ["InaccessiblePaths=-/nonexistent/home/.claude/.credentials.json"];

    [Fact]
    public void TheCover_IsTheFolderThatHoldsTheJobFolders_WhenItCanBeCovered_AndTheJobFolderItselfWhenItCannot()
    {
        // /w holds the worktree here, so it cannot be covered: the job folder is.
        Job().JobCover.Should().Be(J);

        BoundaryJob At(string root, string worktree, string main) =>
            new("j1", new CoderTree(worktree, main, "gitdir: x\n"), root, NetworkChoice.None("t"), NetworkChoice.None("t"), 2000);

        At("/data/jobs/j1", "/w/coai-coder-j1", "/w/main").JobCover.Should().Be("/data/jobs", "a folder of its own: another job's folders are under the cover too");
        At("/j1", "/w/coai-coder-j1", "/w/main").JobCover.Should().Be("/j1", "the root of the file system is never covered");
        At(BoundaryUnit.UnitTemp + "/j1", "/w/coai-coder-j1", "/w/main").JobCover.Should().Be(BoundaryUnit.UnitTemp + "/j1", "/tmp is where the units bind their own temp folder");
        At("/data/jobs/j1", "/data/jobs/coai-coder-j1", "/w/main").JobCover.Should().Be("/data/jobs/j1", "the folder holds the worktree, which the unit is given at that path");
        At("/data/jobs/j1", "/w/coai-coder-j1", "/data/jobs/main").JobCover.Should().Be("/data/jobs/j1", "the folder holds the main checkout, which the unit is given at that path");
    }

    [Fact]
    public void TheChecksUnit_HidesTheKeyFolderAndTheKeysFile_WhereverTheyLie_AsTheCodersUnitDoes()
    {
        var job = Job() with { KeysFolder = "/var/lib/coai/keys", KeysFile = "/srv/coai.keys" };

        var properties = BoundaryUnit.Properties(NoHome(), job, CheckNetwork.Host, TimeSpan.FromMinutes(5));

        properties.Should().Contain(["InaccessiblePaths=-/var/lib/coai/keys", "InaccessiblePaths=-/srv/coai.keys"]);
        BoundaryUnit.KeysHidden(job).Should().Equal(["InaccessiblePaths=-/var/lib/coai/keys", "InaccessiblePaths=-/srv/coai.keys"]);
        CoderUnit.KeysHidden(NoHome() with { BindSignIn = false }, job).Should().Equal(BoundaryUnit.KeysHidden(job), "one list for both units");
        CoderUnit.KeysHidden(NoHome() with { BindSignIn = true }, job).Should().BeEmpty("a launch that binds the person's sign-in has no key to hide it from");
    }

    [Fact]
    public void TheChecksUnit_BindsNoPartOfTheJobFolderButTheFoldersTheChecksUse_AndNeverTheCodersFolder()
    {
        var job = Job();

        var properties = BoundaryUnit.Properties(NoHome(), job, CheckNetwork.Private, TimeSpan.FromMinutes(5));

        properties.Should().NotContain($"BindPaths={J}", "the whole job folder is no longer bound: the coder's home and config are in it");
        properties.Where(property => property.Contains(job.CoderFolder, StringComparison.Ordinal)).Should().BeEmpty("no property of a check unit names the coder's folder");
        properties.Should().Contain($"TemporaryFileSystem={J}:ro");
        job.CheckFolders.Should().NotContain(folder => folder.StartsWith(job.CoderFolder, StringComparison.Ordinal));
        job.CheckFolders.Should().OnlyContain(folder => properties.Contains($"BindPaths={folder}"));
        var cover = properties.ToList().IndexOf($"TemporaryFileSystem={J}:ro");
        job.CheckFolders.Should().OnlyContain(folder => properties.ToList().IndexOf($"BindPaths={folder}") > cover, "a folder is bound after the tmpfs that would otherwise hide it");
    }

    [Fact]
    public void TheChecksUnit_WithNoMount_IsTheListE2Recorded_AndWithOne_ThatListPlusOneReadOnlyBindAndNothingElse()
    {
        string[] e2 =
        [
            .. E2Isolation,
            $"BindPaths={W}", $"BindReadOnlyPaths={M}", $"BindReadOnlyPaths={W}/.git", .. JobView, $"BindPaths={J}/{TempName}:{BoundaryUnit.UnitTemp}", $"BindPaths={J}/vartmp:{BoundaryUnit.UnitVarTemp}",
            .. SignInHidden,
            .. E2Hardening,
            "PrivateNetwork=yes",
            .. E2Limits,
        ];

        BoundaryUnit.Properties(NoHome(), Job(), CheckNetwork.Private, TimeSpan.FromMinutes(5)).Should().Equal(e2, "no mount: not one element more or less than E2's list");

        var with = BoundaryUnit.Properties(NoHome(), Job(OneMount), CheckNetwork.Private, TimeSpan.FromMinutes(5));
        with.Should().HaveCount(e2.Length + 1);
        with.Except([$"BindReadOnlyPaths={OneMount.Source}:{OneMount.Target}"]).Should().Equal(e2, "every other element is E2's, in E2's order");
        with.Should().Contain($"BindReadOnlyPaths={OneMount.Source}:{OneMount.Target}");
        with.Where(property => property.StartsWith("BindPaths=", StringComparison.Ordinal) && property.Contains(OneMount.Source, StringComparison.Ordinal)).Should().BeEmpty("the source is never bound writable");
    }

    [Fact]
    public void TheCodersUnit_WithNoMount_IsTheListE2Recorded_AndWithOne_ThatListPlusOneReadOnlyBindAndNothingElse()
    {
        var program = new CoderProgram("/usr/bin/true", string.Empty, string.Empty);
        string[] e2 =
        [
            .. E2Isolation,
            $"BindReadOnlyPaths={M}", "BindReadOnlyPaths=/w", $"BindPaths={W}", $"BindReadOnlyPaths={W}/.git", $"TemporaryFileSystem={J}:ro", $"BindPaths={J}/coder", $"BindPaths={J}/coder/{TempName}:{BoundaryUnit.UnitTemp}", $"BindPaths={J}/coder/vartmp:{BoundaryUnit.UnitVarTemp}",
            .. E2Hardening,
            "PrivateNetwork=no",
            .. E2Limits,
        ];

        CoderUnit.Properties(NoHome(), Job(), program, TimeSpan.FromMinutes(5)).Should().Equal(e2);

        var with = CoderUnit.Properties(NoHome(), Job(OneMount), program, TimeSpan.FromMinutes(5));
        with.Should().HaveCount(e2.Length + 1);
        with.Except([$"BindReadOnlyPaths={OneMount.Source}:{OneMount.Target}"]).Should().Equal(e2);
        with.Should().Contain($"BindReadOnlyPaths={OneMount.Source}:{OneMount.Target}");
    }

    [Fact]
    public void AMountThatCouldWidenTheBoundary_RefusesTheJobBeforeAnyUnitIsDescribed()
    {
        Job(new ReadOnlyMount("/w/job/inside-the-job-folder", W + "/x")).Problem().Should().Contain("mount");
        Job(new ReadOnlyMount("/w/job.mounts/m0", "/w/elsewhere")).Problem().Should().Contain("mount", "a target outside the worktree");
        Job(new ReadOnlyMount("/w/job.mounts/m 0", W + "/x")).Problem().Should().Contain("mount", "a space cannot be spelled into a bind");
        Job(new ReadOnlyMount("/w/job.mounts/m0", W + "/a:b")).Problem().Should().Contain("mount");
        Job(new ReadOnlyMount(W + "/inside", W + "/x")).Problem().Should().Contain("mount", "a source inside the writable worktree could be changed by the coder");
        Job(OneMount).Problem().Should().BeEmpty("a mount of the right shape is no problem on its own (its folder is looked at by the admission)");
    }

    [Fact]
    public async Task InACheckUnit_TheMountIsReadableAtTheWorktreePath_EveryWriteIntoItIsRefused_AndTheWorktreeAroundItStaysWritable()
    {
        NeedAUserManager();
        await using var bed = await BoundaryBed.CreateAsync();
        var mount = bed.Mount(".agents/conventions", ("rules/a.md", "rule one\n"), ("tools/x.mjs", "x\n"));

        var run = await bed.ShellAsync("""
            cat .agents/conventions/rules/a.md
            if { echo changed > .agents/conventions/rules/a.md; } 2>/dev/null; then echo OVERWRITE-LANDED; else echo overwrite-refused; fi
            if { echo new > .agents/conventions/new.txt; } 2>/dev/null; then echo NEW-LANDED; else echo new-refused; fi
            if rm -f .agents/conventions/tools/x.mjs 2>/dev/null && [ ! -e .agents/conventions/tools/x.mjs ]; then echo REMOVE-LANDED; else echo remove-refused; fi
            if rmdir .agents/conventions 2>/dev/null; then echo RMDIR-LANDED; else echo rmdir-refused; fi
            echo mine > around.txt && echo around-written
            """);

        run.Result.ExitCode.Should().Be(0, run.Result.StdErr);
        run.Result.StdOut.Should().Contain("rule one").And.Contain("overwrite-refused").And.Contain("new-refused").And.Contain("remove-refused").And.Contain("rmdir-refused").And.Contain("around-written");
        run.Result.StdOut.Should().NotContain("LANDED");
        File.ReadAllText(Path.Combine(mount.Source, "rules", "a.md")).Should().Be("rule one\n", "what the unit could not change is byte for byte what coai provided");
        File.Exists(Path.Combine(mount.Source, "new.txt")).Should().BeFalse();
        run.Record.Properties.Should().Contain($"BindReadOnlyPaths={mount.Source}:{mount.Target}");
    }

    [Fact]
    public async Task InACheckUnit_TheMountsSourceFolderIsNotReachableByItsHostPath_SoNoWriteCanGoAroundTheReadOnlyBind()
    {
        NeedAUserManager();
        await using var bed = await BoundaryBed.CreateAsync();
        var mount = bed.Mount(".agents/conventions", ("rules/a.md", "rule one\n"));

        var run = await bed.ShellAsync($$"""
            if [ -e '{{mount.Source}}' ]; then echo SOURCE-VISIBLE; else echo source-hidden; fi
            if { echo x > '{{mount.Source}}/rules/a.md'; } 2>/dev/null; then echo SOURCE-WRITTEN; else echo source-write-refused; fi
            if { echo x > '{{bed.Job.JobRoot}}.mounts/m0-probe'; } 2>/dev/null; then echo BESIDE-JOB-WRITTEN; else echo beside-job-refused; fi
            """);

        run.Result.StdOut.Should().Contain("source-hidden").And.Contain("source-write-refused").And.NotContain("VISIBLE").And.NotContain("WRITTEN");
        File.ReadAllText(Path.Combine(mount.Source, "rules", "a.md")).Should().Be("rule one\n");
    }

    [Fact]
    public async Task ThePreflight_AsksTheOS_WhetherTheMountIsThereAndReadOnly_AndRefusesTheJobWhenThePropertyIsGone()
    {
        NeedAUserManager();
        await using var bed = await BoundaryBed.CreateAsync();
        var mount = bed.Mount(".agents/conventions", ("rules/a.md", "rule one\n"));

        var admitted = (await bed.Boundary().AdmitAsync(bed.Job, [CheckNetwork.Private])).Should().BeOfType<BoundaryAdmission.Admitted>().Subject;
        var row = admitted.Records[CheckNetwork.Private].Preflight.Single(item => item.Item == "submodules-readonly");
        row.Held.Should().BeTrue(row.Detail);

        var refused = await bed.Boundary(arguments => WithoutProperty(arguments, $"BindReadOnlyPaths={mount.Source}")).AdmitAsync(bed.Job, [CheckNetwork.Private]);
        var no = refused.Should().BeOfType<BoundaryAdmission.Refused>(refused.ToString()).Subject;
        no.Sentence.Should().Contain("submodules-readonly");
        no.Record.Preflight.Single(item => item.Item == "submodules-readonly").Held.Should().BeFalse();
    }

    [Fact]
    public async Task ARunWithNoMount_HasNoSubmodulesRow_SoE2sPreflightIsUnchanged()
    {
        NeedAUserManager();
        await using var bed = await BoundaryBed.CreateAsync();

        var admitted = (await bed.Boundary().AdmitAsync(bed.Job, [CheckNetwork.Private])).Should().BeOfType<BoundaryAdmission.Admitted>().Subject;

        admitted.Records[CheckNetwork.Private].Preflight.Select(row => row.Item).Should().NotContain("submodules-readonly");
    }

    [Fact]
    public async Task InTheCodersUnit_TheMountIsReadableAndReadOnly_AWriteIntoItIsRefusedByTheOS_AndTheWorktreeAroundItIsWritable()
    {
        NeedAUserManager();
        await using var bed = await CoderBed.CreateAsync();
        var mount = bed.Inner.Mount(".agents/conventions", ("rules/a.md", "rule one\n"));
        var inside = Path.Combine(mount.Target, "coder-wrote-this.txt");

        var outcome = await bed.Turns().RunAsync(bed.Job, bed.Fake("write-files", Path.Combine(bed.Worktree, "mine.txt"), inside), "c1");

        var stopped = outcome.Should().BeOfType<CoderTurnOutcome.WriteStopped>(outcome.ToString()).Subject;
        stopped.Sentence.Should().Contain(inside, "the OS refused the write and the answer names the path");
        File.Exists(Path.Combine(mount.Source, "coder-wrote-this.txt")).Should().BeFalse();
        File.Exists(Path.Combine(mount.Target, "coder-wrote-this.txt")).Should().BeFalse();
        File.ReadAllText(Path.Combine(bed.Worktree, "mine.txt")).Should().Be("written by the fake coder\n");

        var read = await bed.RunLaunchAsync(bed.Shell("cat .agents/conventions/rules/a.md"), "c2");
        read.Result.StdOut.Should().Contain("rule one", "the coder can read the rules it works under");
        read.Record.Properties.Should().Contain($"BindReadOnlyPaths={mount.Source}:{mount.Target}");
    }
}
