using Xunit;
using CoaiMcp.Core.Collecting;
using CoaiMcp.Runners.Collecting;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Worktrees;
using FluentAssertions;

namespace CoaiMcp.Tests;

/// <summary>
/// Giving a review tree back — real git, temp repositories, and every case that could lose somebody's
/// work.
/// </summary>
/// <remarks>
/// <para>The first four tests are the story: a tree with anything of a person's in it is refused, by
/// name, and afterwards the tree, its lock, its registration and its record are all exactly as they
/// were. Two of the four are about a populated SUBMODULE, which is where a plain status sees the
/// mount and never the file.</para>
/// <para>Teardown unlocks everything BEFORE deleting anything and guards on the repository still
/// existing: a process started in a deleted working directory is tolerated on Windows and throws on
/// Linux, which failed a whole class in CI once already.</para>
/// </remarks>
public sealed class AReviewTreeIsGivenBackTests : IAsyncLifetime
{
    private readonly ProcessLauncher _launcher = new();
    private readonly List<string> _temps = [];
    private string _repo = string.Empty;
    private string _root = string.Empty;
    private string _sha = string.Empty;

    public async ValueTask InitializeAsync()
    {
        _repo = Temp("coai-repo-");
        _root = Temp("coai-review-root-");
        await Git(_repo, "init", "-b", "main");
        await File.WriteAllTextAsync(Path.Combine(_repo, "a.txt"), "v1");
        await File.WriteAllTextAsync(Path.Combine(_repo, ".gitignore"), "ignored-by-name.txt\nbuild/\n");
        await Git(_repo, "add", ".");
        await Git(_repo, "commit", "-m", "v1");
        _sha = await ShaOf(_repo, "HEAD");
    }

    public ValueTask DisposeAsync()
    {
        foreach (var temp in _temps)
        {
            Unlock(temp);
        }

        foreach (var temp in _temps)
        {
            TryDelete(temp);
        }

        return ValueTask.CompletedTask;
    }

    // ---------------------------------------------------------------------------------------
    // The invariant: nothing a person typed is deleted by pressing anything in this product.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task AModifiedTrackedFile_RefusesRemoval_AndNamesIt()
    {
        var tree = await Made();
        await File.WriteAllTextAsync(
            Path.Combine(tree.Path, "a.txt"), "I was editing this", TestContext.Current.CancellationToken);

        var said = await Keeper().RemoveAsync(tree.Name, ct: TestContext.Current.CancellationToken);

        said.Reason.Should().Be(ReviewTreeRemovalReason.Dirty);
        said.InTheWay.Should().Contain("a.txt");
        await Intact(tree);
    }

    [Fact]
    public async Task AnUntrackedFile_RefusesRemoval_AndNamesIt()
    {
        var tree = await Made();
        await File.WriteAllTextAsync(
            Path.Combine(tree.Path, "notes-i-was-writing.md"), "mine", TestContext.Current.CancellationToken);

        var said = await Keeper().RemoveAsync(tree.Name, ct: TestContext.Current.CancellationToken);

        said.Reason.Should().Be(ReviewTreeRemovalReason.Dirty);
        said.InTheWay.Should().Contain("notes-i-was-writing.md");
        await Intact(tree);
    }

    /// <summary>
    /// One status in the parent SEES this and reports the mount, never the file — measured against
    /// real git before the design was written. Naming it takes a second status inside the submodule,
    /// and a refusal nobody can act on is not a refusal.
    /// </summary>
    [Fact]
    public async Task AModifiedFileInsideASubmodule_RefusesRemoval_AndNamesTheFile()
    {
        var tree = await MadeWithSubmodule();
        await File.WriteAllTextAsync(
            Path.Combine(tree.Path, "mods", "sub", "s.txt"), "changed", TestContext.Current.CancellationToken);

        var said = await Keeper().RemoveAsync(tree.Name, ct: TestContext.Current.CancellationToken);

        said.Reason.Should().Be(ReviewTreeRemovalReason.Dirty);
        said.InTheWay.Should().Contain("mods/sub/s.txt", "the mount alone tells a person nothing they can act on");
        said.InTheWay.Should().NotContain("mods/sub");
        await Intact(tree);
    }

