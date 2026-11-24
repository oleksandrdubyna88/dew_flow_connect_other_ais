using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>Scenario 1 of the installer's spec: a fresh HOME with the CLIs present — the entries are written, a manifest is kept, the exit is 0.</summary>
public sealed class AnInstallOnAFreshHomeWritesTheEntriesTests
{
    [Fact]
    public async Task BothEntriesAreWritten_ThroughTheClientsOwnCommands_AndTheManifestSaysVerified()
    {
        using var world = InstallWorld.Create();

        var (code, output, err) = await world.RunAsync("--binary", world.Binary);

        code.Should().Be(0, err);
        InstallOutput.ClaudeEntry(world.ClaudeJsonText)!["command"]!.GetValue<string>().Should().Be(world.Binary);
        world.CodexTomlText.Should().Contain("[mcp_servers.coai]");
        InstallOutput.SameToml(world.CodexTomlText, $"[mcp_servers.coai]\ncommand = {InstallOutput.TomlString(world.Binary)}\n").Should().BeTrue("codex holds the one entry, at the registered path");
        output.Result("claude").Should().Be("changed");
        output.Result("codex").Should().Be("changed");
        output.Result("agy").Should().Be("absent");
        output.Action("claude").Should().Be("add");
        output.Target("claude").GetProperty("via").GetString().Should().Be("claude mcp add-json");

        var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(world.BackupFolders.Single(), "manifest.json"))).RootElement;
        manifest.GetProperty("binary").GetString().Should().Be(world.Binary);
        manifest.GetProperty("targets").EnumerateArray().Select(target => target.GetProperty("state").GetString()).Should().OnlyContain(state => state == "verified");
        output.GetProperty("backup").GetProperty("id").GetString().Should().Be(Path.GetFileName(world.BackupFolders.Single()));
    }

    [Fact]
    public async Task ABinaryThatIsNotThisOne_IsAskedItsVersion()
    {
        using var world = InstallWorld.Create();
        var other = world.AnotherBinary("opt", "named");

        var (code, output, _) = await world.RunAsync(world.Deps(), "setup-clients", "--binary", other);

        code.Should().Be(0);
        output.GetProperty("binary").GetProperty("path").GetString().Should().Be(other);
        output.GetProperty("binary").GetProperty("version").GetString().Should().Be("1.0.0", "the file said `coai-mcp 1.0.0` to --version; the self version is not borrowed");
    }

    [Fact]
    public async Task TheNameAnInstallAnswersToIsInstall_TheSameEngine()
    {
        using var world = InstallWorld.Create();

        var (code, output, _) = await world.RunAsync(world.Deps(), "install", "--binary", world.Binary);

        code.Should().Be(0);
        output.GetProperty("mode").GetString().Should().Be("install");
        InstallOutput.ClaudeEntry(world.ClaudeJsonText).Should().NotBeNull();
    }

    [Fact]
    public async Task WithoutBinary_ItIsThisBinarysOwnPathThatIsRegistered()
    {
        using var world = InstallWorld.Create();

        var (code, output, _) = await world.RunAsync();

        code.Should().Be(0);
        output.GetProperty("binary").GetProperty("path").GetString().Should().Be(world.Binary);
        output.GetProperty("binary").GetProperty("version").GetString().Should().Be("9.9.9");
        InstallOutput.ClaudeEntry(world.ClaudeJsonText)!["command"]!.GetValue<string>().Should().Be(world.Binary);
    }
}
