using CoaiMcp.Core.Collecting;
using CoaiMcp.Core.Normalising;
using CoaiMcp.Normalizer;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The keywords the ingest server checks against are the ones the grammars actually have.
/// </summary>
/// <remarks>
/// <para><b>Why a file at all.</b> The server validates a skeleton's alphabet: every word is a
/// placeholder, runtime vocabulary, or a keyword of the language. It can reach `RuntimeVocabulary` by
/// a project reference — it is ordinary IL in `CoaiMcp.Core` — but the keywords come from tree-sitter,
/// by reading each grammar's anonymous symbols, and shipping sixty megabytes of native grammars into
/// an ingest server so it can ask a parser for a word list is absurd.</para>
/// <para><b>So the file is a CACHE of a derivation, not a hand-written list.</b> That distinction is
/// the whole reason this test exists: `TreeSitterNormalizer.KeywordsOf` says a hand-written list
/// "would be a worse second copy and would go stale the first time a grammar is updated", and it is
/// right. This asserts the checked-in file still equals what the grammars say, so a grammar bump that
/// changes a keyword is a red test rather than a server that starts refusing valid skeletons.</para>
/// <para>A plan reviewer asked for a shared file for the runtime VOCABULARY, on the premise that the
/// server cannot reference the core. It can, and a project reference is better there. The instinct was
/// right about the wrong list: it is the keywords that genuinely cannot cross.</para>
/// </remarks>
public sealed class TheKeywordsAreOneListTests
{
    private static readonly SourceLanguage[] Shipped =
        [SourceLanguage.CSharp, SourceLanguage.TypeScript, SourceLanguage.JavaScript];

    [Fact]
    public void TheFileIsWhatTheGrammarsSay()
    {
        var normalizer = new TreeSitterNormalizer();
        var listed = SkeletonKeywords.Read(Root());

        foreach (var language in Shipped)
        {
            // WORD-shaped tokens only. A grammar's anonymous symbols include its punctuation —
            // `[`, `=>`, `;` — and the alphabet check never looks at those: it examines runs of word
            // characters and has no opinion about anything else. Carrying punctuation in the file
            // would also break its own format, since a token `[]` reads as a section header.
            var real = normalizer.KeywordsOf(language)
                .Where(word => word.Length > 0 && (char.IsLetter(word[0]) || word[0] == '_'))
                .ToHashSet(StringComparer.Ordinal);
            real.Should().NotBeEmpty($"the {language} grammar must report its own tokens");
            listed.TryGetValue(language.ToString(), out var cached).Should().BeTrue(
                $"shared/skeleton-keywords.txt has no section for {language}");

            cached!.Should().BeEquivalentTo(
                real,
                "the file is a cache of what the grammar says — regenerate it when a grammar is bumped, "
                + "because a stale list makes the ingest server refuse skeletons that are perfectly good");
        }
    }

    /// <summary>Every shipped language has a section, and nothing else does.</summary>
    /// <remarks>
    /// Both directions: a language added to the normaliser and forgotten here would have the server
    /// refusing all of its skeletons, and a section for a language nobody parses is a list nobody
    /// maintains.
    /// </remarks>
    [Fact]
    public void EveryShippedLanguageHasASection() =>
        SkeletonKeywords.Read(Root()).Keys.Should().BeEquivalentTo(
            Shipped.Select(one => one.ToString()));

    private static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "shared", "skeleton-keywords.txt")))
            {
                return dir.FullName;
            }
        }

        throw new FileNotFoundException("shared/skeleton-keywords.txt was not found above the test binary");
    }
}
