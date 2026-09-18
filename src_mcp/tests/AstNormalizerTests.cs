using CoaiMcp.Core.Normalising;
using CoaiMcp.Normalizer;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Reading a method back out of a file, which is the first half of turning a finding into material.
/// </summary>
/// <remarks>
/// <para>A finding names a file and a line and nothing else — <c>Finding</c> has no symbol field, and
/// nothing in this product ever resolved that line back to source. The collector needs the METHOD
/// around it, because a corpus of three-line diff hunks teaches nothing about the shape of a defect.
/// </para>
/// <para>The fixtures are real source in each language rather than a minimal snippet: the thing under
/// test is a grammar, and a grammar is exactly what a toy input fails to exercise.</para>
/// </remarks>
public sealed class AstNormalizerTests
{
    private readonly IAstNormalizer _normalizer = new TreeSitterNormalizer();

    private const string CSharp = """
        using System.Collections.Generic;

        public sealed class Cache
        {
            private readonly Dictionary<string, int> _items = new();

            public int GetOrAdd(string key, int value)
            {
                if (!_items.ContainsKey(key))
                {
                    _items.Add(key, value);
                }

                return _items[key];
            }
        }
        """;

    private const string TypeScript = """
        const RETRIES = 3;

        export async function fetchAll(urls: string[]): Promise<string[]> {
          const out: string[] = [];
          for (const url of urls) {
            out.push(await fetch(url).then(r => r.text()));
          }
          return out;
        }
        """;

    [Theory]
    [InlineData("src/Panel.cs", SourceLanguage.CSharp)]
    [InlineData("src_vs_code/src/panelView.ts", SourceLanguage.TypeScript)]
    [InlineData("src/chat.tsx", SourceLanguage.TypeScript)]
    [InlineData("scripts/run-tests.mjs", SourceLanguage.JavaScript)]
    [InlineData("scripts/legacy.cjs", SourceLanguage.JavaScript)]
    [InlineData("scripts/build.js", SourceLanguage.JavaScript)]
    [InlineData(".github/workflows/ci.yml", SourceLanguage.Unsupported)]
    [InlineData("research/module_server.md", SourceLanguage.Unsupported)]
    [InlineData("Makefile", SourceLanguage.Unsupported)]
    public void APathIsReadOrItIsNot_ByItsExtensionAlone(string path, SourceLanguage expected) =>
        _normalizer.LanguageOf(path).Should().Be(expected);

    /// <summary>
    /// <c>.mjs</c> and <c>.cjs</c> are JavaScript, which a naive `.js` check would drop.
    /// </summary>
    /// <remarks>
    /// Measured, not guessed: they are 39 of the 462 candidates — 8 % of the corpus — and this
    /// repository's own build scripts are `.mjs`. Written as its own test because it is the one
    /// mapping in the table that somebody will "tidy up" one day.
    /// </remarks>
    [Fact]
    public void TheNodeExtensionsAreJavaScriptToo()
    {
        _normalizer.LanguageOf("a.mjs").Should().Be(SourceLanguage.JavaScript);
        _normalizer.LanguageOf("a.cjs").Should().Be(SourceLanguage.JavaScript);
    }

    [Fact]
    public void ALineInsideAMethod_FindsThatMethod_AndNotTheClassAroundIt()
    {
        var found = _normalizer.Locate(SourceLanguage.CSharp, CSharp, line: 10);

        found.Should().NotBeNull();
        found!.Kind.Should().Be("method_declaration", "the smallest function around the line, not the type");
        found.StartLine.Should().Be(7);
        found.EndLine.Should().Be(15);
        found.Source.Should().StartWith("public int GetOrAdd").And.EndWith("}");
        found.Source.Should().Contain("_items.Add(key, value);", "the whole method, not the line");
    }

    /// <summary>
    /// The same in TypeScript — and the modifier asymmetry, asserted rather than discovered later.
    /// </summary>
    /// <remarks>
    /// <para>A C# <c>method_declaration</c> INCLUDES its modifiers, so the skeleton of a C# method
    /// begins <c>public int GetOrAdd</c>. A TypeScript <c>function_declaration</c> does NOT include
    /// <c>export</c>: that keyword belongs to an <c>export_statement</c> wrapping it, so the skeleton
    /// begins <c>async function fetchAll</c>.</para>
    /// <para>Left as the grammar has it rather than climbing one more level to even it out. Nothing
    /// needs the two languages to agree — a vector is built per language and compared against its
    /// own — and `export` is not control flow, a synchronisation primitive, an await point or a
    /// runtime type, which is the list the skeleton exists to preserve. Written down because the
    /// difference looks like a bug the first time somebody reads two skeletons side by side.</para>
    /// </remarks>
    [Fact]
    public void ALineInsideAFunction_FindsItInTypeScriptToo_WithoutTheExportKeyword()
    {
        var found = _normalizer.Locate(SourceLanguage.TypeScript, TypeScript, line: 6);

        found.Should().NotBeNull();
        found!.Kind.Should().Be("function_declaration");
        found.Source.Should().StartWith("async function fetchAll", "`export` belongs to the statement above it")
            .And.Contain("await fetch(url)", "the await point, which the skeleton must keep")
            .And.EndWith("}");
    }

