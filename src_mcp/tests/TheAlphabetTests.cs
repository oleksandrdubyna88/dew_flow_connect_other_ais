using CoaiMcp.Core.Collecting;
using CoaiMcp.Core.Normalising;
using CoaiMcp.Normalizer;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The check a server makes when it has never seen the original.
/// </summary>
/// <remarks>
/// <para><b>Two properties, and both matter.</b> It must ADMIT every skeleton this product really
/// produces — a validator that refuses good work turns contributors away and nobody finds out why —
/// and it must REFUSE anything carrying a name, a string or a number, because that is the only thing
/// standing between a leak in a client and a public corpus.</para>
/// <para>The first is tested over this repository's own source, put through the real normaliser,
/// rather than over skeletons written by hand: a fixture proves what its author imagined.</para>
/// </remarks>
public sealed class TheAlphabetTests
{
    private static readonly TreeSitterNormalizer Normalizer = new();

    private static IReadOnlySet<string> Keywords(SourceLanguage language) =>
        Normalizer.KeywordsOf(language);

    /// <summary>
    /// Every skeleton this repository's own methods produce is admitted.
    /// </summary>
    /// <remarks>
    /// The direction that is easy to get wrong and hard to notice: a whitelist that is too narrow
    /// refuses real work, and the person whose upload was refused sees only an error about their own
    /// code. Real files, per grammar, through the shipped normaliser.
    /// </remarks>
    [Theory]
    [InlineData("src_mcp/core", SourceLanguage.CSharp, "*.cs")]
    [InlineData("src_mcp/runners", SourceLanguage.CSharp, "*.cs")]
    [InlineData("src_vs_code/src", SourceLanguage.TypeScript, "*.ts")]
    public void EverySkeletonThisRepositoryProducesIsAdmitted(
        string where, SourceLanguage language, string pattern)
    {
        var keywords = Keywords(language);
        var files = Directory.EnumerateFiles(Path.Combine(Root(), where), pattern, SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                           && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                           && !path.Contains("node_modules", StringComparison.Ordinal))
            .Take(40)
            .ToList();

        files.Should().NotBeEmpty("this test proves nothing over no files");

        foreach (var file in files)
        {
            var source = File.ReadAllText(file);
            for (var line = 2; line < Math.Min(60, source.Split('\n').Length); line += 7)
            {
                if (Normalizer.Locate(language, source, line) is not { } symbol)
                {
                    continue;
                }

                var skeleton = Normalizer.Normalise(language, symbol.Source);
                if (skeleton.Length == 0)
                {
                    continue;
                }

                Alphabet.Refuse(skeleton, language, keywords).Should().BeEmpty(
                    $"{Path.GetFileName(file)}:{line} normalises to something the server would refuse — "
                    + "a whitelist that is too narrow turns contributors away and blames their code");
            }
        }
    }

    /// <summary>A name that got through is refused, and the answer says which word.</summary>
    /// <remarks>
    /// A count tells a person nothing. The word is what they need to find the defect in the client
    /// that produced it — this refusal is the only thing a contributor ever sees when OUR normaliser
    /// is the one at fault.
    /// </remarks>
    [Fact]
    public void AnIdentifierThatSurvivedIsRefused_AndNamed()
    {
        var refusal = Alphabet.Refuse(
            "method_1(var_1) { return ChargeAcmeCustomer(var_1); }",
            SourceLanguage.CSharp,
            Keywords(SourceLanguage.CSharp));

        refusal.Should().Contain("ChargeAcmeCustomer");
    }

    [Fact]
    public void AStringThatSurvivedIsRefused()
    {
        var refusal = Alphabet.Refuse(
            """method_1() { var_1 = "https://api.acme-corp.example/charge"; }""",
            SourceLanguage.CSharp,
            Keywords(SourceLanguage.CSharp));

        refusal.Should().Contain("string literal").And.Contain("acme-corp");
    }

    /// <summary>A number that is not zero is refused; the zero the normaliser writes is not.</summary>
    /// <remarks>
    /// The normaliser writes every numeric literal as `0`, so a `42` is either a literal that escaped
    /// or a skeleton somebody wrote by hand. And the digits INSIDE a placeholder — the `1` of `var_1`
    /// — must not be mistaken for one, which is the whole difficulty of the rule.
    /// </remarks>
    [Theory]
    [InlineData("method_1() { var_1 = 0; }", true)]
    [InlineData("method_12(var_34) { type_7 var_8 = 0; }", true)]
    [InlineData("method_1() { var_1 = 42; }", false)]
    [InlineData("method_1() { var_1 = 3.14; }", false)]
    public void OnlyZeroSurvives(string skeleton, bool admitted) =>
        Alphabet.Admits(skeleton, SourceLanguage.CSharp, Keywords(SourceLanguage.CSharp))
            .Should().Be(admitted);

    /// <summary>Runtime vocabulary is kept, because a skeleton without it says nothing.</summary>
    /// <remarks>
    /// `Monitor`, `lock`, `await` and their kin ship with the language. Erasing them would leave a
    /// shape with no behaviour in it — and the whole point of a pair is that it shows what the fix
    /// DID.
    /// </remarks>
    [Fact]
    public void RuntimeVocabularyIsAdmitted() =>
        Alphabet.Admits(
            "method_1(var_1) { lock (var_2) { Monitor.Wait(var_2); } }",
            SourceLanguage.CSharp,
            Keywords(SourceLanguage.CSharp)).Should().BeTrue();

    [Fact]
    public void AnEmptySkeletonIsRefused() =>
        Alphabet.Refuse("", SourceLanguage.CSharp, Keywords(SourceLanguage.CSharp))
            .Should().Contain("empty");

    /// <summary>
    /// A source name that already looks generated is admitted, and that is a SHAPE check working.
    /// </summary>
    /// <remarks>
    /// A plan reviewer pointed out that a method literally named `method_1` normalises to a
    /// placeholder that may be the same string, so the server cannot tell them apart. That is true,
    /// and it is why this is shape validation rather than proof: nothing unsafe follows, because a
    /// name that coincides with one we would have generated carries no information. The guarantee is
    /// the client's property test over real files. This test exists so nobody later reads the
    /// whitelist as the proof. (Plan round, codex.)
    /// </remarks>
    [Fact]
    public void ANameThatCoincidesWithAPlaceholderIsIndistinguishable_AndThatIsNotAProof() =>
        Alphabet.Admits("method_1() { }", SourceLanguage.CSharp, Keywords(SourceLanguage.CSharp))
            .Should().BeTrue("the shape is right, which is all this check can ever say");

    private static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src_mcp", "core")))
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException("the repository root was not found above the test binary");
    }
}
