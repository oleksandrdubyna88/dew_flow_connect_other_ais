using System.Text.Json.Nodes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>Scenario 15: <c>--dry-run</c> — no file, no backup, no vendor call; the object equals the real run's but for <c>dryRun</c> and <c>backup</c>.</summary>
public sealed class ADryRunChangesNothingTests
{
    [Fact]
    public async Task ADryRun_TouchesNothing_AndTheRealRunThenAnswersTheSameObject()
    {
        using var world = InstallWorld.Create();
        InstallOutput.WithOldEntries(world);
        var tree = world.HomeHash();

        var dry = await world.RunAsync("--dry-run", "--binary", world.Binary);

        world.HomeHash().Should().Be(tree);
        world.Calls.Should().BeEmpty();
        world.BackupFolders.Should().BeEmpty();
        Directory.Exists(world.StateDir).Should().BeFalse();
        dry.Out.GetProperty("dryRun").GetBoolean().Should().BeTrue();
        dry.Out.GetProperty("backup").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);

        var real = await world.RunAsync("--binary", world.Binary);

        real.Code.Should().Be(dry.Code);
        Normal(real.Out).Should().Be(Normal(dry.Out));
        real.Out.GetProperty("dryRun").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task ADryRunAnswersTheCodeTheRealRunWould_ARefusalIs78()
    {
        using var world = InstallWorld.Create(codex: false);
        world.Put(world.ClaudeJson, "not json");

        var dry = await world.RunAsync("--dry-run", "--binary", world.Binary);

        dry.Code.Should().Be(78);
        world.Calls.Should().BeEmpty();
    }

    private static string Normal(System.Text.Json.JsonElement output)
    {
        var node = JsonNode.Parse(output.GetRawText())!.AsObject();
        node.Remove("dryRun");
        node.Remove("backup");

        return node.ToJsonString();
    }
}