    [Fact]
    public async Task AnUntrackedFileInsideASubmodule_RefusesRemoval_AndNamesTheFile()
    {
        var tree = await MadeWithSubmodule();
        await File.WriteAllTextAsync(
            Path.Combine(tree.Path, "mods", "sub", "mine.md"), "mine", TestContext.Current.CancellationToken);

        var said = await Keeper().RemoveAsync(tree.Name, ct: TestContext.Current.CancellationToken);

        said.Reason.Should().Be(ReviewTreeRemovalReason.Dirty);
        said.InTheWay.Should().Contain("mods/sub/mine.md");
        await Intact(tree);
    }

    /// <summary>
    /// The first plan called ignored files reproducible and swept them along. A `.env`, a local
    /// config and a globally ignored note are none of those, and no plain status shows them at all.
    /// (Plan round, codex, Blocking.)
    /// </summary>
    [Fact]
    public async Task AnIgnoredFile_RefusesRemoval_UntilItIsAskedForExplicitly()
    {
        var tree = await Made();
        await File.WriteAllTextAsync(
            Path.Combine(tree.Path, "ignored-by-name.txt"), "MY_TOKEN=...", TestContext.Current.CancellationToken);

        var first = await Keeper().RemoveAsync(tree.Name, ct: TestContext.Current.CancellationToken);

        first.Reason.Should().Be(ReviewTreeRemovalReason.HasIgnored);
        first.Ignored.Should().Be(1);
        first.IgnoredSample.Should().Contain("ignored-by-name.txt", "a count nobody can check is a count nobody can judge");
        Directory.Exists(tree.Path).Should().BeTrue();

        var asked = await Keeper().RemoveAsync(
            tree.Name, withIgnored: true, ct: TestContext.Current.CancellationToken);

        asked.Reason.Should().Be(ReviewTreeRemovalReason.Removed);
        asked.Ignored.Should().Be(1, "the answer still says what went with it");
        Directory.Exists(tree.Path).Should().BeFalse();
    }

    /// <summary>
    /// The hole three reviewers found and a measurement confirmed: a parent's status with
    /// <c>--ignore-submodules=none --ignored=matching</c> is COMPLETELY BLIND to an ignored file
    /// inside a populated submodule. It reports an ignored file in the parent and answers nothing at
    /// all for one in a submodule — so a `.env` in there read as a clean tree and would have gone
    /// with <c>worktree remove --force</c>, silently, which is the exact loss this story exists to
    /// prevent. Every populated mount is inspected in its own right now.
    /// </summary>
    [Fact]
    public async Task AnIgnoredFileInsideASubmodule_IsCountedAndRefused_NotSweptAlong()
    {
        var tree = await MadeWithSubmodule(ignoring: "secrets.env");
        await File.WriteAllTextAsync(
            Path.Combine(tree.Path, "mods", "sub", "secrets.env"),
            "MY_TOKEN=...",
            TestContext.Current.CancellationToken);

        var first = await Keeper().RemoveAsync(tree.Name, ct: TestContext.Current.CancellationToken);

        first.Reason.Should().Be(ReviewTreeRemovalReason.HasIgnored,
            "a parent status cannot see this at all, so nothing but a status inside the mount can");
        first.Ignored.Should().Be(1);
        first.IgnoredSample.Should().Contain("mods/sub/secrets.env", "and it is named where a person can find it");
        Directory.Exists(tree.Path).Should().BeTrue();

        var asked = await Keeper().RemoveAsync(
            tree.Name, withIgnored: true, ct: TestContext.Current.CancellationToken);

        asked.Reason.Should().Be(ReviewTreeRemovalReason.Removed);
    }

    /// <summary>
    /// An inspection that could not RUN is neither clean nor dirty. Calling it dirty would be a fact
    /// we do not have; calling it clean would delete on one. (Code round, codex.)
    /// </summary>
    [Fact]
    public async Task ASubmoduleThatCannotBeInspected_IsGitFailed_NotDirtyAndNotClean()
    {
        var tree = await MadeWithSubmodule();
        var keeper = new ReviewTreeKeeper(new ReviewTreeRoot(new FailsIn("mods", _launcher), _root));

        var said = await keeper.RemoveAsync(tree.Name, ct: TestContext.Current.CancellationToken);

        said.Reason.Should().Be(ReviewTreeRemovalReason.GitFailed);
        Directory.Exists(tree.Path).Should().BeTrue();
    }

