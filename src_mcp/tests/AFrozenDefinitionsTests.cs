using CoaiMcp.Core.Toolchain;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The pure half of the interim gate "frozen build definitions" (E4 s3, pass 2 of the security verify; INTERIM, the owner decides): the list lives in <c>shared/toolchains.json</c> and nowhere in code; a name is matched without case at any depth; the raw records of
/// <c>git diff-tree</c> are read exactly or not at all; a head is refused for a frozen path, a symlink or a submodule, whatever the status (added, deleted, modified, mode-changed); and the sentence holds counts and no path.
/// </summary>
public sealed class AFrozenDefinitionsTests
{
    private static readonly ToolchainDefaults Table = ToolchainDefaults.Current;

    private const string Blob = "1111111111111111111111111111111111111111";

    private const string Other = "2222222222222222222222222222222222222222";

    private static string Raw(params (string OldMode, string NewMode, char Status, string Path)[] changes) =>
        string.Concat(changes.Select(change => $":{change.OldMode} {change.NewMode} {Blob} {Other} {change.Status}\0{change.Path}\0"));

    [Fact]
    public void TheListIsInTheSharedFile_WithTheThreeGroupsOfTheDecision()
    {
        Table.Frozen.Keys.Should().Equal("dotnet", "node", "coai-and-git");
        Table.Frozen["dotnet"].Should().Equal("*.csproj", "*.fsproj", "*.vbproj", "*.props", "*.targets", "*.sln", "*.slnx", "*.slnf", "Directory.Build.*", "Directory.Packages.props", "global.json", "nuget.config", "packages.lock.json", ".config/dotnet-tools.json", "*.runsettings", "xunit.runner.json", "testconfig.json");
        Table.Frozen["node"].Should().Equal("package.json", "package-lock.json", "npm-shrinkwrap.json", "yarn.lock", "pnpm-lock.yaml", "pnpm-workspace.yaml", ".npmrc", ".yarnrc*", "tsconfig*.json", "jest.config.*", "vitest.config.*", "babel.config.*", ".babelrc*", ".mocharc*", ".prettierrc*", "prettier.config.*");
        Table.Frozen["coai-and-git"].Should().Equal(".coai/toolchain.yaml", ".gitmodules", ".gitattributes");
    }

    [Fact]
    public void NoCodeNamesAFrozenFile_TheListIsData_AScanFindsAPlantedName()
    {
        var source = File.ReadAllText(Path.Combine(RepoPaths.Root(), "src_mcp", "core", "Toolchain", "FrozenDefinitions.cs"));

        foreach (var pattern in Table.FrozenPatterns)
        {
            source.Should().NotContain($"\"{pattern}\"", "the frozen list is shared/toolchains.json and nothing else");
        }

        (source + " \"package.json\" ").Should().Contain("\"package.json\"", "the scan would see a name planted in the code");
    }

    [Theory]
    [InlineData("App.csproj", true)]
    [InlineData("src/deep/er/Foo.CSPROJ", true)]
    [InlineData("a/Directory.Build.targets", true)]
    [InlineData("a/directory.build.rsp", true)]
    [InlineData("x/.config/dotnet-tools.json", true)]
    [InlineData("x/.CONFIG/Dotnet-Tools.json", true)]
    [InlineData("web/Tsconfig.Build.json", true)]
    [InlineData("web/.yarnrc.yml", true)]
    [InlineData("web/Jest.Config.ts", true)]
    [InlineData("web/PACKAGE.JSON", true)]
    [InlineData(".coai/toolchain.yaml", true)]
    [InlineData("a/.coai/Toolchain.yaml", true)]
    [InlineData(".gitattributes", true)]
    [InlineData("src/Foo.cs", false)]
    [InlineData("docs/package.json.md", false)]
    [InlineData("x/config/dotnet-tools.json", false)]
    [InlineData("coai/toolchain.yaml", false)]
    [InlineData("scripts/test.sh", false)]
    [InlineData("README.md", false)]
    public void AFrozenName_IsMatchedWithoutCase_AtAnyDepth(string path, bool frozen) =>
        FrozenDefinitions.IsFrozen(path, Table).Should().Be(frozen);

