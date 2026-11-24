using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>Scenario 5: a <c>coai</c> entry whose command is not coai-mcp (a wrapper) — that CLI is refused, the exit is 78, the others are done.</summary>
public sealed class AnEntryWithAForeignCommandIsRefusedTests
{
    [Fact]
    public async Task ClaudeIsRefused_CodexIsDone_AndTheRefusedFileIsUntouched()
    {
        using var world = InstallWorld.Create();
        var wrapped = """{ "mcpServers": { "coai": { "type": "stdio", "command": "wsl.exe", "args": ["-e", "/opt/coai/coai-mcp"] } } }""";
        world.Put(world.ClaudeJson, wrapped);

        var (code, output, _) = await world.RunAsync("--binary", world.Binary);

        code.Should().Be(78);
        output.Result("claude").Should().Be("refused");
        output.Action("claude").Should().Be("refuse");
        output.Client("claude").GetProperty("reason").GetString().Should().Contain("wsl.exe").And.Contain("not ours");
        output.Result("codex").Should().Be("changed");
        world.ClaudeJsonText.Should().Be(wrapped);
        world.Calls.Should().NotContain(line => line.StartsWith("claude "), "no command was run for the refused CLI");
        world.CodexTomlText.Should().Contain("[mcp_servers.coai]");
    }
}
