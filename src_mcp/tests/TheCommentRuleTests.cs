using CoaiMcp.Core.Collecting;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The comment rule itself, on the side that will be the FIRST to apply it.
/// </summary>
/// <remarks>
/// <para><b>Why here as well as in the server's suite.</b> <see cref="CommentRule"/> lives in the
/// core both halves compile against, and until now only <c>src_bugs</c> exercised it — through the
/// ingest path, which reaches the rule after the alphabet and only ever with whole pairs. That is a
/// test of the SERVER's use of it. `coai-mcp`'s `--pairs-decide` (story 4.2a) is the other caller and
/// the one a person meets first: it refuses a comment locally, before anything is sent, so the box
/// can say what is wrong while the words are still on screen. A rule tested only through the server
/// is a rule whose boundaries nobody checked on the side that reports them. (Code round, CodeRabbit.)
/// </para>
/// <para><b>The exact boundary, which the server's suite does not have.</b> Those tests refuse 1 004
/// characters — comfortably over. The interesting values are <see cref="CommentRule.MostChars"/> and
/// one more, because an off-by-one here is a person told their comment is too long when it is not.
/// </para>
/// <para>Code points are built from their NUMBERS and never written as characters; the rule's own
/// docblock says why, and <c>NoSourceFileCarriesAnInvisibleCharacterTests</c> enforces it.</para>
/// </remarks>
public sealed class TheCommentRuleTests
{
    [Fact]
    public void NothingSaidIsNothingRefused() =>
        CommentRule.Refuse(string.Empty).Should().BeEmpty();

