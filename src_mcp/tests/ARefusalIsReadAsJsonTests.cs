using CoaiMcp;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Whether a one-shot mode's reply is a refusal is a question about its SHAPE, not about its text.
/// </summary>
/// <remarks>
/// <para><c>--close-consult</c> read it as <c>answer.Contains("\"error\"")</c> over the serialised
/// reply, and the case that breaks is the one where there is no shape to find: a reply that is not
/// JSON at all — a stack trace, an empty line — contains the substring nowhere, so the mode exited 0
/// and the panel told the person the outcome had been recorded when nothing could be read.</para>
/// <para><b>The other half of that check was sound</b>, and the first draft of this file said
/// otherwise: a value that quotes the word is written <c>\"error\"</c>, which ends backslash-quote,
/// so the seven characters <c>"error"</c> never appear inside one. The test below that was meant to
/// prove the danger refuted it instead, which is the useful outcome of writing it. What survives is
/// the property, asserted directly rather than through a claim about the old code. (codex, the code
/// round of issue #309.)</para>
/// </remarks>
public sealed class ARefusalIsReadAsJsonTests
{
    [Fact]
    public void TheErrorSHAPEIsARefusal()
    {
        Program.Refused("""{"error":"consultation abc is still running a turn"}""").Should().BeTrue();
    }

    [Fact]
    public void AnAnswerIsNotARefusal()
    {
        Program.Refused("""{"id":"abc","outcome":"solved","recorded":true,"said":"consultation abc is recorded as 'solved'"}""")
            .Should().BeFalse();
    }

    /// <summary>A VALUE that quotes the word is still an answer — only the top-level member decides.</summary>
    /// <remarks>
    /// Asserted as the property it is, rather than as a claim about what the old check did with it:
    /// measured, the old check agreed here, because JSON writes such a value as <c>\"error\"</c> and
    /// that ends backslash-quote. What matters is that nothing about a field's contents can decide
    /// this, whatever sentence a future answer carries.
    /// </remarks>
    [Fact]
    public void AFieldWhoseValueQuotesTheWordIsStillAnAnswer()
    {
        var answer = """{"recorded":true,"said":"the \"error\" in the parser was the loop bound"}""";

        Program.Refused(answer).Should().BeFalse("the word is in a value, and a value says nothing about the shape");
    }

    /// <summary>And output nobody can read is a refusal, not a success — the case that broke.</summary>
    /// <remarks>
    /// A reply that will not parse contains the old substring nowhere, so the mode exited 0 and the
    /// panel told the person the outcome had been recorded. The caller has nothing it can act on
    /// either way, and of the two wrong answers "it did not happen" is the one that leads somebody
    /// to look.
    /// </remarks>
    [Fact]
    public void AReplyThatWillNotParseIsARefusal()
    {
        Program.Refused("Unhandled exception. System.IO.IOException: the file is in use")
            .Should().BeTrue();
        Program.Refused(string.Empty).Should().BeTrue();
        Program.Refused("""["not","an","object"]""").Should().BeTrue("the shape is not the one every mode answers in");
    }
}
