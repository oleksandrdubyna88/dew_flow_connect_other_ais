using System.Collections.Frozen;
using CoaiMcp.Core.Outlining;

namespace CoaiMcp.Normalizer;

/// <summary>What an outlined node is to the walk.</summary>
internal enum OutlineRole
{
    /// <summary>Emitted, and its <c>body</c> is walked for members one level deeper (a class, a module, an impl).</summary>
    Container,

    /// <summary>Emitted, and never entered: its body is exactly what an outline must not contain.</summary>
    Callable,

    /// <summary>Emitted whole (within the signature ceiling), and never entered: a field, a constant, a type alias.</summary>
    Declaration,
}

/// <summary>How the name of a declaration is found when the grammar gives it no <c>name</c> field.</summary>
internal enum NameRule
{
    /// <summary>The <c>name</c> field (or <see cref="OutlineNode.NameField"/>), else a name-like child.</summary>
    Field,

    /// <summary>Rust's <c>impl</c>: <c>{trait} for {type}</c>, or just <c>{type}</c> — it has no name of its own.</summary>
    ImplTarget,

    /// <summary>No name: the signature says everything (an <c>extern "C"</c> block, a conversion operator).</summary>
    None,
}

/// <summary>One node kind the outline shows, and how.</summary>
/// <param name="Kind">The short word an entry carries — <c>class</c>, <c>method</c>, <c>fn</c>.</param>
/// <param name="CutAt">
/// Fields whose start ends the signature; the earliest present one wins. <c>body</c> for almost
/// everything; a C# property also stops at its <c>accessors</c> and its <c>value</c>, whose getters
/// and expression bodies are bodies by another name.
/// </param>
/// <param name="CutAtKind">A child KIND whose start ends the signature — a Rust macro's first rule.</param>
/// <param name="NamePath">Child kinds to descend through before reading the name (a C# field's declarator).</param>
/// <param name="NameField">The field that holds the name — <c>name</c>, or Python's <c>left</c>.</param>
/// <param name="FixedName">A name the grammar never spells: a C# indexer is <c>this</c>.</param>
internal sealed record OutlineNode(
    string Kind,
    OutlineRole Role,
    string[] CutAt,
    string CutAtKind = "",
    string[]? NamePath = null,
    string NameField = "name",
    string FixedName = "",
    NameRule Name = NameRule.Field);

/// <summary>One language's table: the kinds it outlines, and the two kinds of node the walk treats specially.</summary>
/// <param name="Wrappers">
/// Nodes that are not declarations themselves but lend their START to the one inside: TypeScript's
/// <c>export</c> and <c>declare</c>, Python's decorators. Without them a signature reads
/// <c>class Point:</c> where the file says <c>@dataclass class Point:</c>.
/// </param>
/// <param name="Opaque">
/// Anonymous callables — a lambda, an arrow function, a closure. Never outlined and never entered, so
/// a function declared inside a callback's body cannot surface as if it were a declaration of the file.
/// </param>
/// <param name="FunctionValues">
/// Values that make a variable a FUNCTION: TypeScript's <c>const f = () =&gt;</c> is a function by
/// every measure a reviewer cares about, and the table says so rather than calling it a variable.
/// </param>
internal sealed record OutlineTable(
    FrozenDictionary<string, OutlineNode> Nodes,
    FrozenSet<string> Wrappers,
    FrozenSet<string> Opaque,
    FrozenSet<string> FunctionValues);

/// <summary>
/// The per-language node kinds of the outline — DATA, one table per language, exhaustive.
/// </summary>
/// <remarks>
/// <para>Read off the grammars by parsing real files (the dumps behind S1.3), not typed from memory:
/// every kind here is one the grammar in TreeSitter.DotNet 1.3.0 actually produces. A kind that is not
/// listed is walked THROUGH — its children may still be declarations — unless it is somebody's body.</para>
/// <para><b>No default arm.</b> <see cref="For"/> names every <see cref="OutlineLanguage"/>; CS8524 (a
/// cast integer) is silenced, so a NAMED language added without a table is CS8509, a build error —
/// the defect the normalizer's <c>_ =&gt;</c> fall-through to JavaScript kinds would have hidden.</para>
/// </remarks>
internal static class OutlineTables
{
    private static readonly string[] Body = ["body"];
    private static readonly string[] Whole = [];

