using CoaiMcp.Install;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>Scenario 13: a vendor command that fails or times out — the target is <c>failed</c>, the old entry is put back, the exit is 74.</summary>
public sealed class AFailedVendorCommandPutsTheOldEntryBackTests
{
    [Fact]
    public async Task AnAddThatFailsOnce_IsUndone_TheOldEntryIsBack()
    {
        using var world = InstallWorld.Create(codex: false);
        InstallOutput.WithOldEntries(world);
        world.Steer(Client.Claude, fail: "add-json:1");

        var (code, output, _) = await world.RunAsync("--binary", world.Binary);

        code.Should().Be(74);
        output.Result("claude").Should().Be("failed");
        output.Client("claude").GetProperty("reason").GetString().Should().Contain("the old entry was put back");
        InstallOutput.ClaudeEntry(world.ClaudeJsonText)!["command"]!.GetValue<string>().Should().Be("/old/coai/versions/1.0.0/coai-mcp");
        InstallOutput.ClaudeEntry(world.ClaudeJsonText)!["env"]!["COAI_DATA_DIR"]!.GetValue<string>().Should().Be("/data/coai");
    }

    [Fact]
    public async Task AnAddThatNeverWorks_SaysTheOldEntryCouldNotBePutBack_AndWhereTheBackupIs()
    {
        using var world = InstallWorld.Create(codex: false);
        InstallOutput.WithOldEntries(world);
        world.Steer(Client.Claude, fail: "add-json");

        var (code, output, _) = await world.RunAsync("--binary", world.Binary);

        code.Should().Be(74);
        output.Client("claude").GetProperty("reason").GetString().Should().Contain("could NOT be put back").And.Contain(output.GetProperty("backup").GetProperty("id").GetString()!);
        File.ReadAllText(Path.Combine(world.BackupFolders.Single(), "manifest.json")).Should().Contain("\"state\": \"failed\"");
    }

    [Fact]
    public async Task ACommandThatDoesNotFinish_IsKilledAtTheTimeout_AndReportedFailed()
    {
        using var world = InstallWorld.Create(claude: false);
        InstallOutput.WithOldEntries(world);
        world.Steer(Client.Codex, hang: "add:1");

        var (code, output, _) = await world.RunAsync(world.Deps(vendorTimeout: TimeSpan.FromSeconds(2)), "setup-clients", "--binary", world.Binary);

        code.Should().Be(74);
        output.Result("codex").Should().Be("failed");
        output.Client("codex").GetProperty("reason").GetString().Should().Contain("did not finish within 2 s");
    }

    [Fact]
    public async Task AnEnvValueNeverReachesAFailureSentence()
    {
        using var world = InstallWorld.Create(claude: false);
        InstallOutput.WithOldEntries(world);
        world.Steer(Client.Codex, fail: "add", echo: true);

        var (_, output, err) = await world.RunAsync("--binary", world.Binary);

        output.Client("codex").GetProperty("reason").GetString().Should().Contain("failed", "positive control: the failure sentence is there, quoting what the vendor was handed");
        output.ToString().Should().NotContain("/data/coai");
        err.Should().NotContain("/data/coai");
    }
}
