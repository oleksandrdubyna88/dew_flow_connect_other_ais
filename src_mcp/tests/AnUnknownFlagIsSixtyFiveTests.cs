using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>Scenario 21: an unknown flag is 65 (never 64) — and no run ever writes a PreToolUse or AskUserQuestion hook, or asks a client's <c>mcp get</c> or <c>list</c>.</summary>
public sealed class AnUnknownFlagIsSixtyFiveTests
{
    [Theory]
    [InlineData("--nope")]
    [InlineData("--only", "claudx")]
    [InlineData("--only")]
    [InlineData("--binary", "relative/coai-mcp")]
    [InlineData("--remove", "--restore", "latest")]
    [InlineData("--dry-run", "--dry-run")]
    public async Task AWrongRequest_IsAKnownModeRefusingItsArguments_65(params string[] args)
    {
        using var world = InstallWorld.Create();

        var (code, output, err) = await world.RunAsync(args);

        code.Should().Be(65);
        err.Should().NotBeEmpty();
        output.ValueKind.Should().Be(System.Text.Json.JsonValueKind.Undefined);
        world.Calls.Should().BeEmpty();
        Directory.GetFiles(world.Home, "*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public async Task TheLegalClientsAreNamed_WhenOneIsNot()
    {
        using var world = InstallWorld.Create();

        var (_, _, err) = await world.RunAsync("--only", "claudx");

        err.Should().Contain("claude, codex, agy");
    }

    [Fact]
    public async Task AfterEveryKindOfRun_NoPreToolUseHookExists_AndNoClientIsAskedMcpGetOrList()
    {
        using var world = InstallWorld.Create();
        InstallOutput.WithOldEntries(world);
        await world.RunAsync("--binary", world.Binary);
        await world.RunAsync("--binary", world.Binary);
        await world.RunAsync("--restore", "latest");
        await world.RunAsync("--remove");

        foreach (var file in Directory.GetFiles(world.Home, "*", SearchOption.AllDirectories))
        {
            File.ReadAllText(file).Should().NotContain("PreToolUse").And.NotContain("AskUserQuestion");
        }

        world.Calls.Should().NotContain(line => line.Contains(" mcp get ") || line.Contains(" mcp list "), "those start the server for a health check — the files are read instead");
        world.Calls.Should().Contain(line => line.Contains(" mcp "), "positive control: the runs did run commands");
    }
}
