using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>Scenario 20: <c>CLAUDE_CONFIG_DIR</c> and <c>CODEX_HOME</c> decide where the files are — those are written, the <c>$HOME</c> ones are not — and <c>--shared</c> is epoch 2.</summary>
public sealed class TheConfigDirsAreHonouredTests
{
    [Fact]
    public async Task TheNamedFoldersAreWritten_TheHomeOnesAreNot_AndTheChildSawTheSameFolders()
    {
        using var world = InstallWorld.Create(claudeConfigDir: true, codexHome: true);

        var (code, _, err) = await world.RunAsync("--binary", world.Binary);

        code.Should().Be(0, err);
        File.Exists(Path.Combine(world.ClaudeConfigDir, ".claude.json")).Should().BeTrue();
        File.Exists(Path.Combine(world.CodexHome, "config.toml")).Should().BeTrue();
        File.Exists(Path.Combine(world.Home, ".claude.json")).Should().BeFalse();
        Directory.Exists(Path.Combine(world.Home, ".codex")).Should().BeFalse();
        world.Calls.Should().OnlyContain(line => line.Contains($"CLAUDE_CONFIG_DIR={world.ClaudeConfigDir}") && line.Contains($"CODEX_HOME={world.CodexHome}") && line.Contains($"HOME={world.Home}"));
    }

    [Fact]
    public async Task TheUnsetOnes_AreRemovedFromTheChild_NotInheritedFromTheRealEnvironment()
    {
        using var world = InstallWorld.Create();

        await world.RunAsync("--binary", world.Binary);

        world.Calls.Should().OnlyContain(line => line.Contains("CLAUDE_CONFIG_DIR= CODEX_HOME="), "an unset variable is empty in the child, whatever the test process has");
    }

    [Theory]
    [InlineData("setup-clients")]
    [InlineData("install")]
    public async Task Shared_IsEpochTwo_RefusedWith65_NothingIsWritten(string mode)
    {
        using var world = InstallWorld.Create();

        var (code, output, err) = await world.RunAsync(world.Deps(), mode, "--shared", "--binary", world.Binary);

        code.Should().Be(65);
        err.Should().Contain("epoch 2");
        output.ValueKind.Should().Be(System.Text.Json.JsonValueKind.Undefined, "stdout carries nothing for a refused request");
        world.Calls.Should().BeEmpty();
    }
}
