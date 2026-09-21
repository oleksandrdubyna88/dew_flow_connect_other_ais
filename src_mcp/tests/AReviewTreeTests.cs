using Xunit;
using CoaiMcp.Core.Collecting;
using CoaiMcp.Runners.Collecting;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Worktrees;
using FluentAssertions;

namespace CoaiMcp.Tests;

/// <summary>
/// Real git, temp repositories — everything here is a rule that only shows up against the real tool.
/// </summary>
/// <remarks>
/// <para>The first test is the story: a tree somebody is reading must survive the gate's own
/// worktree pruning, which runs on every <c>open</c>. It is red against a tree made with the round
/// prefix under the round storage root, which is how it shows its teeth.</para>
/// <para>The submodule tests need <c>protocol.file.allow=always</c>: git refuses a file-protocol
/// submodule by default since CVE-2022-39253, and every submodule here is a local path.</para>
/// </remarks>
public sealed class AReviewTreeTests : IAsyncLifetime
{
    private readonly ProcessLauncher _launcher = new();
    private readonly List<string> _temps = [];
    private string _repo = string.Empty;
    private string _root = string.Empty;
    private string _roundStorage = string.Empty;
    private string _sha = string.Empty;

    public async ValueTask InitializeAsync()
    {
        _repo = Temp("coai-repo-");
        _root = Temp("coai-review-root-");
        _roundStorage = Temp("coai-wt-storage-");
        await Git(_repo, "init", "-b", "main");
        await File.WriteAllTextAsync(Path.Combine(_repo, "a.txt"), "v1");
        await Git(_repo, "add", ".");
        await Git(_repo, "commit", "-m", "v1");
        _sha = await ShaOf(_repo, "HEAD");
    }

    public ValueTask DisposeAsync()
    {
        foreach (var temp in _temps)
        {
            Unlock(temp);
            TryDelete(temp);
        }

        return ValueTask.CompletedTask;
    }

    // ---------------------------------------------------------------------------------------
    // The defect the story exists around.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task AReviewTree_SurvivesTheGatesPrune()
    {
        var made = await Trees().PrepareAsync(7, Place(_sha), TestContext.Current.CancellationToken);
        made.Reason.Should().BeEmpty();
        Directory.Exists(made.Path).Should().BeTrue();

        // The gate's own tree, beside it, in the gate's own place — so the test proves the prune
        // RAN and did its job rather than merely failing to find anything.
        var gate = new WorktreeManager(_launcher, _roundStorage);
        var lease = await gate.AddAsync(_repo, _sha, "s1", round: 1);
        Directory.Exists(lease.Path).Should().BeTrue();

        await gate.PruneOursAsync(_repo);

        Directory.Exists(lease.Path).Should().BeFalse("the gate must still clear what a killed session left");
        Directory.Exists(made.Path).Should().BeTrue("a person is reading this one");
        File.Exists(Path.Combine(_root, $"{Path.GetFileName(made.Path)}.json")).Should().BeTrue();

        // And it is still a registered worktree, not merely a directory that survived deletion.
        var listed = await Run(_repo, "worktree", "list", "--porcelain");
        listed.StdOut.Replace('\\', '/').Should().Contain(made.Path.Replace('\\', '/'));
    }

    /// <summary>
    /// The second, independent guard. The test above keeps the review tree under its own root, so it
    /// would stay green even if the prefix were changed back to the round one — it proves the ROOT
    /// and nothing else. This one puts a review tree in the WORST place, the gate's own storage root,
    /// and shows the prefix alone is enough: <c>ListOursAsync</c> matches on it, and so does the
    /// <c>Directory.Delete</c> glob that no lock can stop.
    /// </summary>
    [Fact]
    public async Task AReviewTreeInTheGatesOwnStorageRoot_IsSparedByItsPrefix()
    {
        var made = await new ReviewWorktrees(_launcher, new GitHistory(_launcher), _roundStorage)
            .PrepareAsync(7, Place(_sha), TestContext.Current.CancellationToken);
        made.Reason.Should().BeEmpty();

        var gate = new WorktreeManager(_launcher, _roundStorage);
        var lease = await gate.AddAsync(_repo, _sha, "s1", round: 1);

        await gate.PruneOursAsync(_repo);

        Directory.Exists(lease.Path).Should().BeFalse("the gate must still clear its own");
        Directory.Exists(made.Path).Should().BeTrue(
            "a review tree is spared by its prefix even when it shares the gate's root");
    }

