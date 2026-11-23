using CoaiMcp.Core.Toolchain;
using CoaiMcp.Runners.Collecting;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Toolchain;
using CoaiMcp.Runners.Worktrees;
using CoaiMcp.Work;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E2 s2 t4, DoD d5: coai's own git commands on a coder worktree pass <c>-c core.hooksPath=/dev/null -c core.fsmonitor=false</c>, run with the worktree ROOT as their working folder, never run <c>git submodule update</c> there, the base
/// toolchain is read from the MAIN repository path and not through the worktree's <c>.git</c> pointer, and a changed pointer is seen before each git command (<c>RESULTS_coder_write_boundary_judgement.md</c> must-change 3).
/// </summary>
/// <remarks>
/// <para><b>The hardening is shown by its EFFECT.</b> The main repository's config names a hooks folder and an fsmonitor command, each a script that writes a canary file. A plain git command in the tree runs them (the positive control, on the same
/// repository); the same command through the hardened door runs neither. Real git, temp folders only.</para>
/// </remarks>
public sealed class ACoderGitIsHardenedTests
{
    private static readonly string[] Quiet = ["--no-optional-locks"];

    private static async Task<(string Hooks, string FsmonitorCanary, string HookCanary)> PlantTrapsAsync(BoundaryBed bed)
    {
        var folder = Path.Combine(bed.Parent(), "traps");
        Directory.CreateDirectory(folder);
        var hookCanary = Path.Combine(folder, "hook-ran");
        var fsmonitorCanary = Path.Combine(folder, "fsmonitor-ran");
        var hooks = Path.Combine(folder, "hooks");
        Directory.CreateDirectory(hooks);
        var hook = Path.Combine(hooks, "pre-commit");
        await File.WriteAllTextAsync(hook, $"#!/bin/sh\necho ran >> '{hookCanary}'\n");
        var monitor = Path.Combine(folder, "monitor.sh");
        await File.WriteAllTextAsync(monitor, $"#!/bin/sh\necho ran >> '{fsmonitorCanary}'\nprintf '\\0'\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(hook, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(monitor, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        foreach (var (key, value) in new[] { ("user.email", "t@t"), ("user.name", "t"), ("commit.gpgsign", "false") })
        {
            await bed.Launcher.RunAsync(new ProcessRequest("git", ["config", key, value], bed.Main));
        }

        await bed.Launcher.RunAsync(new ProcessRequest("git", ["config", "core.hooksPath", hooks], bed.Main));
        await bed.Launcher.RunAsync(new ProcessRequest("git", ["config", "core.fsmonitor", monitor], bed.Main));

        return (hooks, fsmonitorCanary, hookCanary);
    }

    private static async Task<ProcessResult> PlainGitAsync(BoundaryBed bed, params string[] args) =>
        await bed.Launcher.RunAsync(new ProcessRequest("git", [.. args], bed.Worktree));

    [Fact]
    public async Task HooksAndFsmonitorOfTheRepositoryConfig_RunForAPlainGitCommand_AndNotThroughTheHardenedDoor()
    {
        await using var bed = await BoundaryBed.CreateAsync();
        var (_, fsmonitorCanary, hookCanary) = await PlantTrapsAsync(bed);
        var git = new GitHistory(bed.Launcher);

        var plainStatus = await PlainGitAsync(bed, "status", "--porcelain");
        var plainCommit = await PlainGitAsync(bed, "commit", "--allow-empty", "-m", "plain");
        File.Exists(fsmonitorCanary).Should().BeTrue($"POSITIVE CONTROL: a plain git status runs the fsmonitor command of the repository config ({plainStatus.StdErr})");
        File.Exists(hookCanary).Should().BeTrue($"POSITIVE CONTROL: a plain git commit runs the pre-commit hook of core.hooksPath ({plainCommit.StdErr})");
        File.Delete(fsmonitorCanary);
        File.Delete(hookCanary);

        var status = await git.InCoderTreeAsync(bed.Tree, ["status", "--porcelain"]);
        var commit = await git.InCoderTreeAsync(bed.Tree, ["commit", "--allow-empty", "-m", "hardened"]);

        status.Ran.Should().BeTrue(status.Refusal);
        status.Ok.Should().BeTrue();
        commit.Ran.Should().BeTrue();
        File.Exists(fsmonitorCanary).Should().BeFalse("core.fsmonitor=false: the repository's fsmonitor command was not run");
        File.Exists(hookCanary).Should().BeFalse("core.hooksPath=/dev/null: the pre-commit hook was not run");
    }

    [Fact]
    public async Task EveryCommand_HasTheFlagsFirst_TheTreeRootAsItsFolder_AndNoGitDirOverrideInItsEnvironment()
    {
        await using var bed = await BoundaryBed.CreateAsync();
        var git = new GitHistory(bed.Launcher);
        var before = bed.Launcher.Requests.Count;

        await git.InCoderTreeAsync(bed.Tree, ["rev-parse", "HEAD"]);
        await git.InCoderTreeAsync(bed.Tree, [.. Quiet, "status", "--porcelain"]);

        var requests = bed.Launcher.Requests.Skip(before).ToList();
        requests.Should().HaveCount(2);
        foreach (var request in requests)
        {
            request.Executable.Should().Be("git");
            request.WorkingDirectory.Should().Be(bed.Tree.Root, "the worktree ROOT, never a folder below it and never the main checkout");
            request.Arguments.Take(4).Should().Equal("-c", "core.hooksPath=/dev/null", "-c", "core.fsmonitor=false");
            foreach (var name in new[] { "GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE", "GIT_COMMON_DIR", "GIT_OBJECT_DIRECTORY", "GIT_ALTERNATE_OBJECT_DIRECTORIES" })
            {
                request.Environment.Should().ContainKey(name).WhoseValue.Should().BeNull($"{name} is taken out of the environment, so the tree and not a variable decides which repository git reads");
            }
        }
    }

    [Theory]
    [InlineData("submodule", "update", "--init")]
    [InlineData("submodule", "foreach", "true")]
    [InlineData("-c", "core.hooksPath=elsewhere", "status")]
    [InlineData("--git-dir=/elsewhere", "status")]
    [InlineData("-C", "/elsewhere", "status")]
    [InlineData("--exec-path=/elsewhere", "status")]
    [InlineData("--work-tree=/elsewhere", "status")]
    public async Task ASubmoduleCommand_OrAnOptionThatMovesGit_IsRefusedBeforeAProcessStarts(params string[] args)
    {
        await using var bed = await BoundaryBed.CreateAsync();
        var git = new GitHistory(bed.Launcher);
        var before = bed.Launcher.Requests.Count;

        var answer = await git.InCoderTreeAsync(bed.Tree, args);

        answer.Ran.Should().BeFalse();
        answer.Ok.Should().BeFalse();
        answer.Refusal.Should().NotBeEmpty();
        bed.Launcher.Requests.Count.Should().Be(before, "no process was started");
    }

    [Fact]
    public async Task AChangedGitPointer_IsSeenBeforeTheCommand_AndNoProcessStarts()
    {
        await using var bed = await BoundaryBed.CreateAsync();
        var git = new GitHistory(bed.Launcher);
        (await git.InCoderTreeAsync(bed.Tree, ["rev-parse", "HEAD"])).Ok.Should().BeTrue("POSITIVE CONTROL: with the pointer as written the command runs");
        var before = bed.Launcher.Requests.Count;
        var original = File.ReadAllText(bed.Tree.GitEntry);

        foreach (var change in new[] { "gitdir: /somewhere/else/.git/worktrees/x\n", original + "\n\n", "not a pointer\n" })
        {
            await File.WriteAllTextAsync(bed.Tree.GitEntry, change);

            var answer = await git.InCoderTreeAsync(bed.Tree, ["rev-parse", "HEAD"]);

            answer.Ran.Should().BeFalse();
            answer.Refusal.Should().Contain(".git pointer").And.Contain("changed");
        }

        File.Delete(bed.Tree.GitEntry);
        (await git.InCoderTreeAsync(bed.Tree, ["rev-parse", "HEAD"])).Refusal.Should().Contain("not there", "a missing pointer is a changed one");
        bed.Launcher.Requests.Count.Should().Be(before, "not one git command started once the pointer was changed");
    }

    [Fact]
    public async Task APointerReplacedByAFolderOrALink_IsRefused()
    {
        await using var bed = await BoundaryBed.CreateAsync();
        var git = new GitHistory(bed.Launcher);
        File.Delete(bed.Tree.GitEntry);
        Directory.CreateDirectory(bed.Tree.GitEntry);

        var asFolder = await git.InCoderTreeAsync(bed.Tree, ["rev-parse", "HEAD"]);

        asFolder.Ran.Should().BeFalse();
        asFolder.Refusal.Should().Contain("not a regular file");
        Directory.Delete(bed.Tree.GitEntry);
        File.CreateSymbolicLink(bed.Tree.GitEntry, bed.Main + "/.git");
        var asLink = await git.InCoderTreeAsync(bed.Tree, ["rev-parse", "HEAD"]);
        asLink.Ran.Should().BeFalse();
        asLink.Refusal.Should().Contain("not a regular file");
    }

    [Fact]
    public async Task APointerThatPointsOutsideTheMainCheckoutsWorktrees_IsNotTrustedWhenTheTreeIsOpened()
    {
        await using var bed = await BoundaryBed.CreateAsync();
        var original = File.ReadAllText(bed.Tree.GitEntry);
        await File.WriteAllTextAsync(bed.Tree.GitEntry, "gitdir: " + bed.Parent() + "/another/.git/worktrees/x\n");
        try
        {
            var opening = CoderTree.Open(bed.Worktree, bed.Main);

            opening.Should().BeOfType<CoderTreeOpening.Refused>().Which.Sentence.Should().Contain("not a worktree folder of");
            CoderTree.Open(bed.Worktree, bed.Parent()).Should().BeOfType<CoderTreeOpening.Refused>("the pointer points into another repository than the one named as main");
        }
        finally
        {
            await File.WriteAllTextAsync(bed.Tree.GitEntry, original);
        }

        CoderTree.Open(bed.Worktree, bed.Main).Should().BeOfType<CoderTreeOpening.Opened>("POSITIVE CONTROL: the pointer as git wrote it is trusted");
    }

    private const string ToolchainText = "version: 1\nprojects:\n  - path: \"app\"\n    adapter: dotnet\n    build:\n      - \"/bin/sh\"\n      - \"-c\"\n      - \"echo built\"\n    test:\n      - \"/bin/sh\"\n      - \"-c\"\n      - \"echo tested\"\n";

    [Fact]
    public async Task TheCoderRoad_ReadsTheBaseToolchainFromTheMainRepositoryPath_NeverThroughTheWorktree()
    {
        Assert.SkipUnless(SystemdUser.Works(out var why), why);
        await using var bed = await BoundaryBed.CreateAsync(ToolchainText);
        using var logs = TempDir.For("coai-cb-d5-");
        var checks = new ChecksAtBase(new GitHistory(bed.Launcher), new CheckRunner(bed.Launcher, bed.Boundary()));
        var before = bed.Launcher.Requests.Count;

        var result = await checks.RunCoderJobAsync(new DiskWorkFolder(bed.Worktree), bed.BaseSha, bed.Job, logs.At("run"), 2);

        result.Green.Should().BeTrue(result.Note);
        var gitRequests = bed.Launcher.Requests.Skip(before).Where(request => request.Executable == "git").ToList();
        gitRequests.Should().NotBeEmpty("the toolchain was read with git");
        gitRequests.Should().OnlyContain(request => request.WorkingDirectory == bed.Main, "every read of the base toolchain started in the main repository path");
        result.Steps.Should().HaveCount(2);
        result.Steps.Should().OnlyContain(step => step.Boundary.Bounded);
    }

    [Fact]
    public async Task TheCoderRoad_RefusesAChangedPointerBeforeAnyStep_AndDistrustsAResultWhosePointerChangedDuringTheChecks()
    {
        Assert.SkipUnless(SystemdUser.Works(out var why), why);
        await using var bed = await BoundaryBed.CreateAsync(ToolchainText);
        using var logs = TempDir.For("coai-cb-d5-");
        var original = File.ReadAllText(bed.Tree.GitEntry);
        var sneaky = new WatchedLauncher(bed.Launcher, request =>
        {
            // Simulates a boundary that failed: the pointer changes while the build step runs.
            if (request.Arguments.Contains($"coai-check-{bed.JobId}-s1"))
            {
                File.WriteAllText(bed.Tree.GitEntry, "gitdir: /somewhere/else\n");
            }

            return null;
        });
        var checks = new ChecksAtBase(new GitHistory(sneaky), new CheckRunner(sneaky, new CheckBoundary(sneaky, bed.Host)));

        var during = await checks.RunCoderJobAsync(new DiskWorkFolder(bed.Worktree), bed.BaseSha, bed.Job, logs.At("run"), 2);

        during.State.Should().Be(CheckState.Failed, "the steps may have exited 0, and the result still is not trusted");
        during.Note.Should().Contain(".git pointer").And.Contain("not trusted");
        var launches = bed.Launcher.Requests.Count;

        var before = await checks.RunCoderJobAsync(new DiskWorkFolder(bed.Worktree), bed.BaseSha, bed.Job, logs.At("again"), 2);

        before.State.Should().Be(CheckState.BoundaryRefused);
        before.Note.Should().Contain(".git pointer").And.Contain("Nothing was run");
        bed.Launcher.Requests.Count.Should().Be(launches, "no process started: the pointer was compared before the run");
        await File.WriteAllTextAsync(bed.Tree.GitEntry, original);
    }
}