    // ---------------------------------------------------------------------------------------
    // What removal DOES, when it may.
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The case a plain <c>git worktree remove</c> cannot do: measured exit 128, *working trees
    /// containing submodules cannot be moved or removed*. The force is reached only after the
    /// inspection came back empty, twice.
    /// </summary>
    [Fact]
    public async Task ACleanTreeWithAPopulatedSubmodule_IsRemoved()
    {
        var tree = await MadeWithSubmodule();
        File.Exists(Path.Combine(tree.Path, "mods", "sub", "s.txt")).Should().BeTrue("the submodule must be populated");

        var said = await Keeper().RemoveAsync(tree.Name, ct: TestContext.Current.CancellationToken);

        said.Reason.Should().Be(ReviewTreeRemovalReason.Removed);
        Directory.Exists(tree.Path).Should().BeFalse();
        File.Exists(ReviewTreeRecords.FileFor(_root, tree.Name)).Should().BeFalse("the record goes with it");
        (await Registered(tree.Path)).Should().BeFalse("and so does git's registration");
    }

    // ---------------------------------------------------------------------------------------
    // What can never be listed, and therefore can never be asked for.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task ARoundTreeAndAHumanWorktree_AreNeverListedAndNeverRemovable()
    {
        await Made();

        // The gate's own tree, in our very root, and a person's own worktree elsewhere.
        var gate = new WorktreeManager(_launcher, _root);
        var lease = await gate.AddAsync(_repo, _sha, "s1", round: 1);
        var theirs = Temp("coai-someones-");
        TryDelete(theirs);
        (await Run(_repo, "worktree", "add", "--detach", theirs, _sha)).ExitCode.Should().Be(0);

        var listed = await Keeper().ListAsync(TestContext.Current.CancellationToken);

        listed.Trees.Should().ContainSingle("the list is built from OUR records, not from the filesystem");
        listed.Trees.Should().NotContain(t => t.Path.Contains("coai-wt-", StringComparison.Ordinal));

        foreach (var name in new[] { Path.GetFileName(lease.Path), theirs, "../escape", "coai-review-../x" })
        {
            var said = await Keeper().RemoveAsync(name, ct: TestContext.Current.CancellationToken);
            said.Reason.Should().Be(ReviewTreeRemovalReason.NotOurs, $"'{name}' is not a tree this product made");
        }

        Directory.Exists(lease.Path).Should().BeTrue("the round's own tree is untouched");
        Directory.Exists(theirs).Should().BeTrue("and so is the person's");
    }

    [Fact]
    public async Task ANameThatIsAPath_IsRefusedBeforeAnyProcessStarts()
    {
        var watching = new Watching(_launcher);
        var keeper = new ReviewTreeKeeper(new ReviewTreeRoot(watching, _root));

        foreach (var name in new[] { "C:/Windows", "/etc", "coai-review-a/b", "coai-review-a\\b", "coai-review-..", "" })
        {
            var said = await keeper.RemoveAsync(name, ct: TestContext.Current.CancellationToken);
            said.Reason.Should().Be(ReviewTreeRemovalReason.NotOurs);
        }

        watching.Ran.Should().BeEmpty("a name that is a path must never reach git at all");
    }

