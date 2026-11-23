using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>Scenario 4: an old <c>coai</c> entry at another path — one entry after, the new command, the same env and arguments (claude and codex).</summary>
public sealed class AnOldEntryAtAnotherPathKeepsItsEnvTests
{
    [Fact]
    public async Task Claude_TheCommandMoves_TheEnvAndArgsStay()
    {
        using var world = InstallWorld.Create();
        InstallOutput.WithOldEntries(world);

        var (code, output, err) = await world.RunAsync("--binary", world.Binary);

        code.Should().Be(0, err);
        var entry = InstallOutput.ClaudeEntry(world.ClaudeJsonText)!;
        entry["command"]!.GetValue<string>().Should().Be(world.Binary);
        entry["env"]!["COAI_DATA_DIR"]!.GetValue<string>().Should().Be("/data/coai");
        entry["env"]!["COAI_DATA_SIDE"]!.GetValue<string>().Should().Be("wsl");
        entry["args"]![0]!.GetValue<string>().Should().Be("--x");
        entry["type"]!.GetValue<string>().Should().Be("stdio", "every key of the old entry is kept");
        output.Action("claude").Should().Be("update");
        output.Target("claude").GetProperty("after").GetProperty("envKeys").EnumerateArray().Select(one => one.GetString()).Should().BeEquivalentTo(["COAI_DATA_DIR", "COAI_DATA_SIDE"]);
        output.ToString().Should().NotContain("/data/coai", "env names are printed, never values");
    }

    [Fact]
    public async Task Codex_TheEntryIsReplacedAndTheOldEnvIsPassedAgain()
    {
        using var world = InstallWorld.Create();
        InstallOutput.WithOldEntries(world);

        var (code, output, err) = await world.RunAsync("--binary", world.Binary);

        code.Should().Be(0, err);
        InstallOutput.SameToml(
            world.CodexTomlText,
            $"[mcp_servers.coai]\ncommand = {InstallOutput.TomlString(world.Binary)}\nargs = [\"--x\"]\n[mcp_servers.coai.env]\nCOAI_DATA_DIR = \"/data/coai\"\nCOAI_DATA_SIDE = \"wsl\"\n").Should().BeTrue(world.CodexTomlText);
        output.Action("codex").Should().Be("update");
    }

    [Fact]
    public async Task AnotherCoaiMcpThatIsThereIsLeftStanding_UnlessThePersonNamedThisOne()
    {
        using var world = InstallWorld.Create();
        var live = world.AnotherBinary("opt", "other");
        InstallOutput.WithOldEntries(world, live);

        var left = await world.RunAsync();
        var named = await world.RunAsync("--binary", world.Binary);

        left.Out.Action("claude").Should().Be("none");
        left.Out.Target("claude").GetProperty("shadowedBy").EnumerateArray().Select(one => one.GetString()).Should().Contain($"registered:{live}");
        left.Out.Client("claude").GetProperty("reason").GetString().Should().Contain("left standing");
        named.Out.Action("claude").Should().Be("update", "--binary is the person's decision");
        InstallOutput.ClaudeEntry(world.ClaudeJsonText)!["command"]!.GetValue<string>().Should().Be(world.Binary);
    }
}
