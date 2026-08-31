using Xunit;
using CoaiMcp.Core.Context;
using FluentAssertions;

namespace CoaiMcp.Tests;

public sealed class DiffShaperTests
{
    [Fact]
    public void UnderCapDiff_HasNoElisionNote()
    {
        var shaped = DiffShaper.Shape([new FileDiff("src/A.cs", "diff --git a\n+one\n")], maxBytes: 1024);

        shaped.WasElided.Should().BeFalse();
        shaped.Text.Should().Contain("+one").And.NotContain("NOT shown");
    }

    [Fact]
    public void OverCapDiff_NamesEveryElidedFileWithItsSize()
    {
        var big = new string('x', 300);
        var shaped = DiffShaper.Shape(
            [
                new FileDiff("src/Small.cs", "tiny\n"),
                new FileDiff("src/Big.cs", big),
                new FileDiff("src/AlsoBig.cs", big),
            ],
            maxBytes: 100);

        shaped.Text.Should().Contain("tiny", "the files inside the budget still ride whole");
        shaped.Elided.Should().HaveCount(2);
        shaped.Text.Should().Contain("src/Big.cs (300 bytes").And.Contain("src/AlsoBig.cs (300 bytes");
        shaped.Text.Should().Contain("your view is partial");
    }

    [Fact]
    public void Binary_IsNamedWithSize_NotInlined()
    {
        var shaped = DiffShaper.Shape([new FileDiff("img/logo.png", string.Empty, IsBinary: true, BinaryBytes: 4242)]);

        shaped.Text.Should().Contain("img/logo.png (4242 bytes)").And.Contain("content not shown");
    }

    [Fact]
    public void ElisionIsWholeFile_NeverHalfAHunk()
    {
        var big = new string('y', 300);
        var shaped = DiffShaper.Shape([new FileDiff("src/Big.cs", big)], maxBytes: 100);

        shaped.Text.Should().NotContain("yyy", "half a diff hunk is worse than none");
        shaped.Elided.Should().ContainSingle().Which.Path.Should().Be("src/Big.cs");
    }
}
