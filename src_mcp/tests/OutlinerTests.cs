using System.Text;
using CoaiMcp.Core.Outlining;
using CoaiMcp.Normalizer;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The feature reviewer's reader: every declaration of a file, with its signature and lines — and
/// never a line of a body.
/// </summary>
/// <remarks>
/// <para>The outline is D3's whole promise: a reviewer is sent the SHAPE of the change and asks for
/// code by name. A body leaking into it would be code sent without anybody asking, so the fixtures
/// mark every body line with a word (<c>secret…</c>) that must never appear in an outline — the
/// no-body property, checked per language against real grammar output rather than a mock.</para>
/// <para>The goldens were written BY HAND before the outliner existed (S1.3's RED step): they are
/// what a reviewer should read, decided first, not what the code happened to print.</para>
/// </remarks>
public sealed class OutlinerTests
{
    private readonly ISourceOutliner _outliner = new TreeSitterOutliner();

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "outline", name)).ReplaceLineEndings("\n");

    /// <summary>One file per language with bodies, plus the declaration-only files.</summary>
    public static TheoryData<string, OutlineLanguage> Goldens => new()
    {
        { "csharp.cs", OutlineLanguage.CSharp },
        { "abstract-members.cs", OutlineLanguage.CSharp },
        { "typescript.ts", OutlineLanguage.TypeScript },
        { "declarations.d.ts", OutlineLanguage.TypeScript },
        { "component.tsx", OutlineLanguage.Tsx },
        { "javascript.js", OutlineLanguage.JavaScript },
        { "rust.rs", OutlineLanguage.Rust },
        { "traits.rs", OutlineLanguage.Rust },
        { "php.php", OutlineLanguage.Php },
        { "contract.php", OutlineLanguage.Php },
        { "python.py", OutlineLanguage.Python },
        { "stub.pyi", OutlineLanguage.Python },
    };

    [Theory]
    [MemberData(nameof(Goldens))]
    public void EachLanguage_OutlinesToItsGolden(string fixture, OutlineLanguage language)
    {
        _outliner.LanguageOf(fixture).Should().Be(language, "the fixture's own extension names its language");

        var outline = _outliner.Outline(language, Fixture(fixture + ".txt"));

        outline.Status.Should().Be(OutlineStatus.Outlined, outline.Reason);
        outline.Render().Should().Be(Fixture(fixture + ".golden"));
    }

    /// <summary>No line unique to a body appears in any outline.</summary>
    /// <remarks>
    /// Every body line of every fixture carries a word starting <c>secret</c>, and no declaration
    /// does. Asserted on the RENDERED text and on each signature, so neither a signature that ran on
    /// into its body nor a stray body line rendered as an entry can pass.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Goldens))]
    public void NoLineOfABody_AppearsInTheOutline(string fixture, OutlineLanguage language)
    {
        var source = Fixture(fixture + ".txt");
        var bodyLines = source.Split('\n')
            .Where(line => line.Contains("secret", StringComparison.OrdinalIgnoreCase))
            .Select(line => line.Trim())
            .ToList();

        var outline = _outliner.Outline(language, source);

        outline.Entries.Should().NotBeEmpty("a property over an empty outline proves nothing");
        outline.Render().Should().NotContainEquivalentOf("secret");
        foreach (var entry in outline.Entries)
        {
            bodyLines.Should().NotContain(line => entry.Signature.Contains(line, StringComparison.Ordinal),
                $"`{entry.Signature}` must stop where its body starts");
        }
    }

    /// <summary>A declaration-only file outlines EVERY member — a .d.ts, a C# interface, a Python stub.</summary>
    [Theory]
    [InlineData("declarations.d.ts", OutlineLanguage.TypeScript,
        new[] { "shop", "Cart", "id", "add", "total", "open", "version", "Item", "helper", "Legacy", "constructor", "run", "create" })]
    [InlineData("abstract-members.cs", OutlineLanguage.CSharp,
        new[] { "Shop.Contracts", "IRepository", "Find", "Save", "Count", "Saved", "this", "Base", "Run", "Name" })]
    [InlineData("stub.pyi", OutlineLanguage.Python,
        new[] { "Cart", "items", "__init__", "add", "add", "total", "open_cart", "VERSION" })]
    [InlineData("traits.rs", OutlineLanguage.Rust,
        new[] { "Store", "Item", "LIMIT", "save", "count", "", "native_call" })]
    [InlineData("contract.php", OutlineLanguage.Php,
        new[] { "Store", "LIMIT", "save", "create", "Base", "run" })]
    public void ADeclarationOnlyFile_OutlinesEveryMember(string fixture, OutlineLanguage language, string[] names)
    {
        var outline = _outliner.Outline(language, Fixture(fixture + ".txt"));

        outline.Entries.Select(entry => entry.Name).Should().Equal(names);
    }

    /// <summary>The four special cases, each named by what a reader would otherwise lose.</summary>
    [Fact]
    public void APythonDecorator_BelongsToTheDefinitionItDecorates()
    {
        var entry = _outliner.Outline(OutlineLanguage.Python, "@cache\ndef f(x):\n    return secret(x)\n").Entries.Single();

        entry.Should().Be(new OutlineEntry(0, "def", "f", "@cache def f(x):", 1, 3));
    }

    [Fact]
    public void AConstArrowFunction_IsAFunction_NotAVariable()
    {
        var entry = _outliner.Outline(OutlineLanguage.TypeScript, "export const f = async (a: number) => {\n  secret(a);\n};\n")
            .Entries.Single();

        entry.Should().Be(new OutlineEntry(0, "function", "f", "export const f = async (a: number) =>", 1, 3));
    }

    [Fact]
    public void ARustImpl_IsNamedByWhatItImplements()
    {
        var entries = _outliner.Outline(OutlineLanguage.Rust, "impl fmt::Display for Point {\n    fn fmt(&self) {}\n}\nimpl Point {}\n")
            .Entries;

        entries.Select(entry => (entry.Kind, entry.Name)).Should().Equal(
            ("impl", "fmt::Display for Point"), ("fn", "fmt"), ("impl", "Point"));
    }

    /// <summary>
    /// Text that is not ASCII before a declaration must not shift its signature.
    /// </summary>
    /// <remarks>
    /// Raised at epic 1's code round (codex): tree-sitter counts BYTES, a C# string counts UTF-16 code
    /// units, and a signature sliced with one by the other is cut in the wrong place — silently, because
    /// every other fixture here is ASCII. Each language gets a comment with a two-byte, a three-byte and
    /// a four-byte character (é, 日, 😀) in front of the declaration.
    /// </remarks>
    [Theory]
    [InlineData(OutlineLanguage.CSharp, "// café 日本 😀\nclass Foo { void Bar(int x) { secret(); } }\n", "void Bar(int x)")]
    [InlineData(OutlineLanguage.TypeScript, "// café 日本 😀\nfunction bar(x: number) { secret(); }\n", "function bar(x: number)")]
    [InlineData(OutlineLanguage.JavaScript, "// café 日本 😀\nfunction bar(x) { secret(); }\n", "function bar(x)")]
    [InlineData(OutlineLanguage.Rust, "// café 日本 😀\nfn bar(x: i32) { secret(); }\n", "fn bar(x: i32)")]
    [InlineData(OutlineLanguage.Python, "# café 日本 😀\ndef bar(x):\n    return secret(x)\n", "def bar(x):")]
    [InlineData(OutlineLanguage.Php, "<?php // café 日本 😀\nfunction bar($x) { return secret(); }\n", "function bar($x)")]
    public void TextThatIsNotAscii_BeforeADeclaration_DoesNotShiftItsSignature(
        OutlineLanguage language, string source, string expected)
    {
        var outline = _outliner.Outline(language, source);

        outline.Status.Should().Be(OutlineStatus.Outlined);
        outline.Entries.Select(entry => entry.Signature).Should().Contain(expected,
            "byte offsets from tree-sitter must never be used as string indices");
    }

    [Fact]
    public void PhpTextOutsideItsTags_IsNotAParseFailure()
    {
        var outline = _outliner.Outline(OutlineLanguage.Php, "<h1>Title</h1>\n<?php function f() { return 1; } ?>\n<p>more</p>\n");

        outline.Status.Should().Be(OutlineStatus.Outlined, "HTML around PHP is how a .php file is written");
        outline.Entries.Single().Signature.Should().Be("function f()");
    }

    /// <summary>A signature has a ceiling, so one attribute wall cannot eat a budget.</summary>
    [Fact]
    public void ALongSignature_IsCutAtTheCeiling()
    {
        var parameters = string.Join(", ", Enumerable.Range(0, 60).Select(i => $"int parameter{i}"));
        var entry = _outliner.Outline(OutlineLanguage.CSharp, $"class C {{ void M({parameters}) {{ }} }}").Entries[1];

        entry.Signature.Length.Should().Be(OutlineLimits.MaxSignatureChars);
        entry.Signature.Should().StartWith("void M(int parameter0").And.EndWith("…");
    }

    /// <summary>A file over the input ceiling is refused BEFORE any parse, with its size.</summary>
    /// <remarks>
    /// The source is made of one character the C# grammar cannot parse, so a parse would answer
    /// "parse failed" — the only way to get "too large" is to not have parsed at all.
    /// </remarks>
    [Fact]
    public void AFileOverTheCeiling_IsRefusedUnparsed()
    {
        var source = new string('\u0001', (int)OutlineLimits.MaxInputBytes + 1);

        var outline = _outliner.Outline(OutlineLanguage.CSharp, source);

        outline.Status.Should().Be(OutlineStatus.TooLarge);
        outline.Bytes.Should().Be(OutlineLimits.MaxInputBytes + 1);
        outline.Reason.Should().Contain("unsupported (too large)").And.Contain((OutlineLimits.MaxInputBytes + 1).ToString());
        outline.Entries.Should().BeEmpty();
    }

    /// <summary>The ceiling is on BYTES, as the plan states it — a multi-byte file hits it sooner.</summary>
    [Fact]
    public void TheCeilingCountsUtf8Bytes_NotCharacters()
    {
        var source = new string('—', (int)(OutlineLimits.MaxInputBytes / 3) + 1); // 3 bytes each

        _outliner.Outline(OutlineLanguage.CSharp, source).Status.Should().Be(OutlineStatus.TooLarge);
    }

    /// <summary>A file that is mostly ERROR is not outlined: its "declarations" would be guesses.</summary>
    [Fact]
    public void AFileThatDoesNotParse_IsReportedAsAParseFailure()
    {
        var source = "class C { void M() { } }\n" + string.Concat(Enumerable.Repeat("}}}{{{ )))((( ;;; <<< >>>\n", 20));

        var outline = _outliner.Outline(OutlineLanguage.CSharp, source);

        outline.Status.Should().Be(OutlineStatus.ParseFailed);
        outline.Reason.Should().StartWith("unsupported (parse failed)").And.Contain(Encoding.UTF8.GetByteCount(source).ToString());
    }

    /// <summary>A little breakage is NOT a failure: under the threshold, the file still outlines.</summary>
    [Fact]
    public void ASmallSyntaxError_StillOutlines()
    {
        var source = "public class C\n{\n    public void Good() { }\n    public void Broken( { }\n    public void AlsoGood() { }\n}\n";

        var outline = _outliner.Outline(OutlineLanguage.CSharp, source);

        outline.Status.Should().Be(OutlineStatus.Outlined, outline.Reason);
        outline.Entries.Select(entry => entry.Name).Should().Contain(["C", "Good", "AlsoGood"]);
        outline.ErrorShare.Should().BeInRange(double.Epsilon, OutlineLimits.MaxErrorShare, "the breakage is measured, and under the line");
    }

    [Theory]
    [InlineData("README.md")]
    [InlineData("build.yml")]
    [InlineData("Program.fs")]
    public void AnUnsupportedLanguage_IsANamedAnswer(string path)
    {
        var language = _outliner.LanguageOf(path);

        var outline = _outliner.Outline(language, "anything");

        language.Should().Be(OutlineLanguage.Unsupported);
        outline.Status.Should().Be(OutlineStatus.UnsupportedLanguage);
        outline.Reason.Should().Be("unsupported (language)");
    }

    [Theory]
    [InlineData("a.cs", OutlineLanguage.CSharp)]
    [InlineData("a.ts", OutlineLanguage.TypeScript)]
    [InlineData("a.d.ts", OutlineLanguage.TypeScript)]
    [InlineData("a.mts", OutlineLanguage.TypeScript)]
    [InlineData("a.tsx", OutlineLanguage.Tsx)]
    [InlineData("a.js", OutlineLanguage.JavaScript)]
    [InlineData("a.mjs", OutlineLanguage.JavaScript)]
    [InlineData("a.cjs", OutlineLanguage.JavaScript)]
    [InlineData("a.jsx", OutlineLanguage.JavaScript)]
    [InlineData("a.rs", OutlineLanguage.Rust)]
    [InlineData("a.php", OutlineLanguage.Php)]
    [InlineData("a.py", OutlineLanguage.Python)]
    [InlineData("a.pyi", OutlineLanguage.Python)]
    [InlineData("A.CS", OutlineLanguage.CSharp)]
    public void APathIsReadByItsExtension(string path, OutlineLanguage expected) =>
        _outliner.LanguageOf(path).Should().Be(expected);
}
