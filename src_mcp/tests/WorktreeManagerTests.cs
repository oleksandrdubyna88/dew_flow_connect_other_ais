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

    /// <summary>
    /// The source is a clone source with <c>protocol.file.allow</c> lifted, so "inside the checkout"
    /// has to survive a junction — text normalisation cannot see one.
    /// </summary>
    [Fact]
    public async Task ASourceReachedThroughALink_IsNotClonedFrom()
    {
        var outside = await AddSubmoduleAsync("real", "secret");
        await File.WriteAllTextAsync(
            Path.Combine(_repo, ".gitmodules"),
            "[submodule \"linked\"]\n\tpath = linked\n\turl = https://example.invalid/x.git\n");
        await Git(_repo, "add", ".gitmodules");
        await Git(_repo, "commit", "-m", "declare a mount whose path is a link");
        if (!TryLink(Path.Combine(_repo, "linked"), outside))
        {
            Assert.Skip("this machine cannot create a directory link without elevation");
        }

        var sha = await _manager.ResolveShaAsync(_repo, "main");
        await using var lease = await _manager.AddAsync(_repo, sha, "s1", round: 1);

        File.Exists(Path.Combine(lease.Path, "linked", "rules.md"))
            .Should().BeFalse("a link is not a path inside the checkout, whatever it spells");
    }

    // ---------------------------------------------------------------------------------------------
    // A failed round's tree never blocks the next attempt (research/PLAN_a_failed_round_can_be_retried.md, S2).
    // Both failures below were reproduced against git 2.55 before a line was written.

    /// <summary>
    /// A <c>worktree add</c> killed half-way leaves its registration LOCKED "initializing" — which
    /// <c>remove --force</c> refuses and <c>prune</c> skips — so every later add at that path failed
    /// "missing but locked" for ever. The reported "blocks every retry until a console cleanup".
    /// </summary>
    [Fact]
    public async Task AHalfMadeTreeOfADeadServer_NeverBlocksTheNextRound()
    {
        var sha = await _manager.ResolveShaAsync(_repo, "main");
        var leftover = Path.Combine(_storage, "coai-wt-S-r1");
        await Git(_repo, "worktree", "add", "--detach", "--lock", "--reason", "initializing", leftover, sha);
        Directory.Delete(leftover, recursive: true);

        await using (var lease = await _manager.AddAsync(_repo, sha, "S", round: 1))
        {
            Directory.Exists(lease.Path).Should().BeTrue("the retry runs");
        }

        (await GitOut(_repo, "worktree", "list", "--porcelain")).Should().NotContain("coai-wt-S-r1\n",
            "the dead half-made registration is cleared, not left for a human");
        (await _manager.ListOursAsync(_repo)).Should().BeEmpty();
    }

    /// <summary>
    /// A file held open inside the tree (a reviewer's child, a scanner) made removal throw from the
    /// round's <c>await using</c> — and that exception replaced the finished round's verdict with
    /// <c>{"error":"git worktree remove: …"}</c>. Giving a tree back must never throw.
    /// </summary>
    [Fact]
    public async Task ATreeHeldOpen_IsNeverAnErrorWhenItIsGivenBack()
    {
        var sha = await _manager.ResolveShaAsync(_repo, "main");
        var lease = await _manager.AddAsync(_repo, sha, "held", round: 1);
        var held = Held.Inside(lease.Path, _storage);

        var giveBack = async () => await lease.DisposeAsync();

        await giveBack.Should().NotThrowAsync("a tree that will not go is the next sweep's job, never the round's error");
        held.Dispose();
        await _manager.PruneOursAsync(_repo);
        Directory.GetDirectories(_storage).Should().BeEmpty("once let go, the next sweep removes it — moved-aside trash included");
    }

    /// <summary>
    /// Every Claude window runs its own server on one data directory, and <c>open</c>'s sweep used to
    /// remove EVERY round tree by prefix — including a running round's, in another window.
    /// </summary>
    [Fact]
    public async Task OpenInOneServer_LeavesAnotherServersLiveTree()
    {
        var sha = await _manager.ResolveShaAsync(_repo, "main");
        await using var running = await _manager.AddAsync(_repo, sha, "other-window", round: 1);
        var anotherServer = new WorktreeManager(_launcher, _storage);

        await anotherServer.PruneOursAsync(_repo);

        Directory.Exists(running.Path).Should().BeTrue("its owner is alive, so it is not anybody's to sweep");
        File.Exists(Path.Combine(running.Path, "a.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task ATreeWhoseServerIsGone_IsSweptOnOpen()
    {
        var sha = await _manager.ResolveShaAsync(_repo, "main");
        var lease = await _manager.AddAsync(_repo, sha, "dead-window", round: 1);
        // The same pid with another start time is exactly what a reused process number looks like.
        await File.WriteAllTextAsync(
            lease.Path + ".owner",
            $$"""{"Pid":{{Environment.ProcessId}},"StartedUtc":"2001-01-01T00:00:00Z"}""",
            TestContext.Current.CancellationToken);

        await new WorktreeManager(_launcher, _storage).PruneOursAsync(_repo);

        Directory.Exists(lease.Path).Should().BeFalse("its server is gone, so the tree is an orphan");
        (await _manager.ListOursAsync(_repo)).Should().BeEmpty();
    }

    /// <summary>
    /// <c>open</c>'s sweep ended in an unguarded <c>Directory.Delete</c>: one held file in a leftover
    /// directory threw <c>IOException</c> out of <c>open</c> itself, which only caught git's errors.
    /// </summary>
    [Fact]
    public async Task AHeldFileInALeftover_NeverFailsOpen()
    {
        var leftover = Directory.CreateDirectory(Path.Combine(_storage, "coai-wt-gone-r1")).FullName;
        using var held = Held.Inside(leftover, _storage);

        var sweep = () => _manager.PruneOursAsync(_repo);

        await sweep.Should().NotThrowAsync("a directory that will not go is a warning, never a failed open");
    }

    /// <summary>
    /// A git that refuses to make the tree leaves nothing behind — no directory, no owner marker — and
    /// the error quotes git's verdict.
    /// </summary>
    [Fact]
    public async Task AFailedAdd_LeavesNothingBehind_AndSaysWhy()
    {
        var add = () => _manager.AddAsync(_repo, "0123456789abcdef0123456789abcdef01234567", "bad", round: 1);

        (await add.Should().ThrowAsync<WorktreeException>()).Which.Message.Should().Contain("fatal:");
        Directory.GetDirectories(_storage, "coai-wt-bad-*").Should().BeEmpty();
        Directory.GetFiles(_storage, "coai-wt-bad-*").Should().BeEmpty("the owner marker goes with the tree it named");
    }

    /// <summary>A tree an older build made has no owner marker — nobody is using it now, so it is swept.</summary>
    [Fact]
    public async Task ATreeWithNoOwnerMarker_IsSweptOnOpen()
    {
        var sha = await _manager.ResolveShaAsync(_repo, "main");
        var legacy = Path.Combine(_storage, "coai-wt-legacy-r1");
        await Git(_repo, "worktree", "add", "--detach", legacy, sha);

        await _manager.PruneOursAsync(_repo);

        Directory.Exists(legacy).Should().BeFalse();
        (await _manager.ListOursAsync(_repo)).Should().BeEmpty();
    }

    /// <summary>A marker whose tree is gone and whose owner is gone names nothing, and does not pile up.</summary>
    [Fact]
    public async Task AMarkerOfAVanishedTreeWhoseOwnerIsGone_IsForgotten()
    {
        var marker = Path.Combine(_storage, "coai-wt-vanished-r1-0000beef.owner");
        await File.WriteAllTextAsync(
            marker,
            $$"""{"Pid":{{Environment.ProcessId}},"StartedUtc":"2001-01-01T00:00:00Z"}""",
            TestContext.Current.CancellationToken);

        await _manager.PruneOursAsync(_repo);

        File.Exists(marker).Should().BeFalse();
    }

    /// <summary>
    /// The root reached through a LINK is still our root. On macOS the temp directory is
    /// <c>/var/…</c>, a link to <c>/private/var/…</c>, and git lists a tree by its resolved path — so
    /// the sweep, comparing strings, took a half-made tree of a dead server for somebody else's and left
    /// it registered for ever (the macOS job of PR 497).
    /// </summary>
    [Fact]
    public async Task ARootReachedThroughALink_StillKnowsItsOwnTrees()
    {
        var real = Directory.CreateTempSubdirectory("coai-wt-real-").FullName;
        _temps.Add(real);
        var link = Path.Combine(Directory.CreateTempSubdirectory("coai-wt-link-").FullName, "to-real");
        _temps.Add(Path.GetDirectoryName(link)!);
        if (!TryLink(link, real) && !await TryJunctionAsync(link, real))
        {
            Assert.Skip("this machine can create neither a directory link nor a junction");
        }

        var sha = await _manager.ResolveShaAsync(_repo, "main");
        var leftover = Path.Combine(real, "coai-wt-S-r1");
        await Git(_repo, "worktree", "add", "--detach", "--lock", "--reason", "initializing", leftover, sha);
        Directory.Delete(leftover, recursive: true);

        await new WorktreeManager(_launcher, link).PruneOursAsync(_repo);

        (await GitOut(_repo, "worktree", "list", "--porcelain")).Should().NotContain("coai-wt-S-r1",
            "a tree under our root is ours by whichever of the root's names git happens to print");
    }

    /// <summary>
    /// A link whose TARGET runs through another link. A link's target is stored as written, so on macOS
    /// a root linked to <c>/var/…/real</c> resolved to that spelling while git printed
    /// <c>/private/var/…/real</c> — the macOS job of PR 499 caught the first fix stopping one link short.
    /// </summary>
    [Fact]
    public async Task ARootReachedThroughALinkToALink_StillKnowsItsOwnTrees()
    {
        var real = Directory.CreateTempSubdirectory("coai-wt-real-").FullName;
        _temps.Add(real);
        var links = Directory.CreateTempSubdirectory("coai-wt-links-").FullName;
        _temps.Add(links);
        var first = Path.Combine(links, "first");
        var second = Path.Combine(links, "second");
        var storage = Directory.CreateDirectory(Path.Combine(real, "storage")).FullName;
        if (!await LinkedAsync(first, real) || !await LinkedAsync(second, Path.Combine(first, "storage")))
        {
            Assert.Skip("this machine can create neither a directory link nor a junction");
        }

        var sha = await _manager.ResolveShaAsync(_repo, "main");
        var leftover = Path.Combine(storage, "coai-wt-S-r1");
        await Git(_repo, "worktree", "add", "--detach", "--lock", "--reason", "initializing", leftover, sha);
        Directory.Delete(leftover, recursive: true);

        await new WorktreeManager(_launcher, second).PruneOursAsync(_repo);

        (await GitOut(_repo, "worktree", "list", "--porcelain")).Should().NotContain("coai-wt-S-r1",
            "every link on the way is resolved, including the ones inside a link's own target");
    }

    private async Task<bool> LinkedAsync(string link, string target) =>
        TryLink(link, target) || await TryJunctionAsync(link, target);

    /// <summary>
    /// Something inside a directory that cannot be deleted, on every platform: a file held open on
    /// Windows, a read-only subdirectory elsewhere — so the paths that exist for a held tree are run
    /// on the CI machines too, not only on a developer's.
    /// </summary>
    private sealed class Held : IDisposable
    {
        private readonly FileStream? _open;
        private readonly string _root = string.Empty;

        private Held(FileStream open) => _open = open;

        private Held(string root) => _root = root;

        /// <param name="root">
        /// Where to look for it again on release: a tree that could not be deleted is MOVED aside, so
        /// the locked directory is no longer at the path it was made at.
        /// </param>
        public static Held Inside(string directory, string root)
        {
            var inner = Directory.CreateDirectory(Path.Combine(directory, "held")).FullName;
            var file = Path.Combine(inner, "held.txt");
            File.WriteAllText(file, "x");
            if (OperatingSystem.IsWindows())
            {
                return new Held(new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None));
            }

            File.SetUnixFileMode(inner, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            return new Held(root);
        }

        public void Dispose()
        {
            _open?.Dispose();
            if (_root.Length == 0 || OperatingSystem.IsWindows())
            {
                return;
            }

            foreach (var locked in Directory.GetDirectories(_root, "held", SearchOption.AllDirectories))
            {
                File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }
    }

    [Theory]
    [InlineData("Preparing worktree (detached HEAD abc123)\nfatal: 'x' already exists\n", "fatal: 'x' already exists")]
    [InlineData("fatal: 'y' is a missing but locked worktree;\nuse 'add -f -f' to override", "fatal: 'y' is a missing but locked worktree;")]
    [InlineData("error: something else\n", "error: something else")]
    public void AFailedAdd_QuotesGitsVerdict_NotItsPreamble(string stderr, string expected) =>
        WorktreeManager.FatalLine(stderr).Should().Be(expected);

    /// <summary>A junction — Windows' link that needs no elevation, and which .NET reports as a link too.</summary>
    private async Task<bool> TryJunctionAsync(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var made = await _launcher.RunAsync(new ProcessRequest("cmd", ["/c", "mklink", "/J", link, target], _repo));
        return made.ExitCode == 0 && Directory.Exists(link);
    }

    private static bool TryLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
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
