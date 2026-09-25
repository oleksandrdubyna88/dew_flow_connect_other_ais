using CoaiMcp.Core.Normalising;
using CoaiMcp.Core.Outlining;
using CoaiMcp.Normalizer;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The grammars the publish keeps are exactly the grammars this product can parse.
/// </summary>
/// <remarks>
/// <para>The binding ships 28+ native grammars and `coai-mcp` parses seven, so the publish deletes the
/// rest — tens of megabytes per RID, on a file people download. That deletion is an MSBuild
/// target, and a target that drops the wrong file produces a `DllNotFoundException` on somebody
/// else's machine rather than an error on ours. This repository has shipped exactly that once
/// already: mcp-v0.18.1 went out without `e_sqlite3` and died on the first database touch.</para>
/// <para>So the target and the parsers read ONE list — <c>shared/kept-grammars.txt</c> — and this
/// holds them to each other. Adding a language becomes two edits, and forgetting the second one fails
/// here instead of in the field.</para>
/// <para><b>Two interfaces parse, so the list is their UNION</b> (plan §4.7): the normalizer's three
/// <see cref="SourceLanguage"/> values and the outliner's seven <see cref="OutlineLanguage"/> values.
/// Both are asked of <see cref="Grammars"/>, the one table both classes read — a switch in this test
/// would be a third list, and the third copy is the one nobody updates.</para>
/// </remarks>
public sealed class KeptGrammarsTests
{
    /// <summary>The names the publish keeps, as the MSBuild target reads them.</summary>
    private static IReadOnlyList<string> Kept()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var file = Path.Combine(dir.FullName, "shared", "kept-grammars.txt");
            if (File.Exists(file))
            {
                return
                [
                    .. File.ReadAllLines(file)
                        .Select(line => line.Trim())
                        .Where(line => line.Length > 0 && !line.StartsWith('#')),
                ];
            }
        }

        throw new FileNotFoundException($"shared/kept-grammars.txt was not found above {AppContext.BaseDirectory}");
    }

    /// <summary>Every grammar either interface parses with — the union the publish must keep.</summary>
    private static IReadOnlyList<Grammar> Parsed() =>
    [
        .. Enum.GetValues<SourceLanguage>().Select(Grammars.Of)
            .Concat(Enum.GetValues<OutlineLanguage>().Select(Grammars.Of))
            .OfType<Grammar>()
            .Distinct(),
    ];

    /// <summary>Every language the normalizer can read has its grammar in the keep list.</summary>
    [Fact]
    public void EveryLanguageTheNormalizerReads_KeepsItsGrammar()
    {
        var normalizer = new TreeSitterNormalizer();
        var kept = Kept();

        foreach (var language in Enum.GetValues<SourceLanguage>().Where(l => l is not SourceLanguage.Unsupported))
        {
            // It parses, so its grammar must survive the publish.
            normalizer.KeywordsOf(language).Should().NotBeEmpty($"{language} is a language this product reads");

            kept.Should().Contain(
                Grammars.Of(language)!.Value.Library,
                $"{language} is parsed, so the publish must keep its grammar or the binary throws "
                + "DllNotFoundException the first time somebody normalises that language");
        }
    }

    /// <summary>Every language the OUTLINER can read has its grammar in the keep list too.</summary>
    /// <remarks>
    /// Red the moment <see cref="OutlineLanguage"/> named Rust and the list did not (S1.3's RED step):
    /// the feature review would have outlined a <c>.rs</c> file in the suite and thrown in the field.
    /// </remarks>
    [Fact]
    public void EveryLanguageTheOutlinerReads_KeepsItsGrammar()
    {
        var kept = Kept();

        foreach (var language in Enum.GetValues<OutlineLanguage>().Where(l => l is not OutlineLanguage.Unsupported))
        {
            kept.Should().Contain(
                Grammars.Of(language)!.Value.Library,
                $"{language} is outlined, so the publish must keep its grammar or the binary throws "
                + "DllNotFoundException the first time a feature review reaches such a file");
        }
    }

    /// <summary>The keep list holds nothing the parsers cannot use — plus the runtime itself.</summary>
    /// <remarks>
    /// The other direction, and it is not pedantry: a stale name here is megabytes nobody needs
    /// shipped for ever, and the list is the only place anybody would notice.
    /// </remarks>
    [Fact]
    public void TheKeepListHoldsNothingUnused()
    {
        var expected = Parsed().Select(grammar => grammar.Library).Append("tree-sitter").Distinct(StringComparer.Ordinal);

        Kept().Should().BeEquivalentTo(expected, "the list is the publish, and the publish is the parsers");
    }

    /// <summary>The runtime itself is kept, which is the entry nobody thinks of.</summary>
    /// <remarks>
    /// Every grammar is a plug-in for <c>tree-sitter</c> itself. Keeping the grammars and dropping
    /// the library they plug into is the shape of mistake that passes review — the list reads complete
    /// — and fails on the first parse.
    /// </remarks>
    [Fact]
    public void TheParserLibraryItselfIsKept() =>
        Kept().Should().Contain("tree-sitter", "the grammars are plug-ins, and this is what they plug into");
}
