using CoaiMcp.Runners.Context;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// <see cref="RawDiff.GitlinkCount"/> (E2.S4): the submodule pointers a <c>git diff --raw -z</c> changed — read from the
/// modes, never guessed from a path, and null for text that is not a raw diff.
/// </summary>
public sealed class TheGitlinksOfARawDiffAreCountedTests
{
    private const string Sha = "1111111111111111111111111111111111111111";
    private const string Other = "2222222222222222222222222222222222222222";

    private static string Entry(string oldMode, string newMode, string status, params string[] paths) =>
        $":{oldMode} {newMode} {Sha} {Other} {status}\0{string.Join('\0', paths)}\0";

    [Fact]
    public void AnEmptyDiff_IsAMeasuredZero() => RawDiff.GitlinkCount(string.Empty).Should().Be(0);

    [Fact]
    public void AMovedPointer_IsOne_AndAPlainFileIsNot()
    {
        var raw = Entry("100644", "100644", "M", "app.cs") + Entry("160000", "160000", "M", "libs/sub");

        RawDiff.GitlinkCount(raw).Should().Be(1);
    }

    [Fact]
    public void AnAddedAndARemovedPointer_AreCounted_OnEitherSideOfTheMode()
    {
        var raw = Entry("000000", "160000", "A", "libs/new") + Entry("160000", "000000", "D", "libs/old");

        RawDiff.GitlinkCount(raw).Should().Be(2, "a pointer that appears or disappears is a changed gitlink too");
    }

    [Fact]
    public void ARenameNamesTwoPaths_SoAPathThatLooksLikeAMetaLineIsNotOne()
    {
        var raw = Entry("100644", "100644", "R086", "a.cs", ":160000 160000 not a record") + Entry("160000", "160000", "M", "libs/sub");

        RawDiff.GitlinkCount(raw).Should().Be(1, "the second path of a rename is a path, whatever it starts with");
    }

    [Theory]
    [InlineData("not a raw diff at all")]
    [InlineData(":160000 160000 only-three-fields M\0libs/sub\0")]
    public void TextThatIsNotARawDiff_IsNoCount(string raw) => RawDiff.GitlinkCount(raw).Should().BeNull("a count from a stream that does not parse would be a guess");

    [Fact]
    public void AGitlinkRecordWhoseStreamEndsBeforeItsPath_IsMalformed_NotACount()
    {
        var cut = $":160000 160000 {Sha} {Other} M\0";

        RawDiff.GitlinkCount(cut).Should().BeNull("a record that lost its path is a cut stream — a count from it would be a guess");
    }

    [Fact]
    public void ARenameWithOnlyOnePath_IsMalformed()
    {
        var cut = $":160000 160000 {Sha} {Other} R100\0libs/old\0";

        RawDiff.GitlinkCount(cut).Should().BeNull("a rename or copy names two paths");
    }

    [Fact]
    public void AStatusThatIsNotOneOfGitsLetters_IsMalformed()
    {
        RawDiff.GitlinkCount(Entry("160000", "160000", "Z", "libs/sub")).Should().BeNull();
        RawDiff.GitlinkCount(Entry("160000", "160000", "Mx", "libs/sub")).Should().BeNull("a score after the letter is digits only");
    }
}
