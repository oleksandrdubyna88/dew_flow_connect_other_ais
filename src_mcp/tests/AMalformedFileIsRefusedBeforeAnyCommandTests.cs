using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>Scenario 7: a malformed (or wrongly shaped) file is refused BEFORE a vendor command, its bytes unchanged, the others go on, the exit is 78.</summary>
public sealed class AMalformedFileIsRefusedBeforeAnyCommandTests
{
    [Theory]
    [InlineData("{ \"mcpServers\": { oops")]
    [InlineData("[1, 2]")]
    [InlineData("{\"mcpServers\": [1]}")]
    public async Task ClaudeJson_IsRefused_AndCodexIsDone(string bad)
    {
        using var world = InstallWorld.Create();
        world.Put(world.ClaudeJson, bad);

        var (code, output, _) = await world.RunAsync("--binary", world.Binary);

        code.Should().Be(78);
        output.Result("claude").Should().Be("refused");
        output.Client("claude").GetProperty("reason").GetString().Should().Contain(".claude.json").And.Contain("cannot be changed");
        world.ClaudeJsonText.Should().Be(bad);
        world.Calls.Should().NotContain(line => line.StartsWith("claude "), "refused before a vendor command, so the vendor never writes a .corrupted copy either");
        Directory.GetFiles(world.Home, ".claude.json*").Should().HaveCount(1);
        output.Result("codex").Should().Be("changed");
    }

    [Theory]
    [InlineData("[mcp_servers.x\ncommand = ")]
    [InlineData("model = \"unclosed")]
    [InlineData("[a]\nk = 1\n[a]\nk = 2")]
    public async Task ConfigToml_IsRefused_AndClaudeIsDone(string bad)
    {
        using var world = InstallWorld.Create();
        world.Put(world.CodexToml, bad);

        var (code, output, _) = await world.RunAsync("--binary", world.Binary);

        code.Should().Be(78);
        output.Result("codex").Should().Be("refused");
        world.CodexTomlText.Should().Be(bad);
        world.Calls.Should().NotContain(line => line.StartsWith("codex "));
        output.Result("claude").Should().Be("changed");
    }

    [Fact]
    public async Task AnEmptyFile_IsEmpty_NotMalformed()
    {
        using var world = InstallWorld.Create();
        world.Put(world.CodexToml, string.Empty);

        var (code, output, _) = await world.RunAsync("--binary", world.Binary);

        code.Should().Be(0);
        output.Result("codex").Should().Be("changed");
    }
}
