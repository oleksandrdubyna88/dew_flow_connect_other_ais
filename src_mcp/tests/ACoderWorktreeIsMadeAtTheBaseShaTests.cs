using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Worktrees;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E2 s2 t2, DoD d1: a start makes the branch <c>coder/&lt;jobId&gt;</c> and the folder <c>coai-coder-&lt;jobId&gt;</c> at exactly the given base sha, outside the main
/// checkout; the job id is the only free part of both names, and a name that would leave the root is refused BEFORE git runs.
/// </summary>
/// <remarks>Real git over temp repositories. "Before git runs" is read off the launcher's own record of requests, not inferred from the answer.</remarks>
public sealed class ACoderWorktreeIsMadeAtTheBaseShaTests
{
    [Fact]
    public async Task AStart_MakesTheBranchAndTheFolder_AtExactlyTheGivenSha_OutsideTheMainCheckout()
    {
        await using var bed = await CoderTestBed.CreateAsync();
        var id = CoderTestBed.JobId(1);

        var outcome = await bed.Coder.StartAsync(bed.Repo.Path, id, bed.Shas[0]);

        var started = outcome.Should().BeOfType<CoderWorktreeOutcome.Started>(outcome.ToString()).Subject;
        started.Branch.Should().Be($"coder/{id}");
        started.BaseSha.Should().Be(bed.Shas[0]);
        Path.GetFileName(started.Path).Should().Be($"coai-coder-{id}");
        WorktreePaths.Same(Path.GetDirectoryName(started.Path)!, bed.Root).Should().BeTrue("the folder is made directly under the root it was given");
        Path.GetRelativePath(WorktreePaths.Real(bed.Repo.Path), WorktreePaths.Real(started.Path)).Should().StartWith("..", "the worktree is outside the main checkout");
        (await bed.GitIn(started.Path, "rev-parse", "HEAD")).Should().Be(bed.Shas[0], "HEAD of the new tree is the base, not the tip of the main branch");
        (await bed.GitIn(bed.Repo.Path, "rev-parse", $"refs/heads/coder/{id}")).Should().Be(bed.Shas[0]);
        (await bed.GitIn(started.Path, "branch", "--show-current")).Should().Be($"coder/{id}");
        File.ReadAllText(Path.Combine(started.Path, "a.txt")).Should().Be("one\n", "the files are those of the base commit, not of the dirty checkout");
        File.Exists(Path.Combine(started.Path, "b.txt")).Should().BeFalse("b.txt arrived after the base");
        (await bed.GitIn(started.Path, "status", "--porcelain")).Should().BeEmpty("the tree is a clean checkout of the base");
    }

