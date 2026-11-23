using CoaiMcp.Runners.Worktrees;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E2 s2 t2, DoD d2: the 21st live coder worktree of one repository is refused and nothing is evicted; a base sha that does not exist is refused with a
/// sentence; the main checkout's HEAD, index and files are byte-equal before and after — all of it, with the checkout dirty.
/// </summary>
public sealed class ACoderWorktreeCapIsRefusedNotEvictedTests
{
    private static async Task<List<CoderWorktreeOutcome.Started>> StartManyAsync(CoderTestBed bed, int count, int from = 1)
    {
        var made = new List<CoderWorktreeOutcome.Started>();
        for (var n = from; n < from + count; n++)
        {
            var outcome = await bed.Coder.StartAsync(bed.Repo.Path, CoderTestBed.JobId(n), bed.Shas[n % 3]);
            made.Add(outcome.Should().BeOfType<CoderWorktreeOutcome.Started>(outcome.ToString()).Subject);
        }

        return made;
    }

    [Fact]
    public async Task TheTwentyFirstLiveWorktree_IsRefused_AndNoneIsEvicted()
    {
        await using var bed = await CoderTestBed.CreateAsync();
        var twenty = await StartManyAsync(bed, CoderWorktree.Cap);
        await File.WriteAllTextAsync(Path.Combine(twenty[0].Path, "oldest.txt"), "work in the oldest tree");

        var outcome = await bed.Coder.StartAsync(bed.Repo.Path, CoderTestBed.JobId(21), bed.Shas[0]);

        var refused = outcome.Should().BeOfType<CoderWorktreeOutcome.Refused>().Subject;
        refused.Reason.Should().Be(CoderWorktreeRefusal.Full);
        refused.Sentence.Should().Contain("20").And.Contain("remove");
        CoderWorktree.Cap.Should().Be(20);
        twenty.Should().OnlyContain(tree => Directory.Exists(tree.Path), "no tree was evicted to make room");
        File.ReadAllText(Path.Combine(twenty[0].Path, "oldest.txt")).Should().Be("work in the oldest tree");
        (await bed.ListedAsync()).Should().HaveCount(21, "the main checkout and twenty coder trees");
        (await bed.BranchExistsAsync($"coder/{CoderTestBed.JobId(21)}")).Should().BeFalse();
        Directory.Exists(Path.Combine(bed.Root, CoderWorktree.FolderOf(CoderTestBed.JobId(21)))).Should().BeFalse();
    }

    [Fact]
    public async Task ARemoval_MakesRoomForTheNextStart()
    {
        await using var bed = await CoderTestBed.CreateAsync();
        await StartManyAsync(bed, CoderWorktree.Cap);
        (await bed.Coder.StartAsync(bed.Repo.Path, CoderTestBed.JobId(21), bed.Shas[0])).Should().BeOfType<CoderWorktreeOutcome.Refused>();

        (await bed.Coder.RemoveAsync(bed.Repo.Path, CoderTestBed.JobId(5))).Should().BeOfType<CoderWorktreeOutcome.Removed>();

        (await bed.Coder.StartAsync(bed.Repo.Path, CoderTestBed.JobId(21), bed.Shas[0])).Should().BeOfType<CoderWorktreeOutcome.Started>();
        (await bed.Coder.StartAsync(bed.Repo.Path, CoderTestBed.JobId(22), bed.Shas[0])).Should().BeOfType<CoderWorktreeOutcome.Refused>().Which.Reason.Should().Be(CoderWorktreeRefusal.Full);
    }

    [Fact]
    public async Task ATreeWhoseFolderIsGone_IsNotLive_AndDoesNotHoldASlot()
    {
        await using var bed = await CoderTestBed.CreateAsync();
        var twenty = await StartManyAsync(bed, CoderWorktree.Cap);
        Directory.Delete(twenty[3].Path, recursive: true);

        var outcome = await bed.Coder.StartAsync(bed.Repo.Path, CoderTestBed.JobId(21), bed.Shas[0]);

        outcome.Should().BeOfType<CoderWorktreeOutcome.Started>(outcome.ToString());
    }

    [Fact]
    public async Task AnotherRepositorysTrees_DoNotCount_ButATreeUnderAnEarlierRootOfThisOneDoes()
    {
        await using var bed = await CoderTestBed.CreateAsync();
        await using var other = await bed.AnotherRepoAsync();
        for (var n = 1; n <= CoderWorktree.Cap; n++)
        {
            (await bed.Coder.StartAsync(other.Path, CoderTestBed.JobId(100 + n), await other.HeadAsync())).Should().BeOfType<CoderWorktreeOutcome.Started>();
        }

        var twenty = await StartManyAsync(bed, CoderWorktree.Cap - 1);

        twenty.Should().HaveCount(19, "twenty in the other repository do not use this repository's twenty");
        var movedRoot = new CoderWorktree(bed.Launcher, Path.Combine(bed.Parent, "earlier-root"));
        (await movedRoot.StartAsync(bed.Repo.Path, CoderTestBed.JobId(60), bed.Shas[0])).Should().BeOfType<CoderWorktreeOutcome.Started>();
        (await bed.Coder.StartAsync(bed.Repo.Path, CoderTestBed.JobId(21), bed.Shas[0])).Should().BeOfType<CoderWorktreeOutcome.Refused>().Which.Reason.Should().Be(CoderWorktreeRefusal.Full, "a tree under another root of the same repository still holds its disk");
    }