    // ---------------------------------------------------------------------------------------
    // The states a tree can be in, and what the list says about each.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task AVanishedTree_IsForgotten_AndAnUnrelatedUnreachableWorktreeStaysRegistered()
    {
        var tree = await Made();

        // Somebody else's worktree, registered and then made unreachable — the one `git worktree
        // prune` would have taken with ours. (Plan round, codex, Blocking.)
        var theirs = Temp("coai-someones-");
        TryDelete(theirs);
        (await Run(_repo, "worktree", "add", "--detach", theirs, _sha)).ExitCode.Should().Be(0);
        Unlock(_root);
        await Run(_repo, "worktree", "unlock", tree.Path);
        Directory.Delete(tree.Path, recursive: true);
        Directory.Delete(theirs, recursive: true);

        var said = await Keeper().RemoveAsync(tree.Name, ct: TestContext.Current.CancellationToken);

        said.Reason.Should().Be(ReviewTreeRemovalReason.Forgotten);
        File.Exists(ReviewTreeRecords.FileFor(_root, tree.Name)).Should().BeFalse();
        (await Registered(theirs)).Should().BeTrue(
            "prune has no path filter, so this product does not run it here at all");
    }

    [Fact]
    public async Task TheListTellsReadyFromIncompleteFromVanished()
    {
        var ready = await Made();

        // A directory whose maker died before writing a record: nothing else will ever mention it.
        var half = Path.Combine(_root, "coai-review-deadbeef-000000000000");
        Directory.CreateDirectory(half);

        // A record whose tree somebody deleted by hand.
        var orphan = "coai-review-cafebabe-111111111111";
        ReviewTreeRecords.Write(
            ReviewTreeRecords.FileFor(_root, orphan),
            new HeldRecord("/r/.git", _repo, new string('d', 40), "2026-09-01T00:00:00Z", []));

        var listed = await Keeper().ListAsync(TestContext.Current.CancellationToken);

        listed.Root.Should().Be(_root);
        listed.Trees.Should().HaveCount(3);
        listed.Trees.Single(t => t.Name == ready.Name).State.Should().Be(ReviewTreeState.Ready);
        listed.Trees.Single(t => t.Name == Path.GetFileName(half)).State.Should().Be(ReviewTreeState.Incomplete);
        listed.Trees.Single(t => t.Name == orphan).State.Should().Be(ReviewTreeState.Vanished);
    }

    [Fact]
    public async Task ATreeGitNoLongerKnowsAbout_IsSaidToBeUnregistered_AndIsNotDeleted()
    {
        var tree = await Made();
        Unlock(_root);
        // The registration goes; the files stay. Neither ready nor vanished, and the difference
        // matters because the files may be somebody's. (Plan round, codex.)
        await Run(_repo, "worktree", "unlock", tree.Path);
        Directory.Move(tree.Path, tree.Path + "-moved");
        await Run(_repo, "worktree", "prune");
        Directory.Move(tree.Path + "-moved", tree.Path);

        var listed = await Keeper().ListAsync(TestContext.Current.CancellationToken);
        listed.Trees.Single(t => t.Name == tree.Name).State.Should().Be(ReviewTreeState.Unregistered);

        var said = await Keeper().RemoveAsync(tree.Name, ct: TestContext.Current.CancellationToken);
        said.Reason.Should().Be(ReviewTreeRemovalReason.Unregistered);
        Directory.Exists(tree.Path).Should().BeTrue("git could not tell us whose these files are");
    }

    [Fact]
    public async Task AStatusThatCouldNotRun_IsNeverReadAsClean()
    {
        var tree = await Made();
        var keeper = new ReviewTreeKeeper(new ReviewTreeRoot(new FailsOn("status", _launcher), _root));

        var said = await keeper.RemoveAsync(tree.Name, ct: TestContext.Current.CancellationToken);

        said.Reason.Should().Be(ReviewTreeRemovalReason.GitFailed);
        Directory.Exists(tree.Path).Should().BeTrue("we learned nothing, so nothing may be deleted");
    }

    [Fact]
    public async Task AnEmptyRootListsNothingAndRemovesNothing()
    {
        var listed = await Keeper().ListAsync(TestContext.Current.CancellationToken);

        listed.Trees.Should().BeEmpty();
        (await Keeper().RemoveAsync("coai-review-00000000-000000000000", ct: TestContext.Current.CancellationToken))
            .Reason.Should().Be(ReviewTreeRemovalReason.NotOurs);
    }

    // ---------------------------------------------------------------------------------------

    private ReviewTreeKeeper Keeper() => new(new ReviewTreeRoot(_launcher, _root));

