using System.Collections.Immutable;
using CoaiMcp.Core.Outlining;

namespace CoaiMcp.Core.Feature;

/// <summary>
/// What an outline answers for a symbol a reviewer named: the declarations that match (up to the
/// overload cap), how many there were, and — when nothing matched — the names the file does have.
/// </summary>
/// <param name="Entries">The matching declarations in file order, at most <see cref="SourceBudget.MaxOverloads"/>.</param>
/// <param name="Total">How many declarations matched, cap or no cap — so a refusal can say "3 of 7 shown".</param>
/// <param name="Names">The file's distinct declared names in order, at most <see cref="SourceBudget.NamesInARefusal"/>; for the refusal.</param>
/// <param name="NamesInFile">How many distinct names the file declares, so the list can say it was cut.</param>
public sealed record SymbolMatch(
    ImmutableArray<OutlineEntry> Entries, int Total, ImmutableArray<string> Names, int NamesInFile)
{
    public bool IsEmpty => Entries.IsEmpty;
}

/// <summary>
/// Finds a declaration in an outline by the name a reviewer wrote — bare or QUALIFIED, in the
/// spelling of the language: <c>Cart.Add</c>, <c>Point::fmt</c>, <c>App\Billing\Invoice::total</c>,
/// <c>Shop.Orders.Cart.Add</c>.
/// </summary>
/// <remarks>
/// <para><b>Qualified, because that is how reviewers ask.</b> The feature-pack trial (2026-09-26) had
/// reviewers request <c>Type::member</c> and <c>Class.method</c> and be refused by a lookup that
/// compared the whole request against a bare entry name. So the request is split into segments, each
/// entry is given its chain of container names from the outline's depths, and a request matches
/// when its segments appear IN ORDER along that chain and its last segment is the entry's own name.
/// The order-not-adjacency rule is what lets <c>Orders.Cart.Add</c>, <c>Shop.Cart.Add</c> and
/// <c>Display::fmt</c> (the trait side of a Rust <c>impl Display for Point</c>) all find their
/// declaration, while <c>Other.Add</c> still finds nothing.</para>
/// <para>Generic arguments and a parameter list are ignored (<c>Add&lt;T&gt;</c>, <c>Add()</c>,
/// <c>Cart.Add(T item)</c> all mean <c>Add</c>); an exact-case match is tried first and a
/// case-insensitive one only when it found nothing. Pure — an outline in, an answer out — so every
/// spelling is tested without git.</para>
/// </remarks>
public static class SymbolLookup
{
    /// <summary>What separates the parts of a qualified name: C#/TS/Python dots, Rust/PHP <c>::</c>, PHP namespace backslashes.</summary>
    private static readonly string[] PathSeparators = ["::", ".", "\\"];

    /// <summary>The word a Rust <c>impl</c> entry's name carries between its trait and its type.</summary>
    private const string ImplFor = " for ";

    public static SymbolMatch Find(SourceOutline outline, string symbol)
    {
        var names = NamesOf(outline.Entries);
        var query = Segments(symbol);
        var found = query.IsEmpty ? [] : Matching(outline.Entries, query);

        return new SymbolMatch(
            [.. found.Take(SourceBudget.MaxOverloads)],
            found.Length,
            [.. names.Take(SourceBudget.NamesInARefusal)],
            names.Length);
    }

