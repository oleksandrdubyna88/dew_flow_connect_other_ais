using CoaiMcp.Core.Normalising;
using CoaiMcp.Core.Outlining;
using TreeSitter;

namespace CoaiMcp.Normalizer;

/// <summary>A tree-sitter grammar: a native LIBRARY and an ENTRY POINT inside it.</summary>
/// <remarks>
/// Not the same word. The binding's one-argument constructor derives both from a single id —
/// library <c>tree-sitter-{id}</c>, function <c>tree_sitter_{id}</c> — and therefore cannot name
/// C# at all: that grammar is <c>tree-sitter-c-sharp</c> with an entry point of
/// <c>tree_sitter_c_sharp</c>. Spelled out as a pair so it is visibly deliberate.
/// </remarks>
internal readonly record struct Grammar(string Library, string EntryPoint);

/// <summary>
/// The ONE table of grammars, read by both the normalizer and the outliner.
/// </summary>
/// <remarks>
/// <para>It used to be a private method of <see cref="TreeSitterNormalizer"/>. The outliner needs the
/// same pairs plus four more, and a second private table would be two lists of which native library
/// holds which language — the drift <c>KeptGrammarsTests</c> exists to catch in the publish, caught
/// here one step earlier (plan §4.7).</para>
/// <para><b>Two maps over one table, because the two interfaces disagree about one file on purpose.</b>
/// The collector reads <c>.tsx</c> as <see cref="SourceLanguage.TypeScript"/> and that behaviour is
/// the corpus's to change, not this story's; the outliner reads it as <see cref="OutlineLanguage.Tsx"/>
/// with its own grammar, because a JSX expression under the TypeScript grammar is an ERROR node.</para>
/// <para>Both switches are exhaustive with no default arm: CS8524 (an UNNAMED value, i.e. a cast
/// integer) is silenced, so adding a NAMED language without a row here is CS8509 — a build error,
/// not a silent <c>null</c>.</para>
/// </remarks>
internal static class Grammars
{
    internal static readonly Grammar CSharp = new("tree-sitter-c-sharp", "tree_sitter_c_sharp");
    internal static readonly Grammar TypeScript = new("tree-sitter-typescript", "tree_sitter_typescript");
    internal static readonly Grammar Tsx = new("tree-sitter-tsx", "tree_sitter_tsx");
    internal static readonly Grammar JavaScript = new("tree-sitter-javascript", "tree_sitter_javascript");
    internal static readonly Grammar Rust = new("tree-sitter-rust", "tree_sitter_rust");

    /// <summary>PHP, by <c>tree_sitter_php</c> — decided by LOADING both candidates, not by reading.</summary>
    /// <remarks>
    /// The upstream grammar documents two entry points: <c>tree_sitter_php</c> (PHP embedded in HTML,
    /// the way a real <c>.php</c> file is written — text before <c>&lt;?php</c> is a <c>text</c> node)
    /// and <c>tree_sitter_php_only</c> (no HTML). The <c>tree-sitter-php</c> library in
    /// TreeSitter.DotNet 1.3.0 exports only the first: loading <c>tree_sitter_php_only</c> throws
    /// <c>EntryPointNotFoundException</c> (measured 2026-09-25; pinned by
    /// <c>OutlinerGrammarTests.ThePhpEntryPointIsTheOneTheLibraryExports</c>).
    /// </remarks>
    internal static readonly Grammar Php = new("tree-sitter-php", "tree_sitter_php");

    internal static readonly Grammar Python = new("tree-sitter-python", "tree_sitter_python");

#pragma warning disable CS8524 // an unnamed (cast) value throws; a NAMED value without an arm is CS8509
    internal static Grammar? Of(SourceLanguage language) => language switch
    {
        SourceLanguage.Unsupported => null,
        SourceLanguage.CSharp => CSharp,
        SourceLanguage.TypeScript => TypeScript,
        SourceLanguage.JavaScript => JavaScript,
    };

    internal static Grammar? Of(OutlineLanguage language) => language switch
    {
        OutlineLanguage.Unsupported => null,
        OutlineLanguage.CSharp => CSharp,
        OutlineLanguage.TypeScript => TypeScript,
        OutlineLanguage.Tsx => Tsx,
        OutlineLanguage.JavaScript => JavaScript,
        OutlineLanguage.Rust => Rust,
        OutlineLanguage.Php => Php,
        OutlineLanguage.Python => Python,
    };
#pragma warning restore CS8524

    /// <summary>Loads a grammar, or says why it will not — the measurement behind every row above.</summary>
    /// <returns>Empty when it loads and parses; the exception's type and message otherwise.</returns>
    internal static string LoadFailure(Grammar grammar)
    {
        try
        {
            using var language = new Language(grammar.Library, grammar.EntryPoint);
            using var parser = new Parser(language);
            using var tree = parser.Parse(string.Empty);

            return tree is null ? "the parser returned no tree" : string.Empty;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException
                                       or InvalidOperationException)
        {
            return $"{e.GetType().Name}: {e.Message}";
        }
    }
}
