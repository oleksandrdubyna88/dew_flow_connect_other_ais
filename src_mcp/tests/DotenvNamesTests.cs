using System.Text;
using System.Text.Json;
using CoaiMcp.Runners.Vibe;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The names grammar of the person's Vibe sign-in file (DL2 of <c>todo/PLAN_kimi_and_vibe_on_linux.md</c>; A1 spec §1; S-V13): which
/// variable names python-dotenv — the reader Vibe uses — would bind, judged without reading a value.
/// </summary>
/// <remarks>
/// <para>The oracle is <c>shared/dotenv-name-vectors.json</c>: per vector, the bytes, the names python-dotenv 1.2.2 (the one Vibe 2.26.1
/// bundles) bound from them, and the verdict DL2's grammar gives. The Theory asserts the IMPLICATION the soundness argument rests on —
/// <i>coai admits the file ⇒ every name python-dotenv binds is MISTRAL_API_KEY</i> — over every vector, and the verdict itself.</para>
/// <para>Read from the checked-in file, from the test binary up to the repository root (as <see cref="CapabilityMatrixTests"/> does). A
/// missing file fails every case loudly; this suite never skips for it.</para>
/// </remarks>
public sealed class DotenvNamesTests
{
    public static TheoryData<string> Ids() => [.. DotenvVectors.All.Select(v => v.Id)];

    private static DotenvVector Named(string id) => DotenvVectors.All.Single(v => v.Id == id);

    private static DotenvVerdict Judge(string text) => DotenvNames.Judge(Encoding.UTF8.GetBytes(text));

    private static void RefusedAt(string text, int line, DotenvRefusal kind) =>
        Judge(text).Should().Be(new DotenvVerdict.Refused(line, kind), $"the file is refused at its line {line} as {kind}");

