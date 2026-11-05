using CoaiMcp.Runners.Files;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A read root that cannot be resolved has NO canonical path — it is empty, never its spelling — and nothing is served under it.
/// </summary>
/// <remarks>
/// <para>The root is fixed when the reader is made (<see cref="HandleRead.Trust"/>) and the opened handle's own name is compared with it. When
/// the resolver failed (a loop, a chain over the budget, a path over 32 KiB) the canonical path fell back to <c>GetFullPath(root)</c>: a
/// spelling that nothing had resolved, standing where the comparison expects a real path. Empty is the honest answer, and
/// <see cref="HandleRead.Read(ReadRoot, string, long, StringComparison)"/> says so in its refusal instead of failing the comparison by accident.</para>
/// </remarks>
public sealed class AHandleReadRootThatCannotBeResolvedIsRefusedTests : IDisposable
{
    private readonly TempDir _work = TempDir.For("testsToDelete-handleroot-");
    private readonly string _root;

    public AHandleReadRootThatCannotBeResolvedIsRefusedTests()
    {
        _root = Directory.CreateDirectory(_work.At("root")).FullName;
        File.WriteAllText(Path.Combine(_root, "a.txt"), "inside");
    }

    public void Dispose() => _work.Dispose();

    private string LoopRoot()
    {
        var one = _work.At("loop-a");
        var two = _work.At("loop-b");
        try
        {
            File.CreateSymbolicLink(one, two);
            File.CreateSymbolicLink(two, one);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Assert.Skip($"this machine cannot create a symbolic link: {e.Message}");
        }

        return one;
    }

    [Fact]
    public void ARootThatIsALinkLoop_HasNoCanonicalPath_NotItsSpelling()
    {
        var loop = LoopRoot();

        HandleRead.CanonicalDirectory(loop).Should().BeEmpty("a path nothing could resolve is not a real path, and its spelling must not stand in for one");
        HandleRead.Trust(loop).Canonical.Should().BeEmpty();
    }

    [Fact]
    public void ReadingUnderARootThatCannotBeResolved_IsRefused_AndSaysTheRootIsTheReason()
    {
        var unresolved = new ReadRoot(_root, string.Empty);

        var read = HandleRead.Read(unresolved, "a.txt", HandleRead.DefaultCap, StringComparison.Ordinal);

        var refused = read.Should().BeOfType<FileRead.Refused>().Subject;
        refused.Reason.Should().Contain("root", "the root could not be resolved, which is the reason — not a comparison that happened to fail");
    }

    [Fact]
    public void ReadingUnderALoopRoot_IsRefused()
    {
        var read = HandleRead.Read(HandleRead.Trust(LoopRoot()), "a.txt", HandleRead.DefaultCap, StringComparison.Ordinal);

        read.Should().BeOfType<FileRead.Refused>();
    }

    [Fact]
    public void AnOrdinaryRoot_StillReads()
    {
        var read = HandleRead.Read(HandleRead.Trust(_root), "a.txt", HandleRead.DefaultCap, StringComparison.Ordinal);

        read.Should().BeOfType<FileRead.Served>().Which.Bytes.Should().Equal("inside"u8.ToArray());
    }
}
