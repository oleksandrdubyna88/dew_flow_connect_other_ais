using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>Scenario 2: a second run is all <c>none</c> — no vendor call, the same file tree, no new backup.</summary>
public sealed class ASecondInstallChangesNothingTests
{
    [Fact]
    public async Task TheSecondRun_RunsNoCommand_ChangesNoByte_AndMakesNoBackup()
    {
        using var world = InstallWorld.Create();
        (await world.RunAsync("--binary", world.Binary)).Code.Should().Be(0);
        var calls = world.Calls.Count;
        var tree = world.HomeHash();
        var backups = world.BackupFolders.Count;
        calls.Should().BeGreaterThan(0, "positive control: the first run ran the vendors");

        var (code, output, err) = await world.RunAsync("--binary", world.Binary);

        code.Should().Be(0, err);
        output.Action("claude").Should().Be("none");
        output.Action("codex").Should().Be("none");
        output.Result("claude").Should().Be("unchanged");
        world.Calls.Count.Should().Be(calls, "an entry that is already right is read from the file; the vendor is not asked");
        world.HomeHash().Should().Be(tree);
        world.BackupFolders.Count.Should().Be(backups);
        output.GetProperty("backup").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public async Task AFirstRunThatFindsNothingToDo_MakesNoStateFolder()
    {
        using var world = InstallWorld.Create(claude: false, codex: false);

        var (code, _, _) = await world.RunAsync();

        code.Should().Be(69);
        Directory.Exists(world.StateDir).Should().BeFalse("no change, no lock, no backup, no folder");
    }
}
