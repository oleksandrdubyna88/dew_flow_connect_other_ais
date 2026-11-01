using System.Text.Json;
using CoaiMcp.Core.Authors;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A computed property that serialises into every row is a stored copy of a rule (E4 §S2.2a, E5 §8.3): <c>IsMain</c> is
/// <c>[JsonIgnore]</c>, so a serialised key has exactly one member, <c>id</c>.
/// </summary>
public sealed class AnAgentKeyHasOneSerialisedMemberTests
{
    [Fact]
    public void ASerialisedKey_HasExactlyOneMember_Id()
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new AgentKey("a1")));

        document.RootElement.EnumerateObject().Select(member => member.Name.ToLowerInvariant()).Should().Equal("id");
    }

    [Fact]
    public void MainIsTheKeyNamedMain()
    {
        AgentKey.Main.IsMain.Should().BeTrue();
        new AgentKey("a1").IsMain.Should().BeFalse();
    }
}
