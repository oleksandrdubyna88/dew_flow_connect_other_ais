using CoaiMcp.Runners.Worktrees;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// <see cref="WorktreePaths.Real"/> answers what git's <c>realpath</c> answers: every directory inside a link's
/// target resolved, and a <c>..</c> in link text applied to the real directory, never to the string.
/// </summary>
public sealed class AWorktreePathIsComparedByItsRealPathTests : IDisposable
{
    private readonly TempDir _work = TempDir.For("wtpaths-");

    public void Dispose() => _work.Dispose();

    private void Link(string link, string text)
    {
        try
        {
            File.CreateSymbolicLink(link, text);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Assert.Skip($"this machine cannot create a symbolic link: {e.Message}");
        }
    }

    [Fact]
    public void ADotDotInsideALinkText_IsAppliedToTheRealDirectory()
    {
        var root = Directory.CreateDirectory(_work.At("root")).FullName;
        var outside = Directory.CreateDirectory(_work.At("outside", "deep")).FullName;
        File.WriteAllText(_work.At("outside", "x"), "the real x");
        File.WriteAllText(Path.Combine(root, "x"), "the decoy x");
        Link(Path.Combine(root, "l3"), outside);
        Link(Path.Combine(root, "dd"), "l3/../x");

        WorktreePaths.Real(Path.Combine(root, "dd")).Should().Be(
            Path.Combine(WorktreePaths.Real(_work.At("outside")), "x"),
            "l3 is a link to outside/deep, so `l3/..` is outside, not the folder that holds the link");
    }

    [Fact]
    public void TwoFoldersThatDifferOnlyByTheCaseOfALetter_AreNotTheSameWhereTheFileSystemKeepsCase()
    {
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
        {
            Assert.Skip("the file system of this operating system ignores case: Repo and repo are one folder here");
        }

        var upper = Directory.CreateDirectory(_work.At("Repo")).FullName;
        if (Directory.Exists(_work.At("repo")))
        {
            Assert.Skip("the temporary folder of this machine is on a mount that ignores case: Repo and repo cannot be two folders here");
        }

        var lower = Directory.CreateDirectory(_work.At("repo")).FullName;

        WorktreePaths.Same(upper, lower).Should().BeFalse("on Linux Repo and repo are two folders");
    }

    [Fact]
    public void OneFolderSpelledWithADifferentCase_IsTheSameWhereTheOperatingSystemIgnoresCase()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
        {
            Assert.Skip("the file system of this operating system keeps case: Repo and repo are two folders here");
        }

        var folder = Directory.CreateDirectory(_work.At("Repo")).FullName;
        var otherCase = _work.At("repo");
        if (!Directory.Exists(otherCase))
        {
            Assert.Skip("the temporary folder of this machine is on a case-sensitive volume: repo is not Repo here");
        }

        WorktreePaths.Same(folder, otherCase).Should().BeTrue("on Windows and macOS Repo and repo are one folder");
    }

    [Fact]
    public void OneFolderReachedByALinkAndByATrailingSlash_IsTheSame()
    {
        var folder = Directory.CreateDirectory(_work.At("folder")).FullName;
        var link = _work.At("alias");
        Link(link, folder);

        WorktreePaths.Same(folder, link).Should().BeTrue("the link resolves to the folder");
        WorktreePaths.Same(folder, folder + Path.DirectorySeparatorChar).Should().BeTrue("a trailing slash is the same folder");
    }

    [Fact]
    public void ALoopOfLinks_IsNotSilentlyItsOwnSpelling()
    {
        var one = _work.At("one");
        var two = _work.At("two");
        Link(one, two);
        Link(two, one);

        var compare = () => WorktreePaths.Same(one, one);
        var real = () => WorktreePaths.Real(one);

        compare().Should().BeFalse("a path that cannot be resolved is not a place this process could compare");
        real.Should().Throw<IOException>("the spelling of a loop is not its real path");
    }
}