    private ReviewWorktrees Trees() => new(_launcher, new GitHistory(_launcher), _root);

    /// <summary>One tree, made the way the product makes them — locked, recorded, registered.</summary>
    private async Task<HeldReviewTree> Made()
    {
        var made = await Trees().PrepareAsync(
            7, new TreePlace(_repo, _sha), TestContext.Current.CancellationToken);
        made.Reason.Should().BeEmpty();

        return new HeldReviewTree(Name: Path.GetFileName(made.Path), Path: made.Path);
    }

    private async Task<HeldReviewTree> MadeWithSubmodule(string ignoring = "")
    {
        var sub = Temp("coai-sub-");
        await Git(sub, "init", "-b", "main");
        await File.WriteAllTextAsync(Path.Combine(sub, "s.txt"), "sub");
        if (ignoring.Length > 0)
        {
            await File.WriteAllTextAsync(Path.Combine(sub, ".gitignore"), $"{ignoring}{Environment.NewLine}");
        }

        await Git(sub, "add", ".");
        await Git(sub, "commit", "-m", "sub");
        await Git(_repo, "-c", "protocol.file.allow=always", "submodule", "add", "-q", sub.Replace('\\', '/'), "mods/sub");
        await Git(_repo, "commit", "-m", "add submodule");
        var sha = await ShaOf(_repo, "HEAD");

        var made = await Trees().PrepareAsync(
            7, new TreePlace(_repo, sha), TestContext.Current.CancellationToken);
        made.Reason.Should().BeEmpty();

        return new HeldReviewTree(Name: Path.GetFileName(made.Path), Path: made.Path);
    }

    /// <summary>Everything a refusal promises: the files, the lock, the registration and the record.</summary>
    private async Task Intact(HeldReviewTree tree)
    {
        Directory.Exists(tree.Path).Should().BeTrue("a refusal must leave the tree exactly as it was");
        File.Exists(ReviewTreeRecords.FileFor(_root, tree.Name)).Should().BeTrue("and its record");
        (await Registered(tree.Path)).Should().BeTrue("and its registration");

        var forced = await Run(_repo, "worktree", "remove", "--force", tree.Path);
        forced.ExitCode.Should().NotBe(0, "and its lock, which is what refuses a single --force");
        forced.StdErr.Should().Contain("locked");
    }

    private async Task<bool> Registered(string path)
    {
        var listed = await Run(_repo, "worktree", "list", "--porcelain");
        var wanted = Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/');

        return listed.StdOut.Replace("\r", "").Split('\n')
            .Where(l => l.StartsWith("worktree ", StringComparison.Ordinal))
            .Any(l => string.Equals(
                Path.GetFullPath(l["worktree ".Length..].Trim()).Replace('\\', '/').TrimEnd('/'),
                wanted,
                StringComparison.OrdinalIgnoreCase));
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
        if (!Directory.Exists(dir) || !Directory.Exists(_repo))
        {
            return;
        }

        foreach (var tree in Directory.GetDirectories(dir, "coai-review-*"))
        {
            try
            {
                _launcher.RunAsync(new ProcessRequest("git", ["worktree", "unlock", tree], _repo))
                    .GetAwaiter().GetResult();
            }
            catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception)
            {
                // Teardown is not an assertion.
            }
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

    /// <summary>Real git, except where it runs — a working directory under this segment cannot run.</summary>
    private sealed class FailsIn(string segment, IProcessLauncher real) : IProcessLauncher
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default) =>
            request.WorkingDirectory.Replace('\\', '/').Contains($"/{segment}/", StringComparison.Ordinal)
                ? Task.FromResult(new ProcessResult(1, string.Empty, "git could not run here", true, false, false))
                : real.RunAsync(request, ct);
    }

    /// <summary>Real git, except one subcommand, which answers as a process that could not run.</summary>
    private sealed class FailsOn(string subcommand, IProcessLauncher real) : IProcessLauncher
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default) =>
            request.Arguments.Contains(subcommand, StringComparer.Ordinal)
                ? Task.FromResult(new ProcessResult(1, string.Empty, "git could not run", true, false, false))
                : real.RunAsync(request, ct);
    }
}
