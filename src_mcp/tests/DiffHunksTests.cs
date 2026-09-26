using CoaiMcp.Core.Feature;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// One file's piece of a unified diff, read for the <c>*</c> marks and for the member hunks.
/// </summary>
/// <remarks>
/// The pieces are written in the shape git prints — including the two a naive reader gets wrong, a pure
/// deletion (<c>+c,0</c>) and an added line whose text begins <c>++</c>. Whether the shape IS git's is
/// settled by <see cref="FeatureOutlineBuilderTests"/>, which reads real <c>git diff</c> output from a
/// real repository.
/// </remarks>
public sealed class DiffHunksTests
{
    private const string U0 =
        "diff --git a/src/Shop.cs b/src/Shop.cs\n"
        + "index 1111111..2222222 100644\n"
        + "--- a/src/Shop.cs\n"
        + "+++ b/src/Shop.cs\n"
        + "@@ -8 +8 @@ public sealed class Shop\n"
        + "-        var total = n;\n"
        + "+        var total = n * 2;\n"
        + "@@ -20,2 +19,0 @@ public sealed class Shop\n"
        + "-    // one\n"
        + "-    // two\n"
        + "@@ -30,0 +29,3 @@ public sealed class Shop\n"
        + "+a\n"
        + "+b\n"
        + "+c\n";

    [Fact]
    public void EachHunk_BecomesItsHeadSideSpan_AndAPureDeletion_SpansTheLinesEitherSideOfTheGap() =>
        DiffHunks.ChangedSpans(U0).Should().Equal(new LineSpan(8, 8), new LineSpan(19, 20), new LineSpan(29, 31));

    private const string U3 =
        "diff --git a/src/Shop.cs b/src/Shop.cs\n"
        + "index 1111111..2222222 100644\n"
        + "--- a/src/Shop.cs\n"
        + "+++ b/src/Shop.cs\n"
        + "@@ -5,5 +5,5 @@ public sealed class Shop\n"
        + "     public void Buy(int n)\n"
        + "     {\n"
        + "-        var total = n;\n"
        + "+        var total = n * 2;\n"
        + "+++ this added line begins with two plus signs\n"
        + "     }\n"
        + "\\ No newline at end of file\n";

    [Fact]
    public void EveryHunkLine_IsPlacedAtItsHeadLine_ADeletionAtTheLineThatFollowsIt()
    {
        var placed = DiffHunks.Placed(U3);

        placed.Select(l => (l.Kind, l.NewLine)).Should().Equal((' ', 5), (' ', 6), ('-', 7), ('+', 7), ('+', 8), (' ', 9));
        placed.Should().OnlyContain(l => l.Hunk == 0);
    }

    [Fact]
    public void AnAddedLineThatLooksLikeAFileHeader_IsStillAnAddedLine() =>
        DiffHunks.Placed(U3).Should().Contain(l => l.Kind == '+' && l.Text == "+++ this added line begins with two plus signs");

    [Fact]
    public void ACarriageReturn_IsNotPartOfTheLine() =>
        DiffHunks.Placed("@@ -1 +1 @@\r\n-a\r\n+b\r\n").Select(l => l.Text).Should().Equal("-a", "+b");

    [Fact]
    public void NoPiece_NoSpans() => DiffHunks.ChangedSpans(string.Empty).Should().BeEmpty();
}
