using Xunit;
using CoaiMcp.Core.Collecting;
using CoaiMcp.Runners.Collecting;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Worktrees;
using FluentAssertions;

namespace CoaiMcp.Tests;

/// <summary>
/// A review-tree root reached through a LINK — the shape every macOS temp directory has, and any data
/// directory a person moved and linked back.
/// </summary>
/// <remarks>
/// <para>Git records a worktree by its REAL path: <c>/var/folders/…</c> is <c>/private/var/folders/…</c>
/// to <c>git worktree list</c>. A comparison that only normalises spelling therefore never finds our own
/// tree, so a live tree listed as <c>unregistered</c> and a deleted one could not be made again
/// (<c>git_failed</c>). The release build of <c>mcp-v0.31.0</c> found it — nine tests on
/// <c>osx-arm64</c>, the one runner whose temp directory is behind a link; pull-request CI runs on
/// Linux, where it is not.</para>
/// <para>This class makes the link itself, so it fails on every platform rather than only on the one
/// that happened to show it: a junction on Windows (no privilege needed, where a symbolic link needs
/// Developer Mode — and git resolves a junction exactly as macOS resolves <c>/var</c>), a symbolic
/// link everywhere else.</para>
/// </remarks>
public sealed class AReviewRootBehindALinkTests : IAsyncLifetime
{
    private readonly ProcessLauncher _launcher = new();
    private readonly List<string> _temps = [];
    private string _repo = string.Empty;
    private string _real = string.Empty;
    private string _linked = string.Empty;
    private string _sha = string.Empty;

    public async ValueTask InitializeAsync()
    {
        _repo = Temp("coai-repo-");
        _real = Temp("coai-review-root-");
        _linked = Path.Combine(Temp("coai-review-link-"), "root");
        await DirectoryLink.MakeAsync(_launcher, _linked, _real);
        await Git(_repo, "init", "-b", "main");
        await File.WriteAllTextAsync(Path.Combine(_repo, "a.txt"), "v1");
        await Git(_repo, "add", ".");
        await Git(_repo, "commit", "-m", "v1");
        _sha = (await Run(_repo, "rev-parse", "HEAD")).StdOut.Trim();
    }

    /// <summary>Unlock everything, drop the link ITSELF, then delete — the link first, so a
    /// recursive delete can never walk through it into the tree it points at twice.</summary>
    public ValueTask DisposeAsync()
    {
        Unlock(_real);
        DirectoryLink.Remove(_linked);

        foreach (var temp in _temps)
        {
            TryDelete(temp);
        }

        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task ATreeUnderALinkedRoot_IsListedReady()
    {
        var made = await Trees().PrepareAsync(7, new TreePlace(_repo, _sha), TestContext.Current.CancellationToken);
        made.Reason.Should().BeEmpty();

        var listed = await Keeper().ListAsync(TestContext.Current.CancellationToken);

        listed.Trees.Single().State.Should().Be(ReviewTreeState.Ready,
            "git lists this tree by its real path, and it is still the tree we made");
    }

    [Fact]
    public async Task ATreeUnderALinkedRoot_WhoseDirectoryWasDeleted_IsMadeAgain()
    {
        var trees = Trees();
        var first = await trees.PrepareAsync(7, new TreePlace(_repo, _sha), TestContext.Current.CancellationToken);
        first.Reason.Should().BeEmpty();
        Unlock(_real);
        Directory.Delete(first.Path, recursive: true);

        var again = await trees.PrepareAsync(7, new TreePlace(_repo, _sha), TestContext.Current.CancellationToken);

        again.Reason.Should().BeEmpty("the stale registration is OUR path, spelled through the link");
        Directory.Exists(again.Path).Should().BeTrue();
    }

    [Fact]
    public void APathThroughALink_IsTheSamePlaceAsItsTarget()
    {
        WorktreePaths.Same(Path.Combine(_linked, "x"), Path.Combine(_real, "x")).Should().BeTrue();
        WorktreePaths.Real(_linked).Replace('\\', '/').TrimEnd('/').Should().BeEquivalentTo(
            WorktreePaths.Real(_real).Replace('\\', '/').TrimEnd('/'));
    }

    /// <summary>A deleted tree's path still names its place: resolved as far as it exists.</summary>
    [Fact]
    public void AMissingTailBelowALink_ResolvesAsFarAsItExists()
    {
        var missing = Path.Combine(_linked, "coai-review-gone", "deeper");

        Directory.Exists(missing).Should().BeFalse();
        WorktreePaths.Same(missing, Path.Combine(_real, "coai-review-gone", "deeper")).Should().BeTrue();
    }

    [Fact]
    public void TwoDifferentPlaces_AreNotTheSame()
    {
        WorktreePaths.Same(Path.Combine(_linked, "a"), Path.Combine(_real, "b")).Should().BeFalse();
        WorktreePaths.Same(_repo, _real).Should().BeFalse();
    }

    private ReviewTreeKeeper Keeper() => new(new ReviewTreeRoot(_launcher, _linked));

    private ReviewWorktrees Trees() => new(_launcher, new GitHistory(_launcher), _linked);

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
                // Best effort: an already-unlocked tree is the ordinary case.
            }
        }
    }

    private string Temp(string prefix)
    {
        var path = Directory.CreateTempSubdirectory(prefix).FullName;
        _temps.Add(path);

        return path;
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
}
