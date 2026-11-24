using CoaiMcp.Install;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The guard of every installer scenario: each test sets HOME, USERPROFILE, CLAUDE_CONFIG_DIR and CODEX_HOME to a temp folder, and no file the installer can name is under the real home.
/// </summary>
/// <remarks>The installer writes other tools' configuration: one scenario that reached the real <c>~/.claude.json</c> would edit the owner's live Claude Code state.</remarks>
public sealed class TheTestsNeverReachTheRealHomeTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void EveryFileTheInstallerCouldNameIsInsideTheWorld_AndNotUnderTheRealHome(bool claudeConfigDir, bool codexHome)
    {
        using var world = InstallWorld.Create(claudeConfigDir: claudeConfigDir, codexHome: codexHome);
        var real = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetEnvironmentVariable("HOME"),
            Environment.GetEnvironmentVariable("USERPROFILE"),
            Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR"),
            Environment.GetEnvironmentVariable("CODEX_HOME"),
        }.Where(one => !string.IsNullOrEmpty(one)).Select(one => one!.TrimEnd('/', '\\')).Distinct().ToList();

        foreach (var path in world.Paths)
        {
            path.Should().StartWith(world.Root, $"{path} must be inside the scratch world");
            real.Where(home => !world.Root.StartsWith(home, StringComparison.Ordinal))
                .Should().NotContain(home => path.StartsWith(home + Path.DirectorySeparatorChar, StringComparison.Ordinal), $"{path} is under the real home");
        }

        world.Env["HOME"].Should().StartWith(world.Root);
        world.Env["USERPROFILE"].Should().StartWith(world.Root);
    }

    [Fact]
    public void ThePositiveControl_AHostBuiltFromTheRealEnvironmentWouldBeUnderTheRealHome()
    {
        var realHome = Environment.GetEnvironmentVariable("HOME") ?? Environment.GetEnvironmentVariable("USERPROFILE");
        Assert.SkipWhen(string.IsNullOrEmpty(realHome), "this machine names no home");

        var host = InstallHost.From(Environment.GetEnvironmentVariable, OperatingSystem.IsWindows());

        host.ClaudeJson.Should().Contain(realHome!, "so the guard above could fail: it compares against something that is really there");
    }
}
