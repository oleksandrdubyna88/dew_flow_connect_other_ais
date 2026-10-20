using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace CoaiMcp.Tests;

/// <summary>One test-source class, as the structural scans see it.</summary>
/// <param name="File">The source file's name.</param>
/// <param name="Name">The class name.</param>
/// <param name="IsConcrete">Neither <c>abstract</c> nor <c>static</c>: the classes xUnit can run, and so the ones a scan judges.</param>
/// <param name="Bases">The identifiers of its base list — base class and interfaces alike; only the ones that name another class here matter.</param>
/// <param name="Collections">The names in its own <c>[Collection("…")]</c> attributes.</param>
/// <param name="DefinesSerialCollection">The name it defines with <c>[CollectionDefinition("…", DisableParallelization = true)]</c>, empty when it defines none.</param>
/// <param name="Code">Its code: no comments, no string contents, the bodies of classes nested inside it INCLUDED — they run as part of it.</param>
/// <param name="IsNested">Declared inside another class: not judged on its own, its code is its outer class's.</param>
/// <param name="IsStatic">Declared <c>static</c>: not a class xUnit runs, so no collection places it — <see cref="StaticOffenders"/> judges it.</param>
internal sealed record TestClass(
    string File,
    string Name,
    bool IsConcrete,
    IReadOnlyList<string> Bases,
    IReadOnlyList<string> Collections,
    string DefinesSerialCollection,
    string Code,
    bool IsNested,
    bool IsStatic = false);

