using CoaiMcp.Core.Normalising;
using TreeSitter;

namespace CoaiMcp.Normalizer;

/// <summary>
/// <see cref="IAstNormalizer"/> over tree-sitter — the only place in this repository that knows the
/// parser exists.
/// </summary>
/// <remarks>
/// <para><b>Syntax, not semantics, and that is what makes one parser enough.</b> Finding the function
/// around a line and telling an identifier from the syntax around it need node kinds; neither needs a
/// type. That is why a grammar without a semantic model does this job for three languages, where
/// Roslyn would have done it for one.</para>
/// <para>Stateless, and a parser is built per call. A native library is loaded once by the operating
/// system and every later <c>new Language</c> finds it already mapped, so the cost is a handle rather
/// than a load — and a parser shared between threads would be a native handle shared between threads,
/// which is a crash rather than a contention problem.</para>
/// </remarks>
public sealed class TreeSitterNormalizer : IAstNormalizer
{
    /// <summary>
    /// The grammar for a language: a LIBRARY and an ENTRY POINT, which are not the same word.
    /// </summary>
    /// <remarks>
    /// The binding's one-argument constructor derives both from a single string — library
    /// <c>tree-sitter-{id}</c>, function <c>tree_sitter_{id}</c> — and therefore cannot name C# at
    /// all: that grammar is <c>tree-sitter-c-sharp</c> with an entry point of
    /// <c>tree_sitter_c_sharp</c>, and no one id produces both. Spelled out here so the pair is
    /// visibly deliberate rather than looking like a typo a year from now.
    /// </remarks>
    private static (string Library, string Function)? Grammar(SourceLanguage language) => language switch
    {
        SourceLanguage.CSharp => ("tree-sitter-c-sharp", "tree_sitter_c_sharp"),
        SourceLanguage.TypeScript => ("tree-sitter-typescript", "tree_sitter_typescript"),
        SourceLanguage.JavaScript => ("tree-sitter-javascript", "tree_sitter_javascript"),
        _ => null,
    };

    /// <summary>Which node kinds count as "a function" — the per-language table, as data.</summary>
    /// <remarks>
    /// A property counts, because a defect in a getter is a defect in a method that happens to have
    /// no parentheses; so does a local function, which is where a closure capture usually goes wrong.
    /// </remarks>
    private static string[] FunctionKinds(SourceLanguage language) => language switch
    {
        SourceLanguage.CSharp =>
        [
            "method_declaration", "constructor_declaration", "destructor_declaration",
            "operator_declaration", "local_function_statement", "property_declaration",
        ],
        _ =>
        [
            "function_declaration", "generator_function_declaration", "method_definition",
            "arrow_function", "function_expression",
        ],
    };

    /// <summary>
    /// TypeScript and TSX are one grammar file with two entry points; `.tsx` is read as TypeScript.
    /// </summary>
    /// <remarks>
    /// No `.tsx` appeared among the 462 measured candidates, so nothing is lost today by reading one
    /// with the TypeScript grammar rather than the TSX one — the two differ over JSX, and a file
    /// using it would simply fail to resolve a symbol and be recorded as a skip. Kept as one mapping
    /// until a `.tsx` candidate actually turns up.
    /// </remarks>
    public SourceLanguage LanguageOf(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".cs" => SourceLanguage.CSharp,
            ".ts" or ".tsx" => SourceLanguage.TypeScript,
            // `.mjs` and `.cjs` are 39 of 462 candidates — 8 % — and exactly what a plain `.js`
            // check drops. This repository's own build scripts are `.mjs`.
            ".js" or ".mjs" or ".cjs" => SourceLanguage.JavaScript,
            _ => SourceLanguage.Unsupported,
        };

    public EnclosingSymbol? Locate(SourceLanguage language, string source, int line)
    {
        if (Grammar(language) is not { } grammar || line < 1)
        {
            return null;
        }

        using var parsed = new Language(grammar.Library, grammar.Function);
        using var parser = new Parser(parsed);
        using var tree = parser.Parse(source);

        // A line past the end of the file is not an error: one of the measured candidates names
        // line 285 of a 251-line file, so the collector meets this on real data and records a skip.
        return tree is null ? null : Enclosing(tree.RootNode.GetDescendantForPosition(new Point(line - 1, 0)), language);
    }

    /// <summary>The nearest function at or above <paramref name="at"/>, or nothing.</summary>
    /// <remarks>
    /// tree-sitter finds the smallest node covering the position; walking UP from it to the first
    /// function is ours, and it is what makes the answer the method rather than the `if` inside it or
    /// the class around it.
    /// </remarks>
    private static EnclosingSymbol? Enclosing(Node? at, SourceLanguage language)
    {
        var kinds = FunctionKinds(language);
        for (var node = at; node is not null; node = node.Parent)
        {
            if (kinds.Contains(node.Type))
            {
                // Back to 1-based, which is how a finding names a line and how a person reads one.
                return new EnclosingSymbol(
                    node.Type, node.StartPosition.Row + 1, node.EndPosition.Row + 1, node.Text);
            }
        }

        return null;
    }
}
