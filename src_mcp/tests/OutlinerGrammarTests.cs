using CoaiMcp.Core.Normalising;
using CoaiMcp.Core.Outlining;
using CoaiMcp.Normalizer;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Every grammar the outliner names loads by its entry point — and the collector did not notice.
/// </summary>
/// <remarks>
/// <para>A grammar that does not load is a <c>DllNotFoundException</c> or an
/// <c>EntryPointNotFoundException</c> the first time somebody's file is outlined, which the stdio
/// server would report as a failed round. Loaded here, from the same <see cref="Grammars"/> table the
/// outliner reads, so a wrong row fails in the suite rather than on somebody else's machine.</para>
/// <para>The PHP entry point was DECIDED by loading, per the plan (§7.2, S1.3): upstream documents
/// <c>tree_sitter_php</c> and <c>tree_sitter_php_only</c>, and the library this package ships exports
/// one of them.</para>
/// </remarks>
public sealed class OutlinerGrammarTests
{
    public static TheoryData<OutlineLanguage> OutlinedLanguages() =>
        [.. Enum.GetValues<OutlineLanguage>().Where(language => language is not OutlineLanguage.Unsupported)];

    [Theory]
    [MemberData(nameof(OutlinedLanguages))]
    public void EveryOutlineGrammar_LoadsByItsEntryPoint(OutlineLanguage language)
    {
        var grammar = Grammars.Of(language);

        grammar.Should().NotBeNull($"{language} is a language the outliner reads, so it names a grammar");
        Grammars.LoadFailure(grammar!.Value).Should().BeEmpty($"{grammar} must load");
    }

    [Fact]
    public void ThePhpEntryPointIsTheOneTheLibraryExports()
    {
        Grammars.Php.Should().Be(new Grammar("tree-sitter-php", "tree_sitter_php"));
        Grammars.LoadFailure(Grammars.Php).Should().BeEmpty();

        // The other documented entry point is NOT in this build of the library — which is the
        // measurement that decided the row above, kept so an upgrade that adds it is noticed.
        Grammars.LoadFailure(new Grammar("tree-sitter-php", "tree_sitter_php_only"))
            .Should().StartWith(nameof(EntryPointNotFoundException));
    }

    /// <summary>The collector reads <c>.tsx</c> exactly as it did: as TypeScript, with the TypeScript grammar.</summary>
    /// <remarks>
    /// <see cref="SourceLanguage"/> is the corpus's trust boundary; the outliner's separate
    /// <see cref="OutlineLanguage.Tsx"/> must not have leaked into it.
    /// </remarks>
    [Fact]
    public void TheCollectorsTsxBehaviour_IsUnchanged()
    {
        new TreeSitterNormalizer().LanguageOf("src/chat.tsx").Should().Be(SourceLanguage.TypeScript);
        Grammars.Of(SourceLanguage.TypeScript).Should().Be(new Grammar("tree-sitter-typescript", "tree_sitter_typescript"));
        Enum.GetNames<SourceLanguage>().Should().Equal("Unsupported", "CSharp", "TypeScript", "JavaScript");
    }
}