    [Fact]
    public void TheJobId_IsTheOnlyFreePartOfBothNames()
    {
        var one = CoderTestBed.JobId(1);
        var two = CoderTestBed.JobId(2);

        CoderWorktree.FolderOf(one).Should().Be($"coai-coder-{one}");
        CoderWorktree.BranchOf(one).Should().Be($"coder/{one}");
        CoderWorktree.FolderOf(two).Replace(two, "#").Should().Be(CoderWorktree.FolderOf(one).Replace(one, "#"), "only the id differs");
        CoderWorktree.BranchOf(two).Replace(two, "#").Should().Be(CoderWorktree.BranchOf(one).Replace(one, "#"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("../x")]
    [InlineData("..\\x")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\temp\\x")]
    [InlineData("r1/../../escape")]
    [InlineData("R20261123101010-0000000A")]
    [InlineData("-detach")]
    [InlineData("--upload-pack=x")]
    [InlineData("a b")]
    [InlineData("ab\n")]
    [InlineData("a\nb")]
    [InlineData("a\0b")]
    [InlineData("x.lock")]
    [InlineData("x..y")]
    [InlineData("x@{y}")]
    [InlineData("x~1")]
    [InlineData("x:y")]
    [InlineData("\u00e9")]
    public async Task AJobId_ThatCouldLeaveTheRootOrBreakABranchName_IsRefusedBeforeGitRuns(string id)
    {
        await using var bed = await CoderTestBed.CreateAsync();
        var before = bed.Launcher.Requests.Count;
        var parentBefore = Directory.GetFileSystemEntries(bed.Parent).Order().ToList();

        var outcome = await bed.Coder.StartAsync(bed.Repo.Path, id, bed.Shas[0]);

        outcome.Should().BeOfType<CoderWorktreeOutcome.Refused>().Which.Reason.Should().Be(CoderWorktreeRefusal.BadJobId);
        bed.Launcher.Requests.Count.Should().Be(before, "no process may start for a name that is refused");
        Directory.GetFileSystemEntries(bed.Parent).Order().Should().Equal(parentBefore, "nothing was made beside or above the root, nor the root itself");
        CoderWorktree.IsJobId(id).Should().BeFalse();
    }

    [Fact]
    public void AJobId_OfTheRoleJobShape_IsAccepted()
    {
        CoderWorktree.IsJobId("r20261123101010-0a1b2c3d").Should().BeTrue();
        CoderWorktree.IsJobId("job-7").Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("main")]
    [InlineData("HEAD")]
    [InlineData("--upload-pack=x")]
    [InlineData("-b")]
    [InlineData("abc1234")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public async Task ABaseThatIsNotAFullCommitId_IsRefusedBeforeGitRuns(string sha)
    {
        await using var bed = await CoderTestBed.CreateAsync();
        var before = bed.Launcher.Requests.Count;

        var outcome = await bed.Coder.StartAsync(bed.Repo.Path, CoderTestBed.JobId(1), sha);

        outcome.Should().BeOfType<CoderWorktreeOutcome.Refused>().Which.Reason.Should().Be(CoderWorktreeRefusal.BadBaseSha);
        bed.Launcher.Requests.Count.Should().Be(before);
        Directory.Exists(bed.Root).Should().BeFalse();
    }

    [Fact]
    public async Task ASecondStart_OfTheSameJobId_IsRefused_AndTheFirstTreeIsNotTouched()
    {
        await using var bed = await CoderTestBed.CreateAsync();
        var id = CoderTestBed.JobId(1);
        var first = (CoderWorktreeOutcome.Started)await bed.Coder.StartAsync(bed.Repo.Path, id, bed.Shas[0]);
        await File.WriteAllTextAsync(Path.Combine(first.Path, "coder-work.txt"), "the coder wrote this");

        var again = await bed.Coder.StartAsync(bed.Repo.Path, id, bed.Shas[1]);

        again.Should().BeOfType<CoderWorktreeOutcome.Refused>().Which.Reason.Should().Be(CoderWorktreeRefusal.AlreadyExists);
        File.ReadAllText(Path.Combine(first.Path, "coder-work.txt")).Should().Be("the coder wrote this");
        (await bed.GitIn(first.Path, "rev-parse", "HEAD")).Should().Be(bed.Shas[0]);
    }

    [Fact]
    public async Task AFolderThatIsAlreadyThere_IsRefused_AndKeptWithItsContents()
    {
        await using var bed = await CoderTestBed.CreateAsync();
        var id = CoderTestBed.JobId(1);
        var squatter = Directory.CreateDirectory(Path.Combine(bed.Root, CoderWorktree.FolderOf(id))).FullName;
        await File.WriteAllTextAsync(Path.Combine(squatter, "mine.txt"), "somebody's file");

        var outcome = await bed.Coder.StartAsync(bed.Repo.Path, id, bed.Shas[0]);

        outcome.Should().BeOfType<CoderWorktreeOutcome.Refused>().Which.Reason.Should().Be(CoderWorktreeRefusal.AlreadyExists);
        File.ReadAllText(Path.Combine(squatter, "mine.txt")).Should().Be("somebody's file");
        (await bed.BranchExistsAsync($"coder/{id}")).Should().BeFalse("a refused start leaves no branch behind");
    }

    [Fact]
    public async Task ABranchThatIsAlreadyThere_IsRefused_AndKeptAtItsTip()
    {
        await using var bed = await CoderTestBed.CreateAsync();
        var id = CoderTestBed.JobId(1);
        await bed.Repo.GitAsync("branch", $"coder/{id}", bed.Shas[2]);

        var outcome = await bed.Coder.StartAsync(bed.Repo.Path, id, bed.Shas[0]);

        outcome.Should().BeOfType<CoderWorktreeOutcome.Refused>().Which.Reason.Should().Be(CoderWorktreeRefusal.AlreadyExists);
        (await bed.GitIn(bed.Repo.Path, "rev-parse", $"refs/heads/coder/{id}")).Should().Be(bed.Shas[2]);
        Directory.Exists(Path.Combine(bed.Root, CoderWorktree.FolderOf(id))).Should().BeFalse();
    }

    [Fact]
    public async Task ARootInsideTheMainCheckout_IsRefused_AndNothingIsMadeThere()
    {
        await using var bed = await CoderTestBed.CreateAsync();
        var inside = Path.Combine(bed.Repo.Path, "coder-here");
        var coder = new CoderWorktree(bed.Launcher, inside);
        var before = await bed.SnapshotAsync();

        var outcome = await coder.StartAsync(bed.Repo.Path, CoderTestBed.JobId(1), bed.Shas[0]);

        outcome.Should().BeOfType<CoderWorktreeOutcome.Refused>().Which.Reason.Should().Be(CoderWorktreeRefusal.RootInsideCheckout);
        Directory.Exists(inside).Should().BeFalse("not even the root folder is made inside the checkout");
        (await bed.SnapshotAsync()).Should().Equal(before);
    }

    [Fact]
    public async Task APathThatIsNotARepository_IsRefusedWithASentence()
    {
        await using var bed = await CoderTestBed.CreateAsync();
        var plain = Directory.CreateDirectory(Path.Combine(bed.Parent, "plain")).FullName;

        var outcome = await bed.Coder.StartAsync(plain, CoderTestBed.JobId(1), bed.Shas[0]);

        outcome.Should().BeOfType<CoderWorktreeOutcome.Refused>().Which.Reason.Should().Be(CoderWorktreeRefusal.NotARepository);
        (await bed.Coder.StartAsync(Path.Combine(bed.Parent, "absent"), CoderTestBed.JobId(1), bed.Shas[0]))
            .Should().BeOfType<CoderWorktreeOutcome.Refused>().Which.Reason.Should().Be(CoderWorktreeRefusal.NotARepository);
    }

    [Fact]
    public void TheDefaultRoot_IsMachineLocal_OutsideTheCheckoutAndTheTempFolder_AndItsNameIsShort()
    {
        Assert.SkipWhen(PerUserFolderForTheRun.MadeByTheRun, "this run has no per-user data folder of the machine (a check unit: an empty HOME), so it made its own inside the run folder, which is under the temp folder");
        var root = CoderWorktree.Default;

        Path.IsPathFullyQualified(root).Should().BeTrue();
        Path.GetFileName(root).Should().Be("coder", "one short leaf under the product's machine-local folder");
        root.Should().Be(Runners.Worktrees.ReviewTreeRoot.MachineLocal("coder"));
        Path.GetRelativePath(RepoPaths.Root(), root).Should().StartWith("..", "never inside this checkout");
        Path.GetRelativePath(TempDir.Root, root).Should().StartWith("..", "a temp folder may be a RAM disk with too little room for a build");
        root.Length.Should().BeLessThan(Path.GetFullPath(RepoPaths.Root()).Length + 80, "a coder tree is built in, and a long root went red");
    }

    [Fact]
    public void TheRootVariable_MovesTheRoot_WhenAbsolute_AndIsSaidAndIgnored_WhenRelative()
    {
        var said = new List<string>();
        var absolute = Path.Combine(TempDir.Root, "somewhere-short");

        CoderWorktree.DefaultIn(name => name == "COAI_CODER_ROOT" ? absolute : null, said.Add).Should().Be(absolute);
        CoderWorktree.DefaultIn(static _ => null, said.Add).Should().Be(CoderWorktree.Default);
        said.Should().BeEmpty();
        CoderWorktree.DefaultIn(name => name == "COAI_CODER_ROOT" ? "relative/dir" : null, said.Add).Should().Be(CoderWorktree.Default);
        said.Should().ContainSingle().Which.Should().Contain("COAI_CODER_ROOT").And.Contain(CoderWorktree.Default);
    }

    [Fact]
    public async Task EveryProcessStarted_IsGit_ThroughTheLauncher_WithArgvAndATimeout()
    {
        await using var bed = await CoderTestBed.CreateAsync();
        var before = bed.Launcher.Requests.Count;

        await bed.Coder.StartAsync(bed.Repo.Path, CoderTestBed.JobId(1), bed.Shas[0]);
        await bed.Coder.RemoveAsync(bed.Repo.Path, CoderTestBed.JobId(1));

        var ours = bed.Launcher.Requests.Skip(before).ToList();
        ours.Should().NotBeEmpty();
        ours.Should().OnlyContain(request => request.Executable == "git" && request.Timeout > TimeSpan.Zero && request.Timeout <= TimeSpan.FromMinutes(10));
        ours.Should().OnlyContain(request => request.Arguments.All(argument => !argument.Contains('\0')));
    }
}