/// <summary>
/// The classes of <c>src_mcp/tests</c>, read from their source — the one parser the structural scans of the test tree share
/// (<c>EveryWriterOfTheSteeringFileTests</c>, <c>EveryClassThatSetsTheProcessEnvironmentTests</c>).
/// </summary>
/// <remarks>
/// <para>Source, not reflection, because the question is what a class DOES in its own code, and a call site is not in the type
/// metadata. The lexer is <see cref="ProductionSources.Joined"/>'s, the one every census here uses: comments and string contents are
/// gone before anything is searched, so a sentence that names a call is not the call. Attributes are read from the view that keeps
/// string contents, because a collection's name is a string.</para>
/// <para><b>Judged on the CONCRETE class.</b> A base class's calls count through each class derived from it
/// (<see cref="Reaches"/>), and a collection counts only when the concrete class carries it itself — which is also what is written in
/// every derived class of <c>ConsultScenarioBase</c> today. It is a scan of the classes' own code: a call made from a static helper
/// is the helper's, and is not seen through the test class that calls the helper. A class NESTED in another is part of its outer
/// class (a private scoped-variable helper inside a test class runs inside that class's collection): its code counts for the outer
/// class and it is not judged alone.</para>
/// </remarks>
internal static class TestSourceIndex
{
    private static readonly Regex ClassDeclaration = new(
        @"(?<mods>(?:\b(?:public|internal|private|protected|sealed|abstract|static|partial|file|unsafe|new)\s+)*)\bclass\s+(?<name>\w+)\s*(?:<[^<>{};]*>)?\s*(?:\((?:[^()]|\((?:[^()]|\([^()]*\))*\))*\))?\s*(?::\s*(?<bases>[^{;]*?))?\s*(?:where\s+[^{;]+?)?\s*(?<open>[{;])",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    private static readonly Regex CollectionName = new(@"\[\s*Collection\s*\(\s*""(?<name>[^""]*)""\s*\)\s*\]", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static readonly Regex Definition = new(
        @"\[\s*CollectionDefinition\s*\(\s*""(?<name>[^""]*)""(?<args>[^\]]*)\]", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static readonly Regex StaticModifier = new(@"\bstatic\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static readonly Regex Abstractness = new(@"\b(?:abstract|static)\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static readonly Regex Identifier = new(@"\b[A-Za-z_]\w*\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static readonly Regex Serial = new(@"DisableParallelization\s*=\s*true", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static readonly ConditionalWeakTable<IReadOnlyList<TestClass>, ILookup<string, TestClass>> ByName = new();

    private static readonly Lazy<IReadOnlyList<TestClass>> AllClasses = new(Discover, isThreadSafe: true);

    /// <summary>Every class of every source file under <c>src_mcp/tests</c> (build output excluded).</summary>
    public static IReadOnlyList<TestClass> Classes => AllClasses.Value;

    /// <summary>The names of the collections whose definition says <c>DisableParallelization = true</c> — the ones that run alone.</summary>
    public static IReadOnlySet<string> SerialCollections(IEnumerable<TestClass> classes) =>
        classes.Select(c => c.DefinesSerialCollection).Where(name => name.Length > 0).ToHashSet(StringComparer.Ordinal);

    /// <summary>The classes a scan judges: top-level, neither abstract nor static — the ones xUnit runs.</summary>
    public static IEnumerable<TestClass> Concrete(IEnumerable<TestClass> classes) => classes.Where(c => c.IsConcrete && !c.IsNested);

    /// <summary>
    /// The top-level STATIC classes, other than <paramref name="allowed"/>, whose code matches <paramref name="what"/>: <c>file: class</c>.
    /// A static class is no test class, so no collection places it and the scans that judge concrete classes never see it — a helper that
    /// wrote the steering file or set the environment from there would run in whichever class called it.
    /// </summary>
    public static List<string> StaticOffenders(IEnumerable<TestClass> classes, Regex what, string allowed) =>
    [
        .. classes
            .Where(c => c.IsStatic && !c.IsNested && c.Name != allowed && what.IsMatch(c.Code))
            .Select(c => $"{c.File}: {c.Name}"),
    ];

    /// <summary>Whether the class's own attributes put it in one of the <paramref name="serial"/> collections.</summary>
    public static bool InASerialCollection(TestClass c, IReadOnlySet<string> serial) => c.Collections.Any(serial.Contains);

    /// <summary>Whether the code of <paramref name="c"/> or of any class it derives from (by name, within <paramref name="all"/>) matches <paramref name="pattern"/>.</summary>
    public static bool Reaches(TestClass c, IReadOnlyList<TestClass> all, Regex pattern) =>
        Lineage(c, all).Any(member => pattern.IsMatch(member.Code));

    /// <summary>
    /// The classes by name, built ONCE per list: a lineage is a handful of dictionary reads instead of a pass over every class
    /// per ancestor (the scans walk every class's lineage, which made the first version quadratic in the size of the test tree).
    /// </summary>
    private static ILookup<string, TestClass> NamesIn(IReadOnlyList<TestClass> all) =>
        ByName.GetValue(all, static list => list.ToLookup(c => c.Name, StringComparer.Ordinal));

    /// <summary>The class and every ancestor of it found in <paramref name="all"/> — each once, so a cycle cannot loop.</summary>
    private static IEnumerable<TestClass> Lineage(TestClass c, IReadOnlyList<TestClass> all)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<TestClass>([c]);
        while (queue.TryDequeue(out var next))
        {
            if (!seen.Add(next.File + "/" + next.Name))
            {
                continue;
            }

            yield return next;
            foreach (var parent in next.Bases.SelectMany(name => NamesIn(all)[name]))
            {
                queue.Enqueue(parent);
            }
        }
    }

    /// <summary>The classes of one source text — the seam the parser's own tests use.</summary>
    public static IReadOnlyList<TestClass> Parse(string file, string source)
    {
        var code = ProductionSources.Joined(source);
        var text = ProductionSources.JoinedWithText(source);
        var spans = Spans(code);

        return [.. spans.Select(span => ClassOf(file, span, spans, code, text))];
    }

    private sealed record Span(Match Declaration, int BodyStart, int BodyEnd);

    private static List<Span> Spans(string code) =>
    [
        .. ClassDeclaration.Matches(code)
            .Select(match => new Span(match, match.Index + match.Length, ClosedAt(match, code))),
    ];

    /// <summary>Where the class ends: its closing brace, or right after the declaration for <c>class Name;</c>, which has no body.</summary>
    private static int ClosedAt(Match declaration, string code) =>
        declaration.Groups["open"].Value == "{" ? ClosingBrace(code, declaration.Index + declaration.Length) : declaration.Index + declaration.Length;

    /// <summary>The index of the brace that closes the one just before <paramref name="from"/> — the end of the text when it never closes.</summary>
    private static int ClosingBrace(string code, int from)
    {
        var depth = 1;
        for (var at = from; at < code.Length; at++)
        {
            depth += DepthChange(code[at]);
            if (depth == 0)
            {
                return at;
            }
        }

        return code.Length;
    }

    /// <summary>How one character moves the brace depth: an opening brace goes one deeper, a closing one one shallower.</summary>
    private static int DepthChange(char c) => c switch { '{' => 1, '}' => -1, _ => 0 };

    private static TestClass ClassOf(string file, Span span, List<Span> all, string code, string text)
    {
        var name = span.Declaration.Groups["name"].Value;
        var header = HeaderOf(text, name);

        return new TestClass(
            file,
            name,
            IsConcrete: !Abstractness.IsMatch(span.Declaration.Groups["mods"].Value),
            Bases: [.. Identifier.Matches(span.Declaration.Groups["bases"].Value).Select(m => m.Value)],
            Collections: [.. CollectionName.Matches(header).Select(m => m.Groups["name"].Value)],
            DefinesSerialCollection: SerialDefinition(header),
            Code: code[span.BodyStart..span.BodyEnd],
            IsNested: all.Any(other => other != span && other.BodyStart < span.Declaration.Index && span.BodyEnd <= other.BodyEnd),
            IsStatic: StaticModifier.IsMatch(span.Declaration.Groups["mods"].Value));
    }

    /// <summary>What stands between the previous statement or brace and <c>class Name</c>: the attributes and modifiers.</summary>
    private static string HeaderOf(string text, string name)
    {
        var at = Regex.Match(text, $@"\bclass\s+{Regex.Escape(name)}\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (!at.Success)
        {
            return string.Empty;
        }

        var boundary = new[] { text.LastIndexOf(';', at.Index), text.LastIndexOf('}', at.Index), text.LastIndexOf('{', at.Index) }.Max();

        return text[(boundary + 1)..at.Index];
    }

    private static string SerialDefinition(string header)
    {
        var match = Definition.Match(header);

        return match.Success && Serial.IsMatch(match.Groups["args"].Value) ? match.Groups["name"].Value : string.Empty;
    }

    private static IReadOnlyList<TestClass> Discover()
    {
        var tests = Path.Combine(RepoPaths.Root(), "src_mcp", "tests");
        var bin = $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}";
        var obj = $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}";

        return [.. Directory.EnumerateFiles(tests, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains(bin, StringComparison.Ordinal) && !file.Contains(obj, StringComparison.Ordinal))
            .SelectMany(file => Parse(Path.GetFileName(file), File.ReadAllText(file)))];
    }
}
