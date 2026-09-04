using Xunit;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Worktrees;
using FluentAssertions;

namespace CoaiMcp.Tests;

/// <summary>
/// Real git, temp repositories — the lifecycle rules are exactly the ones that only show up
/// against the real tool.
/// </summary>
public sealed class WorktreeManagerTests : IAsyncLifetime
{
    private readonly ProcessLauncher _launcher = new();
    private readonly List<string> _temps = [];
    private string _repo = string.Empty;
    private string _storage = string.Empty;
    private WorktreeManager _manager = null!;

    public async ValueTask InitializeAsync()
    {
        _repo = Directory.CreateTempSubdirectory("coai-repo-").FullName;
        _storage = Directory.CreateTempSubdirectory("coai-wt-storage-").FullName;
        _manager = new WorktreeManager(_launcher, _storage);
        await Git(_repo, "init", "-b", "main");
        await File.WriteAllTextAsync(Path.Combine(_repo, "a.txt"), "v1");
        await Git(_repo, "add", ".");
        await Git(_repo, "commit", "-m", "v1");
    }

    public ValueTask DisposeAsync()
    {
        TryDelete(_repo);
        TryDelete(_storage);
        foreach (var temp in _temps)
        {
            TryDelete(temp);
        }

        return ValueTask.CompletedTask;
    }

