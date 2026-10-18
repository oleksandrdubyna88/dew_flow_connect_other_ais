using CoaiMcp.Runners.Context;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What ONE git call says about a folder, and how a linked worktree is told from every other checkout (D9 of
/// <c>todo/PLAN_vibe_reads_a_folder_of_repositories.md</c>). The decision is git's answer — the absolute git dir against the common
/// dir — never the text of a <c>.git</c> file: a submodule's top has such a file too, and a submodule INSIDE a linked worktree writes a
/// gitdir that contains <c>/worktrees/</c> (<c>src_vs_code/src/projectIdentity.ts</c> documents the trap; its scenarios are mirrored here).
/// </summary>
public sealed class RepositoryFactsTests : IDisposable
{
    private readonly ProcessLauncher _launcher = new();
    private readonly TempDir _dir = TempDir.For("testsToDelete-repo-facts-");

    public void Dispose() => _dir.Dispose();

    private async Task<string> GitAsync(string at, params string[] args)
    {
        var result = await _launcher.RunAsync(new ProcessRequest("git", args, at), TestContext.Current.CancellationToken);
        result.ExitCode.Should().Be(0, string.Join(' ', args) + ": " + result.StdErr);

        return result.StdOut;
    }

    private async Task<string> RepositoryAsync(string at)
    {
        Directory.CreateDirectory(at);
        await File.WriteAllTextAsync(Path.Combine(at, "f.txt"), "f", TestContext.Current.CancellationToken);
        foreach (var args in (string[][])[["init", "-b", "main"], ["config", "user.email", "t@example.com"], ["config", "user.name", "t"], ["add", "."], ["commit", "-m", "base"]])
        {
            await GitAsync(at, args);
        }

        return at;
    }

    private Task<RepositoryFacts> AskAsync(string folder) =>
        RepositoryFacts.AskAsync(_launcher, folder, TestContext.Current.CancellationToken);

    [Fact]
    public async Task AnOrdinaryCheckout_IsATopAndMain_ItsGitDirIsItsCommonDir()
    {
        var main = await RepositoryAsync(_dir.At("main"));

        var facts = await AskAsync(main);

        facts.InsideWorkTree.Should().BeTrue();
        facts.IsTop.Should().BeTrue();
        facts.IsLinked.Should().BeFalse();
        facts.IsMain.Should().BeTrue();
        facts.GitDir.Should().NotBeEmpty().And.Be(facts.CommonDir);
        // Never compared with the path the test made: git resolves links (macOS's /var is /private/var), and the product only
        // ever compares git's answers with git's answers.
        facts.CommonDir.Should().EndWith("/.git");
    }

    [Fact]
    public async Task AFolderInsideACheckout_IsInsideTheWorkTree_ButNotItsTop()
    {
        var main = await RepositoryAsync(_dir.At("main"));
        Directory.CreateDirectory(Path.Combine(main, "src", "deep"));

        var facts = await AskAsync(Path.Combine(main, "src", "deep"));

        facts.InsideWorkTree.Should().BeTrue();
        facts.IsTop.Should().BeFalse("git says there is a work tree above it to climb to");
        facts.IsMain.Should().BeFalse("a subfolder is not a main checkout");
    }

    [Fact]
    public async Task ALinkedWorktree_IsLinked_ItsCommonDirIsTheMainsGitDir()
    {
        var main = await RepositoryAsync(_dir.At("main"));
        var worktree = _dir.At("wt");
        await GitAsync(main, "worktree", "add", "-b", "wt", worktree);

        var mainFacts = await AskAsync(main);
        var facts = await AskAsync(worktree);

        File.Exists(Path.Combine(worktree, ".git")).Should().BeTrue("a linked worktree's .git is a file");
        facts.IsTop.Should().BeTrue();
        facts.IsLinked.Should().BeTrue();
        facts.IsMain.Should().BeFalse();
        facts.GitDir.Should().NotBe(facts.CommonDir);
        facts.CommonDir.Should().Be(mainFacts.GitDir, "every worktree of a repository names the same common dir — the main checkout's own git dir");
    }

    [Fact]
    public async Task ACheckoutWithASeparateGitDir_HasAGitFile_ButIsNotLinked()
    {
        var at = Directory.CreateDirectory(_dir.At("separate")).FullName;
        await GitAsync(at, "init", "-b", "main", "--separate-git-dir", _dir.At("separate-gitdir"));

        var facts = await AskAsync(at);

        File.Exists(Path.Combine(at, ".git")).Should().BeTrue();
        facts.IsTop.Should().BeTrue();
        facts.IsLinked.Should().BeFalse("a .git FILE alone says nothing about being a worktree");
        facts.IsMain.Should().BeTrue();
    }

    /// <summary>The scenarios of <c>projectIdentity.ts</c>: a submodule's top, and — the trap — a submodule inside a linked worktree.</summary>
    [Fact]
    public async Task ASubmoduleCheckout_AndASubmoduleInsideALinkedWorktree_AreNeverLinked()
    {
        var lib = await RepositoryAsync(_dir.At("lib"));
        var main = await RepositoryAsync(_dir.At("main"));
        await GitAsync(main, "-c", "protocol.file.allow=always", "submodule", "add", lib, "vendor-lib");
        await GitAsync(main, "commit", "-m", "add the submodule");
        var worktree = _dir.At("wt");
        await GitAsync(main, "worktree", "add", "-b", "wt", worktree);
        await GitAsync(worktree, "-c", "protocol.file.allow=always", "submodule", "update", "--init");

        var inMain = await AskAsync(Path.Combine(main, "vendor-lib"));
        var inWorktree = await AskAsync(Path.Combine(worktree, "vendor-lib"));
        var worktreeItself = await AskAsync(worktree);

        File.Exists(Path.Combine(worktree, "vendor-lib", ".git")).Should().BeTrue("a submodule's top holds a .git file");
        inMain.IsTop.Should().BeTrue();
        inMain.IsLinked.Should().BeFalse("a submodule is not a linked worktree");
        inWorktree.IsTop.Should().BeTrue();
        inWorktree.IsLinked.Should().BeFalse("the submodule's gitdir lies under .git/worktrees/<name>/modules/ — it merely CONTAINS /worktrees/");
        inWorktree.GitDir.Should().Contain("worktrees").And.Be(inWorktree.CommonDir);
        worktreeItself.IsLinked.Should().BeTrue("the worktree around it is");
    }

    [Fact]
    public async Task AFolderThatIsNoRepository_AndAFolderInsideADotGitDirectory_AreNone()
    {
        var plain = Directory.CreateDirectory(_dir.At("plain")).FullName;
        var main = await RepositoryAsync(_dir.At("main"));

        (await AskAsync(plain)).Should().Be(RepositoryFacts.None);
        (await AskAsync(Path.Combine(main, ".git"))).Should().Be(RepositoryFacts.None, "inside a .git directory is not inside a work tree");
    }

    [Fact]
    public void GitsFourLines_ReadAsFacts_AnEmptyCdupIsTheTop_AnythingElseIsNone()
    {
        var top = RepositoryFacts.Parse(0, "true\n\nD:/r/app/.git\nD:/r/app/.git\n");
        var deeper = RepositoryFacts.Parse(0, "true\n../\nD:/r/app/.git\nD:/r/app/.git\n");
        var crlf = RepositoryFacts.Parse(0, "true\r\n\r\nD:/r/app/.git\r\nD:/r/app/.git\r\n");

        top.IsTop.Should().BeTrue();
        deeper.IsTop.Should().BeFalse();
        crlf.Should().Be(top, "Windows line ends read as the same answer");
        RepositoryFacts.Parse(0, "false\n/r/app/.git\n/r/app/.git\n").Should().Be(RepositoryFacts.None, "inside a git dir git prints three lines and not true");
        RepositoryFacts.Parse(128, string.Empty).Should().Be(RepositoryFacts.None);
        var noIdentity = RepositoryFacts.Parse(0, "true\n\n/r/app/.git\n");
        noIdentity.InsideWorkTree.Should().BeTrue("git did call it a work tree");
        noIdentity.CommonDir.Should().BeEmpty("but a missing common dir is not an identity");
        noIdentity.IsLinked.Should().BeFalse();
    }

    // ---------- the selection (pure) ----------

    private static ConfirmedRepository Repo(string name, string gitDir, string commonDir, bool top = true) =>
        new(new FoundRepository($"/root/{name}", [name]), new RepositoryFacts(InsideWorkTree: true, top, gitDir, commonDir));

    [Fact]
    public void ALinkedWorktreeWithItsMainInTheRow_IsLeftOut_OneWithoutItIsKept()
    {
        var main = Repo("app", "/m/app/.git", "/m/app/.git");
        var inside = Repo("app-wt", "/m/app/.git/worktrees/wt", "/m/app/.git");
        var orphan = Repo("other-wt", "/m/other/.git/worktrees/wt", "/m/other/.git");

        var selection = LinkedWorktrees.Select([main, inside, orphan], new HashSet<string> { "/m/app/.git" });

        selection.Kept.Select(one => one.Repository.Segments[0]).Should().Equal("app", "other-wt");
        selection.Left.Select(one => one.Repository.Segments[0]).Should().Equal("app-wt");
    }

    [Fact]
    public void AnUnknownIdentity_IsNeverLinked_SoItIsKept()
    {
        var unknown = Repo("odd", string.Empty, string.Empty);
        var half = Repo("half", "/m/x/.git/worktrees/h", string.Empty);

        var selection = LinkedWorktrees.Select([unknown, half], new HashSet<string> { string.Empty, "/m/x/.git" });

        selection.Kept.Should().HaveCount(2);
        selection.Left.Should().BeEmpty();
    }

    [Fact]
    public void ASubmodulesFacts_AreNeverLinked_WhateverTheGitDirContains()
    {
        // projectIdentity.ts: a submodule inside a worktree has gitdir .../worktrees/wt-rp/modules/lib — and a common dir equal to it.
        var submodule = Repo("lib", "/m/app/.git/worktrees/wt-rp/modules/lib", "/m/app/.git/worktrees/wt-rp/modules/lib");

        submodule.Facts.IsLinked.Should().BeFalse();
        LinkedWorktrees.Select([submodule], new HashSet<string> { "/m/app/.git" }).Kept.Should().ContainSingle();
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(1, "skipped 1 linked worktree whose main checkout is copied — its branch and uncommitted files were not read; grant a worktree directly to include it")]
    [InlineData(392, "skipped 392 linked worktrees whose main checkouts are copied — their branches and uncommitted files were not read; grant a worktree directly to include it")]
    public void TheSummarySentence_IsInTheSingularForOne_AndEmptyForNone(int skipped, string expected) =>
        LinkedWorktrees.Sentence(skipped).Should().Be(expected);

    // ---------- the identity key: widened, not copied ----------

    [Fact]
    public void TheKeyOnASensitivePath_KeepsCaseAndBackslashes_TheDefaultIsTheOldKeyUnchanged()
    {
        RepositoryIdentity.Normalised("D:\\Repos\\App\\.git\\").Should().Be("d:/repos/app/.git", "the stored keys keep their form: slashes one way, case folded");
        RepositoryIdentity.Normalised("/Repos/App/.git/", StringComparison.Ordinal).Should().Be("/Repos/App/.git", "a case-sensitive path keeps its case, and only the trailing slash goes");
        RepositoryIdentity.Normalised("/Repos/a\\b/.git", StringComparison.Ordinal).Should().Be("/Repos/a\\b/.git", "a backslash is a file-name character there, never a separator");
        RepositoryIdentity.Normalised("/Repos/App/.git", StringComparison.Ordinal).Should().NotBe(RepositoryIdentity.Normalised("/repos/app/.git", StringComparison.Ordinal));
        RepositoryIdentity.KeyOf(string.Empty).Should().BeEmpty("git did not say");
    }
}