    private static OutlineNode Container(string kind) => new(kind, OutlineRole.Container, Body);

    private static OutlineNode Callable(string kind, params string[] cutAt) =>
        new(kind, OutlineRole.Callable, cutAt.Length == 0 ? Body : cutAt);

    private static OutlineNode Declaration(string kind) => new(kind, OutlineRole.Declaration, Whole);

    private static FrozenSet<string> Set(params string[] kinds) => kinds.ToFrozenSet(StringComparer.Ordinal);

    internal static readonly OutlineTable CSharp = new(
        new Dictionary<string, OutlineNode>
        {
            ["namespace_declaration"] = Container("namespace"),
            ["file_scoped_namespace_declaration"] = Declaration("namespace"),
            ["class_declaration"] = Container("class"),
            ["struct_declaration"] = Container("struct"),
            ["record_declaration"] = Container("record"),
            ["interface_declaration"] = Container("interface"),
            ["enum_declaration"] = new("enum", OutlineRole.Declaration, Body),
            ["delegate_declaration"] = Declaration("delegate"),
            ["method_declaration"] = Callable("method"),
            ["constructor_declaration"] = Callable("constructor"),
            ["destructor_declaration"] = Callable("destructor"),
            ["operator_declaration"] = Callable("operator") with { Name = NameRule.None },
            ["conversion_operator_declaration"] = Callable("operator") with { Name = NameRule.None },
            ["local_function_statement"] = Callable("function"),
            ["property_declaration"] = Callable("property", "accessors", "value"),
            ["indexer_declaration"] = Callable("indexer", "accessors", "value") with { FixedName = "this" },
            ["event_declaration"] = Callable("event", "accessors"),
            ["field_declaration"] = Declaration("field") with { NamePath = ["variable_declaration", "variable_declarator"] },
            ["event_field_declaration"] = Declaration("event") with { NamePath = ["variable_declaration", "variable_declarator"] },
        }.ToFrozenDictionary(StringComparer.Ordinal),
        Set(),
        Set("lambda_expression", "anonymous_method_expression"),
        Set());

    /// <summary>TypeScript — and TSX, whose grammar is TypeScript's plus JSX and names declarations the same.</summary>
    internal static readonly OutlineTable TypeScript = new(
        new Dictionary<string, OutlineNode>
        {
            ["class_declaration"] = Container("class"),
            ["abstract_class_declaration"] = Container("class"),
            ["interface_declaration"] = Container("interface"),
            ["internal_module"] = Container("namespace"),
            ["module"] = Container("module"),
            ["enum_declaration"] = new("enum", OutlineRole.Declaration, Body),
            ["type_alias_declaration"] = Declaration("type"),
            ["function_declaration"] = Callable("function"),
            ["generator_function_declaration"] = Callable("function"),
            ["function_signature"] = Callable("function"),
            ["method_definition"] = Callable("method"),
            ["method_signature"] = Callable("method"),
            ["abstract_method_signature"] = Callable("method"),
            ["construct_signature"] = Callable("constructor") with { Name = NameRule.None },
            ["call_signature"] = Callable("call") with { Name = NameRule.None },
            ["index_signature"] = Declaration("index") with { Name = NameRule.None },
            ["public_field_definition"] = Declaration("field"),
            ["property_signature"] = Declaration("field"),
            ["lexical_declaration"] = Declaration("variable") with { NamePath = ["variable_declarator"] },
            ["variable_declaration"] = Declaration("variable") with { NamePath = ["variable_declarator"] },
        }.ToFrozenDictionary(StringComparer.Ordinal),
        Set("export_statement", "ambient_declaration"),
        Set("arrow_function", "function_expression", "generator_function", "class"),
        Set("arrow_function", "function_expression", "generator_function"));

