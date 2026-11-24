using CoaiMcp.Install;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>The strict TOML reader that lets the installer refuse a malformed <c>config.toml</c> before it starts a vendor command, and read an entry without one.</summary>
public sealed class TomlSubsetTests
{
    [Fact]
    public void ACodexConfig_WithTablesArraysInlineTablesAndEveryStringKind_IsRead()
    {
        const string text = """
            # a comment
            model = "gpt-6"   # trailing
            n = 5
            f = 1.5e3
            on = true
            when = 2026-11-22T10:00:00Z
            literal = 'C:\Users\A B\x'
            multi = '''
            two
            lines'''
            [mcp_servers.coai]
            command = "C:\\Users\\A B\\coai-mcp.exe"
            args = [
              "--x",   # why
              "é<",
            ]
            env = { A = "1", B = "2" }
            [[profiles]]
            name = "a"
            [[profiles]]
            name = "b"
            """;

        var parsed = TomlSubset.Parse(text);

        parsed.Error.Should().BeEmpty();
        var servers = (Dictionary<string, object>)parsed.Root!["mcp_servers"];
        var coai = (Dictionary<string, object>)servers["coai"];
        coai["command"].Should().Be(@"C:\Users\A B\coai-mcp.exe");
        ((List<object>)coai["args"]).Should().Equal("--x", "é<");
        ((Dictionary<string, object>)coai["env"]).Should().Equal(new Dictionary<string, object> { ["A"] = "1", ["B"] = "2" });
        parsed.Root["literal"].Should().Be(@"C:\Users\A B\x");
        parsed.Root["multi"].Should().Be("two\nlines");
        ((List<object>)parsed.Root["profiles"]).Should().HaveCount(2);
    }

    [Theory]
    [InlineData("[a")]
    [InlineData("k = \"open")]
    [InlineData("k = 'open")]
    [InlineData("k = ")]
    [InlineData("just words")]
    [InlineData("k = 1 2")]
    [InlineData("[a]\nx = 1\n[a]\ny = 2")]
    [InlineData("k = 1\nk = 2")]
    [InlineData("k = [1, 2")]
    [InlineData("k = \"bad \\q escape\"")]
    [InlineData("k = nope")]
    [InlineData("[a]\nb = 1\n[a.b]\nc = 1")]
    public void NotToml_IsAnAnswer_NeverAnException(string text)
    {
        var parsed = TomlSubset.Parse(text);

        parsed.Root.Should().BeNull();
        parsed.Error.Should().NotBeEmpty();
    }
}
