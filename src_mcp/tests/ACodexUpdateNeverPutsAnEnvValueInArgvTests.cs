using CoaiMcp.Install;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// E3 plan gate, finding 1: <c>codex mcp add --env K=V</c> has no stdin form, so an env VALUE passed again on an update sits in the vendor child's argv. Project rule: no secret ever reaches argv.
/// Only the keys the extension writes (<c>COAI_DATA_DIR</c>, <c>COAI_DATA_SIDE</c>) are known not to be secret; an entry with any other key is refused — by name — and nothing is run for it.
/// </summary>
public sealed class ACodexUpdateNeverPutsAnEnvValueInArgvTests
{
    private const string Sentinel = "sentinel-secret-7f3a9c";

    private const string EntryWithASecret = """
        [mcp_servers.coai]
        command = "/old/coai/versions/1.0.0/coai-mcp"
        args = ["--x"]

        [mcp_servers.coai.env]
        COAI_DATA_DIR = "/data/coai"
        COAI_API_TOKEN = "sentinel-secret-7f3a9c"
        """;

    [Fact]
    public async Task AnEntryWithAnotherKey_IsRefusedByName_CodexIsNeverCalled_AndTheOthersAreDone()
    {
        using var world = InstallWorld.Create();
        InstallOutput.WithOldEntries(world);
        world.Put(world.CodexToml, EntryWithASecret);

        var (code, output, err) = await world.RunAsync("--binary", world.Binary);

        code.Should().Be(78);
        output.Result("codex").Should().Be("refused");
        output.Action("codex").Should().Be("refuse");
        output.Client("codex").GetProperty("reason").GetString().Should().Contain("COAI_API_TOKEN").And.NotContain(Sentinel);
        output.Result("claude").Should().Be("changed", "the other CLIs are done");
        world.Calls.Should().NotContain(line => line.StartsWith("codex mcp add") || line.StartsWith("codex mcp remove"), "nothing is run for a refused target");
        world.CodexTomlText.Should().Be(EntryWithASecret, "a refused file is untouched");
        world.BackupFolders.Should().ContainSingle();
    }

    [Fact]
    public async Task TheSecretValue_IsNowhereInArgv_TheOutputOrTheLog()
    {
        using var world = InstallWorld.Create();
        InstallOutput.WithOldEntries(world);
        world.Put(world.CodexToml, EntryWithASecret);
        world.Steer(Client.Codex, fail: "add", echo: true);

        var (_, output, err) = await world.RunAsync("--binary", world.Binary);

        world.Calls.Should().NotContain(line => line.Contains(Sentinel));
        output.ToString().Should().NotContain(Sentinel);
        err.Should().NotContain(Sentinel);
        File.ReadAllText(Path.Combine(world.BackupFolders.Single(), "manifest.json")).Should().NotContain(Sentinel, "a backup manifest is a log line of the run too");
    }

    [Fact]
    public async Task ADryRun_SaysTheSameAndRunsNothing()
    {
        using var world = InstallWorld.Create();
        InstallOutput.WithOldEntries(world);
        world.Put(world.CodexToml, EntryWithASecret);

        var (code, output, _) = await world.RunAsync("--binary", world.Binary, "--dry-run");

        code.Should().Be(78, "a dry run answers the code the real run would");
        output.Result("codex").Should().Be("refused");
        world.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task TheTwoKeysTheExtensionWrites_StillCrossToCodexWithTheirArguments()
    {
        using var world = InstallWorld.Create();
        InstallOutput.WithOldEntries(world);

        var (code, _, err) = await world.RunAsync("--binary", world.Binary);

        code.Should().Be(0, err);
        var add = world.Calls.Single(line => line.StartsWith("codex mcp add"));
        add.Should().Contain("--env COAI_DATA_DIR=/data/coai --env COAI_DATA_SIDE=wsl coai -- ").And.Contain($"{world.Binary} --x |");
    }
}
