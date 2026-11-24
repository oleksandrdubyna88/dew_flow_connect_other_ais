using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>Scenario 11: a read-only file or folder is <c>refused</c> with the OS's reason, before the lock and the backup — and no temp file is left.</summary>
public sealed class AReadOnlyFileIsRefusedWithTheOsReasonTests
{
    private static bool IsRoot => !OperatingSystem.IsWindows() && Environment.UserName == "root";

    [Fact]
    public async Task ARead_OnlyClaudeJson_IsRefused_AndCodexIsDone()
    {
        Assert.SkipWhen(IsRoot, "root ignores a file's mode, so a read-only file cannot be made here");
        using var world = InstallWorld.Create();
        world.Put(world.ClaudeJson, "{}");
        ReadOnly(world.ClaudeJson, on: true);
        try
        {
            var (code, output, _) = await world.RunAsync("--binary", world.Binary);

            code.Should().Be(78);
            output.Result("claude").Should().Be("refused");
            output.Client("claude").GetProperty("reason").GetString().Should().Contain("cannot be written");
            world.ClaudeJsonText.Should().Be("{}");
            Directory.GetFiles(world.Home, "*", SearchOption.AllDirectories).Where(file => file.Contains(".tmp") || file.Contains(".coai-probe")).Should().BeEmpty();
            output.Result("codex").Should().Be("changed");
        }
        finally
        {
            ReadOnly(world.ClaudeJson, on: false);
        }
    }

    [Fact]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public async Task ARead_OnlyHomeFolder_IsRefusedForTheFileItWouldMake_NothingIsLeftBehind()
    {
        Assert.SkipWhen(IsRoot || OperatingSystem.IsWindows(), "needs a folder mode a non-root POSIX user is held to");
        using var world = InstallWorld.Create(codex: false);
        var tree = world.HomeHash();
        File.SetUnixFileMode(world.Home, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            var (code, output, _) = await world.RunAsync("--binary", world.Binary);

            code.Should().Be(78);
            output.Client("claude").GetProperty("reason").GetString().Should().Contain("cannot be written");
            world.Calls.Should().BeEmpty();
        }
        finally
        {
            File.SetUnixFileMode(world.Home, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        world.HomeHash().Should().Be(tree);
        Directory.GetFileSystemEntries(world.Home).Should().BeEmpty("no probe file, no state folder");
    }

    private static void ReadOnly(string path, bool on)
    {
        if (OperatingSystem.IsWindows())
        {
            File.SetAttributes(path, on ? FileAttributes.ReadOnly : FileAttributes.Normal);
        }
        else
        {
            File.SetUnixFileMode(path, on ? UnixFileMode.UserRead : UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