    [Fact]
    public async Task AReviewTree_SurvivesASingleForcedRemove()
    {
        var made = await Trees().PrepareAsync(7, Place(_sha), TestContext.Current.CancellationToken);

        // Exactly the call WorktreeManager.RemoveAsync makes. The lock is what refuses it.
        var forced = await Run(_repo, "worktree", "remove", "--force", made.Path);

        forced.ExitCode.Should().NotBe(0);
        forced.StdErr.Should().Contain("locked");
        Directory.Exists(made.Path).Should().BeTrue();

        var pruned = await Run(_repo, "worktree", "prune");
        pruned.ExitCode.Should().Be(0);
        Directory.Exists(made.Path).Should().BeTrue("prune must not take a locked tree either");
    }

    // ---------------------------------------------------------------------------------------
    // What the tree is FOR: the commits that have no other way to be seen.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task AnOrphanedCommit_GetsATree_AndTheTreePinsItAgainstGc()
    {
        var orphan = await OrphanedCommit();
        (await Run(_repo, "branch", "-a", "--contains", orphan)).StdOut.Trim().Should().BeEmpty(
            "the commit must be reachable from no ref, which is the 55.7 % case");

        var made = await Trees().PrepareAsync(7, Place(orphan), TestContext.Current.CancellationToken);
        made.Reason.Should().BeEmpty("an orphaned commit is an OBJECT, and a worktree can be checked out at one");

        await Run(_repo, "reflog", "expire", "--expire=now", "--all");
        await Run(_repo, "gc", "--prune=now");

        var still = await Run(_repo, "cat-file", "-e", $"{orphan}^{{commit}}");
        still.ExitCode.Should().Be(0, "a worktree HEAD is a gc root, so the tree preserves the very commit it holds");
    }

    // ---------------------------------------------------------------------------------------
    // Identity (D2): one object store, one tree.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task TwoSpellingsOfOneCheckout_ShareOneTree()
    {
        var trees = Trees();
        var first = await trees.PrepareAsync(7, Place(_sha), TestContext.Current.CancellationToken);

        // The same checkout named differently: a trailing separator and the other slash.
        var spelled = _repo.Replace('\\', '/') + "/";
        var second = await trees.PrepareAsync(
            8, new TreePlace(spelled, _sha), TestContext.Current.CancellationToken);

        second.Reason.Should().BeEmpty();
        second.Reused.Should().BeTrue("a spelling is not a second repository");
        second.Path.Should().Be(first.Path);
        Directory.GetDirectories(_root).Should().ContainSingle();
    }

    [Fact]
    public async Task ALinkedWorktreeOfTheCheckout_SharesItsTree()
    {
        var trees = Trees();
        var first = await trees.PrepareAsync(7, Place(_sha), TestContext.Current.CancellationToken);

        var linked = Temp("coai-linked-");
        TryDelete(linked);
        (await Run(_repo, "worktree", "add", "--detach", linked, _sha)).ExitCode.Should().Be(0);

        var second = await trees.PrepareAsync(
            8, new TreePlace(linked, _sha), TestContext.Current.CancellationToken);

        second.Reused.Should().BeTrue("a linked worktree is registered in the SAME object store");
        second.Path.Should().Be(first.Path);
    }

    [Fact]
    public async Task TwoClonesOfOneRemote_GetTwoTrees()
    {
        var clone = Temp("coai-clone-");
        TryDelete(clone);
        (await Run(Path.GetDirectoryName(clone)!, "clone", "--quiet", _repo, clone)).ExitCode.Should().Be(0);
        var cloned = await ShaOf(clone, "HEAD");
        cloned.Should().Be(_sha, "a clone holds the same commit, which is what makes this the hard case");

        var trees = Trees();
        var here = await trees.PrepareAsync(7, Place(_sha), TestContext.Current.CancellationToken);
        var there = await trees.PrepareAsync(8, new TreePlace(clone, cloned), TestContext.Current.CancellationToken);

        there.Path.Should().NotBe(here.Path, "two clones cannot share a worktree however alike they look");
        there.Reused.Should().BeFalse();
    }

    // ---------------------------------------------------------------------------------------
    // Reuse, the cap, and the states only a durable tree can be in.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task ASecondPress_ReusesTheTree_AndRunsNoAdd()
    {
        var trees = Trees();
        await trees.PrepareAsync(7, Place(_sha), TestContext.Current.CancellationToken);

        var watching = new Watching(_launcher);
        var again = await new ReviewWorktrees(watching, new GitHistory(watching), _root)
            .PrepareAsync(7, Place(_sha), TestContext.Current.CancellationToken);

        again.Reused.Should().BeTrue();
        watching.Ran.Should().NotContain(a => a.Contains("worktree add"), "a reuse must cost no checkout");
        watching.Ran.Should().NotContain(a => a.Contains("submodule"), "and no population either");
    }

