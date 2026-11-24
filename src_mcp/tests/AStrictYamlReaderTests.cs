using CoaiMcp.Core.Work;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>Epoch 3 E1 s2 t3 (B10) — the strict YAML subset: what it reads, what type each plain scalar is, and the named refusal for everything else.</summary>
public sealed class AStrictYamlReaderTests
{
    private static YamlRead Read(string text) => new StrictYamlReader().Read(text);

    private static YamlMap Map(string text) => (YamlMap)((YamlRead.Parsed)Read(text)).Root;

    private static YamlScalar Scalar(string text, string key) => (YamlScalar)Map(text).Find(key)!.Value;

    private static YamlRead.Refused Refused(string text) => Read(text).Should().BeOfType<YamlRead.Refused>().Subject;

    [Fact]
    public void AGeneratedTaskFile_IsRead_WithItsNestedMapsAndLists()
    {
        var map = Map("# coai v2 work contract\nid: t1-x\ntitle: \"A \\\"quoted\\\" title\"\nkind: code\ncomplexity: 5\ndependsOn: []\nparallel: false\nfiles:\n  - src/a.cs\n  - src/b\ndod:\n  - id: d1\n    item: \"one\"\n  - id: d2\n    item: two\n");

        map.Entries.Select(entry => entry.Key).Should().Equal("id", "title", "kind", "complexity", "dependsOn", "parallel", "files", "dod");
        ((YamlScalar)map.Find("title")!.Value).Text.Should().Be("A \"quoted\" title");
        ((YamlList)map.Find("dependsOn")!.Value).Items.Should().BeEmpty();
        ((YamlList)map.Find("files")!.Value).Items.Cast<YamlScalar>().Select(item => item.Text).Should().Equal("src/a.cs", "src/b");
        var dod = ((YamlList)map.Find("dod")!.Value).Items.Cast<YamlMap>().ToList();
        dod.Should().HaveCount(2);
        ((YamlScalar)dod[1].Find("item")!.Value).Text.Should().Be("two");
    }

    [Theory]
    [InlineData("true", YamlScalarKind.Boolean)]
    [InlineData("false", YamlScalarKind.Boolean)]
    [InlineData("5", YamlScalarKind.Integer)]
    [InlineData("-3", YamlScalarKind.Integer)]
    [InlineData("0", YamlScalarKind.Integer)]
    [InlineData("\"5\"", YamlScalarKind.String)]
    [InlineData("'true'", YamlScalarKind.String)]
    [InlineData("yes", YamlScalarKind.String)]
    [InlineData("True", YamlScalarKind.String)]
    [InlineData("5.0", YamlScalarKind.String)]
    [InlineData("007", YamlScalarKind.String)]
    [InlineData("null", YamlScalarKind.Null)]
    [InlineData("~", YamlScalarKind.Null)]
    [InlineData("src/a.cs", YamlScalarKind.String)]
    public void APlainScalar_HasTheTypeTheSpecSays_AQuotedOneIsAlwaysAString(string written, YamlScalarKind kind)
    {
        Scalar($"key: {written}\n", "key").Kind.Should().Be(kind);
    }

    [Fact]
    public void AKeyWithNoValue_IsNull_AndAnIntegerKeepsItsValue()
    {
        Scalar("a:\nb: 1\n", "a").Kind.Should().Be(YamlScalarKind.Null);
        Scalar("n: -12\n", "n").Integer.Should().Be(-12);
        Scalar("a: # nothing here\n", "a").Kind.Should().Be(YamlScalarKind.Null);
    }

    [Fact]
    public void Comments_AreDropped_ExceptInsideQuotes_AndAHashWithoutASpaceIsPlainText()
    {
        var map = Map("# first\na: one # trailing\nb: \"x # kept\"\nc: x#kept\n\n  # indented comment\nd: 'it''s'\n");

        ((YamlScalar)map.Find("a")!.Value).Text.Should().Be("one");
        ((YamlScalar)map.Find("b")!.Value).Text.Should().Be("x # kept");
        ((YamlScalar)map.Find("c")!.Value).Text.Should().Be("x#kept");
        ((YamlScalar)map.Find("d")!.Value).Text.Should().Be("it's");
    }

    [Fact]
    public void AListMaySitAtTheIndentOfItsKey()
    {
        var map = Map("files:\n- a\n- b\nnext: 1\n");

        ((YamlList)map.Find("files")!.Value).Items.Should().HaveCount(2);
        ((YamlScalar)map.Find("next")!.Value).Integer.Should().Be(1);
    }

    [Fact]
    public void CrLf_AndABom_AreAccepted()
    {
        var map = Map("\uFEFFa: 1\r\nb:\r\n  - x\r\n");

        ((YamlScalar)map.Find("a")!.Value).Integer.Should().Be(1);
        ((YamlList)map.Find("b")!.Value).Items.Should().HaveCount(1);
    }

    [Theory]
    [InlineData("a: \"caf\\u00e9\"", "café")]
    [InlineData("a: \"line\\nbreak\"", "line\nbreak")]
    [InlineData("a: \"back\\\\slash\"", "back\\slash")]
    [InlineData("a: \"sol\\/idus\"", "sol/idus")]
    public void ADoubleQuotedScalar_HasJsonEscapes(string line, string expected)
    {
        Scalar(line + "\n", "a").Text.Should().Be(expected);
    }