    [Fact]
    public async Task ParallelStarts_AtNineteen_LeaveExactlyTwenty()
    {
        await using var bed = await CoderTestBed.CreateAsync();
        await StartManyAsync(bed, CoderWorktree.Cap - 1);

        var racing = await Task.WhenAll(Enumerable.Range(50, 4).Select(n => bed.Coder.StartAsync(bed.Repo.Path, CoderTestBed.JobId(n), bed.Shas[0])));

        racing.OfType<CoderWorktreeOutcome.Started>().Should().HaveCount(1, "the count and the creation are one step");
        racing.OfType<CoderWorktreeOutcome.Refused>().Should().HaveCount(3).And.OnlyContain(refused => refused.Reason == CoderWorktreeRefusal.Full);
        (await bed.ListedAsync()).Should().HaveCount(21);
    }

    [Fact]
    public async Task ABaseThatDoesNotExist_IsRefusedWithASentence_AndNothingIsMade()
    {
        await using var bed = await CoderTestBed.CreateAsync();
        const string Absent = "0123456789abcdef0123456789abcdef01234567";
        var id = CoderTestBed.JobId(1);

        var outcome = await bed.Coder.StartAsync(bed.Repo.Path, id, Absent);

        var refused = outcome.Should().BeOfType<CoderWorktreeOutcome.Refused>().Subject;
        refused.Reason.Should().Be(CoderWorktreeRefusal.UnknownBase);
        refused.Sentence.Should().Contain(Absent).And.Contain("does not exist");
        (await bed.BranchExistsAsync($"coder/{id}")).Should().BeFalse();
        Directory.Exists(Path.Combine(bed.Root, CoderWorktree.FolderOf(id))).Should().BeFalse();
        (await bed.ListedAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task ATreeObject_IsNotACommit_AndIsRefusedAsAnUnknownBase()
    {
        await using var bed = await CoderTestBed.CreateAsync();
        var tree = await bed.GitIn(bed.Repo.Path, "rev-parse", $"{bed.Shas[0]}^{{tree}}");

        var outcome = await bed.Coder.StartAsync(bed.Repo.Path, CoderTestBed.JobId(1), tree);

        outcome.Should().BeOfType<CoderWorktreeOutcome.Refused>().Which.Reason.Should().Be(CoderWorktreeRefusal.UnknownBase);
    }

    [Fact]
    public async Task TheMainCheckout_IsByteEqualBeforeAndAfter_StartsRefusalsAndRemovals()
    {
        await using var bed = await CoderTestBed.CreateAsync();
        var before = await bed.SnapshotAsync();
        before.Keys.Should().Contain(["a.txt", "untracked.txt", "ignored.log", "staged.txt", ".git/HEAD", ".git/index"], "the snapshot sees every kind of state a start could disturb");
        var head = await bed.Repo.HeadAsync();

        var twenty = await StartManyAsync(bed, CoderWorktree.Cap);
        (await bed.Coder.StartAsync(bed.Repo.Path, CoderTestBed.JobId(21), bed.Shas[0])).Should().BeOfType<CoderWorktreeOutcome.Refused>();
        (await bed.Coder.StartAsync(bed.Repo.Path, CoderTestBed.JobId(22), "0123456789abcdef0123456789abcdef01234567")).Should().BeOfType<CoderWorktreeOutcome.Refused>();
        (await bed.Coder.StartAsync(bed.Repo.Path, "../x", bed.Shas[0])).Should().BeOfType<CoderWorktreeOutcome.Refused>();
        (await CoderTestBed.SnapshotOfAsync(bed.Repo.Path)).Should().Equal(before, "after the starts and the refusals");

        foreach (var tree in twenty)
        {
            (await bed.Coder.RemoveAsync(bed.Repo.Path, Path.GetFileName(tree.Path)["coai-coder-".Length..])).Should().BeOfType<CoderWorktreeOutcome.Removed>();
        }

        (await CoderTestBed.SnapshotOfAsync(bed.Repo.Path)).Should().Equal(before, "after the removals");
        (await bed.Repo.HeadAsync()).Should().Be(head);
        (await bed.GitIn(bed.Repo.Path, "branch", "--show-current")).Should().Be("main");
        (await bed.GitIn(bed.Repo.Path, "status", "--porcelain")).Should().Contain("staged.txt").And.Contain("untracked.txt");
    }
}
