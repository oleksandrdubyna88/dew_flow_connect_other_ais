using CoaiMcp.Runners.Kimi;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The model block coai writes into an owned Kimi home (E4.2 brief sections 1 and 6, S2.7–S2.10): the person's own
/// <c>[models."id"]</c> block when they have one and it is whole and on the Kimi Code plan, else the pinned entry of
/// <c>shared/kimi-model-blocks.json</c> — one source for the curated list as well.
/// </summary>
public sealed class KimiModelBlocksTests
{
    private const string Persons = "kimi-code/kimi-for-coding";

    private static KimiModelBlocks Pinned() =>
        KimiModelBlocks.Builtin.Should().BeOfType<BlocksLoad.Loaded>("the embedded table must load").Subject.Blocks;

    [Fact]
    public void TheModelBlock_IsThePersonsWhenTheyHaveOne()
    {
        var read = KimiModelBlocks.For(Persons, KimiFixture.OwnerConfig(), Pinned());

        var block = read.Should().BeOfType<BlockRead.Found>(read is BlockRead.Refused refused ? $"got: '{refused.Reason}'" : string.Empty).Subject.Block;
        block.MaxContextSize.Should().Be(1048576, "the owner's own block says so, where the pinned table's would say 262144");
        block.Capabilities.Should().Equal("tool_use", "thinking", "image_in");
        block.Model.Should().Be("kimi-for-coding");
        block.Provider.Should().Be("managed:kimi-code");
        var slot = new KimiOauthSlot("kimi", KimiFixture.BaseUrl, "file", KimiFixture.SlotKey, KimiFixture.OauthHost);
        KimiConfig.Render(slot, block).Should().Contain("max_context_size = 1048576")
            .And.Contain("capabilities = [ \"tool_use\", \"thinking\", \"image_in\" ]");
    }

    [Fact]
    public void TheModelBlock_IsThePinnedEntry_ForEveryCuratedId()
    {
        var pinned = Pinned();
        pinned.Blocks.Should().NotBeEmpty();

        foreach (var id in pinned.Blocks.Select(block => block.Id))
        {
            var read = KimiModelBlocks.For(id, "default_model = \"x\"\n", pinned);

            var block = read.Should().BeOfType<BlockRead.Found>($"{id} has a pinned entry").Subject.Block;
            block.Id.Should().Be(id);
            block.Provider.Should().Be("managed:kimi-code");
            block.Source.Should().Contain("RESULTS_kimi_confinement.md", "every pinned value names the section that measured it");
        }
    }

    public static TheoryData<string, string> Broken => new()
    {
        { "another provider", KimiFixture.OwnerConfig().Replace("provider = \"managed:kimi-code\"\nmodel = \"kimi-for-coding\"", "provider = \"moonshot-ai\"\nmodel = \"kimi-for-coding\"") },
        { "a multi-line capabilities", KimiFixture.OwnerConfig().Replace("capabilities = [ \"tool_use\", \"thinking\", \"image_in\" ]", "capabilities = [\n  \"tool_use\",\n  \"thinking\",\n]") },
        { "no max_context_size", KimiFixture.OwnerConfig().Replace("max_context_size = 1048576\n", string.Empty) },
    };

    [Theory]
    [MemberData(nameof(Broken))]
    public void APersonsBlockOnAnotherProvider_OrUnreadable_IsRefused(string row, string config)
    {
        var read = KimiModelBlocks.For(Persons, config, Pinned());

        var reason = read.Should().BeOfType<BlockRead.Refused>($"{row}: a silent fall to the pinned table would write a home for other values than the person's own").Subject.Reason;
        if (row == "another provider")
        {
            reason.Should().Contain($"routes '{Persons}' through 'moonshot-ai'").And.Contain("not the Kimi Code plan").And.Contain("D10");
        }
        else
        {
            reason.Should().Contain($"[models.\"{Persons}\"] block coai cannot read whole").And.Contain("max_context_size");
        }

        reason.Should().NotContain(KimiFixture.ApiKeyCanary, "a refusal names the key and the cure, never a line of the file");
    }

    [Fact]
    public void TheCuratedList_IsTheTablesIds_InOrder_AndDefaultIsFirst()
    {
        var table = Pinned().Blocks.Select(block => block.Id).ToList();

        KimiModels.Curated.Should().Equal(table, "one source");
        KimiModels.Curated.Should().Equal("kimi-code/k3-256k", "kimi-code/k3", "kimi-code/kimi-for-coding", "kimi-code/kimi-for-coding-highspeed");
        KimiModels.Default.Should().Be("kimi-code/k3-256k").And.Be(KimiModels.Curated[0]);
        KimiModels.SeenOn.Should().Be("2.1.1");
        Pinned().SeenOn.Should().Be("2.1.1");
        KimiModels.PlanPrefix.Should().Be("kimi-code/");
    }

    [Fact]
    public void ABrokenDocument_IsBroken_WithTheSentence()
    {
        KimiModelBlocks.Parse("{ not json").Should().BeOfType<BlocksLoad.Broken>().Which.Reason.Should().Contain("shared/kimi-model-blocks.json");
        KimiModelBlocks.Parse("""{"seenOn":"2.1.1","blocks":[]}""").Should().BeOfType<BlocksLoad.Broken>("an empty table is a broken one, never an empty table that admits nothing and says so badly");
        KimiModelBlocks.Parse("""{"seenOn":"2.1.1","blocks":[{"id":"kimi-code/x"}]}""").Should().BeOfType<BlocksLoad.Broken>("an entry that lacks its values is broken");
    }
}