    /// <summary>The server's limit is the one in <c>shared/comment-limit.json</c>.</summary>
    /// <remarks>
    /// The review page's <c>maxlength</c> holds the same number and asserts it against the same file
    /// (story 4.2). Neither program reads the other's source for it: a test that parsed another
    /// program's code would go quiet on a reformat instead of red, which is what
    /// <c>NothingReadsAnotherProgramsSourceTests</c> exists to stop.
    /// </remarks>
    [Fact]
    public void TheLimitIsTheOneBothHalvesAgreeOn()
    {
        // tests/bin/<cfg>/net10.0 → the repository root, then the shared folder both sides read.
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "shared", "comment-limit.json"));
        using var shared = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(path));

        shared.RootElement.GetProperty("mostChars").GetInt32().Should().Be(
            CommentRule.MostChars, "the box and the server must stop a comment at the same length");
    }

    /// <summary>Exactly at the limit is taken; one more is not.</summary>
    [Fact]
    public void TheLimitIsInclusive()
    {
        CommentRule.Refuse(new string('x', CommentRule.MostChars)).Should().BeEmpty(
            "a person who filled the box exactly is inside it");

        CommentRule.Refuse(new string('x', CommentRule.MostChars + 1))
            .Should().Contain("1001").And.Contain("1000",
                "and the refusal names both numbers, so the box and the server cannot disagree "
                + "silently about which one was meant");
    }

    /// <summary>
    /// The length is counted in UTF-16 code units, which is what the box will count.
    /// </summary>
    /// <remarks>
    /// An emoji outside the BMP is TWO units. Counting runes here and units in the page's
    /// <c>maxlength</c> would let somebody type a comment the box accepts and the server refuses —
    /// the one failure a shared rule exists to prevent.
    /// </remarks>
    [Fact]
    public void LengthIsCountedInTheUnitsTheBoxCounts()
    {
        var rocket = char.ConvertFromUtf32(0x1F680);
        rocket.Length.Should().Be(2, "this is the premise, not the assertion");

        CommentRule.Refuse(string.Concat(Enumerable.Repeat(rocket, CommentRule.MostChars / 2)))
            .Should().BeEmpty("500 rockets are 1 000 units");
        CommentRule.Refuse(string.Concat(Enumerable.Repeat(rocket, (CommentRule.MostChars / 2) + 1)))
            .Should().Contain("1002");
    }

    [Theory]
    [InlineData(0x0000)] // NUL truncates sqlite3's own output of a TEXT column
    [InlineData(0x0007)]
    [InlineData(0x000b)]
    [InlineData(0x000d)] // a bare CR: the client normalises to LF, so one arriving is a client defect
    [InlineData(0x001b)] // ESC hands a terminal listing to whoever wrote the comment
    [InlineData(0x001f)] // the last C0
    [InlineData(0x007f)] // DEL, the first of the C1 block as this rule counts it
    [InlineData(0x0085)]
    [InlineData(0x009f)] // the last C1
    public void AControlThatBreaksAReaderIsRefusedByCodePoint(int code) =>
        CommentRule.Refuse($"before{(char)code}after")
            .Should().Contain($"U+{code:X4}")
            .And.NotContain("before", "a refusal names a code point, never the content");

    /// <summary>LF and TAB are the two controls a person legitimately types.</summary>
    [Theory]
    [InlineData(0x0009)]
    [InlineData(0x000a)]
    public void TheTwoControlsAPersonTypesAreTaken(int code) =>
        CommentRule.Refuse($"two lines{(char)code}of it").Should().BeEmpty();

    /// <summary>Every bidirectional control, not a sample of them.</summary>
    /// <remarks>
    /// Three literals and two ranges, and a test that checked one would stay green with either range
    /// emptied — which is exactly what nearly happened: the rule was first written in these
    /// characters themselves, where any tool that strips them would have removed a range in silence.
    /// </remarks>
    [Theory]
    [InlineData(0x061c)]
    [InlineData(0x200e)]
    [InlineData(0x200f)]
    [InlineData(0x202a)]
    [InlineData(0x202b)]
    [InlineData(0x202c)]
    [InlineData(0x202d)]
    [InlineData(0x202e)]
    [InlineData(0x2066)]
    [InlineData(0x2067)]
    [InlineData(0x2068)]
    [InlineData(0x2069)]
    public void EveryBidirectionalControlIsRefused(int code) =>
        CommentRule.Refuse($"looks{(char)code}harmless").Should().Contain($"U+{code:X4}");

    /// <summary>And the characters on either side of those ranges are ordinary text.</summary>
    [Theory]
    [InlineData(0x0629)]
    [InlineData(0x200d)] // ZERO WIDTH JOINER — what holds a family emoji together
    [InlineData(0x2028)] // LINE SEPARATOR, deliberately allowed
    [InlineData(0x2029)] // PARAGRAPH SEPARATOR, deliberately allowed
    [InlineData(0x202f)]
    [InlineData(0x2065)]
    [InlineData(0x206a)]
    public void TheCharactersAroundThoseRangesAreTaken(int code) =>
        CommentRule.Refuse($"looks{(char)code}harmless").Should().BeEmpty();

    /// <summary>A surrogate PAIR is a character; half of one is not.</summary>
    /// <remarks>
    /// The three positions matter separately: a high surrogate at the end of the string has no
    /// neighbour to look at, a low one at the start has none behind it, and one in the middle has a
    /// neighbour that is simply the wrong kind. An implementation that read only forwards would pass
    /// two of the three.
    /// </remarks>
    [Fact]
    public void HalfAnEmojiIsNotACharacter()
    {
        var rocket = char.ConvertFromUtf32(0x1F680);
        var high = rocket[0];
        var low = rocket[1];

        CommentRule.Refuse($"a rocket {rocket} flew").Should().BeEmpty("the pair is one character");

        CommentRule.Refuse($"trailing {high}").Should().Contain("U+D83D", "nothing follows it");
        CommentRule.Refuse($"{low} leading").Should().Contain("U+DE80", "nothing precedes it");
        CommentRule.Refuse($"{high}x{low}").Should().Contain(
            "U+D83D", "a high surrogate followed by an ordinary letter completes nothing");
    }

    /// <summary>What a person actually writes is taken whole, and the rule answers nothing.</summary>
    [Theory]
    [InlineData("this one bit us in production")]
    [InlineData("two lines\nof it")]
    [InlineData("  padded  ")]
    [InlineData("markup </div><script>alert(1)</script> is text here")]
    [InlineData("quotes \" and ' and a backslash \\")]
    public void OrdinaryWritingIsTaken(string said) => CommentRule.Refuse(said).Should().BeEmpty();
}
