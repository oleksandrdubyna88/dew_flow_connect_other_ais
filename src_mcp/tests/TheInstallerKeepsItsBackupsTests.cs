using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>The backup of the contract: a full copy of each file to change, a manifest with its hash, private modes, the last ten kept, never the data folder.</summary>
public sealed class TheInstallerKeepsItsBackupsTests
{
    [Fact]
    public async Task TheBackupHoldsTheFilesAsTheyWereAndTheirHash_BeforeTheFirstWrite()
    {
        using var world = InstallWorld.Create();
        world.Put(world.ClaudeJson, InstallOutput.LivedInClaudeJson);
        world.Put(world.CodexToml, InstallOutput.LivedInCodexToml);

        var (code, output, _) = await world.RunAsync("--binary", world.Binary);

        code.Should().Be(0);
        var dir = world.BackupFolders.Single();
        dir.Should().StartWith(world.StateDir, "the state folder, never the data folder");
        File.ReadAllText(Path.Combine(dir, "claude-mcp-.claude.json")).Should().Be(InstallOutput.LivedInClaudeJson);
        File.ReadAllText(Path.Combine(dir, "codex-mcp-config.toml")).Should().Be(InstallOutput.LivedInCodexToml);
        var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "manifest.json"))).RootElement;
        manifest.GetProperty("id").GetString().Should().Be(output.GetProperty("backup").GetProperty("id").GetString());
        var claude = manifest.GetProperty("targets").EnumerateArray().Single(target => target.GetProperty("cli").GetString() == "claude" && target.GetProperty("kind").GetString() == "mcp");
        claude.GetProperty("sha256Before").GetString().Should().Be(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(InstallOutput.LivedInClaudeJson))));
        claude.GetProperty("before").ValueKind.Should().Be(JsonValueKind.Null);
        claude.GetProperty("after").GetProperty("command").GetString().Should().Be(world.Binary);
    }

    [Fact]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public async Task TheFolderIsPrivate_0700_AndTheFiles0600()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "POSIX modes");
        using var world = InstallWorld.Create();
        world.Put(world.ClaudeJson, "{}");

        await world.RunAsync("--binary", world.Binary);

        var dir = world.BackupFolders.Single();
        File.GetUnixFileMode(dir).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        foreach (var file in Directory.GetFiles(dir))
        {
            File.GetUnixFileMode(file).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite, file);
        }
    }

    [Fact]
    public async Task OnlyTheLastTenAreKept()
    {
        using var world = InstallWorld.Create(codex: false);
        var other = world.AnotherBinary("opt", "second");
        for (var run = 0; run < 12; run++)
        {
            (await world.RunAsync("--binary", run % 2 == 0 ? world.Binary : other)).Code.Should().Be(0);
        }

        world.BackupFolders.Should().HaveCount(10);
    }
}
