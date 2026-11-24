using System.Text.Json.Nodes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>Scenario 19: <c>projects[&lt;repo&gt;].mcpServers.coai</c> wins over the user entry — it is reported in <c>shadowedBy</c> and left exactly as it is.</summary>
public sealed class AProjectScopeEntryIsReportedAsShadowingTests
{
    [Fact]
    public async Task TheProjectEntryIsNamed_AndNotTouched()
    {
        using var world = InstallWorld.Create(codex: false);
        var text = """
            { "projects": { "/repo": { "mcpServers": { "coai": { "type": "stdio", "command": "/elsewhere/coai-mcp", "env": { "K": "v" } } } }, "/quiet": { "mcpServers": {} } } }
            """;
        world.Put(world.ClaudeJson, text);

        var (code, output, err) = await world.RunAsync("--binary", world.Binary);

        code.Should().Be(0, err);
        output.Target("claude").GetProperty("shadowedBy").EnumerateArray().Select(one => one.GetString()).Should().Equal("project:/repo");
        var after = JsonNode.Parse(world.ClaudeJsonText)!;
        after["projects"]!.ToJsonString().Should().Be(JsonNode.Parse(text)!["projects"]!.ToJsonString());
        after["mcpServers"]!["coai"]!["command"]!.GetValue<string>().Should().Be(world.Binary, "the user entry is still written");
    }
}