    [Fact]
    public void TheRawRecordsOfDiffTree_AreReadExactly_OrNotAtAll()
    {
        FrozenDefinitions.ParseRaw(Raw(("100644", "100644", 'M', "a/b.cs"), ("000000", "100644", 'A', "c d/e.cs"))).Should().Equal(
            new RawTreeChange("100644", "100644", Blob, Other, 'M', "a/b.cs"), new RawTreeChange("000000", "100644", Blob, Other, 'A', "c d/e.cs"));
        FrozenDefinitions.ParseRaw(string.Empty).Should().BeEmpty();
        FrozenDefinitions.ParseRaw(":100644 100644 x\0a\0").Should().BeNull("too few fields");
        FrozenDefinitions.ParseRaw($":100644 100644 {Blob} {Other} MM\0a\0").Should().BeNull("a status is one letter");
        FrozenDefinitions.ParseRaw($":100644 100644 {Blob} {Other} R\0a\0").Should().BeNull("renames are switched off, so an R is not read");
        FrozenDefinitions.ParseRaw($":100644 100644 {Blob} {Other} M\0").Should().BeNull("a record with no path");
        FrozenDefinitions.ParseRaw($"junk\0a\0").Should().BeNull();
    }

    [Fact]
    public void ACleanHead_AnythingButAFrozenPath_IsNotRefused()
    {
        var changes = FrozenDefinitions.ParseRaw(Raw(("100644", "100644", 'M', "src/Foo.cs"), ("000000", "100644", 'A', "tests/FooTests.cs"), ("100644", "000000", 'D', "docs/old.md"), ("100644", "100755", 'M', "src/web/app.TS"), ("100644", "100644", 'M', "tests/__snapshots__/a.snap")));

        FrozenDefinitions.Refusal(changes, cut: false, Table).Should().BeEmpty();
    }

    [Theory]
    [InlineData("000000", "100644", 'A', "tools/N.csproj")]
    [InlineData("100644", "000000", 'D', "App.slnx")]
    [InlineData("100644", "100644", 'M', "package-lock.json")]
    [InlineData("100644", "100755", 'M', "package.json")]
    [InlineData("100644", "100644", 'M', "a/b/c/Directory.Build.props")]
    [InlineData("100644", "120000", 'T', "docs/readme.md")]
    [InlineData("000000", "120000", 'A', "docs/zq.lnk")]
    [InlineData("120000", "000000", 'D', "docs/zq.lnk")]
    [InlineData("000000", "160000", 'A', "vendor/x")]
    [InlineData("160000", "160000", 'M', "vendor/x")]
    public void AnAddedDeletedModifiedOrModeChangedFrozenPath_AndAnyLinkOrSubmodule_IsRefused_WithCountsAndNoPath(string oldMode, string newMode, char status, string path)
    {
        var sentence = FrozenDefinitions.Refusal(FrozenDefinitions.ParseRaw(Raw((oldMode, newMode, status, path), ("100644", "100644", 'M', "src/Foo.cs"))), cut: false, Table);

        sentence.Should().Contain("frozen build definition path(s)").And.Contain("symlink or submodule path(s)").And.Contain("interim rule").And.Contain("by hand").And.NotContain(path.Split('/')[^1]);
    }

    [Fact]
    public void TheCounts_AreInTheSentence_AFrozenLinkIsCountedOnceAsALink()
    {
        var changes = FrozenDefinitions.ParseRaw(Raw(("100644", "100644", 'M', "A.csproj"), ("100644", "100644", 'M', "B.csproj"), ("000000", "120000", 'A', "package.json")));

        FrozenDefinitions.Refusal(changes, cut: false, Table).Should().StartWith("the head changed 2 frozen build definition path(s) and 1 symlink or submodule path(s) against the base");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AListingThatWasCutOrCouldNotBeRead_IsRefused_APartialListDecidesNothing(bool cut)
    {
        var changes = cut ? FrozenDefinitions.ParseRaw(Raw(("100644", "100644", 'M', "src/Foo.cs"))) : null;

        FrozenDefinitions.Refusal(changes, cut, Table).Should().Contain("could not be read completely").And.Contain("nothing was run");
    }

    [Fact]
    public void AMalformedPattern_IsAFaultOfTheTable()
    {
        var json = System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(Path.Combine(RepoPaths.Root(), "shared", "toolchains.json")), "\"\\.gitmodules\"", "\"a\\\\\\\\b\"");

        var act = () => ToolchainDefaults.Parse(json);

        act.Should().Throw<ToolchainDefaultsException>().WithMessage("*frozen.coai-and-git*");
    }

