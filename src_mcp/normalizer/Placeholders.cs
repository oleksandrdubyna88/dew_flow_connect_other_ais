using CoaiMcp.Core.Normalising;
using TreeSitter;

namespace CoaiMcp.Normalizer;

/// <summary>
/// Gives every name we chose a number, and gives the same name the same number every time.
/// </summary>
/// <remarks>
/// <para><b>First appearance decides the number</b>, per skeleton and per kind. That is what makes two
/// methods differing only in their names normalise to identical text — the property the corpus rests
/// on, because a vector keyed on shape has to be blind to renaming or the same defect in two
/// repositories is two unrelated entries.</para>
/// <para>A name in <see cref="RuntimeVocabulary"/> is kept instead of numbered, UNLESS this method
/// declares it. That is the one place original text survives, and it is a closed list of words that
/// ship with the language — never a word of ours.</para>
/// </remarks>
/// <param name="declared">
/// Every name this method declares, collected in a pass of its own before any rewriting.
/// </param>
internal sealed class Placeholders(IReadOnlySet<string> vocabulary, IReadOnlySet<string> declared)
{
    private readonly Dictionary<string, string> _given = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);

    /// <summary>What this identifier becomes: its own text if the runtime owns it, else a number.</summary>
    /// <remarks>
    /// <para><b>The vocabulary protects a REFERENCE, never a DECLARATION.</b> `GetOrAdd` is a runtime
    /// name when something calls it on a concurrent dictionary, and it is OUR name when it is the
    /// method being declared — you cannot declare a name the runtime owns.</para>
    /// <para><b>And a name this method declares is ours EVERYWHERE in it</b>, not only where it is
    /// declared. `function tally(entries)` renamed the parameter and then kept `entries` in the body,
    /// because `entries` is also `Object.entries` — one name, two spellings, in a skeleton whose whole
    /// job is to be comparable. Hence <paramref name="declared"/>, gathered in a first pass: a rule
    /// that depended on which occurrence the walker met first would be a rule about tree order.</para>
    /// </remarks>
    internal string For(Node node)
    {
        var text = node.Text;
        if (!declared.Contains(text) && vocabulary.Contains(text))
        {
            return text;
        }

        var kind = KindOf(node).Kind;
        var key = kind + ':' + text;
        if (_given.TryGetValue(key, out var already))
        {
            return already;
        }

        // Counted per kind, so the numbering reads as `var_1`, `var_2`, `method_1` rather than as one
        // run of numbers across three different things.
        var next = _counts.TryGetValue(kind, out var seen) ? seen + 1 : 1;
        _counts[kind] = next;
        var given = $"{kind}_{next}";
        _given[key] = given;

        return given;
    }

    /// <summary>Whether this identifier is a name the code under normalisation declares.</summary>
    /// <remarks>
    /// Static and public to the assembly because the FIRST pass needs it before any
    /// <see cref="Placeholders"/> exists — the set it gathers is what the constructor takes.
    /// </remarks>
    internal static bool IsDeclaredName(Node node) => KindOf(node).Declared;

    /// <summary>
    /// Whether a name names a type, a function, or a value — decided by the grammar, not by guessing.
    /// </summary>
    /// <remarks>
    /// <para>A <c>type_identifier</c> is a type in every grammar here. A name that a declaration owns
    /// is what that declaration declares, which is how a C# method's name is told from the variables
    /// around it — C# spells both <c>identifier</c>, so the node kind alone cannot separate them. A
    /// <c>property_identifier</c> is a member access, the closest thing to a method name in
    /// TypeScript and JavaScript.</para>
    /// <para>Nothing here needs a semantic model, which is the whole reason a parser without one is
    /// enough for this job.</para>
    /// </remarks>
    private static (string Kind, bool Declared) KindOf(Node node)
    {
        if (node.Type == "type_identifier")
        {
            return ("type", false);
        }

        var parent = node.Parent;
        if (parent is not null && DeclaresIt(parent, node))
        {
            return (Declared(parent.Type), true);
        }

        // A member access names something on another object: `x.ContainsKey(...)`. Not declared —
        // we did not create the name — so the vocabulary still protects it if the runtime owns it.
        if (parent is not null && parent.Type == "member_access_expression" && IsNameOf(parent, node))
        {
            return ("method", false);
        }

        return (node.Type == "property_identifier" ? "method" : "var", false);
    }

    /// <summary>Whether <paramref name="parent"/> declares <paramref name="node"/> as its own name.</summary>
    /// <remarks>
    /// <para>Three shapes, because three grammars disagree about where a parameter's name lives. C#
    /// and TypeScript put it in a <c>name</c> or <c>pattern</c> field of a declaring node. JavaScript
    /// hangs the identifiers straight off <c>formal_parameters</c> with no field at all — so
    /// `function tally(entries)` left `entries` looking like a reference, and the vocabulary kept it,
    /// because `entries` is also `Object.entries`. A parameter named after a runtime member is exactly
    /// the case the declaration check exists for, and the published binary is where it surfaced.</para>
    /// </remarks>
    private static bool DeclaresIt(Node parent, Node node) =>
        parent.Type == "formal_parameters"
        || (Declares(parent.Type) && (IsNameOf(parent, node) || IsPatternOf(parent, node)));

    /// <summary>What a declaration declares: a type, a member, or a value.</summary>
    /// <remarks>
    /// A parameter and a variable declarator declare VALUES, and calling them `method_2` — which the
    /// first version did, because everything that was not a type fell through to "method" — makes a
    /// skeleton read as though a method took two other methods.
    /// </remarks>
    private static string Declared(string kind) =>
        kind.Contains("class", StringComparison.Ordinal)
        || kind.Contains("interface", StringComparison.Ordinal)
        || kind.Contains("struct", StringComparison.Ordinal)
        || kind.Contains("enum", StringComparison.Ordinal)
        || kind == "type_parameter"
            ? "type"
            : kind is "local_function_statement" or "method_declaration" or "constructor_declaration"
                or "destructor_declaration" or "operator_declaration" or "property_declaration"
                or "function_declaration" or "generator_function_declaration" or "method_definition"
                ? "method"
                : "var";

    /// <summary>
    /// The node kinds whose <c>name</c> or <c>pattern</c> field is a name THIS code chose.
    /// </summary>
    /// <remarks>
    /// Spelled out rather than matched on the substring "declaration", which is what the first version
    /// did and what let a leak through: a bare method — exactly what the collector extracts — parses
    /// as <c>global_statement → local_function_statement</c>, a kind containing neither "declaration"
    /// nor "definition". Its name went unrenamed, and because the fixture's method was called
    /// `GetOrAdd` the vocabulary then kept it verbatim.
    /// </remarks>
    private static bool Declares(string kind) =>
        kind is "local_function_statement" or "parameter" or "type_parameter" or "enum_member_declaration"
        || kind is "required_parameter" or "optional_parameter" or "formal_parameters"
        || kind.EndsWith("_parameter", StringComparison.Ordinal)
        || kind.EndsWith("_declaration", StringComparison.Ordinal)
        || kind.EndsWith("_definition", StringComparison.Ordinal)
        || kind.EndsWith("_declarator", StringComparison.Ordinal);

    private static bool IsPatternOf(Node parent, Node node) =>
        parent.GetChildForField("pattern") is { } bound && bound.Id == node.Id;

    private static bool IsNameOf(Node parent, Node node) =>
        parent.GetChildForField("name") is { } named && named.Id == node.Id;
}
