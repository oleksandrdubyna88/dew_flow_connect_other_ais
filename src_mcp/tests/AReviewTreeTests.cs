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

    /// <summary>
    /// Unlock EVERYTHING first, then delete everything — two passes, and the order is the whole point.
    /// </summary>
    /// <remarks>
    /// One pass deleted the repository first (it is the first temp made) and then ran
    /// <c>git worktree unlock</c> for the next temp with that repository as its working directory. On
    /// Windows a missing working directory is tolerated; on Linux <c>Process.Start</c> throws
    /// <c>Win32Exception: No such file or directory</c>, which failed the TEARDOWN of all 26 tests in
    /// this class while every test body passed. Found by CI, which is the only place this class runs
    /// on Linux — the same split as `a host platform in a sentence`.
    /// </remarks>
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
        await OrphanedAsync(lease.Path);

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
        await OrphanedAsync(lease.Path);

        await gate.PruneOursAsync(_repo);

        Directory.Exists(lease.Path).Should().BeFalse("the gate must still clear its own");
        Directory.Exists(made.Path).Should().BeTrue(
            "a review tree is spared by its prefix even when it shares the gate's root");
    }

    [Fact]
    public async Task AReviewTree_SurvivesASingleForcedRemove()
    {
        var made = await Trees().PrepareAsync(7, Place(_sha), TestContext.Current.CancellationToken);

        // A single forced remove, which the lock refuses. The round-tree eraser now unlocks and forces
        // TWICE (a half-made add is locked "initializing" and must be clearable), so for it the lock is
        // no longer the guard — the prefix and the root are: it never touches a path that is not a
        // `coai-wt-` under its own root, which the two tests above prove.
        var forced = await Run(_repo, "worktree", "remove", "--force", made.Path);

        forced.ExitCode.Should().NotBe(0);
        forced.StdErr.Should().Contain("locked");
        Directory.Exists(made.Path).Should().BeTrue();

        var pruned = await Run(_repo, "worktree", "prune");
        pruned.ExitCode.Should().Be(0);
        Directory.Exists(made.Path).Should().BeTrue("prune must not take a locked tree either");
    }

    /// <summary>
    /// The lock carries a REASON, and git shows it to whoever tries to remove the tree.
    /// </summary>
    /// <remarks>
    /// A code-round finding worried the reason might be empty or ignored. It is a compile-time
    /// constant, so it cannot be empty — but "cannot" is what a test is for, and the interesting half
    /// is the one nobody had checked: that git STORES it and hands it back. It does, in
    /// `worktree list --porcelain`, and it is what a person meets in the refusal message when they
    /// reach for `worktree remove` by hand. That makes the reason a piece of user-facing text rather
    /// than a comment, which is why it names where the tree should be removed from instead.
    /// </remarks>
    [Fact]
    public async Task TheTreeIsLocked_WithAReasonThatSaysWhereToRemoveItFrom()
    {
        var made = await Trees().PrepareAsync(7, Place(_sha), TestContext.Current.CancellationToken);

        var listed = await Run(_repo, "worktree", "list", "--porcelain");
        var lines = listed.StdOut.Replace("\r", "").Split('\n');
        // By the path git REALLY recorded — behind a link (every macOS temp directory) it is not the
        // spelling we made the tree under. (The mcp-v0.31.0 release build, osx-arm64.)
        var at = Array.FindIndex(lines, l =>
            l.StartsWith("worktree ", StringComparison.Ordinal) && WorktreePaths.Same(l["worktree ".Length..], made.Path));
        at.Should().BeGreaterThan(-1, "the tree must be registered at all");

        var reason = lines.Skip(at).TakeWhile(l => l.Length > 0).FirstOrDefault(l => l.StartsWith("locked", StringComparison.Ordinal));
        reason.Should().NotBeNull("the tree must be locked, which is what refuses a single --force");
        reason!.Length.Should().BeGreaterThan("locked".Length, "an empty reason tells the person nothing");
        reason.Should().Contain("ConnectOtherAIs", "the refusal a person meets must say where to remove it from");
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
        Fill(ReviewWorktrees.Cap);

        var refused = await Trees().PrepareAsync(7, Place(_sha), TestContext.Current.CancellationToken);

        refused.Reason.Should().Be(ReviewTreeReason.Budget);
        refused.Path.Should().BeEmpty("nothing was made, so there is nothing to open");
        refused.Trees.Should().HaveCount(ReviewWorktrees.Cap);
        refused.Trees.Should().OnlyContain(t => t.Repository.Length > 0 && t.Sha.Length > 0 && t.Path.Length > 0,
            "a refusal that says 'you have ten' without saying which ten leaves a person no move");
        Directory.GetDirectories(_root).Should().HaveCount(ReviewWorktrees.Cap,
            "a cap refuses; it never evicts, so the ten that were there are all still there");
    }

    /// <summary>
    /// A record whose tree somebody deleted by hand is not a tree. Counting it would wedge a cap slot
    /// against a checkout that no longer exists, and handing it back would open a folder that is not
    /// there. (Code round, gemini and codex, three findings.)
    /// </summary>
    [Fact]
    public async Task ARecordWhoseTreeWasDeletedByHand_NeitherOpensNorSpendsASlot()
    {
        Fill(ReviewWorktrees.Cap);
        Directory.Delete(Directory.GetDirectories(_root).First(), recursive: true);

        var made = await Trees().PrepareAsync(7, Place(_sha), TestContext.Current.CancellationToken);

        made.Reason.Should().BeEmpty("nine trees remain, so there is room for one more");
        Directory.Exists(made.Path).Should().BeTrue();
    }

    [Fact]
    public async Task AReusedTreeIsHandedBackOnlyWhenItsDirectoryIsStillThere()
    {
        var trees = Trees();
        var first = await trees.PrepareAsync(7, Place(_sha), TestContext.Current.CancellationToken);
        Unlock(_root);
        Directory.Delete(first.Path, recursive: true);

        var again = await trees.PrepareAsync(7, Place(_sha), TestContext.Current.CancellationToken);

        again.Reason.Should().BeEmpty();
        again.Reused.Should().BeFalse("the record alone is not a checkout");
        Directory.Exists(again.Path).Should().BeTrue();
    }

    /// <summary>
    /// A record that EXISTS and cannot be read is not the same fact as no record. Collapsing the two
    /// let the cleanup path unlock and delete a finished tree a person may have been reading.
    /// (Code round, codex.)
    /// </summary>
    [Fact]
    public async Task ARecordThatCannotBeRead_RefusesRatherThanDeletingTheTree()
    {
        var made = await Trees().PrepareAsync(7, Place(_sha), TestContext.Current.CancellationToken);
        var record = Path.Combine(_root, $"{Path.GetFileName(made.Path)}.json");
        await File.WriteAllTextAsync(record, "{ this is not json", TestContext.Current.CancellationToken);
        Directory.SetLastWriteTimeUtc(made.Path, DateTime.UtcNow.AddHours(-2));

        var answer = await Trees().PrepareAsync(7, Place(_sha), TestContext.Current.CancellationToken);

        answer.Reason.Should().Be(ReviewTreeReason.IncompleteAndDirty);
        Directory.Exists(made.Path).Should().BeTrue("a record we cannot read is never a licence to delete");
    }

    /// <summary>
    /// A record missing the fields that carry the identity used to deserialise as null into
    /// non-nullable properties and crash the mode on `record.Sha.Length`. (Code round, codex, twice.)
    /// </summary>
    [Fact]
    public async Task ARecordMissingItsFields_IsRefusedAndDoesNotCrash()
    {
        var made = await Trees().PrepareAsync(7, Place(_sha), TestContext.Current.CancellationToken);
        var record = Path.Combine(_root, $"{Path.GetFileName(made.Path)}.json");
        await File.WriteAllTextAsync(record, "{}", TestContext.Current.CancellationToken);

        var answer = await Trees().PrepareAsync(7, Place(_sha), TestContext.Current.CancellationToken);

        answer.Reason.Should().Be(ReviewTreeReason.IncompleteAndDirty,
            "a record with no identity cannot prove the tree never finished, so nothing may be removed");
        Directory.Exists(made.Path).Should().BeTrue();
    }

    /// <summary>
    /// The directory name truncates the digest and the sha, so two identities could in principle land
    /// on one name. The record carries the FULL identity and is compared before its tree is handed
    /// back, so a collision costs a rebuild rather than the wrong repository opened. (Code round, codex.)
    /// </summary>
    [Fact]
    public async Task ARecordForAnotherIdentity_IsNotHandedBackForThisOne()
    {
        var made = await Trees().PrepareAsync(7, Place(_sha), TestContext.Current.CancellationToken);
        var record = Path.Combine(_root, $"{Path.GetFileName(made.Path)}.json");
        ReviewTreeRecords.Write(record, new HeldRecord(
            "/somewhere/else/.git", "/somewhere/else", new string('c', 40), "2026-01-01T00:00:00Z", []));

        var answer = await Trees().PrepareAsync(7, Place(_sha), TestContext.Current.CancellationToken);

        answer.Reused.Should().BeFalse("a record that names another commit does not prove this tree");
        answer.Reason.Should().Be(ReviewTreeReason.InProgress, "and the directory was touched moments ago");
    }

    /// <summary>
    /// A leftover directory that is not a worktree at all — `worktree add` died before it wrote the
    /// `.git` file. Refusing every such directory forever wedged that identity permanently, with no
    /// route back but deleting it by hand. An EMPTY one holds nothing and is cleared. (Code round,
    /// gemini, round 2.)
    /// </summary>
    [Fact]
    public async Task AnEmptyLeftoverThatIsNotAWorktree_IsClearedRatherThanWedgingTheIdentity()
    {
        var made = await Trees().PrepareAsync(7, Place(_sha), TestContext.Current.CancellationToken);
        var path = made.Path;
        Unlock(_root);
        await Run(_repo, "worktree", "remove", "--force", path);
        File.Delete(Path.Combine(_root, $"{Path.GetFileName(path)}.json"));
        Directory.CreateDirectory(path);
        Age(path);

        var again = await Trees().PrepareAsync(7, Place(_sha), TestContext.Current.CancellationToken);

        again.Reason.Should().BeEmpty("an empty leftover is nobody's work, so it must not wedge the commit");
        Directory.Exists(again.Path).Should().BeTrue();
    }

    [Fact]
    public async Task ALeftoverThatIsNotAWorktreeButHoldsFiles_IsNamedAndLeftAlone()
    {
        var made = await Trees().PrepareAsync(7, Place(_sha), TestContext.Current.CancellationToken);
        var path = made.Path;
        Unlock(_root);
        await Run(_repo, "worktree", "remove", "--force", path);
        File.Delete(Path.Combine(_root, $"{Path.GetFileName(path)}.json"));
        Directory.CreateDirectory(path);
        await File.WriteAllTextAsync(
            Path.Combine(path, "mine.md"), "something", TestContext.Current.CancellationToken);
        Age(path);

        var again = await Trees().PrepareAsync(7, Place(_sha), TestContext.Current.CancellationToken);

        again.Reason.Should().Be(ReviewTreeReason.IncompleteAndDirty);
        File.Exists(Path.Combine(path, "mine.md")).Should().BeTrue("a directory with files in it is somebody's");
    }

    /// <summary>
    /// The cap counts a tree whose record cannot be read. It occupies disk and it is somebody's; the
    /// docblock said so while the code filtered it out. (Code round, gemini, round 2.)
    /// </summary>
    [Fact]
    public async Task ATreeWhoseRecordCannotBeRead_StillSpendsItsSlot()
    {
        Fill(ReviewWorktrees.Cap);
        var one = Directory.GetDirectories(_root).First();
        await File.WriteAllTextAsync(
            Path.Combine(_root, $"{Path.GetFileName(one)}.json"), "not json", TestContext.Current.CancellationToken);

        var refused = await Trees().PrepareAsync(7, Place(_sha), TestContext.Current.CancellationToken);

        refused.Reason.Should().Be(ReviewTreeReason.Budget, "a tree we cannot read about is still a tree");
        refused.Trees.Should().HaveCount(ReviewWorktrees.Cap);
        refused.Trees.Should().OnlyContain(t => t.Path.Length > 0,
            "its PATH comes from the file name, which an unreadable record still has");
    }

    /// <summary>
    /// A record with no working tree recorded describes a checkout nothing can ever remove, so it is
    /// not a finished tree. (Code round, codex, round 2.)
    /// </summary>
    [Fact]
    public async Task ARecordWithNoWorkingTree_IsNotTreatedAsFinished()
    {
        var made = await Trees().PrepareAsync(7, Place(_sha), TestContext.Current.CancellationToken);
        var file = Path.Combine(_root, $"{Path.GetFileName(made.Path)}.json");
        var without = (await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken))
            .Replace($"\"repoPath\":\"{_repo.Replace("\\", "\\\\")}\"", "\"repoPath\":\"\"", StringComparison.Ordinal);
        await File.WriteAllTextAsync(file, without, TestContext.Current.CancellationToken);

        var read = ReviewTreeRecords.Read(file);

        read.State.Should().Be(RecordState.Unreadable,
            "a record that cannot support unlock and remove is not one this product may hand back");
    }

    [Fact]
    public async Task TheRecordKeepsAWorkingTreeToRunGitFrom()
    {
        var made = await Trees().PrepareAsync(7, Place(_sha), TestContext.Current.CancellationToken);

        var read = ReviewTreeRecords.Read(Path.Combine(_root, $"{Path.GetFileName(made.Path)}.json"));

        read.State.Should().Be(RecordState.Found);
        // `worktree unlock` and `worktree remove` refuse to run from a bare .git directory, so story
        // 3.2b needs somewhere to run them FROM. Discovering it later would mean migrating every
        // record written before. (Code round, gemini.)
        read.Record.RepoPath.Should().Be(_repo);
        read.Record.Repository.Should().NotBe(read.Record.RepoPath);
    }

    [Fact]
    public async Task AHalfMadeTree_IsRebuiltNotReused()
    {
        var path = await HalfMade();
        await File.WriteAllTextAsync(
            Path.Combine(path, "a.txt"), "v1", TestContext.Current.CancellationToken);
        Age(path);

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
        Age(path);

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

    /// <summary>
    /// Fills the root with trees the cap will count: a record AND the directory beside it, because
    /// a record whose tree is gone is deliberately not counted.
    /// </summary>
    private void Fill(int count)
    {
        for (var i = 0; i < count; i++)
        {
            var name = $"coai-review-0000000{i}-{i:D12}";
            Directory.CreateDirectory(Path.Combine(_root, name));
            ReviewTreeRecords.Write(
                Path.Combine(_root, $"{name}.json"),
                new HeldRecord($"/r{i}/.git", $"/r{i}", new string('a', 39) + i, $"2026-09-0{i % 9 + 1}T00:00:00Z", []));
        }
    }

    private TreePlace Place(string sha) => new(_repo, sha);

    /// <summary>A directory where the tree would go, with no record beside it — what a crash leaves.</summary>
    private async Task<string> HalfMade(bool old = true)
    {
        var made = await Trees().PrepareAsync(7, Place(_sha), TestContext.Current.CancellationToken);
        File.Delete(Path.Combine(_root, $"{Path.GetFileName(made.Path)}.json"));
        if (old)
        {
            Age(made.Path);
        }

        return made.Path;
    }

    /// <summary>Makes a directory look untouched for long enough that its maker is presumed dead.</summary>
    private static void Age(string path) =>
        Directory.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(-2));

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

    /// <summary>
    /// Makes a round tree what a KILLED session leaves: its owner marker now names a server that is
    /// gone (this pid with another start time — what a reused number looks like). A tree whose owner
    /// is alive is no longer swept at all (S2 of research/PLAN_a_failed_round_can_be_retried.md), so the
    /// "prune ran and did its job" half of these tests needs a tree that is genuinely an orphan.
    /// </summary>
    private static Task OrphanedAsync(string treePath) =>
        File.WriteAllTextAsync(
            treePath + ".owner",
            $$"""{"Pid":{{Environment.ProcessId}},"StartedUtc":"2001-01-01T00:00:00Z"}""");

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

    /// <summary>
    /// Unlocks every review tree under a root so the fixture can delete it.
    /// </summary>
    /// <remarks>
    /// The repository must still be there: a process started with a working directory that does not
    /// exist throws on Linux and is tolerated on Windows, so the guard is what keeps this class's
    /// teardown the same on both. The launch is wrapped because a best-effort unlock during teardown
    /// must never become the reason a test is reported as failing.
    /// </remarks>
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
                // Teardown is not an assertion. Whatever could not be unlocked is deleted anyway.
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
}
