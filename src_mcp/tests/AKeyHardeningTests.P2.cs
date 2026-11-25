using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server.Keys;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>The second security judgement of s7 (cca029ee..cb82b7ee): exposed is final, the process environment leaves for real, an unreadable pool is not an empty one.</summary>
public sealed partial class AKeyHardeningTests
{
    [Fact]
    public void P2_AnExposedKey_IsFinal_EnableRefusesIt_AndItsTextCannotComeBackByAddOrSync()
    {
        using var world = new KeyWorld();
        var row = world.AddKey("A", "a");
        var pool = new KeyPool(world.Access());
        pool.Record(row.Id, new KeyLaunchReport(KeyLaunchEnd.Exposed)).Ok.Should().BeTrue();

        world.Admin.Enable(row.Id).Ok.Should().BeFalse("the text was seen where it must never be: only a NEW key can replace it");
        world.Admin.List(false).Value!.Rows.Single().State.Should().Be(KeyStates.Dead);
        world.Admin.Remove(row.Id).Ok.Should().BeTrue();
        world.Admin.Add(new KeyInput(1, "again", KeyWorld.Token("a"), string.Empty, KeySources.Local)).Ok.Should().BeFalse("remove and add must not make the exposed text a new enabled key");
        var run = new KeysRun(world);
        run.Variables["COAI_CLAUDE_KEY_one"] = KeyWorld.Token("a");
        run.Do(string.Empty, "sync", "--json").Out.Should().NotContain("\"added\":1", "nor may a sync");
        world.Admin.List(false).Value!.Rows.Should().BeEmpty();
        world.Admin.Add(new KeyInput(1, "fresh", KeyWorld.Token("b"), string.Empty, KeySources.Local)).Ok.Should().BeTrue("the companion: a different key is added as always");
    }

    [Fact]
    public void P2_APoolWhoseMetadataCannotBeRead_AnswersFailed_NotEmpty()
    {
        using var world = new KeyWorld();
        world.AddKey("A", "a");
        File.WriteAllText(world.MetaFile("a"), "{ not json");
        var pool = new KeyPool(world.Access());

        pool.Pick().Should().BeOfType<PickResult.Failed>("keys exist but cannot be read: that is not an empty pool, and no switch may answer it");
        pool.Check().Should().BeOfType<KeyAvailability.Failed>();
    }
}