    [Fact]
    public async Task TheEleventhTree_IsRefused_AndTheTenAreNamed()
    {
        for (var i = 0; i < ReviewWorktrees.Cap; i++)
        {
            ReviewTreeRecords.Write(
                Path.Combine(_root, $"coai-review-0000000{i}-{i:D12}.json"),
                new ReviewTreeRecord($"/r{i}/.git", new string('a', 39) + i, $"2026-09-0{i % 9 + 1}T00:00:00Z", []));
        }

        var refused = await Trees().PrepareAsync(7, Place(_sha), TestContext.Current.CancellationToken);

        refused.Reason.Should().Be(ReviewTreeReason.Budget);
        refused.Path.Should().BeEmpty("nothing was made, so there is nothing to open");
        refused.Trees.Should().HaveCount(ReviewWorktrees.Cap);
        refused.Trees.Should().OnlyContain(t => t.Repository.Length > 0 && t.Sha.Length > 0 && t.Path.Length > 0,
            "a refusal that says 'you have ten' without saying which ten leaves a person no move");
        Directory.GetDirectories(_root).Should().BeEmpty("a cap refuses; it never evicts");
    }

    [Fact]
    public async Task AHalfMadeTree_IsRebuiltNotReused()
    {
        var path = await HalfMade();
        await File.WriteAllTextAsync(
            Path.Combine(path, "a.txt"), "v1", TestContext.Current.CancellationToken);

        var made = await Trees().PrepareAsync(7, Place(_sha), TestContext.Current.CancellationToken);

        made.Reason.Should().BeEmpty();
        made.Reused.Should().BeFalse("a tree with no record never finished being made");
        File.Exists(Path.Combine(_root, $"{Path.GetFileName(made.Path)}.json")).Should().BeTrue();
    }

    [Fact]
    public async Task AHalfMadeTreeSomebodyEdited_IsRefusedByName()
    {
        var path = await HalfMade();
        await File.WriteAllTextAsync(
            Path.Combine(path, "notes-i-was-writing.md"), "mine", TestContext.Current.CancellationToken);

        var refused = await Trees().PrepareAsync(7, Place(_sha), TestContext.Current.CancellationToken);

        refused.Reason.Should().Be(ReviewTreeReason.IncompleteAndDirty);
        refused.Path.Should().Be(path, "it is named precisely so it can be looked at by hand");
        Directory.Exists(path).Should().BeTrue("work nobody has looked at is never deleted to make room");
        File.Exists(Path.Combine(path, "notes-i-was-writing.md")).Should().BeTrue();
    }

    [Fact]
    public async Task AYoungTreeWithNoRecord_IsAnotherPressStillWorking()
    {
        var path = Path.Combine(_root, "coai-review-placeholder");
        Directory.CreateDirectory(path);
        // Young: created now, so the maker is presumed alive. The name has to be the one the
        // identity computes, which the half-made helper does for us; here we only need a young one.
        var made = await HalfMade(old: false);

        var answer = await Trees().PrepareAsync(7, Place(_sha), TestContext.Current.CancellationToken);

        answer.Reason.Should().Be(ReviewTreeReason.InProgress);
        Directory.Exists(made).Should().BeTrue();
        Directory.Delete(path, recursive: true);
    }

    // ---------------------------------------------------------------------------------------
    // Submodules (D6).
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Submodules_ComeFromTheParent_AndAnEmptyMountIsNamed()
    {
        var sub = Temp("coai-sub-");
        await Git(sub, "init", "-b", "main");
        await File.WriteAllTextAsync(Path.Combine(sub, "s.txt"), "sub");
        await Git(sub, "add", ".");
        await Git(sub, "commit", "-m", "sub");

        await Git(_repo, "-c", "protocol.file.allow=always", "submodule", "add", "-q", sub.Replace('\\', '/'), "mods/sub");
        await Git(_repo, "commit", "-m", "add submodule");
        var sha = await ShaOf(_repo, "HEAD");

        var made = await Trees().PrepareAsync(7, new TreePlace(_repo, sha), TestContext.Current.CancellationToken);

        made.Reason.Should().BeEmpty();
        File.Exists(Path.Combine(made.Path, "mods", "sub", "s.txt")).Should().BeTrue(
            "a linked worktree gets no submodules from git, and in this family the rules ARE submodules");
        made.EmptyMounts.Should().BeEmpty();
    }

