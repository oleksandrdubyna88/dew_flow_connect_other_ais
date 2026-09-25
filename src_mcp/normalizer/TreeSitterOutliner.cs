using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using CoaiMcp.Core.Outlining;
using TreeSitter;

namespace CoaiMcp.Normalizer;

/// <summary>
/// <see cref="ISourceOutliner"/> over tree-sitter: every declaration of a file, its signature up to its
/// body, and its lines — the one algorithm, driven by <see cref="OutlineTables"/>.
/// </summary>
/// <remarks>
/// <para><b>No body, by construction.</b> A signature is the source from the declaration's start to the
/// start of its <c>body</c> field (or the table's other cut fields), and a callable is never walked
/// into. A declaration with no body field — a field, a constant — is shown whole within the ceiling,
/// cut at the first nested body inside it, so <c>const router = make(() =&gt; { … })</c> stops at the
/// arrow. Nothing here decides what to leave out of a body; the outline never reaches one.</para>
/// <para><b>The walk goes THROUGH what the table does not name</b> — an <c>export</c> statement, a
/// top-level <c>if</c>, an ERROR node — because declarations live inside those. It never goes into a
/// node that is somebody's <c>body</c>, nor into an anonymous callable, which is what keeps a helper
/// declared inside a test's callback out of the file's outline.</para>
/// <para>Iterative, not recursive: a generated file can nest an expression thousands deep, and the
/// stdio server's stack is not the place to find out how deep.</para>
/// <para>Stateless; a parser per call, as in <see cref="TreeSitterNormalizer"/> and for the same reason:
/// a native handle shared between threads is a crash, not a contention problem.</para>
/// </remarks>
public sealed partial class TreeSitterOutliner : ISourceOutliner
{
    /// <summary>Kinds that hold a name when the grammar gives no <c>name</c> field.</summary>
    private static readonly string[] NameLike =
        ["identifier", "name", "type_identifier", "property_identifier", "field_identifier", "variable_name"];

    public OutlineLanguage LanguageOf(string path) => OutlineLanguages.Of(path);

    public SourceOutline Outline(OutlineLanguage language, string source)
    {
        // The ceiling FIRST, before the grammar is even looked at: a file over it is never parsed.
        var bytes = (long)Encoding.UTF8.GetByteCount(source);
        if (bytes > OutlineLimits.MaxInputBytes)
        {
            return SourceOutline.TooLarge(language, bytes);
        }

        if (Grammars.Of(language) is not { } grammar)
        {
            return SourceOutline.UnsupportedLanguage(language, bytes);
        }

        using var parsed = new Language(grammar.Library, grammar.EntryPoint);
        using var parser = new Parser(parsed);
        using var tree = parser.Parse(source);
        if (tree is null)
        {
            return SourceOutline.ParseFailed(language, bytes, 1.0);
        }

        var share = source.Length == 0 ? 0.0 : (double)ErrorChars(tree.RootNode) / source.Length;
        if (share > OutlineLimits.MaxErrorShare)
        {
            return SourceOutline.ParseFailed(language, bytes, share);
        }

        return SourceOutline.Of(language, bytes, new Walk(source, OutlineTables.For(language)).Run(tree.RootNode), share);
    }

