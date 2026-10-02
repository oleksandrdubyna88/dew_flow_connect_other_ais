using System.Text;
using CoaiMcp.Core.QuestionConsult;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The text as a model reads it (S4b item 4): format characters removed, compatibility forms folded to the ASCII
/// they spell — what the web sanitiser and the secret check read, so an invisible or full-width character cannot
/// hide a path, a host or a key from them.
/// </summary>
public sealed class TextAsReadTests
{
    /// <remarks>
    /// The measurement the fold rests on, kept as a test: under <c>InvariantGlobalization</c> (every binary here)
    /// <c>Normalize(FormKC)</c> folds nothing. The day it does, this goes red, and the managed fold can be reconsidered.
    /// </remarks>
    [Fact]
    public void NormalizeFormKC_FoldsNothingUnderInvariantGlobalization_WhichIsWhyTheFoldIsOurs()
    {
        "\uFF23\uFF1A\uFF3C".Normalize(NormalizationForm.FormKC).Should().Be("\uFF23\uFF1A\uFF3C");
    }

    [Theory]
    [InlineData("\uFF23\uFF1A\uFF3C\uFF57\uFF4F\uFF52\uFF4B", "C:\\work")]   // the full-width ASCII block
    [InlineData("a\u00A0b\u3000c\u2009d", "a b c d")]                       // every space separator
    [InlineData("C\uFE55\uFE68x \uFE6Bhost", "C:\\x @host")]                // small form variants
    [InlineData("\U0001D42C\U0001D424\U0001D7CF", "sk1")]                    // mathematical letters and digits
    [InlineData("192.168.\u00B9.\u2082\u2080", "192.168.1.20")]             // super- and subscript digits
    [InlineData("con\uFB01g", "config")]                                     // a ligature
    [InlineData("\u2026/", ".../")]                                         // a dot leader
    [InlineData("plain ASCII stays as it is: D:/x?a=b", "plain ASCII stays as it is: D:/x?a=b")]
    public void ACompatibilityForm_FoldsToTheAsciiItSpells(string text, string read) =>
        TextAsRead.Normalised(text).Should().Be(read);

    [Fact]
    public void FormatCharacters_AreRemovedAndCounted_ZeroWidthBidiSoftHyphenAndTags()
    {
        const string text = "s\u200Bk\u200D-\u202Ex\u202C\u00AD\U000E0041";

        TextAsRead.Normalised(text).Should().Be("sk-x");
        TextAsRead.FormatCharacters(text).Should().Be(6);
        TextAsRead.FormatCharacters("no format characters — an em dash is punctuation").Should().Be(0);
    }

    [Fact]
    public void ALoneSurrogate_ReadsAsTheReplacementCharacter_AndNeverThrows()
    {
        TextAsRead.Normalised("a\uD800b").Should().Be("a\uFFFDb");
    }
}
