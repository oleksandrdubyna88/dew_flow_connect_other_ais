using CoaiMcp.Install;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>Scenario 9: two runs at once — one entry, no corrupt file; the second answers 0 (it found nothing to do) or 75 (it found the lock held).</summary>
public sealed class TwoRunsAtOnceLeaveOneEntryTests
{
    [Fact]
    public async Task TwoConcurrentRuns_LeaveOneEntryInEachFile_AndOnlyZeroOr75()
    {
        using var world = InstallWorld.Create();

        var codes = await Task.WhenAll(world.RunAsync("--binary", world.Binary), world.RunAsync("--binary", world.Binary));

        codes.Select(one => one.Code).Should().OnlyContain(code => code == 0 || code == 75);
        codes.Select(one => one.Code).Should().Contain(0, "one of them did the work");
        System.Text.Json.Nodes.JsonNode.Parse(world.ClaudeJsonText)!["mcpServers"]!.AsObject().Count(pair => pair.Key == "coai").Should().Be(1);
        world.CodexTomlText.Split("[mcp_servers.coai]").Length.Should().Be(2, "one table, not two");
    }

    [Fact]
    public async Task WhileTheLockIsHeld_ARunChangesNothingAndAnswers75()
    {
        using var world = InstallWorld.Create();
        var host = InstallHost.From(name => world.Env.GetValueOrDefault(name), OperatingSystem.IsWindows());
        using var held = InstallLock.TryTake(host);
        held.Should().NotBeNull();
        var tree = world.HomeHash();

        var (code, _, err) = await world.RunAsync("--binary", world.Binary);

        code.Should().Be(75);
        err.Should().Contain("another");
        world.Calls.Should().BeEmpty();
        world.HomeHash().Should().Be(tree);
    }
}
