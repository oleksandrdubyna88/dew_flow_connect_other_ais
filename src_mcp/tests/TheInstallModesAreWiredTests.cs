using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// <c>--setup-clients</c> and <c>--install</c> as the extension and the install line start them: the built binary, in a child process, on a scratch home — stdout is ONE JSON object, a
/// bad argument is 65, and 64 stays the answer for a mode this binary has never heard of.
/// </summary>
public sealed class TheInstallModesAreWiredTests
{
    private static (int Code, string Out, string Err) Run(InstallWorld world, params string[] args)
    {
        var info = OneShotProcess.StartInfo(Path.Combine(world.Root, "data"), args);
        foreach (var (name, value) in world.Env)
        {
            if (value is null)
            {
                info.Environment.Remove(name);
            }
            else
            {
                info.Environment[name] = value;
            }
        }

        return OneShotProcess.Run(info, TimeSpan.FromMinutes(1));
    }

    [Theory]
    [InlineData("--setup-clients")]
    [InlineData("--install")]
    public void TheModesAreKnown_NotTheUsageAnOlderBinaryAnswers(string mode)
    {
        Program.Classify([mode]).Should().NotBe(Program.Startup.Usage, "64 is how a caller recognises a binary too old for the mode");
    }

    [Fact]
    public void ARealRun_PrintsOneObject_AndWritesTheEntries_ThroughTheBuiltBinary()
    {
        using var world = InstallWorld.Create();
        var binary = ServerBinary.Path;

        var (code, said, err) = Run(world, "--setup-clients", "--binary", binary);

        code.Should().Be(0, err + said);
        said.TrimEnd('\n').Should().NotContain("\n", "ONE object, on one line");
        var root = JsonDocument.Parse(said).RootElement;
        root.GetProperty("mode").GetString().Should().Be("setup-clients");
        root.GetProperty("binary").GetProperty("path").GetString().Should().Be(binary);
        root.GetProperty("binary").GetProperty("version").GetString().Should().NotBeEmpty("it asked the binary it registers");
        root.Result("claude").Should().Be("changed");
        InstallOutput.ClaudeEntry(world.ClaudeJsonText)!["command"]!.GetValue<string>().Should().Be(binary);
    }

    [Fact]
    public void TheInstallName_WithDryRun_ChangesNothing()
    {
        using var world = InstallWorld.Create();
        var tree = world.HomeHash();

        var (code, said, _) = Run(world, "--install", "--dry-run", "--binary", ServerBinary.Path);

        code.Should().Be(0);
        JsonDocument.Parse(said).RootElement.GetProperty("mode").GetString().Should().Be("install");
        world.HomeHash().Should().Be(tree);
    }

    [Theory]
    [InlineData("--setup-clients", "--shared")]
    [InlineData("--install", "--nope")]
    public void ABadArgument_Is65_NeverTheUnknownModesCode(params string[] args)
    {
        using var world = InstallWorld.Create();

        var (code, said, err) = Run(world, args);

        code.Should().Be(65);
        said.Should().BeEmpty();
        err.Should().Contain("usage");
    }

    [Fact]
    public void AModeThisBinaryHasNeverHeardOf_StillAnswers64()
    {
        using var world = InstallWorld.Create();

        Run(world, "--install-everything").Code.Should().Be(64);
    }
}
