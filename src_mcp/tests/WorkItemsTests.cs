using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The <c>workItem</c> argument is a relative path inside the repository, in one spelling, or nothing (coai v2 E3.S8, task t1, dod d1 and d2).
/// </summary>
public sealed class WorkItemsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NoItem_IsTheOldCall_NoItemAndNoRefusal(string? none)
    {
        WorkItems.Check(none).Should().Be((string.Empty, string.Empty));
    }

    [Theory]
    [InlineData("v2/epoch-1/e3-x/stories/s8-y/tasks/t1-z", "v2/epoch-1/e3-x/stories/s8-y/tasks/t1-z")]
    [InlineData("v2\\epoch-1\\e3-x", "v2/epoch-1/e3-x")]
    [InlineData("  v2/epoch-1/e3-x/  ", "v2/epoch-1/e3-x")]
    [InlineData("./v2//epoch-1/./e3-x", "v2/epoch-1/e3-x")]
    [InlineData("epoch-1", "epoch-1")]
    public void AnItem_IsKeptInOneSpelling(string given, string stored)
    {
        WorkItems.Check(given).Should().Be((stored, string.Empty));
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("v2/../../outside")]
    [InlineData("v2/epoch-1/..")]
    [InlineData("..\\outside")]
    [InlineData("/etc/passwd")]
    [InlineData("\\\\server\\share\\x")]
    [InlineData("C:\\work\\repo")]
    [InlineData("C:/work/repo")]
    [InlineData(".")]
    [InlineData("./")]
    [InlineData("//")]
    [InlineData("v2/epoch-1\u0000/x")]
    [InlineData("v2/epoch-1\n/x")]
    public void APathThatCouldLeaveTheRepository_OrNamesNoItem_IsRefused_WithTheCure(string given)
    {
        var (item, refusal) = WorkItems.Check(given);

        item.Should().BeEmpty();
        refusal.Should().StartWith("workItem ").And.Contain("leave `workItem` out", "the sentence says what to do");
    }

    [Fact]
    public void ATooLongPath_IsRefused_AndOneOfTheLimitIsNot()
    {
        WorkItems.Check(new string('a', WorkItems.MaxLength)).Refusal.Should().BeEmpty();
        WorkItems.Check(new string('a', WorkItems.MaxLength + 1)).Refusal.Should().Contain("at most");
    }

    [Fact]
    public void ADotDotInsideASegmentName_IsAName_NotATraversal()
    {
        WorkItems.Check("v2/a..b/c").Should().Be(("v2/a..b/c", string.Empty));
    }
}
