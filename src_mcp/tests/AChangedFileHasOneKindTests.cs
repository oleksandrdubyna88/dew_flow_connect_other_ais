using Xunit;
using CoaiMcp.Core.Context;
using CoaiMcp.Core.Outlining;
using FluentAssertions;

namespace CoaiMcp.Tests;

/// <summary>
/// The kind function of E2.S1 (§S1.3): five words, first match wins, decided on the NEW path alone — pure, no git.
/// </summary>
public sealed class AChangedFileHasOneKindTests
{
    /// <summary>§S1.3's table, row by row — the expected column is the specification's, never invented here.</summary>
    [Theory]
    // row 1, a segment (any case)
    [InlineData("src_mcp/tests/X.cs", "test")]
    [InlineData("tests/v2.Tests/Foo.cs", "test")]
    [InlineData("src/__tests__/a.js", "test")]
    [InlineData("Tests/Foo.cs", "test")]
    // row 1, the name
    [InlineData("src_vs_code/src/test/x.test.ts", "test")]
    [InlineData("src/Foo.spec.ts", "test")]
    [InlineData("src/FooTests.cs", "test")]
    [InlineData("scripts/test_runner.py", "test")]
    [InlineData("tools/test_helpers.ps1", "test")]
    // row 1 before 2 and 3
    [InlineData("tests/fixtures/data.json", "test")]
    [InlineData("tests/README.md", "test")]
    // row 2, the name
    [InlineData("research/x.md", "docs")]
    [InlineData("todo/README.md", "docs")]
    [InlineData("README.md", "docs")]
    [InlineData("src/notes.TXT", "docs")]
    [InlineData("CodeMetricsConfig.txt", "docs")]
    // row 2, a segment, before 3
    [InlineData("docs/openapi.json", "docs")]
    [InlineData("src_vs_code/docs/x.png", "docs")]
    // row 3, the name
    [InlineData("Directory.Packages.props", "config")]
    [InlineData("src_mcp/src/CoaiMcp.csproj", "config")]
    [InlineData("dew_flow_connect_other_ais.slnx", "config")]
    [InlineData("appsettings.json", "config")]
    [InlineData("Cargo.toml", "config")]
    [InlineData(".github/workflows/ci.yml", "config")]
    [InlineData("web.config", "config")]
    // row 3, a dotfile
    [InlineData(".gitignore", "config")]
    [InlineData(".editorconfig", "config")]
    [InlineData("src/.eslintrc.json", "config")]
    // row 4
    [InlineData("src/a.cs", "code")]
    [InlineData("b.ts", "code")]
    [InlineData("c.tsx", "code")]
    [InlineData("d.mjs", "code")]
    [InlineData("e.rs", "code")]
    [InlineData("f.php", "code")]
    [InlineData("g.pyi", "code")]
    [InlineData("h.d.ts", "code")]
    [InlineData("src/Foo.Tests/Bar.cs", "code")]
    [InlineData("src/testing/Helper.cs", "code")]
    [InlineData("contest/x.cs", "code")]
    [InlineData("latest.cs", "code")]
    // row 5
    [InlineData("x.ps1", "unknown")]
    [InlineData("x.razor", "unknown")]
    [InlineData("x.sh", "unknown")]
    [InlineData("x.html", "unknown")]
    [InlineData("x.css", "unknown")]
    [InlineData("x.xml", "unknown")]
    [InlineData("x.sln", "unknown")]
    [InlineData("logo.png", "unknown")]
    [InlineData("bun.lockb", "unknown")]
    [InlineData(".agents/conventions", "unknown")]
    [InlineData("src/test", "unknown")]
    public void EveryPathInTheTable_HasTheKindTheTableSays(string path, string kind) =>
        ChangeKind.Of(path).Should().Be(kind, $"{path} is a row of the specification's table");

    [Fact]
    public void TheFirstMatchingRowWins_TestBeforeDocsBeforeConfigBeforeCode()
    {
        ChangeKind.Of("tests/fixtures/data.json").Should().Be(ChangeKind.Test, "row 1 holds before row 3");
        ChangeKind.Of("docs/openapi.json").Should().Be(ChangeKind.Docs, "row 2 holds before row 3");
        ChangeKind.Of("tests/README.md").Should().Be(ChangeKind.Test, "row 1 holds before row 2");
    }

    [Fact]
    public void CodeIsExactlyWhatTheOutlinerMaps_NeverASecondList()
    {
        string[] extensions = [".cs", ".ts", ".mts", ".cts", ".tsx", ".js", ".mjs", ".cjs", ".jsx", ".rs", ".php", ".py", ".pyi"];
        var mapped = extensions.Where(e => OutlineLanguages.Of($"src/x{e}") != OutlineLanguage.Unsupported).ToList();

        Enum.GetValues<OutlineLanguage>().Where(l => l != OutlineLanguage.Unsupported)
            .Should().OnlyContain(
                l => mapped.Any(e => OutlineLanguages.Of($"src/x{e}") == l),
                "every language the outliner maps has a path here — a new one must be added to this test");

        foreach (var extension in mapped)
        {
            ChangeKind.Of($"src/x{extension}").Should().Be(ChangeKind.Code, extension);
        }

        foreach (var extension in new[] { ".ps1", ".razor", ".sh", ".html", ".css" })
        {
            ChangeKind.Of($"src/x{extension}").Should().Be(ChangeKind.Unknown, extension);
        }
    }

    [Theory]
    [InlineData("Tests/Foo.cs", "test")]
    [InlineData("README.MD", "docs")]
    [InlineData("X.JSON", "config")]
    [InlineData("SRC/TEST/a.ts", "test")]
    public void TheComparisonIgnoresCase(string path, string kind) =>
        ChangeKind.Of(path).Should().Be(kind);

    [Fact]
    public void ASegmentIsAWholeDirectoryName_AtAnyDepth()
    {
        ChangeKind.Of("contest/x.cs").Should().Be(ChangeKind.Code);
        ChangeKind.Of("src/testing/x.cs").Should().Be(ChangeKind.Code);
        ChangeKind.Of("a/b/c/tests/d.cs").Should().Be(ChangeKind.Test);
        ChangeKind.Of("src/test").Should().Be(ChangeKind.Unknown, "a bare name is the last component, not a directory");
    }

    [Theory]
    [InlineData("")]
    [InlineData("a/b/")]
    public void AnEmptyOrTrailingSlashPath_IsUnknownAndNeverThrows(string path)
    {
        var kind = string.Empty;

        var act = () => kind = ChangeKind.Of(path);

        act.Should().NotThrow();
        kind.Should().Be(ChangeKind.Unknown);
    }

    [Fact]
    public void TheKindsAreListedInTheOrderTheReportReadsThem() =>
        ChangeKind.All.Should().Equal("code", "test", "docs", "config", "unknown");
}
