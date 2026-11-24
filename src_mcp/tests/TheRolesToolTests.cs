using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>The <c>roles</c> tool over a REAL stdio server (v2 E3.S7 t1): it answers the table, names the client's AI as the main one, and needs no argument.</summary>
public sealed class TheRolesToolTests : IDisposable
{
    private readonly TempDir _root = TempDir.For("coai-roles-tool-");

    [Fact]
    public async Task Roles_AnswersTheTable_ForTheClientThatCalled()
    {
        using var server = StdioServer.Start(_root.At("data"), alsoInTheEnvironment: [("COAI_ROLES_HAIKU_KINDS", """["translate"]""")]);
        try
        {
            await StdioServer.InitializeAsync(server, client: "codex-mcp-client");

            var answer = await StdioServer.Call(server, 2, "roles", "{}");

            answer.GetProperty("mainAi").GetProperty("vendor").GetString().Should().Be("codex", "the client's own name is what says which AI is the main one");
            answer.GetProperty("mainAi").GetProperty("version").GetString().Should().Be("0");
            answer.GetProperty("haikuKinds").EnumerateArray().Select(kind => kind.GetString()).Should().Equal("translate");
            answer.GetProperty("workMethod").GetProperty("maxEpicsPerEpoch").GetInt32().Should().Be(5);
        }
        finally
        {
            await StdioServer.StopAsync(server);
        }
    }

    public void Dispose() => _root.Dispose();
}
