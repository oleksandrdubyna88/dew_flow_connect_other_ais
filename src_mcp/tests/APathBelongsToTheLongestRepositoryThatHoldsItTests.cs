using CoaiMcp.Core.Authors;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Which repository a path is in: the LONGEST root of the universe that equals or contains it, or none (E4.S1,
/// <see cref="Repositories.RepositoryOf"/>). Q8 of the E1 measurements is why the separators, the drive case and the dot segments
/// are rules and not accidents.
/// </summary>
public sealed class APathBelongsToTheLongestRepositoryThatHoldsItTests
{
    private static string In(string path, params string[] universe) => Repositories.RepositoryOf(path, universe);

    [Fact]
    public void AFileUnderTheRoot_IsInIt() => In("/repo/src/a.cs", "/repo").Should().Be("/repo");

    [Fact]
    public void TheRootItself_IsInIt() => In("/repo", "/repo").Should().Be("/repo");

    /// <summary>The separator is part of the test, so <c>/repo-two</c> is not inside <c>/repo</c>.</summary>
    [Fact]
    public void ASiblingWithTheRootAsPrefix_IsNot() => In("/repo-two/a.cs", "/repo").Should().BeEmpty();

    [Fact]
    public void BackslashesAndATrailingSlash_DoNotMatter() => In(@"D:\x\a.cs", "D:/x/").Should().Be("D:/x/");

    [Fact]
    public void CaseDoesNotMatter() => In("D:/X/A.cs", "d:/x").Should().Be("d:/x");

    /// <summary>A worktree nested inside another checkout is the inner root.</summary>
    [Fact]
    public void ANestedWorktree_BelongsToTheInnerRoot() =>
        In("D:/rsd/x/.claude/worktrees/y/a.cs", "D:/rsd/x", "D:/rsd/x/.claude/worktrees/y").Should().Be("D:/rsd/x/.claude/worktrees/y");

    [Theory]
    [InlineData("src/a.cs")]
    [InlineData("a.cs")]
    [InlineData("")]
    public void ARelativeOrEmptyPath_IsInNone(string path) => In(path, "D:/x", "src").Should().BeEmpty();

    /// <summary>No translation is guessed: E5 passes the universe in the spelling the transcript uses.</summary>
    [Fact]
    public void AWslSpellingIsNotTranslated() => In("/mnt/d/x/a.cs", "D:/x").Should().BeEmpty();

    [Fact]
    public void MixedSeparatorsInsideOnePath_DoNotMatter() => In(@"D:\x/y\z.cs", "D:/x").Should().Be("D:/x");

    [Fact]
    public void ALowercaseDriveAgainstAnUppercaseRoot_IsInIt()
    {
        In(@"d:\x\a.cs", "D:/x").Should().Be("D:/x");
        In(@"D:\x\a.cs", "d:/x").Should().Be("d:/x");
    }

    [Fact]
    public void ANonAsciiFolderMatchesAcrossCase() => In("d:/проект/a.cs", "D:/Проект").Should().Be("D:/Проект");

    /// <summary>
    /// Nothing is resolved, because resolving needs the disk or a working directory: a path that has a dot segment is in no
    /// repository, so it costs evidence and never produces a wrong attribution.
    /// </summary>
    [Fact]
    public void ADotSegment_IsInNone()
    {
        In("D:/x/../y/a.cs", "D:/x").Should().BeEmpty();
        In("D:/x/./a.cs", "D:/x").Should().BeEmpty();
        In("D:/x/a.cs", "D:/x/..").Should().BeEmpty();
    }

    /// <summary>E3's keys are lower-cased and the gate's <c>repo_path</c> is as typed: one root, whichever order they arrive in.</summary>
    [Fact]
    public void TwoSpellingsOfOneRoot_AreOneRoot()
    {
        var forward = In("D:/x/a.cs", "d:/x", "D:/x");
        var backward = In("D:/x/a.cs", "D:/x", "d:/x");

        forward.Should().Be(backward);
        forward.Should().Be("D:/x", "the first in ordinal order, so a tie never depends on the order the universe arrived in");
    }

    [Fact]
    public void ContainsKeepsTheRuleItWasMovedFrom()
    {
        Repositories.Contains("/repo", "/repo/a.cs").Should().BeTrue();
        Repositories.Contains("/repo", "/repo-two/a.cs").Should().BeFalse();
        Repositories.Contains(string.Empty, "/repo").Should().BeFalse();
        Repositories.Contains("/repo", string.Empty).Should().BeFalse();
    }
}
