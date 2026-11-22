using System.Text.Json.Nodes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>Scenario 16: <c>--restore latest</c> — our keys go back to <c>before</c>; a server added after the install stays; a target that is no longer what the install wrote is refused.</summary>
public sealed class ARestoreBringsOurKeysBackTests
{
    [Fact]
    public async Task OurEntriesGoBack_ALaterServerStays()
    {
        using var world = InstallWorld.Create();
        InstallOutput.WithOldEntries(world);
        var install = await world.RunAsync("--binary", world.Binary);
        install.Code.Should().Be(0);
        var installId = install.Out.GetProperty("backup").GetProperty("id").GetString()!;
        AddAServerAfterTheInstall(world);

        var (code, output, err) = await world.RunAsync("--restore", "latest");

        code.Should().Be(0, err);
        var entry = InstallOutput.ClaudeEntry(world.ClaudeJsonText)!;
        entry["command"]!.GetValue<string>().Should().Be("/old/coai/versions/1.0.0/coai-mcp");
        entry["env"]!["COAI_DATA_DIR"]!.GetValue<string>().Should().Be("/data/coai");
        JsonNode.Parse(world.ClaudeJsonText)!["mcpServers"]!["later"]!["command"]!.GetValue<string>().Should().Be("/usr/bin/later");
        world.CodexTomlText.Should().Contain("/old/coai/versions/1.0.0/coai-mcp");
        output.Action("claude").Should().Be("restore");
        output.Result("codex").Should().Be("changed");

        var again = await world.RunAsync("--restore", installId);
        again.Code.Should().Be(0);
        again.Out.Action("claude").Should().Be("none", "already as it was before that install");
    }

    [Fact]
    public async Task AnInstallThatAddedTheEntry_IsRestoredByRemovingIt()
    {
        using var world = InstallWorld.Create(codex: false);
        world.Put(world.ClaudeJson, InstallOutput.LivedInClaudeJson);
        (await world.RunAsync("--binary", world.Binary)).Code.Should().Be(0);

        var (code, _, err) = await world.RunAsync("--restore", "latest");

        code.Should().Be(0, err);
        InstallOutput.ClaudeEntry(world.ClaudeJsonText).Should().BeNull();
        InstallOutput.WithoutOurs(world.ClaudeJsonText).Should().Be(InstallOutput.Compact(InstallOutput.LivedInClaudeJson));
    }

    [Fact]
    public async Task ATargetThatChangedSinceTheInstall_IsRefused_NotPutBackOverTheChange()
    {
        using var world = InstallWorld.Create(codex: false);
        InstallOutput.WithOldEntries(world);
        (await world.RunAsync("--binary", world.Binary)).Code.Should().Be(0);
        var edited = world.ClaudeJsonText.Replace(world.BinaryJson, InstallWorld.Escaped(world.AnotherBinary("opt", "later")));
        world.Put(world.ClaudeJson, edited);

        var (code, output, _) = await world.RunAsync("--restore", "latest");

        code.Should().Be(78);
        output.Result("claude").Should().Be("refused");
        world.ClaudeJsonText.Should().Be(edited);
    }

    [Fact]
    public async Task AnUnknownBackupId_Is65()
    {
        using var world = InstallWorld.Create();

        var (code, _, err) = await world.RunAsync("--restore", "20990101T000000000Z-1");

        code.Should().Be(65);
        err.Should().Contain("no backup");
        (await world.RunAsync("--restore", "../../etc")).Code.Should().Be(65);
    }

    private static void AddAServerAfterTheInstall(InstallWorld world)
    {
        var root = JsonNode.Parse(world.ClaudeJsonText)!.AsObject();
        root["mcpServers"]!.AsObject()["later"] = new JsonObject { ["type"] = "stdio", ["command"] = "/usr/bin/later" };
        world.Put(world.ClaudeJson, root.ToJsonString());
    }
}
