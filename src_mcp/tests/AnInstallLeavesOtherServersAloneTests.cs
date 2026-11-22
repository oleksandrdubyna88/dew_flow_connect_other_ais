using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>Scenario 3: other servers (with env), other keys and projects are deep-equal after the install — the one added entry is the only difference.</summary>
public sealed class AnInstallLeavesOtherServersAloneTests
{
    [Fact]
    public async Task Claude_EverythingButOurEntryIsDeepEqual_AndAnEnvValueIsNeverPrinted()
    {
        using var world = InstallWorld.Create();
        world.Put(world.ClaudeJson, InstallOutput.LivedInClaudeJson);

        var (code, output, err) = await world.RunAsync("--binary", world.Binary);

        code.Should().Be(0, err);
        InstallOutput.WithoutOurs(world.ClaudeJsonText).Should().Be(InstallOutput.Compact(InstallOutput.LivedInClaudeJson), "the vendor wrote the file; nothing else in it moved");
        InstallOutput.ClaudeEntry(world.ClaudeJsonText).Should().NotBeNull();
        output.ToString().Should().NotContain("s3cr3t-value");
    }

    [Fact]
    public async Task Codex_OtherServersAndKeysAreTheSameDocumentAfter()
    {
        using var world = InstallWorld.Create();
        world.Put(world.CodexToml, InstallOutput.LivedInCodexToml);

        var (code, output, err) = await world.RunAsync("--binary", world.Binary);

        code.Should().Be(0, err);
        var after = world.CodexTomlText;
        InstallOutput.SameToml(
            after + "\n",
            InstallOutput.LivedInCodexToml + $"\n\n[mcp_servers.coai]\ncommand = \"{world.Binary}\"\n").Should().BeTrue(after);
        output.ToString().Should().NotContain("value-4711");
    }
}
