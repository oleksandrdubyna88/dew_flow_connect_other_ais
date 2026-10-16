using Xunit;
using CoaiMcp.Runners.Collecting;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Worktrees;
using CoaiMcp.Core.Collecting;
using FluentAssertions;

namespace CoaiMcp.Tests;

/// <summary>
/// A person's review tree whose commit tracks a path longer than Windows' 259 units: made, reused,
/// inspected and given back — real git, plumbing-built commits.
/// </summary>
/// <remarks>
/// <para>Without <c>core.longpaths</c> git fails each of these differently — the checkout fails, a
/// clean tree reads dirty (so the tree can never be removed from the panel), a remove drops the
/// registration and leaves the files, a populated submodule reads dirty. One flag on every call fixes
/// all of them, and each test below is one way the flag could be missed on ONE call.</para>
/// <para>Each test first proves, with <c>-c core.longpaths=false</c> on that one command, that its commit
/// cannot be checked out without the flag, so a runner whose own config sets it cannot make the test
/// pass for the wrong reason.</para>
/// </remarks>
public sealed class AReviewTreeWithALongPathTests : IAsyncLifetime
{
    private readonly ProcessLauncher _launcher = new();
    private readonly List<string> _temps = [];
    private string _repo = string.Empty;
    private string _root = string.Empty;

    public async ValueTask InitializeAsync()
    {
        _repo = Temp("testsToDelete-repo-");
        _root = Temp("testsToDelete-review-root-");
        await Git(_repo, "init", "-b", "main");
        await File.WriteAllTextAsync(Path.Combine(_repo, "a.txt"), "v1", TestContext.Current.CancellationToken);
        await Git(_repo, "add", ".");
        await Git(_repo, "commit", "-m", "v1");
    }

    public ValueTask DisposeAsync()
    {
        foreach (var temp in _temps)
        {
            TryDelete(temp);
        }

        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task AReviewTreeWithALongPathIsMadeCleanAndGivenBack()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), DeepCommit.WindowsOnly);
        var relative = DeepCommit.Relative(_root);
        var sha = await DeepCommit.CommitAsync(_launcher, _repo, relative);
        await TheFixtureNeedsTheFlag(sha);
        var recording = new ExitRecordingLauncher(_launcher);

        var made = await Trees(recording).PrepareAsync(7, new TreePlace(_repo, sha), TestContext.Current.CancellationToken);

        made.Reason.Should().BeEmpty();
        File.Exists(Path.Combine(made.Path, relative.Replace('/', '\\'))).Should().BeTrue();
        await TheTreeIsGivenBackAsync(recording, made);
    }

    /// <summary>
    /// The submodule variant: the deep file is INSIDE the mount, so the populate (a nested clone and
    /// checkout) and the parent's status over a populated mount both see it.
    /// </summary>
    [Fact]
    public async Task AReviewTreeWithALongPathInASubmoduleIsMadeCleanAndGivenBack()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), DeepCommit.WindowsOnly);
        var relative = DeepCommit.Relative(_root);
        var sha = await MountADeepSubmoduleAsync(relative);
        var recording = new ExitRecordingLauncher(_launcher);

        var made = await Trees(recording).PrepareAsync(7, new TreePlace(_repo, sha), TestContext.Current.CancellationToken);

        made.Reason.Should().BeEmpty();
        made.EmptyMounts.Should().BeEmpty("the mount was populated, long path and all");
        File.Exists(Path.Combine(made.Path, "mods", "sub", relative.Replace('/', '\\'))).Should().BeTrue();
        await TheTreeIsGivenBackAsync(recording, made);
    }

    /// <summary>Reused on a second press, clean in the panel's eyes, removed as <c>removed</c> — never <c>dirty</c>.</summary>
    private async Task TheTreeIsGivenBackAsync(ExitRecordingLauncher recording, ReviewTree made)
    {
        recording.Calls.Should().OnlyContain(
            call => call.Request.Arguments.Take(2).SequenceEqual(new[] { "-c", "core.longpaths=true" }),
            "every git call that makes, inspects or removes the tree asks for long paths");

        var again = await Trees(recording).PrepareAsync(
            7, new TreePlace(_repo, made.Sha), TestContext.Current.CancellationToken);
        again.Reused.Should().BeTrue("a clean tree is handed back, not rebuilt");

        var said = await new ReviewTreeKeeper(new ReviewTreeRoot(recording, _root))
            .RemoveAsync(Path.GetFileName(made.Path), ct: TestContext.Current.CancellationToken);

        said.Reason.Should().Be(ReviewTreeRemovalReason.Removed, $"a long path is not a change nobody made: {said.InTheWay}");
        Directory.Exists(made.Path).Should().BeFalse();
    }

    /// <summary>
    /// A parent whose submodule tracks <paramref name="relative"/>; returns the parent's commit. Built
    /// with the flag on every command, because the PARENT's own working copy of the mount holds the
    /// long path too.
    /// </summary>
    private async Task<string> MountADeepSubmoduleAsync(string relative)
    {
        var sub = Temp("testsToDelete-sub-");
        await Git(sub, "init", "-b", "main");
        await File.WriteAllTextAsync(Path.Combine(sub, "s.txt"), "sub", TestContext.Current.CancellationToken);
        await Git(sub, "add", ".");
        await Git(sub, "commit", "-m", "sub");
        var deep = await DeepCommit.CommitAsync(_launcher, sub, relative);
        await Git(sub, "update-ref", "HEAD", deep);

        await Git(
            _repo,
            "-c", "core.longpaths=true",
            "-c", "protocol.file.allow=always",
            "submodule", "add", "-q", sub.Replace('\\', '/'), "mods/sub");
        await Git(_repo, "-c", "core.longpaths=true", "commit", "-m", "mount the deep submodule");

        return (await Run(_repo, "rev-parse", "HEAD")).StdOut.Trim();
    }

    private async Task TheFixtureNeedsTheFlag(string sha)
    {
        var control = Path.Combine(_root, "testsToDelete-wt-control-longpaths");
        var refused = await Run(_repo, "-c", "core.longpaths=false", "worktree", "add", "--detach", control, sha);

        refused.ExitCode.Should().NotBe(0, "the control is a commit this git cannot check out without the flag");
        refused.StdErr.Should().Contain("Filename too long", "for the reason under test, not for another");
    }

    /// <summary>The history reader stays on the plain launcher: it is not one of the calls under test.</summary>
    private ReviewWorktrees Trees(IProcessLauncher recording) => new(recording, new GitHistory(_launcher), _root);

    private string Temp(string prefix)
    {
        var path = Directory.CreateTempSubdirectory(prefix).FullName;
        _temps.Add(path);

        return path;
    }

    private Task<ProcessResult> Run(string cwd, params string[] args) =>
        _launcher.RunAsync(
            new ProcessRequest(
                "git",
                ["-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", .. args],
                cwd),
            TestContext.Current.CancellationToken);

    private async Task Git(string cwd, params string[] args)
    {
        var result = await Run(cwd, args);
        result.ExitCode.Should().Be(0, $"git {string.Join(' ', args)} must succeed: {result.StdErr}");
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
