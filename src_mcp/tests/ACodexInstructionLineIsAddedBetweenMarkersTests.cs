using CoaiMcp.Install;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// v2 E3.S7 (spec decision 9): Codex's global instruction file gets ONE line — call coai's <c>roles</c> tool at the start of a task — between marker lines, added once, replaced in place when its wording is
/// old, taken away on request, and never anywhere but between the markers. agy has no such target: its file could not be measured.
/// </summary>
public sealed class ACodexInstructionLineIsAddedBetweenMarkersTests
{
    private const string Mine = "# My rules\n\nAlways answer in English.\n- é <1e5>\n";

    private static string Block(string line = CodexInstructions.Line) => $"{CodexInstructions.Start}\n{line}\n{CodexInstructions.End}\n";

    [Fact]
    public async Task OnAFreshHome_TheFileHoldsOneBlock_AndASecondRunChangesNothing()
    {
        using var world = InstallWorld.Create(claude: false);

        var (code, output, err) = await world.RunAsync("--binary", world.Binary);

        code.Should().Be(0, err);
        output.Action("codex", "instructions").Should().Be("add");
        File.ReadAllText(world.CodexAgents).Should().Be(Block());
        var tree = world.HomeHash();

        var second = await world.RunAsync("--binary", world.Binary);

        second.Code.Should().Be(0);
        second.Out.Action("codex", "instructions").Should().Be("none");
        world.HomeHash().Should().Be(tree);
    }

    [Fact]
    public async Task APersonsOwnText_IsKept_AndTheDiffIsAddedLinesOnly()
    {
        using var world = InstallWorld.Create(claude: false);
        world.Put(world.CodexAgents, Mine);

        (await world.RunAsync("--binary", world.Binary)).Code.Should().Be(0);

        var after = File.ReadAllText(world.CodexAgents);
        after.Should().StartWith(Mine);
        ClaudeHookFixtures.AddedLines(Mine, after).Should().Equal(string.Empty, CodexInstructions.Start, CodexInstructions.Line, CodexInstructions.End);
    }

    [Fact]
    public async Task AFileWithoutATrailingNewline_GetsTheBlockOnNewLines()
    {
        using var world = InstallWorld.Create(claude: false);
        world.Put(world.CodexAgents, "no newline at the end");

        (await world.RunAsync("--binary", world.Binary)).Code.Should().Be(0);

        File.ReadAllText(world.CodexAgents).Should().Be("no newline at the end\n\n" + Block());
    }

    [Fact]
    public async Task AnOldWording_IsReplacedInPlace_TheTextAroundItUntouched()
    {
        using var world = InstallWorld.Create(claude: false);
        var old = $"before\n{Block("Call roles sometimes.")}after\n";
        world.Put(world.CodexAgents, old);

        var (code, output, err) = await world.RunAsync("--binary", world.Binary);

        code.Should().Be(0, err);
        output.Action("codex", "instructions").Should().Be("update");
        File.ReadAllText(world.CodexAgents).Should().Be($"before\n{Block()}after\n");
    }

    [Theory]
    [InlineData("<!-- coai:roles:start -->\nno end\n")]
    [InlineData("<!-- coai:roles:end -->\n<!-- coai:roles:start -->\n")]
    [InlineData("<!-- coai:roles:start -->\n<!-- coai:roles:start -->\n<!-- coai:roles:end -->\n")]
    public async Task MarkersThatAreNotOneMatchedPair_AreRefused_BytesUnchanged_TheMcpHalfIsDone(string bad)
    {
        using var world = InstallWorld.Create(claude: false);
        world.Put(world.CodexAgents, bad);

        var (code, output, _) = await world.RunAsync("--binary", world.Binary);

        code.Should().Be(78);
        output.Action("codex", "instructions").Should().Be("refuse");
        File.ReadAllText(world.CodexAgents).Should().Be(bad);
        output.Action("codex", "mcp").Should().Be("add");
    }

    [Fact]
    public async Task Remove_TakesTheBlockAway_ThePersonsFileIsAsItWas()
    {
        using var world = InstallWorld.Create(claude: false);
        world.Put(world.CodexAgents, Mine);
        (await world.RunAsync("--binary", world.Binary)).Code.Should().Be(0);

        var (code, output, err) = await world.RunAsync("--remove");

        code.Should().Be(0, err);
        output.Action("codex", "instructions").Should().Be("remove");
        File.ReadAllText(world.CodexAgents).Should().Be(Mine);
        (await world.RunAsync("--remove")).Out.Action("codex", "instructions").Should().Be("none");
    }

    [Fact]
    public async Task Restore_PutsTheFileBack_AFileWeMadeIsLeftEmpty()
    {
        using var world = InstallWorld.Create(claude: false);
        (await world.RunAsync("--binary", world.Binary)).Code.Should().Be(0);

        var (code, _, err) = await world.RunAsync("--restore", "latest");

        code.Should().Be(0, err);
        File.ReadAllText(world.CodexAgents).Should().BeEmpty();
    }

    [Fact]
    public async Task CodexHome_IsHonoured_AndTheHomeFolderIsNotTouched()
    {
        using var world = InstallWorld.Create(claude: false, codexHome: true);

        (await world.RunAsync("--binary", world.Binary)).Code.Should().Be(0);

        File.Exists(Path.Combine(world.CodexHome, "AGENTS.md")).Should().BeTrue();
        File.Exists(Path.Combine(world.Home, ".codex", "AGENTS.md")).Should().BeFalse();
    }

    [Fact]
    public void AgyHasNoInstructionTarget_BecauseItsFileCouldNotBeMeasured()
    {
        InstallTargets.For(Client.Agy).Select(target => target.Kind).Should().Equal("mcp");
        InstallTargets.For(Client.Claude).Select(target => target.Kind).Should().Equal("mcp", "hook");
        InstallTargets.For(Client.Codex).Select(target => target.Kind).Should().Equal("mcp", "instructions");
    }
}
