using CoaiMcp.Core.Feature;
using CoaiMcp.Core.Outlining;
using CoaiMcp.Normalizer;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A reviewer names a declaration the way it is written in the language — <c>Cart.Add</c>,
/// <c>Point::fmt</c>, <c>Shop.Orders.Cart.Add</c> — and the outline answers with that declaration's
/// lines, its overloads, or the names it does have.
/// </summary>
/// <remarks>
/// <para>The trial's reviewers asked with QUALIFIED names and were refused: the first lookup compared
/// the whole request against a bare entry name, so <c>Cart.Add</c> found nothing in a file that
/// declares <c>Add</c> inside <c>Cart</c>. This suite drives the real outliner over the golden
/// fixtures, so the chains it walks are the ones the reviewer will actually be shown.</para>
/// </remarks>
public sealed class ASymbolIsFoundByItsQualifiedNameTests
{
    private static readonly ISourceOutliner Outliner = new TreeSitterOutliner();

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "outline", name)).ReplaceLineEndings("\n");

    private static SourceOutline Outlined(string fixture)
    {
        var outline = Outliner.Outline(OutlineLanguages.Of(fixture), Fixture(fixture + ".txt"));
        outline.IsOutlined.Should().BeTrue(outline.Reason);

        return outline;
    }

    [Theory]
    [InlineData("Add")]
    [InlineData("Cart.Add")]
    [InlineData("Cart::Add")]
    [InlineData("Orders.Cart.Add")]
    [InlineData("Shop.Orders.Cart.Add")]
    [InlineData("Add<T>")]
    [InlineData("Add()")]
    [InlineData("Cart.Add(T item)")]
    [InlineData(" cart.add ")]
    public void ACSharpMethod_IsFoundUnderEverySpellingAReviewerUses(string symbol)
    {
        var found = SymbolLookup.Find(Outlined("csharp.cs"), symbol);

        var entry = found.Entries.Should().ContainSingle($"'{symbol}' names exactly one declaration").Subject;
        entry.Name.Should().Be("Add");
        (entry.StartLine, entry.EndLine).Should().Be((28, 35), "the golden's own lines");
        found.Total.Should().Be(1);
    }

    [Theory]
    [InlineData("Point::fmt", "fmt")]
    [InlineData("Display::fmt", "fmt")]
    [InlineData("fmt::Display::fmt", "fmt")]
    [InlineData("Wrapper::new", "new")]
    [InlineData("Wrapper<T>::new", "new")]
    [InlineData("geometry::distance", "distance")]
    public void ARustItem_IsFoundByItsPathOrByTheTypeItIsImplementedFor(string symbol, string name)
    {
        var found = SymbolLookup.Find(Outlined("rust.rs"), symbol);

        found.Entries.Should().ContainSingle().Which.Name.Should().Be(name);
    }

    [Theory]
    [InlineData("typescript.ts", "Loader.load", "load")]
    [InlineData("typescript.ts", "Internal.helper", "helper")]
    [InlineData("python.py", "Point.distance", "distance")]
    [InlineData("php.php", "InvoiceService::total", "total")]
    [InlineData("php.php", "App\\Billing\\InvoiceService::total", "total")]
    public void AMemberOfAContainer_IsFoundQualified_InEveryLanguage(string fixture, string symbol, string name)
    {
        var found = SymbolLookup.Find(Outlined(fixture), symbol);

        found.Entries.Should().NotBeEmpty($"'{symbol}' is declared in {fixture}");
        found.Entries.Should().OnlyContain(entry => entry.Name == name);
    }

    /// <summary>
    /// A container's own name serves the whole container — and a match INSIDE it of the same name
    /// (the constructor <c>Cart</c> inside class <c>Cart</c>) is not served twice, because the
    /// container's lines already carry it.
    /// </summary>
    [Fact]
    public void AContainer_IsItselfADeclaration_AndItsSameNamedMemberIsNotServedTwice()
    {
        var outline = Outlined("csharp.cs");
        outline.Entries.Count(entry => entry.Name == "Cart").Should().Be(2, "the class and its constructor share the name");

        var found = SymbolLookup.Find(outline, "Cart");

        var entry = found.Entries.Should().ContainSingle().Subject;
        (entry.StartLine, entry.EndLine).Should().Be((7, 46));
        found.Total.Should().Be(1, "what is inside a served span is not another overload");
    }

    /// <summary>
    /// A file-scoped namespace — <c>namespace Shop.Orders;</c>, <c>namespace App\Billing;</c> — is a
    /// declaration with no body, so the outline nests nothing under it; a qualified name that starts
    /// with it must still find the member it scopes.
    /// </summary>
    [Fact]
    public void AFileScopedNamespace_QualifiesEverythingAfterIt()
    {
        var outline = Outliner.Outline(OutlineLanguage.CSharp, "namespace Shop.Orders;\n\npublic sealed class Cart\n{\n    public void Add() { }\n}\n");
        outline.Entries[0].Depth.Should().Be(outline.Entries[1].Depth, "the fixture must really be file-scoped");

        var found = SymbolLookup.Find(outline, "Shop.Orders.Cart.Add");

        found.Entries.Should().ContainSingle().Which.Name.Should().Be("Add");
    }

    /// <summary>Overloads: every match up to the cap, in file order, and the total says how many there were.</summary>
    [Fact]
    public void Overloads_AreServedUpToTheCap_AndTheTotalSaysHowMany()
    {
        const string source = """
            public static class Maths
            {
                public static int Add(int a) => a;
                public static int Add(int a, int b) => a + b;
                public static int Add(int a, int b, int c) => a + b + c;
                public static int Add(int a, int b, int c, int d) => a + b + c + d;
                public static int Sub(int a) => a;
            }
            """;
        var outline = Outliner.Outline(OutlineLanguage.CSharp, source);

        var found = SymbolLookup.Find(outline, "Maths.Add");

        found.Total.Should().Be(4);
        found.Entries.Should().HaveCount(SourceBudget.MaxOverloads, "no more than the cap are served");
        found.Entries.Select(entry => entry.StartLine).Should().BeInAscendingOrder("file order, so the first three are the first three");
        found.Entries.Should().OnlyContain(entry => entry.Name == "Add");
    }

    /// <summary>An unknown symbol is a refusal that lists what the file DOES declare — up to twenty names, and how many there are.</summary>
    [Fact]
    public void AnUnknownSymbol_ListsTheNamesTheFileHas()
    {
        var found = SymbolLookup.Find(Outlined("csharp.cs"), "Remove");

        found.Entries.Should().BeEmpty();
        found.Total.Should().Be(0);
        found.Names.Should().StartWith(["Shop.Orders", "Cart", "_items", "Changed", "Count"]);
        found.Names.Should().Contain("Add").And.Contain("Line");
        found.Names.Should().OnlyHaveUniqueItems();
        found.Names.Length.Should().BeLessThanOrEqualTo(SourceBudget.NamesInARefusal);
    }

    [Fact]
    public void AFileWithMoreNamesThanTheRefusalShows_IsCutAtTheCap_AndSaysSo()
    {
        var source = string.Join('\n', Enumerable.Range(1, 30).Select(i => $"public static int F{i}() => {i};"));
        var outline = Outliner.Outline(OutlineLanguage.CSharp, "public static class Many\n{\n" + source + "\n}\n");

        var found = SymbolLookup.Find(outline, "Nope");

        found.Names.Should().HaveCount(SourceBudget.NamesInARefusal);
        found.NamesInFile.Should().Be(31, "the class and its thirty members");
    }

    /// <summary>A qualifier the file does not have is not silently dropped — `Other.Add` is not `Cart.Add`.</summary>
    [Fact]
    public void AWrongQualifier_FindsNothing()
    {
        var found = SymbolLookup.Find(Outlined("csharp.cs"), "Other.Add");

        found.Entries.Should().BeEmpty("the qualifier is part of the question");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("::")]
    [InlineData("()")]
    public void ASymbolThatNamesNothing_FindsNothing_AndStillListsTheNames(string symbol)
    {
        var found = SymbolLookup.Find(Outlined("csharp.cs"), symbol);

        found.Entries.Should().BeEmpty();
        found.Names.Should().NotBeEmpty();
    }
}
