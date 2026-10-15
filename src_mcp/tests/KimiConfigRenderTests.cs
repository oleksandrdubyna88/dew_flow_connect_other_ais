using System.Text;
using CoaiMcp.Runners.Kimi;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The <c>config.toml</c> coai WRITES into an owned Kimi home (E4.2 brief section 7, S2.5–S2.6): rendered from values, byte for
/// byte the probe's rung-3 text (research/RESULTS_kimi_confinement.md section 17), no BOM, one trailing LF — and a value that
/// would need escaping is a refusal, never an escape coai did not measure.
/// </summary>
public sealed class KimiConfigRenderTests
{
    private static readonly KimiOauthSlot Slot = new("kimi", "https://api.kimi.com/coding/v1", "file", "oauth/kimi-code-env-x", "https://auth.kimi.com");

    private static readonly KimiModelBlock Block = new(
        "kimi-code/k3-256k", "managed:kimi-code", "k3-256k", 262144,
        ["thinking", "always_thinking", "image_in", "tool_use", "dynamically_loaded_tools"], "research/RESULTS_kimi_confinement.md section 17");

    [Fact]
    public void TheRender_IsTheProbesBytes()
    {
        var expected = string.Join(
            "\n",
            "default_model = \"kimi-code/k3-256k\"",
            "",
            "[providers.\"managed:kimi-code\"]",
            "type = \"kimi\"",
            "base_url = \"https://api.kimi.com/coding/v1\"",
            "",
            "[providers.\"managed:kimi-code\".oauth]",
            "storage = \"file\"",
            "key = \"oauth/kimi-code-env-x\"",
            "oauth_host = \"https://auth.kimi.com\"",
            "",
            "[models.\"kimi-code/k3-256k\"]",
            "provider = \"managed:kimi-code\"",
            "model = \"k3-256k\"",
            "max_context_size = 262144",
            "capabilities = [ \"thinking\", \"always_thinking\", \"image_in\", \"tool_use\", \"dynamically_loaded_tools\" ]",
            "");

        var text = KimiConfig.Render(Slot, Block);

        text.Should().Be(expected);
        var bytes = KimiConfig.Bytes(text);
        bytes.Should().Equal(new UTF8Encoding(false).GetBytes(expected), "UTF-8 with no byte-order mark");
        bytes.Take(3).Should().NotEqual([0xEF, 0xBB, 0xBF]);
        text.Should().EndWith("]\n").And.NotEndWith("\n\n");
        text.Should().NotContain("\r");
    }

    [Theory]
    [InlineData("key", "\"")]
    [InlineData("key", "\\")]
    [InlineData("key", "\n")]
    [InlineData("base_url", "\"")]
    [InlineData("base_url", "\\")]
    [InlineData("base_url", "\n")]
    [InlineData("model", "\"")]
    [InlineData("model", "\\")]
    [InlineData("model", "\n")]
    public void TheRender_RefusesAQuoteOrABackslashOrAControlCharacterInAValue(string name, string character)
    {
        var slot = name switch
        {
            "key" => Slot with { Key = $"oauth/{character}" },
            "base_url" => Slot with { BaseUrl = $"https://{character}" },
            _ => Slot,
        };
        var block = name == "model" ? Block with { Model = $"k3{character}" } : Block;

        var render = () => KimiConfig.Render(slot, block);

        var thrown = render.Should().Throw<ArgumentException>($"a {name} holding a quote, a backslash or a newline would need an escape nobody measured").Which;
        thrown.Message.Should().Contain(name).And.NotContain("oauth/", "the message names the key, never the value");
    }
}
