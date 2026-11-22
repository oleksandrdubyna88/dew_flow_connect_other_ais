using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>Scenario 6: a CLI that is not there is <c>absent</c> and no file is made for it; no CLI at all is 69.</summary>
public sealed class AnAbsentCliIsReportedAbsentTests
{
    [Fact]
    public async Task OneAbsent_NoFileIsMadeForIt_AndTheExitIsZero()
    {
        using var world = InstallWorld.Create(codex: false);

        var (code, output, err) = await world.RunAsync("--binary", world.Binary);

        code.Should().Be(0, err);
        output.Result("codex").Should().Be("absent");
        output.Client("codex").GetProperty("found").GetBoolean().Should().BeFalse();
        File.Exists(world.CodexToml).Should().BeFalse();
        Directory.Exists(Path.GetDirectoryName(world.CodexToml)).Should().BeFalse();
        output.Result("claude").Should().Be("changed");
    }

    [Fact]
    public async Task NoCliAtAll_Is69_AndTheObjectSaysEachIsAbsent()
    {
        using var world = InstallWorld.Create(claude: false, codex: false);

        var (code, output, _) = await world.RunAsync("--binary", world.Binary);

        code.Should().Be(69);
        output.GetProperty("exitCode").GetInt32().Should().Be(69);
        output.GetProperty("clients").EnumerateArray().Select(one => one.GetProperty("result").GetString()).Should().OnlyContain(result => result == "absent");
    }
}
