using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>Scenario 17: <c>--remove</c> — our entries are gone, all else equal; a second <c>--remove</c> is <c>none</c>; an entry that is not ours is left alone.</summary>
public sealed class ARemoveTakesOurEntriesAwayTests
{
    [Fact]
    public async Task OurEntriesGo_EverythingElseIsEqual_AndASecondRemoveIsNone()
    {
        using var world = InstallWorld.Create();
        world.Put(world.ClaudeJson, InstallOutput.LivedInClaudeJson);
        world.Put(world.CodexToml, InstallOutput.LivedInCodexToml);
        (await world.RunAsync("--binary", world.Binary)).Code.Should().Be(0);

        var (code, output, err) = await world.RunAsync("--remove");

        code.Should().Be(0, err);
        output.Action("claude").Should().Be("remove");
        InstallOutput.ClaudeEntry(world.ClaudeJsonText).Should().BeNull();
        InstallOutput.WithoutOurs(world.ClaudeJsonText).Should().Be(InstallOutput.Compact(InstallOutput.LivedInClaudeJson));
        InstallOutput.SameToml(world.CodexTomlText, InstallOutput.LivedInCodexToml).Should().BeTrue(world.CodexTomlText);

        var second = await world.RunAsync("--remove");
        second.Code.Should().Be(0);
        second.Out.Action("claude").Should().Be("none");
        second.Out.Action("codex").Should().Be("none");
    }

    [Fact]
    public async Task AnEntryNamedCoaiThatIsNotOurs_IsLeftAlone()
    {
        using var world = InstallWorld.Create(codex: false);
        var wrapped = """{ "mcpServers": { "coai": { "type": "stdio", "command": "wsl.exe" } } }""";
        world.Put(world.ClaudeJson, wrapped);

        var (code, output, _) = await world.RunAsync("--remove");

        code.Should().Be(0);
        output.Action("claude").Should().Be("none");
        world.ClaudeJsonText.Should().Be(wrapped);
    }
}