    // ---------------------------------------------------------------------------------------
    // The row's coordinates, refused before any process.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task AShaThatIsNotOne_NeverReachesGit()
    {
        var watching = new Watching(_launcher);
        var refused = await new ReviewWorktrees(watching, new GitHistory(watching), _root).PrepareAsync(
            7, new TreePlace(_repo, "--upload-pack=touch"), TestContext.Current.CancellationToken);

        refused.Reason.Should().Be(ReviewTreeReason.CommitUnreachable);
        watching.Ran.Should().BeEmpty("a value that could be an option must never start a process");
    }

    [Fact]
    public async Task ACheckoutThatIsGone_IsSaidAsRepoPathMissing()
    {
        var gone = Path.Combine(Path.GetTempPath(), $"coai-never-{Guid.NewGuid():N}");

        var refused = await Trees().PrepareAsync(
            7, new TreePlace(gone, _sha), TestContext.Current.CancellationToken);

        refused.Reason.Should().Be(ReviewTreeReason.RepoPathMissing);
        refused.Path.Should().BeEmpty();
    }

    [Fact]
    public async Task ACommitThisRepositoryNeverHad_IsSaidAsCommitUnreachable()
    {
        var refused = await Trees().PrepareAsync(
            7, Place(new string('b', 40)), TestContext.Current.CancellationToken);

        refused.Reason.Should().Be(ReviewTreeReason.CommitUnreachable);
        Directory.GetDirectories(_root).Should().BeEmpty("nothing is created for a commit that is not there");
    }

    // ---------------------------------------------------------------------------------------

    private ReviewWorktrees Trees() => new(_launcher, new GitHistory(_launcher), _root);

    private TreePlace Place(string sha) => new(_repo, sha);

    /// <summary>A directory where the tree would go, with no record beside it — what a crash leaves.</summary>
    private async Task<string> HalfMade(bool old = true)
    {
        var made = await Trees().PrepareAsync(7, Place(_sha), TestContext.Current.CancellationToken);
        File.Delete(Path.Combine(_root, $"{Path.GetFileName(made.Path)}.json"));
        if (old)
        {
            Directory.SetCreationTimeUtc(made.Path, DateTime.UtcNow.AddHours(-2));
        }

        return made.Path;
    }

    private async Task<string> OrphanedCommit()
    {
        await Git(_repo, "checkout", "-q", "-b", "throwaway");
        await File.WriteAllTextAsync(Path.Combine(_repo, "o.txt"), "orphan");
        await Git(_repo, "add", ".");
        await Git(_repo, "commit", "-m", "the commit a reviewer read");
        var sha = await ShaOf(_repo, "HEAD");
        await Git(_repo, "checkout", "-q", "main");
        await Git(_repo, "branch", "-q", "-D", "throwaway");

        return sha;
    }

    private async Task<string> ShaOf(string repo, string rev)
    {
        var result = await Run(repo, "rev-parse", rev);
        result.ExitCode.Should().Be(0);

        return result.StdOut.Trim();
    }

    private string Temp(string prefix)
    {
        var path = Directory.CreateTempSubdirectory(prefix).FullName;
        _temps.Add(path);

        return path;
    }

    private Task<ProcessResult> Run(string cwd, params string[] args) =>
        _launcher.RunAsync(new ProcessRequest(
            "git",
            ["-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", .. args],
            cwd));

    private async Task Git(string cwd, params string[] args)
    {
        var result = await Run(cwd, args);
        result.ExitCode.Should().Be(0, $"git {string.Join(' ', args)} must succeed: {result.StdErr}");
    }

    /// <summary>Unlocks every review tree under a root so the fixture can delete it.</summary>
    private void Unlock(string dir)
    {
        if (!Directory.Exists(dir))
        {
            return;
        }

        foreach (var tree in Directory.GetDirectories(dir, "coai-review-*"))
        {
            _launcher.RunAsync(new ProcessRequest("git", ["worktree", "unlock", tree], _repo))
                .GetAwaiter().GetResult();
        }
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

    /// <summary>Real git, and a note of everything it was asked — for proving a call did NOT happen.</summary>
    private sealed class Watching(IProcessLauncher real) : IProcessLauncher
    {
        public List<string> Ran { get; } = [];

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            Ran.Add($"{request.Executable} {string.Join(' ', request.Arguments)}");

            return real.RunAsync(request, ct);
        }
    }
}
