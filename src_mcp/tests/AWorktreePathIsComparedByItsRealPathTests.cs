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