    internal static readonly OutlineTable JavaScript = new(
        new Dictionary<string, OutlineNode>
        {
            ["class_declaration"] = Container("class"),
            ["function_declaration"] = Callable("function"),
            ["generator_function_declaration"] = Callable("function"),
            ["method_definition"] = Callable("method"),
            ["field_definition"] = Declaration("field") with { NameField = "property" },
            ["lexical_declaration"] = Declaration("variable") with { NamePath = ["variable_declarator"] },
            ["variable_declaration"] = Declaration("variable") with { NamePath = ["variable_declarator"] },
        }.ToFrozenDictionary(StringComparer.Ordinal),
        Set("export_statement"),
        Set("arrow_function", "function_expression", "generator_function", "class"),
        Set("arrow_function", "function_expression", "generator_function"));

    internal static readonly OutlineTable Rust = new(
        new Dictionary<string, OutlineNode>
        {
            ["mod_item"] = Container("mod"),
            ["struct_item"] = Container("struct"),
            ["union_item"] = Container("union"),
            ["trait_item"] = Container("trait"),
            ["impl_item"] = Container("impl") with { Name = NameRule.ImplTarget },
            ["foreign_mod_item"] = Container("extern") with { Name = NameRule.None },
            ["enum_item"] = new("enum", OutlineRole.Declaration, Body),
            ["function_item"] = Callable("fn"),
            ["function_signature_item"] = Callable("fn"),
            ["const_item"] = Declaration("const"),
            ["static_item"] = Declaration("static"),
            ["type_item"] = Declaration("type"),
            ["associated_type"] = Declaration("type"),
            ["field_declaration"] = Declaration("field"),
            ["macro_definition"] = new("macro", OutlineRole.Declaration, Whole, CutAtKind: "macro_rule"),
        }.ToFrozenDictionary(StringComparer.Ordinal),
        Set(),
        Set("closure_expression"),
        Set());

    /// <summary>PHP, read with the <c>php</c> entry point: HTML around the tags is <c>text</c>, not an error.</summary>
    internal static readonly OutlineTable Php = new(
        new Dictionary<string, OutlineNode>
        {
            ["namespace_definition"] = Container("namespace"),
            ["class_declaration"] = Container("class"),
            ["interface_declaration"] = Container("interface"),
            ["trait_declaration"] = Container("trait"),
            ["enum_declaration"] = Container("enum"),
            ["function_definition"] = Callable("function"),
            ["method_declaration"] = Callable("method"),
            ["property_declaration"] = Declaration("property") with { NamePath = ["property_element"] },
            ["const_declaration"] = Declaration("const") with { NamePath = ["const_element"] },
            ["enum_case"] = Declaration("case"),
        }.ToFrozenDictionary(StringComparer.Ordinal),
        Set(),
        Set("anonymous_function", "arrow_function", "anonymous_class"),
        Set());

    internal static readonly OutlineTable Python = new(
        new Dictionary<string, OutlineNode>
        {
            ["class_definition"] = Container("class"),
            ["function_definition"] = Callable("def"),
            ["assignment"] = Declaration("variable") with { NameField = "left" },
        }.ToFrozenDictionary(StringComparer.Ordinal),
        Set("decorated_definition"),
        Set("lambda"),
        Set());

    private static readonly OutlineTable Nothing = new(
        FrozenDictionary<string, OutlineNode>.Empty, Set(), Set(), Set());

#pragma warning disable CS8524 // an unnamed (cast) value throws; a NAMED language without a table is CS8509
    internal static OutlineTable For(OutlineLanguage language) => language switch
    {
        OutlineLanguage.Unsupported => Nothing,
        OutlineLanguage.CSharp => CSharp,
        OutlineLanguage.TypeScript => TypeScript,
        OutlineLanguage.Tsx => TypeScript,
        OutlineLanguage.JavaScript => JavaScript,
        OutlineLanguage.Rust => Rust,
        OutlineLanguage.Php => Php,
        OutlineLanguage.Python => Python,
    };
#pragma warning restore CS8524
}
