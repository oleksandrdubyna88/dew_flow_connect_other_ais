using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The scanner's own contract — what counts as code, and what a census can therefore never miss.
/// </summary>
/// <remarks>
/// <para><b>Why the scanner has tests of its own.</b> <see cref="ProductionSources"/> answers two
/// censuses — <see cref="TheOneAppendTests"/> (one append) and
/// <see cref="TheRefusalRoadsAreCountedTests"/> (one refusal boundary) — and every guarantee either
/// of them makes is really a claim about THIS file. Every rule below was earned by a bypass somebody
/// found, and each one is a way a census could have gone green over the thing it exists to
/// forbid.</para>
///
/// <para><b>The second code round found the last of them.</b> codex: the lexer removed the contents
/// of a string literal, and an interpolation hole is not contents — it is CODE. A construction
/// written as <c>$"{new ErrorAnswer(sentence)}"</c> was therefore invisible to the boundary rule that
/// story 2.2's whole promise rests on. The tests below were written RED against that, and the lexer
/// now walks a hole as the code it is.</para>
/// </remarks>
public sealed class ProductionSourcesTests
{
    [Fact]
    public void AWrappedMEMBER_IsGluedBackTogether()
    {
        // Story 1.4's code round: a per-line scan missed a call split across two lines, which a long
        // qualified name invites somebody to write.
        var split = string.Join("\n", ["        JsonlLedger", "            .AppendLine(path, line);"]);

        ProductionSources.Joined(split).Should().Contain("JsonlLedger.AppendLine(",
            "the scan joins a file's code before searching, so a wrapped call is the same string a "
            + "call written on one line is");
    }

    [Fact]
    public void AWrappedWORD_IsNotGluedIntoADifferentOne()
    {
        // The other half of the join rule, and the reason it has a separator at all: the first
        // version glued every line with nothing, so `using` over `static` became `usingstatic` and
        // story 2.1's import guard was searching for a word C# cannot produce.
        var split = string.Join("\n", ["using", "static CoaiMcp.Server.Refusal;"]);

        ProductionSources.Joined(split).Should().Contain("using static",
            "a wrapped keyword is two words, and a guard that searched the glued spelling could not "
            + "fail — which is what six reviewers found in story 2.1's code round");
    }

    [Fact]
    public void ACarriageReturn_IsNotACharacterInTheJoinedCode()
    {
        // The second code round proposed joining with Environment.NewLine because a CRLF file might
        // leave a hidden character in `using static`. Rejected — the join is the mechanism — but the
        // caution deserved a test rather than a reading: every file in this repository is CRLF, and
        // the trim that removes indentation removes the CR with it.
        var split = string.Join("\r\n", ["using", "static CoaiMcp.Server.Refusal;", "namespace X;"]);

        ProductionSources.Joined(split).Should().Be("using static CoaiMcp.Server.Refusal; namespace X;");
        ProductionSources.Joined(split).Should().NotContain("\r");
    }

    [Fact]
    public void TheTwoViewsDisagreeAboutStringsAndAgreeAboutComments()
    {
        // What separates the census of a CALL from the census of a NAME.
        const string source = "var name = \"server-notices.jsonl\"; // server-notices.jsonl";

        ProductionSources.Joined(source).Should().NotContain("server-notices",
            "the code view drops what is inside a literal, so a sentence that mentions a call is not "
            + "counted as one");
        ProductionSources.JoinedWithText(source).Should().Contain("\"server-notices.jsonl\"",
            "and the spelling view keeps it, because a file name IS a literal");
        ProductionSources.JoinedWithText(source).Should().EndWith("\";",
            "while the comment is gone from BOTH — a file that talks about a name has not spelled it");
    }

    [Theory]
    [InlineData("var m = $\"{Refusal.Answer(sentence)}\";", "Refusal.Answer(")]
    [InlineData("var m = $\"refused: {new ErrorAnswer(why)}\";", "new ErrorAnswer(")]
    [InlineData("var m = $@\"{JsonlLedger.AppendLine(p, l)}\";", "JsonlLedger.AppendLine(")]
    [InlineData("var m = $\"{Outer(Inner(new ErrorAnswer(x)))}\";", "new ErrorAnswer(")]
    public void CodeInsideAnInterpolationHole_IsStillCode(string source, string call)
    {
        // RED before the fix, and the hole it names is the one story 2.2 could not have survived:
        // every census here reads text, so a construction the lexer deletes is a construction no
        // guarantee covers. Written after codex raised it in the second code round.
        ProductionSources.Joined(source).Should().Contain(call,
            "an interpolation hole is code that RUNS, and dropping it with the text around it is how "
            + "a construction hides from the boundary rule the whole promise rests on");
    }

    [Fact]
    public void TheTEXTAroundAHole_IsStillDropped()
    {
        ProductionSources.Joined("var m = $\"server-notices.jsonl {Count()}\";")
            .Should().NotContain("server-notices",
                "the prose in an interpolated string is prose, exactly as in any other literal");
    }

    [Fact]
    public void AnEscapedBrace_IsNotAHole()
    {
        ProductionSources.Joined("var m = $\"{{Error( is not a call}}\";")
            .Should().NotContain("Error(",
                "`{{` is how C# writes a literal brace, so what follows it is text and not a call");
    }

    [Fact]
    public void AStringInsideAHole_DoesNotEndTheLiteralEarly()
    {
        // The end-of-literal bug the hole walk fixes on the way past: `$"{d["k"]}"` used to end at
        // the quote before `k`, which left the rest of the line being read as something it is not.
        ProductionSources.Joined("var m = $\"{d[\"k\"]}\"; Error();")
            .Should().EndWith("Error();",
                "a nested literal inside a hole is lexed as a literal, so the code after the "
                + "interpolated string is still found");
    }

    [Fact]
    public void ADeclarationIsSubtractedHoweverItIsSpaced()
    {
        // The second code round, codex: the subtraction looked for the exact seven characters
        // `(string `, so `Error( string sentence)` — which changes no behaviour — would have counted
        // the declaration as a refusal and moved a number the documents quote.
        const string tight = "string Error(string sentence) { } var a = Error(x); var b = Error(y);";
        const string loose = "string Error(  string sentence) { } var a = Error(x); var b = Error(y);";

        ProductionSources.UnqualifiedCalls(ProductionSources.Joined(tight), "Error").Should().Be(2);
        ProductionSources.UnqualifiedCalls(ProductionSources.Joined(loose), "Error").Should().Be(2,
            "a behaviour-preserving space is not a refusal");
    }

    [Fact]
    public void AQualifiedCallIsNotCounted()
    {
        ProductionSources.UnqualifiedCalls(ProductionSources.Joined("_log.Error(x); Error(y);"), "Error")
            .Should().Be(1, "this codebase logs constantly, and a logger call is not a refusal");
    }
}
