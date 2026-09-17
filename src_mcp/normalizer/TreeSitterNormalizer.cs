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

    public IReadOnlySet<string> KeywordsOf(SourceLanguage language)
    {
        if (Grammar(language) is not { } grammar)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        using var parsed = new Language(grammar.Library, grammar.Function);

        // ANONYMOUS symbols are tree-sitter's literal tokens: the keywords and the punctuation. The
        // grammar already knows them exactly, so reading them off it beats a hand-written list that
        // would be a worse second copy and would go stale the first time a grammar is updated.
        return new HashSet<string>(
            parsed.Symbols.Where(symbol => symbol.Type == SymbolType.Anonymous).Select(symbol => symbol.Name)
                .Concat(LiteralWords(language)),
            StringComparer.Ordinal);
    }

    /// <summary>Words of the language that some grammars model as NAMED nodes rather than tokens.</summary>
    /// <remarks>
    /// <c>true</c>, <c>false</c> and <c>null</c> are not reliably among the anonymous symbols: one
    /// grammar spells them as literal tokens and another as nodes of their own, and the second kind
    /// never appears in <see cref="Language.Symbols"/> as anonymous. The property test found them
    /// surviving into skeletons and reported them as leaks, which they are not — they are words of
    /// the language and carry nothing of ours.
    /// </remarks>
    private static string[] LiteralWords(SourceLanguage language) => language switch
    {
        // `_` is the discard. It is a name nobody chose and it carries nothing — the opposite of a
        // leak — so it is kept rather than numbered, which also stops `(_, i) =>` reading as if the
        // discard were a variable somebody meant to use.
        SourceLanguage.CSharp => ["true", "false", "null", "this", "base", "value", "default", "_"],
        SourceLanguage.Unsupported => [],
        _ => ["true", "false", "null", "undefined", "this", "super"],
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
                    node.Type,
                    node.StartPosition.Row + 1,
                    node.EndPosition.Row + 1,
                    node.Text,
                    NameOf(node));
            }
        }

        return null;
    }

    /// <summary>What a function node calls itself, or empty for one that has no name.</summary>
    private static string NameOf(Node node) =>
        node.GetChildForField("name") is { } named ? named.Text : string.Empty;

    public EnclosingSymbol? LocateNamed(SourceLanguage language, string source, string name)
    {
        if (Grammar(language) is not { } grammar || name.Length == 0)
        {
            return null;
        }

        using var parsed = new Language(grammar.Library, grammar.Function);
        using var parser = new Parser(parsed);
        using var tree = parser.Parse(source);

        return tree is null ? null : Named(tree.RootNode, FunctionKinds(language), name);
    }

    public int CountNamed(SourceLanguage language, string source, string name)
    {
        if (Grammar(language) is not { } grammar || name.Length == 0)
        {
            return 0;
        }

        using var parsed = new Language(grammar.Library, grammar.Function);
        using var parser = new Parser(parsed);
        using var tree = parser.Parse(source);

        return tree is null ? 0 : CountNamed(tree.RootNode, FunctionKinds(language), name);
    }

    private static int CountNamed(Node node, string[] kinds, string name)
    {
        var found = 0;
        foreach (var child in node.Children)
        {
            if (kinds.Contains(child.Type) && NameOf(child) == name)
            {
                found += 1;
            }

            found += CountNamed(child, kinds, name);
        }

        return found;
    }

    /// <summary>The first function of this name, anywhere in the tree.</summary>
    /// <remarks>
    /// FIRST rather than only, and safe ONLY because the caller refuses an ambiguous name before it
    /// asks: an overload set shares a name, and the first match is then a coin toss whose loss is a
    /// commit sha recorded as some other overload's fix. See <see cref="CountNamed"/>, which the
    /// collector consults first. The approximation was argued for on the plan round and the code
    /// round refused it, twice and independently — rightly.
    /// </remarks>
    private static EnclosingSymbol? Named(Node node, string[] kinds, string name)
    {
        foreach (var child in node.Children)
        {
            if (kinds.Contains(child.Type) && NameOf(child) == name)
            {
                return new EnclosingSymbol(
                    child.Type,
                    child.StartPosition.Row + 1,
                    child.EndPosition.Row + 1,
                    child.Text,
                    name);
            }

            if (Named(child, kinds, name) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    public string Normalise(SourceLanguage language, string source)
    {
        if (Grammar(language) is not { } grammar)
        {
            return string.Empty;
        }

        using var parsed = new Language(grammar.Library, grammar.Function);
        using var parser = new Parser(parsed);

        // A METHOD parses on its own; a CONSTRUCTOR does not, and neither does a property or a class
        // method lifted out of its type. The collector only ever hands over a bare member, so this is
        // the common case rather than an edge of it: parsed alone, `private Thing(int a)` becomes a
        // malformed tree whose parameters carry no `name` field, and their names go unrenamed —
        // found by the property test on this repository's own `DecisionAt.cs`, where `ordinal` and
        // `decision` survived into a skeleton.
        //
        // So: try it bare, and when the grammar itself says the tree is broken, parse it again inside
        // a type and take the wrapper off afterwards.
        using var bare = parser.Parse(source);
        var wrapped = bare is null || bare.RootNode.HasError;
        var text = wrapped ? Wrapper + "\n" + source + "\n}" : source;
        using var reparsed = wrapped ? parser.Parse(text) : null;

        if ((wrapped ? reparsed : bare)?.RootNode is not { } root)
        {
            return string.Empty;
        }

        // TWO passes, and the first one is not an optimisation. A name this method declares is ours
        // everywhere in it, including where the runtime happens to use the same word — and which
        // occurrence the walker meets first is a fact about tree order, not about the code. Gathering
        // the declared names before rewriting anything makes the answer independent of both.
        var declared = new HashSet<string>(StringComparer.Ordinal);
        CollectDeclared(root, declared);

        var rewrites = new List<Rewrite>();
        Collect(root, new Placeholders(RuntimeVocabulary.For(language), declared), rewrites);
        var skeleton = Rewriting.Apply(text, rewrites);

        return wrapped ? Unwrap(skeleton) : skeleton;
    }

    /// <summary>
    /// The smallest thing a member can legally live inside — the same word in all three languages.
    /// </summary>
    private const string Wrapper = "class W {";

    /// <summary>Takes the wrapper back off: its opening line and its closing brace.</summary>
    /// <remarks>
    /// The wrapper's own name is normalised with everything else, so it leaves no WORD behind — only
    /// a line, which this removes. Numbering shifts by one type against an unwrapped parse, and that
    /// is harmless because it shifts the same way for every member parsed the same way, which is what
    /// determinism actually requires.
    /// </remarks>
    private static string Unwrap(string skeleton)
    {
        var lines = skeleton.Split('\n').ToList();
        if (lines.Count > 0)
        {
            lines.RemoveAt(0);
        }

        if (lines.Count > 0 && lines[^1].Trim() == "}")
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return string.Join('\n', lines);
    }

    /// <summary>The first pass: every name this code declares, before anything is rewritten.</summary>
    private static void CollectDeclared(Node node, HashSet<string> declared)
    {
        foreach (var child in node.Children)
        {
            if (!child.Children.Any() && NamesSomething(child.Type) && Placeholders.IsDeclaredName(child))
            {
                declared.Add(child.Text);
            }

            CollectDeclared(child, declared);
        }
    }

    /// <summary>Walks the tree once, deciding what each leaf becomes.</summary>
    /// <remarks>
    /// Leaves only: a comment, a literal and an identifier are all leaves, and replacing a parent
    /// would throw away the structure the skeleton exists to keep.
    /// </remarks>
    private static void Collect(Node node, Placeholders placeholders, List<Rewrite> rewrites)
    {
        foreach (var child in node.Children)
        {
            if (Replacement(child, placeholders) is { } text)
            {
                rewrites.Add(new Rewrite(child.StartIndex, child.EndIndex, text));
                continue;
            }

            Collect(child, placeholders, rewrites);
        }
    }

    /// <summary>What a node becomes, or nothing when it is kept and descended into.</summary>
    private static string? Replacement(Node node, Placeholders placeholders)
    {
        var kind = node.Type;

        // An ANONYMOUS token is the language's own syntax, and tree-sitter names it by its own text:
        // the TypeScript type keyword `string` is a node whose kind IS "string". Without this guard
        // the literal check below rewrote a TYPE as `""`, so `Promise<string[]>` normalised to
        // `Promise<""[]>` and the skeleton lost the runtime type it exists to keep. Caught by running
        // the published binary, which is the only place all three grammars meet at once.
        if (kind == node.Text)
        {
            return null;
        }

        if (kind.Contains("comment", StringComparison.Ordinal))
        {
            // Comments say the most of all: a ticket number, a customer, a person's name.
            return string.Empty;
        }

        if (kind.Contains("string", StringComparison.Ordinal) || kind == "character_literal")
        {
            // The whole node, including an interpolated one's expressions: a domain leaks through a
            // string more often than through anything else, and half a string is not safer.
            return "\"\"";
        }

        if (kind is "integer_literal" or "real_literal" or "number")
        {
            return "0";
        }

        if (kind is "regex")
        {
            // A regex is a LITERAL, and it went through verbatim until this line. That is the same
            // leak a string would be and arguably a worse one: what people put in a pattern is
            // hostnames, internal URL shapes, ticket prefixes, a product name in a parser. The
            // whole node goes, flags included — `/x/g` left its `g` standing as a bare word, which
            // is how the property test noticed the pattern beside it had never been touched.
            //
            // `/(?:)/` rather than `//`, because two slashes begin a comment and a skeleton that
            // reads as one is a skeleton a person misreads. It is what an empty regex stringifies
            // to, it carries no word characters, and it keeps the shape of the call it sits in.
            return "/(?:)/";
        }

        return node.Children.Any() || !NamesSomething(kind) ? null : placeholders.For(node);
    }

    /// <summary>Whether a leaf is a NAME, which is the only thing that gets renamed.</summary>
    /// <remarks>
    /// Not just "contains identifier". A C# lambda written <c>f => f.Name</c> gives its parameter the
    /// kind <c>implicit_parameter</c>, which contains no such word — so it was skipped entirely while
    /// the <c>f</c> in the BODY, an ordinary identifier, was renamed. The skeleton then read
    /// <c>method_2(f => var_3.method_3)</c> and leaked a name. Found by the property test over this
    /// repository's own `AgentLog.cs`; a one-letter name is still a name.
    /// </remarks>
    private static bool NamesSomething(string kind) =>
        kind.Contains("identifier", StringComparison.Ordinal) || kind == "implicit_parameter";
}