    /// <summary>Characters under OUTERMOST ERROR nodes — only subtrees that report an error are entered.</summary>
    private static long ErrorChars(Node root)
    {
        var total = 0L;
        var pending = new Stack<Node>([root]);
        while (pending.TryPop(out var node))
        {
            if (!node.HasError)
            {
                continue;
            }

            if (node.IsError)
            {
                total += node.EndIndex - node.StartIndex;
                continue;
            }

            foreach (var child in node.Children)
            {
                pending.Push(child);
            }
        }

        return total;
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace { get; }

    /// <summary>One visit waiting on the walk's stack.</summary>
    /// <param name="Field">The field this node occupies in its parent, or empty.</param>
    /// <param name="Start">Where its signature starts — a wrapper's start when it is wrapped.</param>
    /// <param name="StartRow">0-based row of <paramref name="Start"/>.</param>
    private readonly record struct Visit(Node Node, string Field, int Depth, int Start, int StartRow);

    /// <summary>One outline being built: the source, the language's table, and the entries in file order.</summary>
    private sealed class Walk(string source, OutlineTable table)
    {
        private readonly ImmutableArray<OutlineEntry>.Builder _entries = ImmutableArray.CreateBuilder<OutlineEntry>();
        private readonly Stack<Visit> _pending = new();

        public ImmutableArray<OutlineEntry> Run(Node root)
        {
            PushChildren(root, root.Children.ToList(), depth: 0, wrapper: null);
            while (_pending.TryPop(out var visit))
            {
                Step(visit);
            }

            return _entries.ToImmutable();
        }

        /// <summary>Pushes children in REVERSE, so they pop — and are emitted — in file order.</summary>
        private void PushChildren(Node parent, List<Node> children, int depth, Visit? wrapper)
        {
            for (var index = children.Count - 1; index >= 0; index--)
            {
                var child = children[index];
                if (!child.IsNamed)
                {
                    continue;
                }

                var field = parent.GetFieldNameForChild(index) ?? string.Empty;
                _pending.Push(wrapper is { } outer
                    ? new Visit(child, field, depth, outer.Start, outer.StartRow)
                    : new Visit(child, field, depth, child.StartIndex, child.StartPosition.Row));
            }
        }

        private void Step(Visit visit)
        {
            var node = visit.Node;
            if (table.Opaque.Contains(node.Type))
            {
                return;
            }

            if (table.Wrappers.Contains(node.Type))
            {
                // `export`, `declare`, a decorator: not a declaration, but the one inside starts HERE.
                PushChildren(node, node.Children.ToList(), visit.Depth, visit);
                return;
            }

            if (table.Nodes.TryGetValue(node.Type, out var spec))
            {
                Emit(visit, spec);
                if (spec.Role is OutlineRole.Container && node.GetChildForField("body") is { } body)
                {
                    PushChildren(body, body.Children.ToList(), visit.Depth + 1, wrapper: null);
                }

                return;
            }

            // Somebody's body — a loop's, an `if`'s in a grammar that calls it that. Not ours to show.
            if (visit.Field == "body")
            {
                return;
            }

            PushChildren(node, node.Children.ToList(), visit.Depth, wrapper: null);
        }

        private void Emit(Visit visit, OutlineNode spec)
        {
            var node = visit.Node;
            var named = NameTarget(node, spec);

            _entries.Add(new OutlineEntry(
                visit.Depth,
                KindOf(spec, named),
                NameOf(node, named, spec),
                Signature(node, spec, visit.Start),
                visit.StartRow + 1,
                node.EndPosition.Row + 1));
        }

        /// <summary>A variable whose value is a function IS a function — <c>const f = () =&gt;</c>.</summary>
        private string KindOf(OutlineNode spec, Node named) =>
            table.FunctionValues.Count > 0
            && named.GetChildForField("value") is { } value
            && table.FunctionValues.Contains(value.Type)
                ? "function"
                : spec.Kind;

        /// <summary>The node that carries the name: the declaration itself, or a child down its name path.</summary>
        private static Node NameTarget(Node node, OutlineNode spec)
        {
            var target = node;
            foreach (var kind in spec.NamePath ?? [])
            {
                if (target.NamedChildren.FirstOrDefault(child => child.Type == kind) is not { } next)
                {
                    break;
                }

                target = next;
            }

            return target;
        }

        private static string NameOf(Node node, Node target, OutlineNode spec) => spec.Name switch
        {
            NameRule.None => string.Empty,
            NameRule.ImplTarget => ImplName(node),
            _ when spec.FixedName.Length > 0 => spec.FixedName,
            _ => Clean(
                target.GetChildForField(spec.NameField)?.Text
                ?? target.NamedChildren.FirstOrDefault(child => NameLike.Contains(child.Type))?.Text
                ?? string.Empty),
        };

        /// <summary>A Rust <c>impl</c> is named by what it implements: <c>Display for Point</c>, or <c>Point</c>.</summary>
        private static string ImplName(Node node)
        {
            var type = node.GetChildForField("type")?.Text ?? string.Empty;

            return node.GetChildForField("trait") is { } trait ? $"{Clean(trait.Text)} for {Clean(type)}" : Clean(type);
        }

        /// <summary>A name as a reader would write it: quotes off a module string, whitespace single.</summary>
        private static string Clean(string text)
        {
            var name = Whitespace.Replace(text, " ").Trim();

            return name.Length > 1 && name[0] is '"' or '\'' or '`' && name[^1] == name[0] ? name[1..^1] : name;
        }

        /// <summary>From <paramref name="start"/> to the first place a body begins, collapsed and capped.</summary>
        private string Signature(Node node, OutlineNode spec, int start)
        {
            var cut = node.EndIndex;
            foreach (var field in spec.CutAt)
            {
                if (node.GetChildForField(field) is { } part)
                {
                    cut = Math.Min(cut, part.StartIndex);
                }
            }

            if (spec.CutAtKind.Length > 0 && node.NamedChildren.FirstOrDefault(child => child.Type == spec.CutAtKind) is { } first)
            {
                cut = Math.Min(cut, first.StartIndex);
            }

            cut = NestedBodyStart(node, cut);

            return Capped(source, Math.Clamp(start, 0, source.Length), Math.Clamp(cut, 0, source.Length));
        }

        /// <summary>The start of the first body nested inside the signature's span — a lambda in an initializer.</summary>
        /// <remarks>Only nodes that START before the current cut are looked at, so this never reads past it.</remarks>
        private int NestedBodyStart(Node node, int cut)
        {
            var pending = new Stack<Node>(node.Children);
            while (pending.TryPop(out var child))
            {
                if (child.StartIndex >= cut)
                {
                    continue;
                }

                if (child.GetChildForField("body") is { } body && body.StartIndex < cut && body.StartIndex > node.StartIndex)
                {
                    cut = body.StartIndex;
                }
                else if (child.IsNamed && table.Opaque.Contains(child.Type))
                {
                    // An anonymous callable with no body field of its own: it starts a body.
                    cut = Math.Min(cut, child.StartIndex);
                    continue;
                }

                foreach (var grandchild in child.Children)
                {
                    pending.Push(grandchild);
                }
            }

            return cut;
        }
    }

    /// <summary>Collapses whitespace and applies the ceiling — reading at most what could be shown.</summary>
    /// <remarks>
    /// A constant that is a 500 KB array literal has no body field, so its "signature" is the whole
    /// node; collapsing all of it to keep 240 characters would be work for nothing. The raw read is
    /// bounded to sixteen times the ceiling, which only whitespace can fail to fill — and whatever was
    /// left unread is marked with the ellipsis like any cut.
    /// </remarks>
    private static string Capped(string source, int start, int cut)
    {
        var readTo = Math.Min(cut, start + (16 * OutlineLimits.MaxSignatureChars));
        var text = Whitespace.Replace(source[start..Math.Max(start, readTo)], " ").Trim().TrimEnd('{').TrimEnd();
        var max = OutlineLimits.MaxSignatureChars;
        if (text.Length <= max && readTo == cut)
        {
            return text;
        }

        var keep = Math.Min(text.Length, max - 1);
        if (keep > 0 && char.IsHighSurrogate(text[keep - 1]))
        {
            keep -= 1;
        }

        return string.Concat(text.AsSpan(0, keep), "…");
    }
}
