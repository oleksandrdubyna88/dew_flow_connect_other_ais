using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Worktrees;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// <see cref="ReviewTreeRoot.EnsureGoneAsync"/> deletes recursively, so it asks whether the tree is under the root by the REAL paths.
/// </summary>
/// <remarks>
/// <c>Holds</c> compared <c>GetFullPath</c> strings, so a name lexically under the root whose parent is a link to an outside folder
/// passed, and <c>Directory.Delete(recursive)</c> ran there (2026-11-01, found by the review of the resolver change).
/// </remarks>
public sealed class AReviewTreeIsErasedOnlyAtItsRealPathTests : IDisposable
{
    private readonly TempDir _work = TempDir.For("rtroot-");
    private readonly string _root;
    private readonly string _outside;
    private readonly ReviewTreeRoot _at;

    public AReviewTreeIsErasedOnlyAtItsRealPathTests()
    {
        _root = Directory.CreateDirectory(_work.At("root")).FullName;
        _outside = Directory.CreateDirectory(_work.At("outside")).FullName;
        _at = new ReviewTreeRoot(new ProcessLauncher(), _root);
    }

    public void Dispose() => _work.Dispose();

    private static void Link(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Assert.Skip($"this machine cannot create a symbolic link: {e.Message}");
        }
    }

    private static string Canary(string folder)
    {
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "canary.txt");
        File.WriteAllText(file, "must survive");

        return file;
    }

    [Fact]
    public async Task ANameLexicallyUnderTheRoot_WhoseParentIsALinkOutside_IsNotDeleted()
    {
        Link(Path.Combine(_root, "sub"), _outside);
        var canary = Canary(Path.Combine(_outside, "coai-review-x"));

        var gone = await _at.EnsureGoneAsync(Path.Combine(_root, "sub", "coai-review-x"), TestContext.Current.CancellationToken);

        gone.Should().BeFalse();
        File.Exists(canary).Should().BeTrue("the tree is outside the root, whatever its spelling says");
    }

    [Fact]
    public async Task ATreeThatIsItselfALink_IsNotDeleted_NotEvenUnlinked()
    {
        var canary = Canary(Path.Combine(_outside, "coai-review-real"));
        var leaf = Path.Combine(_root, "coai-review-leaf");
        Link(leaf, Path.Combine(_outside, "coai-review-real"));

        var gone = await _at.EnsureGoneAsync(leaf, TestContext.Current.CancellationToken);

        gone.Should().BeFalse();
        File.Exists(canary).Should().BeTrue();
        new DirectoryInfo(leaf).LinkTarget.Should().NotBeNull("a link is not a tree; it is left as it was");
    }

    [Fact]
    public async Task ATreeReallyUnderTheRoot_IsStillDeleted()
    {
        var canary = Canary(Path.Combine(_root, "coai-review-mine"));

        var gone = await _at.EnsureGoneAsync(Path.Combine(_root, "coai-review-mine"), TestContext.Current.CancellationToken);

        gone.Should().BeTrue();
        File.Exists(canary).Should().BeFalse();
    }
}
