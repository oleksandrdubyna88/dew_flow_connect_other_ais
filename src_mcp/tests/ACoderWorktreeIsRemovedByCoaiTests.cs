using CoaiMcp.Runners.Worktrees;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E2 s2 t2: the coder worktree is removed by coai — <c>git worktree remove --force</c>, then the branch — whatever the coder left in it, and only a tree of ours.
/// </summary>
public sealed class ACoderWorktreeIsRemovedByCoaiTests
{
    [Fact]
    public async Task ARemoval_TakesTheTreeWithWhatTheCoderLeftInIt_AndTheBranch()
    {
        await using var bed = await CoderTestBed.CreateAsync();
        var id = CoderTestBed.JobId(1);
        var started = (CoderWorktreeOutcome.Started)await bed.Coder.StartAsync(bed.Repo.Path, id, bed.Shas[0]);
        await File.WriteAllTextAsync(Path.Combine(started.Path, "a.txt"), "the coder edited this");
        await File.WriteAllTextAsync(Path.Combine(started.Path, "new.txt"), "and added this");
        await bed.GitIn(started.Path, "add", "new.txt");
        await bed.GitIn(started.Path, "-c", "user.email=c@c", "-c", "user.name=c", "-c", "commit.gpgsign=false", "commit", "-m", "coder commit");
        await File.WriteAllTextAsync(Path.Combine(started.Path, "dirty.txt"), "uncommitted");
        var before = await bed.SnapshotAsync();

        var outcome = await bed.Coder.RemoveAsync(bed.Repo.Path, id);

        outcome.Should().BeOfType<CoderWorktreeOutcome.Removed>(outcome.ToString());
        Directory.Exists(started.Path).Should().BeFalse();
        (await bed.BranchExistsAsync($"coder/{id}")).Should().BeFalse("the branch goes with the tree");
        (await bed.ListedAsync()).Should().ContainSingle("only the main checkout is left");
        (await bed.SnapshotAsync()).Should().Equal(before, "the main checkout is not touched by a removal either");
    }

    [Fact]
    public async Task ARemoval_OfATreeThatIsAlreadyGone_Succeeds_AndIsTheSameAnswerTwice()
    {
        await using var bed = await CoderTestBed.CreateAsync();
        var id = CoderTestBed.JobId(1);
        await bed.Coder.StartAsync(bed.Repo.Path, id, bed.Shas[0]);

        (await bed.Coder.RemoveAsync(bed.Repo.Path, id)).Should().BeOfType<CoderWorktreeOutcome.Removed>();
        (await bed.Coder.RemoveAsync(bed.Repo.Path, id)).Should().BeOfType<CoderWorktreeOutcome.Removed>("nothing is there, and that is what was asked for");
        (await bed.Coder.RemoveAsync(bed.Repo.Path, CoderTestBed.JobId(99))).Should().BeOfType<CoderWorktreeOutcome.Removed>("an id never started is also nothing to remove");
    }

    [Fact]
    public async Task ARemoval_OfATreeWhoseFolderWasDeletedByHand_StillFreesTheBranch()
    {
        await using var bed = await CoderTestBed.CreateAsync();
        var id = CoderTestBed.JobId(1);
        var started = (CoderWorktreeOutcome.Started)await bed.Coder.StartAsync(bed.Repo.Path, id, bed.Shas[0]);
        Directory.Delete(started.Path, recursive: true);

        var outcome = await bed.Coder.RemoveAsync(bed.Repo.Path, id);

        outcome.Should().BeOfType<CoderWorktreeOutcome.Removed>(outcome.ToString());
        (await bed.BranchExistsAsync($"coder/{id}")).Should().BeFalse();
        (await bed.ListedAsync()).Should().ContainSingle();
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../x")]
    [InlineData("a/b")]
    [InlineData("/etc")]
    [InlineData("")]
    public async Task ARemoval_OfAnIdThatCouldLeaveTheRoot_IsRefusedBeforeGitRuns(string id)
    {
        await using var bed = await CoderTestBed.CreateAsync();
        var before = bed.Launcher.Requests.Count;

        var outcome = await bed.Coder.RemoveAsync(bed.Repo.Path, id);

        outcome.Should().BeOfType<CoderWorktreeOutcome.Refused>().Which.Reason.Should().Be(CoderWorktreeRefusal.BadJobId);
        bed.Launcher.Requests.Count.Should().Be(before);
    }

    [Fact]
    public async Task AFolderThatIsNotAWorktreeOfThisRepository_IsNotRemoved()
    {
        await using var bed = await CoderTestBed.CreateAsync();
        var id = CoderTestBed.JobId(1);
        var squatter = Directory.CreateDirectory(Path.Combine(bed.Root, CoderWorktree.FolderOf(id))).FullName;
        await File.WriteAllTextAsync(Path.Combine(squatter, "mine.txt"), "somebody's file");

        var outcome = await bed.Coder.RemoveAsync(bed.Repo.Path, id);

        outcome.Should().BeOfType<CoderWorktreeOutcome.Refused>().Which.Reason.Should().Be(CoderWorktreeRefusal.NotOurs);
        File.ReadAllText(Path.Combine(squatter, "mine.txt")).Should().Be("somebody's file");
    }

    [Fact]
    public async Task ATreeOfAnotherRepository_UnderTheSameRoot_IsNotRemovedByThisOne()
    {
        await using var bed = await CoderTestBed.CreateAsync();
        await using var other = await bed.AnotherRepoAsync();
        var id = CoderTestBed.JobId(1);
        var theirs = (CoderWorktreeOutcome.Started)await bed.Coder.StartAsync(other.Path, id, await other.HeadAsync());

        var outcome = await bed.Coder.RemoveAsync(bed.Repo.Path, id);

        outcome.Should().BeOfType<CoderWorktreeOutcome.Refused>().Which.Reason.Should().Be(CoderWorktreeRefusal.NotOurs, "the folder is registered in the other repository, not in this one");
        Directory.Exists(theirs.Path).Should().BeTrue();
        (await bed.Coder.RemoveAsync(other.Path, id)).Should().BeOfType<CoderWorktreeOutcome.Removed>();
    }
}
