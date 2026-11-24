using CoaiMcp.Install;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>Scenario 12: a run killed between claude's <c>remove</c> and <c>add-json</c> — the next run reads the manifest and completes with the OLD env.</summary>
public sealed class AKilledUpdateIsFinishedByTheNextRunTests
{
    private sealed class Killed : Exception;

    [Fact]
    public async Task TheNextRunPutsTheEntryBack_WithTheEnvTheOldOneHad()
    {
        using var world = InstallWorld.Create(codex: false);
        InstallOutput.WithOldEntries(world);
        var faults = new InstallFaults((cli, kind, state) =>
        {
            if (cli == Client.Claude && state == ManifestStates.Removed)
            {
                throw new Killed();
            }
        });

        var dead = async () => await world.RunAsync(world.Deps(faults: faults), "setup-clients", "--binary", world.Binary);

        await dead.Should().ThrowAsync<Killed>();
        InstallOutput.ClaudeEntry(world.ClaudeJsonText).Should().BeNull("positive control: the run died with the old entry removed and the new one not written");

        var (code, output, err) = await world.RunAsync("--binary", world.Binary);

        code.Should().Be(0, err);
        var entry = InstallOutput.ClaudeEntry(world.ClaudeJsonText)!;
        entry["command"]!.GetValue<string>().Should().Be(world.Binary);
        entry["env"]!["COAI_DATA_DIR"]!.GetValue<string>().Should().Be("/data/coai");
        output.Client("claude").GetProperty("reason").GetString().Should().Contain("interrupted");

        var third = await world.RunAsync("--binary", world.Binary);
        third.Out.Action("claude").Should().Be("none", "the half-done line was closed");
        world.BackupFolders.Select(folder => File.ReadAllText(Path.Combine(folder, "manifest.json"))).Should().NotContain(text => text.Contains("\"state\": \"removed\""));
    }
}