    // ---- pass 3, M15: an allow list ----

    [Fact]
    public void TheAllowListIsInTheSharedFile()
    {
        Table.AllowedSourceExtensions.Should().Equal(".cs", ".fs", ".vb", ".razor", ".cshtml", ".ts", ".tsx", ".mts", ".cts", ".js", ".jsx", ".mjs", ".cjs", ".css", ".scss", ".html", ".md", ".txt", ".snap");
        Table.ConfigNamePattern.Should().Be("(^\\.|config|rc$|rc\\.|^setup|^global|^jest|^vite|^vitest|^playwright|^webpack|^rollup|^babel|^eslint|^prettier|^tsup|^esbuild|^karma|^mocha|^ava|^cypress|^nx|^turbo)");
    }

    [Theory]
    [InlineData("src/Foo.cs", true)]
    [InlineData("tests/FooTests.cs", true)]
    [InlineData("src/web/App.TSX", true)]
    [InlineData("docs/readme.md", true)]
    [InlineData("tests/__snapshots__/a.snap", true)]
    [InlineData("a/b/c/styles.scss", true)]
    [InlineData("tests/T.csproj.user", false)]
    [InlineData("tests/Properties/launchSettings.json", false)]
    [InlineData("src/Shared.projitems", false)]
    [InlineData(".editorconfig", false)]
    [InlineData(".globalconfig", false)]
    [InlineData("vite.config.ts", false)]
    [InlineData("jest.unit.config.js", false)]
    [InlineData("test/jest-e2e.json", false)]
    [InlineData(".env", false)]
    [InlineData("packages/Foo.1.0.0.nupkg", false)]
    [InlineData("scripts/test.sh", false)]
    [InlineData("Makefile", false)]
    [InlineData("src/.hidden.cs", false)]
    [InlineData("setup.ts", false)]
    [InlineData("src/GlobalUsings.cs", false)]
    [InlineData("src/AppConfig.cs", false)]
    [InlineData("webpack.dev.js", false)]
    [InlineData("src/Foo.cs.", false)]
    [InlineData("src/Foo.cs ", false)]
    [InlineData("src/Foo", false)]
    public void OnlySourceFilesOfTheClosedListWithANonConfigNameMayBeChanged(string path, bool allowed) =>
        FrozenDefinitions.IsAllowedChange(path, Table).Should().Be(allowed);

    [Fact]
    public void ADeleteOrARenameOfAnythingElse_IsRefusedToo_ARenameIsADeleteAndAnAdd()
    {
        var changes = FrozenDefinitions.ParseRaw(Raw(("100644", "000000", 'D', "tests/T.csproj.user"), ("000000", "100644", 'A', "tests/T.csproj.user.bak")));

        FrozenDefinitions.Refusal(changes, cut: false, Table).Should().StartWith("the head changed 2 frozen build definition path(s)");
    }

    [Fact]
    public void TheTestFilesTheHeadChanged_AreCountedByTheirStatus()
    {
        var changes = FrozenDefinitions.ParseRaw(Raw(("100644", "100644", 'M', "tests/FooTests.cs"), ("000000", "100644", 'A', "tests/NewTests.cs"), ("100644", "000000", 'D', "src/App.test.ts"), ("100644", "100644", 'M', "src/Foo.cs"), ("100644", "000000", 'D', "src/Foo.spec.js")))!;

        FrozenDefinitions.TestFiles(changes).Should().Be(new TestFileCounts(1, 1, 2));
    }
}