    [Fact]
    public void AnEmptyFile_IsOneNull_AndARootListIsAList()
    {
        ((YamlRead.Parsed)Read(string.Empty)).Root.Should().BeOfType<YamlScalar>().Which.Kind.Should().Be(YamlScalarKind.Null);
        ((YamlRead.Parsed)Read("- a\n- b\n")).Root.Should().BeOfType<YamlList>();
    }

    [Fact]
    public void OneLeadingDocumentMarker_IsAccepted()
    {
        Scalar("---\n# c\na: 1\n", "a").Integer.Should().Be(1);
    }

    [Theory]
    [InlineData("a: &anchor x\n", "yaml-forbidden", "anchor")]
    [InlineData("a: *alias\n", "yaml-forbidden", "alias")]
    [InlineData("a: !!int 3\n", "yaml-forbidden", "tag")]
    [InlineData("a: |\n  text\n", "yaml-forbidden", "block scalar")]
    [InlineData("a: >\n  text\n", "yaml-forbidden", "block scalar")]
    [InlineData("a: [x, y]\n", "yaml-forbidden", "flow collection")]
    [InlineData("a: {x: 1}\n", "yaml-forbidden", "flow collection")]
    [InlineData("a: {}\n", "yaml-forbidden", "flow collection")]
    [InlineData("a: 1\n---\nb: 2\n", "yaml-forbidden", "second document")]
    [InlineData("---\na: 1\n...\n", "yaml-forbidden", "document end")]
    [InlineData("%YAML 1.2\na: 1\n", "yaml-forbidden", "directive")]
    [InlineData("a:\n  - - x\n", "yaml-forbidden", "list inside a list")]
    [InlineData("&a b: 1\n", "yaml-forbidden", "anchor")]
    [InlineData("? a\n", "yaml-forbidden", "complex key")]
    public void AForbiddenFeature_IsRefusedByName(string text, string code, string wording)
    {
        var refused = Refused(text);

        refused.Code.Should().Be(code);
        refused.Message.Should().Contain(wording).And.StartWith("line ");
    }

    [Fact]
    public void ADuplicateKey_IsRefused_ButTheSameKeyInTwoMapsIsNot()
    {
        Refused("a: 1\nb: 2\na: 3\n").Code.Should().Be("yaml-duplicate-key");
        Refused("a: 1\nb: 2\na: 3\n").Line.Should().Be(3);
        Read("x:\n  a: 1\ny:\n  a: 2\n").Should().BeOfType<YamlRead.Parsed>();
        Read("- id: 1\n  k: 2\n- id: 2\n  k: 3\n").Should().BeOfType<YamlRead.Parsed>();
    }

    [Fact]
    public void AFileOverTheCap_IsRefusedWithoutBeingParsed()
    {
        var refused = Refused("# " + new string('x', StrictYamlReader.MaxBytes) + "\na: 1\n");

        refused.Code.Should().Be("yaml-too-large");
        Read("# " + new string('x', StrictYamlReader.MaxBytes - 20) + "\na: 1\n").Should().BeOfType<YamlRead.Parsed>();
    }

    [Fact]
    public void TheCap_CountsBytesNotCharacters()
    {
        Refused("# " + string.Concat(Enumerable.Repeat("é", StrictYamlReader.MaxBytes / 2)) + "\na: 1\n").Code.Should().Be("yaml-too-large");
    }

    [Theory]
    [InlineData("a: 1\n\tb: 2\n", "tab")]
    [InlineData("a: \"never ends\n", "does not end")]
    [InlineData("a: 'never ends\n", "does not end")]
    [InlineData("a: 1\nstray line\n", "expected 'key: value'")]
    [InlineData("a: x: y\n", "':' inside a plain value")]
    [InlineData("a: \"x\" tail\n", "after a closing quote")]
    [InlineData("a: \"bad \\q escape\"\n", "escape")]
    [InlineData("a: 1\n   b: 2\n", "indentation")]
    [InlineData("a:\n  b: 1\n c: 2\n", "indentation")]
    [InlineData("\"quoted\": 1\n", "plain word")]
    [InlineData("a: - x\n", "list item cannot follow")]
    [InlineData(": 1\n", "empty key")]
    public void ASyntaxFault_IsRefused_NamingTheFaultAndTheLine(string text, string wording)
    {
        var refused = Refused(text);

        refused.Code.Should().Be("yaml-syntax");
        refused.Message.Should().Contain(wording).And.StartWith("line ");
    }

    [Fact]
    public void TheRefusalNamesTheLineOfTheFault()
    {
        Refused("a: 1\nb: 2\nc: &x 3\n").Line.Should().Be(3);
        Refused("a: 1\r\nb: \"open\r\n").Line.Should().Be(2);
    }

    [Fact]
    public void EveryYamlFileOfTheRepositorysV2Folder_IsRead()
    {
        var plan = new[] { "feature.yaml", "epoch.yaml", "epic.yaml", "story.yaml", "task.yaml" };
        var files = Directory.EnumerateFiles(Path.Combine(RepoPaths.Root(), "v2"), "*.yaml", SearchOption.AllDirectories).Where(file => plan.Contains(Path.GetFileName(file))).ToList();

        files.Count.Should().BeGreaterThan(100, "the walk found the plan (the generator's own input file in _gen is not a plan file)");
        foreach (var file in files)
        {
            Read(File.ReadAllText(file)).Should().BeOfType<YamlRead.Parsed>(file);
        }
    }
}
