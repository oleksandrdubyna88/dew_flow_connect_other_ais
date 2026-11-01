using Xunit;
using CoaiMcp.Runners.Processes;
using FluentAssertions;

namespace CoaiMcp.Tests;

/// <summary>
/// Commits whose tracked path is longer than Windows' 259-unit limit — built with git's plumbing, so the
/// source checkout never has to hold the long path itself.
/// </summary>
/// <remarks>
/// <para>The length is computed from the folder the tree will live under, so the ABSOLUTE path in the tree
/// is at least <see cref="AbsoluteFloor"/> units on any runner whatever its temp folder is called, and every
/// component stays far under NTFS's own 255-unit limit — the fixture crosses ONE limit, the total.</para>
/// <para>The index of <c>repo</c> is touched, its working tree never: a test then reads the commit by
/// sha.</para>
/// </remarks>
internal static class DeepCommit
{
    /// <summary>The smallest absolute length of the deep file in a tree: well past 259, so no rounding hides it.</summary>
    public const int AbsoluteFloor = 300;

    /// <summary>Why a deep-path test is Windows-only: the limit it crosses is Windows'.</summary>
    public const string WindowsOnly =
        "MAX_PATH and core.longpaths are Windows' — Linux and macOS have no 259-unit limit to cross";

    private const int ComponentLength = 30;

    private static readonly string[] Author = ["-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false"];

    /// <summary>
    /// A forward-slash relative path that puts the file at no fewer than <see cref="AbsoluteFloor"/> units
    /// when it lies under <paramref name="parent"/>, whatever the tree's own folder is called.
    /// </summary>
    public static string Relative(string parent)
    {
        var missing = AbsoluteFloor - parent.Length - 1;
        var folders = Enumerable.Range(0, (missing / (ComponentLength + 1)) + 1)
            .Select(i => $"deep-dir-{i:D3}".PadRight(ComponentLength, 'x'));

        return string.Join('/', [.. folders, "deep-file.txt"]);
    }

    /// <summary>
    /// A commit on top of <c>HEAD</c> that also tracks <paramref name="relative"/>; returns its sha.
    /// </summary>
    public static async Task<string> CommitAsync(IProcessLauncher launcher, string repo, string relative)
    {
        var blob = await RunAsync(launcher, repo, "deep", "hash-object", "-w", "--stdin");
        await RunAsync(launcher, repo, string.Empty, "update-index", "--add", "--cacheinfo", $"100644,{blob},{relative}");
        var tree = await RunAsync(launcher, repo, string.Empty, "write-tree");

        return await RunAsync(launcher, repo, string.Empty, [.. Author, "commit-tree", tree, "-p", "HEAD", "-m", "deep"]);
    }

    private static async Task<string> RunAsync(
        IProcessLauncher launcher, string repo, string stdin, params string[] args)
    {
        var result = await launcher.RunAsync(
            new ProcessRequest("git", args, repo) { StdIn = stdin }, TestContext.Current.CancellationToken);
        result.ExitCode.Should().Be(0, $"git {string.Join(' ', args)} builds the fixture: {result.StdErr}");

        return result.StdOut.Trim();
    }
}