    [Fact]
    public void TheSharedVectors_Loaded_AndCoverEveryKindOfRefusal()
    {
        var vectors = DotenvVectors.All;
        vectors.Should().NotBeEmpty("a Theory over zero vectors passes for ever");
        vectors.Where(v => v.Verdict == "refused").Select(v => v.Kind).Distinct().Should().BeEquivalentTo(Enum.GetNames<DotenvRefusal>(), "each of DL2's four kinds has a vector");
        vectors.Count(v => v.Verdict == "admitted").Should().BeGreaterThan(5, "the admitted forms are the positive companion every refusal needs");
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void EveryVector_IsJudgedAsPythonDotenvBinds(string id)
    {
        var vector = Named(id);

        var verdict = DotenvNames.Judge(vector.Bytes);

        if (vector.Verdict == "admitted")
        {
            verdict.Should().Be(DotenvVerdict.Admitted.Instance, $"vector '{id}': coai refused a file python-dotenv reads as nothing but {string.Join(", ", vector.Binds)}");
            vector.Binds.Concat(vector.NoValue).Should().BeSubsetOf([DotenvNames.Admitted],
                $"vector '{id}': coai admits the file ⇒ every name python-dotenv binds from it is {DotenvNames.Admitted}");
        }
        else
        {
            verdict.Should().Be(new DotenvVerdict.Refused(vector.Line, Enum.Parse<DotenvRefusal>(vector.Kind)),
                $"vector '{id}': the FIRST refused line, by DL2's four kinds");
        }
    }

    /// <summary>The implication alone, over every vector — stated apart from the verdict's equality so that a drift in a refusal's line number cannot hide an admission.</summary>
    [Theory]
    [MemberData(nameof(Ids))]
    public void AnAdmittedFile_BindsNothingButTheKey_InPythonDotenvsOwnWords(string id)
    {
        var vector = Named(id);

        if (DotenvNames.Judge(vector.Bytes) is DotenvVerdict.Admitted)
        {
            vector.Binds.Concat(vector.NoValue).Where(name => name != DotenvNames.Admitted).Should().BeEmpty(
                $"python-dotenv binds a variable other than {DotenvNames.Admitted} from vector '{id}', which coai admitted");
        }
    }

    [Theory]
    [InlineData("MISTRAL_API_KEY=v")]
    [InlineData("MISTRAL_API_KEY = \"v\"")]
    [InlineData("export MISTRAL_API_KEY='v'")]
    [InlineData("  MISTRAL_API_KEY=v  # note")]
    [InlineData("'MISTRAL_API_KEY'=v")]
    [InlineData("\n# a comment\n\n")]
    [InlineData("")]
    public void TheAdmittedForms_AreAdmitted(string text)
    {
        Judge(text).Should().Be(DotenvVerdict.Admitted.Instance, $"python-dotenv binds MISTRAL_API_KEY from '{text}' but coai refused the line as unreadable");
    }

    /// <summary>S-V13: python-dotenv's line break is a bare CR too — a reader that splits on LF alone never sees the second variable.</summary>
    [Theory]
    [InlineData("MISTRAL_API_KEY=x\rVIBE_X=1", 2, DotenvRefusal.OtherName)]
    [InlineData("MISTRAL_API_KEY=x\r\nVIBE_X=1", 2, DotenvRefusal.OtherName)]
    [InlineData("MISTRAL_API_KEY=x\nVIBE_X=1", 2, DotenvRefusal.OtherName)]
    [InlineData("MISTRAL_API_KEY=x\r\rVIBE_X=1", 3, DotenvRefusal.OtherName)]
    public void ABareCr_IsALineBreak_SoARefusedBindingAfterItIsSeen(string text, int line, DotenvRefusal kind)
    {
        Judge(text).Should().Be(new DotenvVerdict.Refused(line, kind), "a bare CR hid VIBE_X from coai, which python-dotenv binds");
    }

    /// <summary>A vertical tab, form feed, NEL and U+2028 bind no second name in python-dotenv (measured); splitting there would only refuse more.</summary>
    [Theory]
    [InlineData("MISTRAL_API_KEY=x\u000bVIBE_X=1")]
    [InlineData("MISTRAL_API_KEY=x\u000cVIBE_X=1")]
    [InlineData("MISTRAL_API_KEY=x\u0085VIBE_X=1")]
    [InlineData("MISTRAL_API_KEY=x\u2028VIBE_X=1")]
    public void AVerticalTabAndItsKin_AreNotLineBreaks(string text)
    {
        Judge(text).Should().Be(DotenvVerdict.Admitted.Instance, "python-dotenv's value swallows it, so one name is bound");
    }

    /// <summary>The three multi-line shapes of DL2, whole files.</summary>
    [Fact]
    public void AQuotedValueThatContinuesOnTheNextLine_IsRefusedOnlyWhenTheContinuationLooksLikeABinding()
    {
        RefusedAt("MISTRAL_API_KEY=\"dummy\nLEAK_CANARY=ending\"", 2, DotenvRefusal.OtherName);
        RefusedAt("MISTRAL_API_KEY=\"a\n\"\nVIBE_X=1", 2, DotenvRefusal.NoBinding);
        Judge("MISTRAL_API_KEY=\"dummy\n#ending\"").Should().Be(DotenvVerdict.Admitted.Instance, "python-dotenv binds one name, and every name it can bind starts on a line coai reads");
    }

    [Fact]
    public void ABom_RefusesAtLineOne_AsAByteOrderMark_EvenWhenTheRestIsAdmitted()
    {
        DotenvNames.Judge([0xEF, 0xBB, 0xBF, .. "MISTRAL_API_KEY=v"u8]).Should().Be(new DotenvVerdict.Refused(1, DotenvRefusal.ByteOrderMark));
    }

    [Fact]
    public void ANul_AndInvalidUtf8_RefuseAsNotText_AtTheLineHoldingThem()
    {
        DotenvNames.Judge("MISTRAL_API_KEY=a\0b"u8.ToArray()).Should().Be(new DotenvVerdict.Refused(1, DotenvRefusal.NotText));
        DotenvNames.Judge([.. "MISTRAL_API_KEY=v\n"u8, 0xFF, 0xFE]).Should().Be(new DotenvVerdict.Refused(2, DotenvRefusal.NotText));
        DotenvNames.Judge([.. "no binding here "u8, 0xC3, 0x28]).Should().Be(new DotenvVerdict.Refused(1, DotenvRefusal.NotText),
            "a line that is not text is NotText before it is anything else");
    }

    [Theory]
    [InlineData("sk-test-LEAK", 1, DotenvRefusal.NoBinding)]
    [InlineData("=sk-test-v", 1, DotenvRefusal.NoBinding)]
    [InlineData("MISTRAL_API_KEY junk", 1, DotenvRefusal.NoBinding)]
    [InlineData("MISTRAL_API_KEY#x=1", 1, DotenvRefusal.NoBinding)]
    [InlineData("export =1", 1, DotenvRefusal.NoBinding)]
    [InlineData("'MISTRAL_API_KEY=sk-test-v", 1, DotenvRefusal.NoBinding)]
    [InlineData("''=v", 1, DotenvRefusal.NoBinding)]
    [InlineData("exportMISTRAL_API_KEY=1", 1, DotenvRefusal.OtherName)]
    [InlineData("MISTRAL_API_KEY=sk-test-v\nVIBE_X=1", 2, DotenvRefusal.OtherName)]
    [InlineData("export=1", 1, DotenvRefusal.OtherName)]
    public void ALineThatIsNotTheKeyBinding_IsRefused_ByItsLineAndKind(string text, int line, DotenvRefusal kind)
    {
        RefusedAt(text, line, kind);
    }

    /// <summary>
    /// The one place the two grammars must agree in a direction: a code point coai takes for whitespace that python-dotenv does not
    /// would let <c>MISTRAL_API_KEY&lt;c&gt;=v</c> read as the admitted name while python-dotenv binds a different one.
    /// </summary>
    [Fact]
    public void WhitespaceIsNeverWiderThanPythons()
    {
        var python = PythonWhitespace();
        var wider = Enumerable.Range(0, 0x110000)
            .Where(code => Rune.IsValid(code) && DotenvNames.IsDotenvWhitespace(new Rune(code)) && !python.Contains(code))
            .Select(code => $"U+{code:X4}")
            .ToList();

        wider.Should().BeEmpty("coai's whitespace set must be a subset of python-dotenv's \\s; these are not in it");
        python.Should().Contain([0x1C, 0xA0, 0x2028, 0x3000], "the oracle list loaded and holds the non-ASCII members the spec pins");
    }

    private static HashSet<int> PythonWhitespace()
    {
        using var document = DotenvVectors.Document();

        return [.. document.RootElement.GetProperty("oracle").GetProperty("whitespace").EnumerateArray().Select(c => c.GetInt32())];
    }

    [Fact]
    public void ANonAsciiWhitespaceAfterTheName_EndsItTheWayPythonsDoes()
    {
        Judge("MISTRAL_API_KEY\u00a0=v").Should().Be(DotenvVerdict.Admitted.Instance, "both readers end the name at NBSP");
        Judge("MISTRAL_API_KEY\u001c=v").Should().Be(DotenvVerdict.Admitted.Instance, "U+001C is python's whitespace and not .NET's: the one addition");
    }

    /// <summary>I4: a refusal is a function of the path, the line and the kind — never of a byte of the file.</summary>
    [Theory]
    [MemberData(nameof(Ids))]
    public void NoSentence_HoldsAByteOfTheValue_OrOfAnyLineItNames(string id)
    {
        var vector = Named(id);
        if (DotenvNames.Judge(vector.Bytes) is not DotenvVerdict.Refused(var line, var kind))
        {
            return;
        }

        var sentence = VibeSignInSentences.Name("/home/p/.vibe/.env", line, kind);

        var echoed = vector.MustNotEcho.Where(word => word.Length > 0 && !Template.Contains(word, StringComparison.Ordinal) && sentence.Contains(word, StringComparison.Ordinal));
        echoed.Should().BeEmpty($"the refusal echoed text from the .env (vector '{id}'): a found name may be the continuation of a quoted value");
        sentence.Should().NotContain("sk-test-", "the refusal must not contain the value").And.NotContain("VIBE_X").And.NotContain("LEAK_CANARY", "the refusal named LEAK_CANARY, which is value text");
    }

    /// <summary>
    /// What the four sentences say of THEMSELVES: a word of the file that is also a word of the sentence's own wording (<c>the</c>,
    /// <c>MISTRAL_API_KEY</c>) is not an echo. Everything else in <c>mustNotEcho</c> is.
    /// </summary>
    private static readonly string Template = string.Join(' ', Enum.GetValues<DotenvRefusal>().Select(kind => VibeSignInSentences.Name("/p/.env", 7, kind)));

    [Fact]
    public void TheSentences_NameTheRuleAndTheLine_AndTheLineIsTheOnlyThingFromTheFile()
    {
        var other = VibeSignInSentences.Name("/home/p/.vibe/.env", 3, DotenvRefusal.OtherName);
        var none = VibeSignInSentences.Name("/home/p/.vibe/.env", 5, DotenvRefusal.NoBinding);
        var bom = VibeSignInSentences.Name("/home/p/.vibe/.env", 1, DotenvRefusal.ByteOrderMark);

        other.Should().Contain("line 3").And.Contain("/home/p/.vibe/.env").And.Contain("keep only `MISTRAL_API_KEY=…`").And.Contain("`VIBE_*`");
        none.Should().Contain("line 5").And.Contain("`NAME=value`");
        bom.Should().Contain("byte-order mark").And.Contain("without one");
        VibeSignInSentences.Name("/p/.env", 2, DotenvRefusal.NotText).Should().Contain("cannot put in its environment", "a NUL is read by python-dotenv; the refusal is Vibe's reason, not python-dotenv's");
    }
}