    private static void TryDelete(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException) { /* a straggling handle on a temp dir is not a test failure */ }
        catch (UnauthorizedAccessException) { /* read-only .git files on Windows */ }
    }

    private async Task Git(string cwd, params string[] args)
    {
        var result = await _launcher.RunAsync(new ProcessRequest(
            "git",
            ["-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", .. args],
            cwd));
        result.ExitCode.Should().Be(0, $"git {string.Join(' ', args)} must succeed: {result.StdErr}");
    }

    [Fact]
    public async Task RoundWorktree_IsPinnedToTheShaResolvedAtRoundStart()
    {
        var sha = await _manager.ResolveShaAsync(_repo, "main");
        await using var lease = await _manager.AddAsync(_repo, sha, "s1", round: 1);

        // The branch moves mid-round — the main AI keeps editing. The worktree must not.
        await File.WriteAllTextAsync(Path.Combine(_repo, "a.txt"), "v2", TestContext.Current.CancellationToken);
        await Git(_repo, "add", ".");
        await Git(_repo, "commit", "-m", "v2");

        (await File.ReadAllTextAsync(Path.Combine(lease.Path, "a.txt"), TestContext.Current.CancellationToken)).Should().Be("v1");
    }

    [Fact]
    public async Task Fanout_Throws_FinallyRemovesTheWorktree()
    {
        var sha = await _manager.ResolveShaAsync(_repo, "main");
        var leasedPath = string.Empty;
        try
        {
            await using var lease = await _manager.AddAsync(_repo, sha, "s1", round: 1);
            leasedPath = lease.Path;
            throw new InvalidOperationException("the fan-out died");
        }
        catch (InvalidOperationException)
        {
            // expected — the point is what disposal left behind
        }

        Directory.Exists(leasedPath).Should().BeFalse("the lease's disposal is the finally");
        (await _manager.ListOursAsync(_repo)).Should().BeEmpty();
    }

    [Fact]
    public async Task Open_PrunesAnOrphanFromAKilledSession()
    {
        var sha = await _manager.ResolveShaAsync(_repo, "main");
        var lease = await _manager.AddAsync(_repo, sha, "killed", round: 2);

        // A killed session runs no finally: the directory just vanishes, metadata stays.
        Directory.Delete(lease.Path, recursive: true);

        await _manager.PruneOursAsync(_repo);

        (await _manager.ListOursAsync(_repo)).Should().BeEmpty();
        // And the next add at the same name works — the block the epic exists to prevent.
        await using var again = await _manager.AddAsync(_repo, sha, "killed", round: 2);
        Directory.Exists(again.Path).Should().BeTrue();
    }

    [Fact]
    public async Task HumanWorktree_IsNeverRemoved()
    {
        var humanPath = Path.Combine(Directory.CreateTempSubdirectory("human-wt-").FullName, "mine");
        await Git(_repo, "worktree", "add", "--detach", humanPath);

        await _manager.PruneOursAsync(_repo);

        Directory.Exists(humanPath).Should().BeTrue("pruning must never touch a worktree it did not create");
        await Git(_repo, "worktree", "remove", "--force", humanPath);
    }

    [Fact]
    public async Task LiveCheckout_IsByteIdenticalBeforeAndAfterARound()
    {
        var before = await File.ReadAllTextAsync(Path.Combine(_repo, "a.txt"), TestContext.Current.CancellationToken);
        var sha = await _manager.ResolveShaAsync(_repo, "main");
        await using (await _manager.AddAsync(_repo, sha, "s1", round: 1))
        {
            // a round happens here
        }

        (await File.ReadAllTextAsync(Path.Combine(_repo, "a.txt"), TestContext.Current.CancellationToken)).Should().Be(before);
        var status = await _launcher.RunAsync(
            new ProcessRequest("git", ["status", "--short"], _repo),
            TestContext.Current.CancellationToken);
        status.StdOut.Trim().Should().BeEmpty("a round must leave the live checkout untouched");
    }

    [Fact]
    public async Task WorktreeLivesOutsideTheRepository()
    {
        var sha = await _manager.ResolveShaAsync(_repo, "main");
        await using var lease = await _manager.AddAsync(_repo, sha, "s1", round: 1);

        Path.GetFullPath(lease.Path).Should().NotStartWith(Path.GetFullPath(_repo),
            "a crash must never leave an untracked directory inside someone's project");
        Path.GetFullPath(lease.Path).Should().StartWith(Path.GetFullPath(_storage));
    }

    [Fact]
    public async Task UnresolvableBranch_IsANamedRefusal()
    {
        var act = () => _manager.ResolveShaAsync(_repo, "no-such-branch");

        (await act.Should().ThrowAsync<WorktreeException>()).Which.Message.Should().Contain("no-such-branch");
    }

    /// <summary>
    /// The family's rules are a submodule, and git does not populate submodules in a linked
    /// worktree — so every conventions pass was judging diffs against an empty directory where
    /// 26 rule files were supposed to be.
    /// </summary>
    [Fact]
    public async Task RoundWorktree_CarriesTheSubmodulePinnedByTheReviewedCommit()
    {
        await AddSubmoduleAsync("rules", "v1");
        var sha = await _manager.ResolveShaAsync(_repo, "main");
        // The parent's own copy moves on. The worktree must show the commit under review, not this.
        await MoveSubmoduleTipAsync("rules", "v2");

        await using var lease = await _manager.AddAsync(_repo, sha, "s1", round: 1);

        var file = Path.Combine(lease.Path, "rules", "rules.md");
        File.Exists(file).Should().BeTrue("the reviewers read the rules out of the round's worktree");
        (await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken))
            .Should().Be("v1", "a round is judged against the rules as of its own commit");
    }

    /// <summary>
    /// A round must not depend on the network to know the project's rules: the parent checkout
    /// already holds every object the pinned commit needs.
    /// </summary>
    [Fact]
    public async Task TheSubmoduleIsClonedFromTheParentCheckout_NotFromItsRemote()
    {
        var upstream = await AddSubmoduleAsync("rules", "v1");
        var sha = await _manager.ResolveShaAsync(_repo, "main");

        await using var lease = await _manager.AddAsync(_repo, sha, "s1", round: 1);

        var origin = await GitOut(Path.Combine(lease.Path, "rules"), "remote", "get-url", "origin");
        origin.Trim().Should().NotBe(upstream.Replace('\\', '/'), "the remote is what we are avoiding");
        origin.Trim().Should().Be(Path.GetFullPath(Path.Combine(_repo, "rules")).Replace('\\', '/'));
    }

    /// <summary>
    /// A declared mount the parent cannot serve is a round with fewer rules, never a round that
    /// refuses to start.
    /// </summary>
    [Fact]
    public async Task ASubmoduleTheParentCannotServe_StillLeavesAUsableWorktree()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_repo, ".gitmodules"),
            "[submodule \"rules\"]\n\tpath = rules\n\turl = https://example.invalid/nope.git\n");
        await Git(_repo, "add", ".");
        await Git(_repo, "commit", "-m", "declare a mount nobody can fetch");
        var sha = await _manager.ResolveShaAsync(_repo, "main");

        await using var lease = await _manager.AddAsync(_repo, sha, "s1", round: 1);

        Directory.Exists(lease.Path).Should().BeTrue();
        File.Exists(Path.Combine(lease.Path, "a.txt")).Should().BeTrue("the rest of the tree is still there");
    }

    /// <summary>An upstream repository, added to <c>_repo</c> as a submodule at <paramref name="path"/>.</summary>
    private async Task<string> AddSubmoduleAsync(string path, string content)
    {
        var upstream = Directory.CreateTempSubdirectory("coai-sub-").FullName;
        _temps.Add(upstream);
        await Git(upstream, "init", "-b", "main");
        await File.WriteAllTextAsync(Path.Combine(upstream, "rules.md"), content);
        await Git(upstream, "add", ".");
        await Git(upstream, "commit", "-m", "the rules");
        await Git(_repo, "-c", "protocol.file.allow=always", "submodule", "add", upstream.Replace('\\', '/'), path);
        await Git(_repo, "commit", "-m", "mount the rules");

        return upstream;
    }

    /// <summary>Moves the parent's WORKING copy of the submodule ahead of the pin it records.</summary>
    private async Task MoveSubmoduleTipAsync(string path, string content)
    {
        var inside = Path.Combine(_repo, path);
        await File.WriteAllTextAsync(Path.Combine(inside, "rules.md"), content);
        await Git(inside, "add", ".");
        await Git(inside, "commit", "-m", "the rules, later");
    }

    private async Task<string> GitOut(string cwd, params string[] args)
    {
        var result = await _launcher.RunAsync(new ProcessRequest("git", args, cwd));
        result.ExitCode.Should().Be(0, $"git {string.Join(' ', args)} must succeed: {result.StdErr}");

        return result.StdOut;
    }
}
