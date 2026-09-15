using CoaiMcp.Core.Normalising;
using CoaiMcp.Normalizer;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The grammars the publish keeps are exactly the grammars this product can parse.
/// </summary>
/// <remarks>
/// <para>The binding ships 28+ native grammars and `coai-mcp` parses three, so the publish deletes the
/// rest — 69 MB of a 170 MB publish, per RID, on a file people download. That deletion is an MSBuild
/// target, and a target that drops the wrong file produces a `DllNotFoundException` on somebody
/// else's machine rather than an error on ours. This repository has shipped exactly that once
/// already: mcp-v0.18.1 went out without `e_sqlite3` and died on the first database touch.</para>
/// <para>So the target and the normalizer read ONE list — <c>shared/kept-grammars.txt</c> — and this
/// holds them to each other. Adding a language becomes two edits, and forgetting the second one fails
/// here instead of in the field.</para>
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

    /// <summary>Every language the normalizer can read has its grammar in the keep list.</summary>
    /// <remarks>
    /// Asked of the normalizer rather than of a second list here: the thing that must not drift is the
    /// publish against the PARSER, and a list in a test would drift from both.
    /// </remarks>
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
                GrammarLibraryOf(language),
                $"{language} is parsed, so the publish must keep its grammar or the binary throws "
                + "DllNotFoundException the first time somebody normalises that language");
        }
    }

    /// <summary>The keep list holds nothing the parser cannot use — plus the runtime itself.</summary>
    /// <remarks>
    /// The other direction, and it is not pedantry: a stale name here is 2 MB nobody needs shipped for
    /// ever, and the list is the only place anybody would notice.
    /// </remarks>
    [Fact]
    public void TheKeepListHoldsNothingUnused()
    {
        var expected = Enum.GetValues<SourceLanguage>()
            .Where(l => l is not SourceLanguage.Unsupported)
            .Select(GrammarLibraryOf)
            .Append("tree-sitter")
            .Distinct(StringComparer.Ordinal);

        Kept().Should().BeEquivalentTo(expected, "the list is the publish, and the publish is the parser");
    }

    /// <summary>The runtime itself is kept, which is the entry nobody thinks of.</summary>
    /// <remarks>
    /// Every grammar is a plug-in for <c>tree-sitter</c> itself. Keeping three grammars and dropping
    /// the library they plug into is the shape of mistake that passes review — the list reads complete
    /// — and fails on the first parse.
    /// </remarks>
    [Fact]
    public void TheParserLibraryItselfIsKept() =>
        Kept().Should().Contain("tree-sitter", "the grammars are plug-ins, and this is what they plug into");

    /// <summary>The library a language's grammar lives in, as the normalizer names it.</summary>
    private static string GrammarLibraryOf(SourceLanguage language) => language switch
    {
        SourceLanguage.CSharp => "tree-sitter-c-sharp",
        SourceLanguage.TypeScript => "tree-sitter-typescript",
        SourceLanguage.JavaScript => "tree-sitter-javascript",
        _ => throw new ArgumentOutOfRangeException(
            nameof(language),
            language,
            "a language was added to SourceLanguage without naming the grammar it needs — add it here "
            + "and in shared/kept-grammars.txt, or the publish will drop the grammar it parses with"),
    };
}
