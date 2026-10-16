using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The reader of an AI CLI's <c>config.toml</c>, widened for Kimi's (E4.2 brief section 4, S2.4): a key inside ONE named table,
/// read for the same reason and with the same "unsure means silent" — never a second parser.
/// </summary>
public sealed class CodexConfigFileTests
{
    private const string Toml = """
        root_key = "only at the root"

        [providers."managed:kimi-code"]
        type = "kimi"
        base_url = "https://managed.example/v1"

        [providers."moonshot-ai"]
        base_url = "https://moonshot.example/v1"
        api_key = "not-a-real-one"

        [models."kimi-code/k3"]
        max_context_size = 262144
        capabilities = [ "a", "b" ]
        wide = [
          "c",
          "d",
        ]
        """;

    [Fact]
    public void TableValue_ReadsInsideOneNamedTableOnly()
    {
        CodexConfigFile.TableValue(Toml, "[providers.\"managed:kimi-code\"]", "base_url").Should().Be("https://managed.example/v1");
        CodexConfigFile.TableValue(Toml, "[providers.\"moonshot-ai\"]", "base_url").Should().Be("https://moonshot.example/v1");
        CodexConfigFile.TableValue(Toml, "[models.\"kimi-code/k3\"]", "max_context_size").Should().Be("262144");
        CodexConfigFile.TableValue(Toml, "[providers.\"managed:kimi-code\"]", "api_key").Should().BeEmpty("the key lives in the OTHER table");
        CodexConfigFile.TableValue(Toml, "[providers.\"managed:kimi-code\"]", "root_key").Should().BeEmpty("a root key is not answered for a table");
        CodexConfigFile.TableValue(Toml, "[nowhere]", "type").Should().BeEmpty();
    }

    [Fact]
    public void TableStrings_ReadsAOneLineArrayOfBasicStrings_AndNothingElse()
    {
        CodexConfigFile.TableStrings(Toml, "[models.\"kimi-code/k3\"]", "capabilities").Should().Equal("a", "b");
        CodexConfigFile.TableStrings(Toml, "[models.\"kimi-code/k3\"]", "wide").Should().BeEmpty("a multi-line array is another form, and unsure means silent");
        CodexConfigFile.TableStrings(Toml, "[models.\"kimi-code/k3\"]", "missing").Should().BeEmpty();
        CodexConfigFile.TableStrings("[t]\nk = [\"a\",\"b\"] # trailing\n", "[t]", "k").Should().Equal("a", "b");
        CodexConfigFile.TableStrings("[t]\nk = [ \"a\", 3 ]\n", "[t]", "k").Should().BeEmpty("a non-string item is another form");
    }

    [Fact]
    public void HasTable_IsTrueForTheExactHeaderOnly()
    {
        CodexConfigFile.HasTable(Toml, "[models.\"kimi-code/k3\"]").Should().BeTrue();
        CodexConfigFile.HasTable(Toml, "[models.\"kimi-code/k3-256k\"]").Should().BeFalse("a header is matched whole, not by prefix");
        CodexConfigFile.HasTable(Toml, "[nowhere]").Should().BeFalse();
    }
}