    /// <summary>
    /// A line inside no function is nothing, and that is an answer rather than a failure.
    /// </summary>
    /// <remarks>
    /// It is the <c>symbol_not_resolved</c> path, and it will be taken often: a finding's line is
    /// whatever the reviewing model wrote, and plenty of real findings point at a field, a using
    /// block, or a blank line. Measured at 98.6 % of reachable candidates landing inside the file at
    /// all — how many land inside a FUNCTION is what this makes countable.
    /// </remarks>
    [Theory]
    [InlineData(1, "a using directive")]
    [InlineData(5, "a field, outside every method")]
    [InlineData(17, "the closing brace of the type")]
    public void ALineInsideNoFunction_IsNothing_NotAGuess(int line, string what) =>
        _normalizer.Locate(SourceLanguage.CSharp, CSharp, line)
            .Should().BeNull($"line {line} is {what}");

    [Fact]
    public void ALanguageWeDoNotRead_IsNothing_RatherThanAParseOfTheWrongGrammar()
    {
        _normalizer.Locate(SourceLanguage.Unsupported, CSharp, line: 10).Should().BeNull();
    }

    // --------------------------------------------------------------------------------------------
    // The class around a function — one more step along the chain Locate already walks (story 2.3).
    // --------------------------------------------------------------------------------------------

    /// <summary>A method's class is the type around it, asked at the method's own first line.</summary>
    [Fact]
    public void TheClassAroundAMethod_IsNamed()
    {
        var method = _normalizer.Locate(SourceLanguage.CSharp, CSharp, line: 10)!;

        _normalizer.EnclosingType(SourceLanguage.CSharp, CSharp, method.StartLine).Should().Be("Cache");
    }

    /// <summary>
    /// A method in a NESTED class names the inner class, not the outer — the innermost type wins.
    /// </summary>
    /// <remarks>
    /// The walk goes UP from the line and stops at the first type it meets, which is what makes the
    /// answer the class the method is actually declared in rather than the file's top-level type.
    /// </remarks>
    [Fact]
    public void AMethodInANestedClass_NamesTheInnerClass()
    {
        const string nested = """
            public sealed class Outer
            {
                public int Field;

                public sealed class Inner
                {
                    public int Twice(int x)
                    {
                        return x * 2;
                    }
                }
            }
            """;

        var method = _normalizer.Locate(SourceLanguage.CSharp, nested, line: 9)!;
        method.Name.Should().Be("Twice", "the fixture must land inside the inner class's method");

        _normalizer.EnclosingType(SourceLanguage.CSharp, nested, method.StartLine).Should().Be("Inner");
    }

    /// <summary>A top-level function has no class, and none is guessed.</summary>
    [Fact]
    public void ATopLevelFunction_HasNoClass_AndNoneIsInvented()
    {
        var function = _normalizer.Locate(SourceLanguage.TypeScript, TypeScript, line: 6)!;
        function.Name.Should().Be("fetchAll");

        _normalizer.EnclosingType(SourceLanguage.TypeScript, TypeScript, function.StartLine)
            .Should().BeEmpty("a function outside every class has no class, and empty is the honest answer");
    }

    /// <summary>The same walk reads a TypeScript and a JavaScript class — the grammars share the node.</summary>
    [Theory]
    [InlineData(SourceLanguage.TypeScript, "class Totals {\n  private items: number[] = [];\n\n  add(x: number): void {\n    this.items.push(x);\n  }\n}\n")]
    [InlineData(SourceLanguage.JavaScript, "class Totals {\n  constructor() { this.items = []; }\n\n  add(x) {\n    this.items.push(x);\n  }\n}\n")]
    public void AClassMethodInTypeScriptOrJavaScript_NamesItsClass(SourceLanguage language, string source)
    {
        var method = _normalizer.Locate(language, source, line: 5)!;
        method.Name.Should().Be("add");

        _normalizer.EnclosingType(language, source, method.StartLine).Should().Be("Totals");
    }

    /// <summary>A language we do not read has no class node to walk to, and answers empty rather than a guess.</summary>
    [Fact]
    public void ALanguageWeDoNotRead_HasNoClass() =>
        _normalizer.EnclosingType(SourceLanguage.Unsupported, CSharp, line: 7).Should().BeEmpty();

    /// <summary>A line past the end of the file answers nothing instead of throwing.</summary>
    /// <remarks>
    /// One of the 462 candidates names a line past its file's end (`chatPresetsPanel.ts:285` of 251),
    /// so this is not a hypothetical: the collector meets it on real data and must record a skip.
    /// </remarks>
    [Fact]
    public void ALinePastTheEndOfTheFile_IsNothing_NotAThrow()
    {
        _normalizer.Locate(SourceLanguage.CSharp, CSharp, line: 9_999).Should().BeNull();
    }
}