    /// <summary>A name's segments: cut at a parameter list, split on every separator, generic arguments dropped.</summary>
    internal static ImmutableArray<string> Segments(string name)
    {
        var withoutParameters = CutAt(name, '(');

        return
        [
            .. withoutParameters
                .Split(PathSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(segment => CutAt(segment, '<').Trim())
                .Where(segment => segment.Length > 0),
        ];
    }

    private static string CutAt(string text, char stop)
    {
        var at = text.IndexOf(stop);

        return at < 0 ? text : text[..at];
    }

    /// <summary>An entry's own segments — both sides of a Rust <c>Trait for Type</c>, in that order.</summary>
    private static ImmutableArray<string> OwnSegments(string name) =>
        [.. name.Split(ImplFor, StringSplitOptions.RemoveEmptyEntries).SelectMany(part => (IEnumerable<string>)Segments(part))];

    private static ImmutableArray<OutlineEntry> Matching(ImmutableArray<OutlineEntry> entries, ImmutableArray<string> query)
    {
        var chains = Chains(entries);
        var exact = Outermost([.. chains.Where(one => Matches(one.Chain, query, StringComparison.Ordinal)).Select(one => one.Entry)]);

        return exact.IsEmpty
            ? Outermost([.. chains.Where(one => Matches(one.Chain, query, StringComparison.OrdinalIgnoreCase)).Select(one => one.Entry)])
            : exact;
    }

    /// <summary>
    /// The matches that are not inside another match: a class <c>Cart</c> and its constructor
    /// <c>Cart</c> both answer to the name, and the class's lines already carry the constructor's.
    /// </summary>
    private static ImmutableArray<OutlineEntry> Outermost(ImmutableArray<OutlineEntry> found) =>
        [.. found.Where(entry => !found.Any(other => other != entry && Encloses(other, entry)))];

    private static bool Encloses(OutlineEntry outer, OutlineEntry inner) =>
        outer.StartLine <= inner.StartLine && inner.EndLine <= outer.EndLine && outer.Depth < inner.Depth;

    /// <summary>The kind word every language's table gives a namespace — the one declaration that scopes what FOLLOWS it when it has no body.</summary>
    private const string NamespaceKind = "namespace";

    /// <summary>
    /// Every entry with the segments of its containers (by depth) followed by its own — and, before
    /// them, the file-scoped namespace in force: <c>namespace Shop.Orders;</c> in C#,
    /// <c>namespace App\Billing;</c> in PHP, a declaration the outline nests nothing under.
    /// </summary>
    private static ImmutableArray<(OutlineEntry Entry, ImmutableArray<string> Chain)> Chains(ImmutableArray<OutlineEntry> entries)
    {
        var chains = ImmutableArray.CreateBuilder<(OutlineEntry, ImmutableArray<string>)>(entries.Length);
        var containers = new List<ImmutableArray<string>>();
        var fileScope = ImmutableArray<string>.Empty;
        foreach (var (entry, at) in entries.Select((one, at) => (one, at)))
        {
            TrimTo(containers, entry.Depth);
            var own = OwnSegments(entry.Name);
            chains.Add((entry, [.. fileScope, .. containers.SelectMany(segments => segments), .. own]));
            containers.Add(own);
            fileScope = IsFileScopedNamespace(entries, at) ? own : fileScope;
        }

        return chains.ToImmutable();
    }

    /// <summary>A namespace entry nothing is nested under: the next entry is not deeper, so it scopes by position rather than by body.</summary>
    private static bool IsFileScopedNamespace(ImmutableArray<OutlineEntry> entries, int at) =>
        entries[at].Kind == NamespaceKind && (at + 1 >= entries.Length || entries[at + 1].Depth <= entries[at].Depth);

    /// <summary>Keeps the containers ABOVE <paramref name="depth"/>, padding when the outline skipped a level.</summary>
    private static void TrimTo(List<ImmutableArray<string>> containers, int depth)
    {
        if (containers.Count > depth)
        {
            containers.RemoveRange(depth, containers.Count - depth);
        }

        while (containers.Count < depth)
        {
            containers.Add([]);
        }
    }

    /// <summary>
    /// The query's last segment IS the entry's last segment, and the rest of the query appears in
    /// order somewhere along the chain before it.
    /// </summary>
    private static bool Matches(ImmutableArray<string> chain, ImmutableArray<string> query, StringComparison comparison)
    {
        if (chain.IsEmpty || !string.Equals(chain[^1], query[^1], comparison))
        {
            return false;
        }

        return InOrder(chain[..^1], query[..^1], comparison);
    }

    private static bool InOrder(ImmutableArray<string> chain, ImmutableArray<string> query, StringComparison comparison)
    {
        var next = 0;
        foreach (var segment in chain)
        {
            next += next < query.Length && string.Equals(segment, query[next], comparison) ? 1 : 0;
        }

        return next == query.Length;
    }

    /// <summary>The distinct declared names, in file order — an entry the grammar gave no name is not one.</summary>
    private static ImmutableArray<string> NamesOf(ImmutableArray<OutlineEntry> entries) =>
        [.. entries.Select(entry => entry.Name).Where(name => name.Length > 0).Distinct(StringComparer.Ordinal)];
}
