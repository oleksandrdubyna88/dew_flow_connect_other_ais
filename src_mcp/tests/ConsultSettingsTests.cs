using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The five consult settings as they arrive from the environment — which is also the file the panel
/// writes, since both go through one parser.
/// </summary>
/// <remarks>
/// <para>The defaults are pinned here because the PANEL ships its own copy of them and writes a key
/// only when it differs: a default that moves on one side alone makes a pristine install read one
/// number off the screen while another one runs. <c>consultDefaultsAgree.test.ts</c> is the other
/// half of that guarantee and reads these numbers out of the C#.</para>
/// </remarks>
public sealed class ConsultSettingsTests
{
    private static PanelSettings From(params (string Name, string Value)[] env) =>
        PanelSettings.FromEnvironment(name =>
            env.FirstOrDefault(row => row.Name == name) is { Name.Length: > 0 } found ? found.Value : null);

    [Fact]
    public void WithNothingConfigured_TheShippedNumbersAreInForce()
    {
        var settings = From();

        settings.ConsultTurns.Should().Be(5);
        settings.ConsultCallsPerSession.Should().Be(10);
        settings.ConsultIdle.Should().Be(TimeSpan.FromMinutes(15));
        settings.ConsultEnabled.Should().BeTrue("absence is not a person switching something off");
        settings.Consultants.Should().Equal(ConsultantRouting.Shipped);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("FALSE")]
    [InlineData("False")]
    public void TheFourSpellingsOfFalse_SwitchTheToolOff(string value)
    {
        From(("COAI_CONSULT_ENABLED", value)).ConsultEnabled.Should().BeFalse();
    }

    /// <summary>
    /// Everything else leaves it ON, and that asymmetry is deliberate.
    /// </summary>
    /// <remarks>
    /// A consultant wrongly available costs nothing, because nothing calls it until an agent is
    /// stuck. One wrongly unavailable is a refusal in the one moment it was wanted — so a typo, a
    /// value some shell mangled, and a spelling this parser has never heard of all leave the tool
    /// working. The same rule, and the same method, as a reviewer's own switch.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("no")]
    [InlineData("0.0")]
    [InlineData("nope")]
    [InlineData("true")]
    public void EverythingElse_LeavesItOn(string value)
    {
        From(("COAI_CONSULT_ENABLED", value)).ConsultEnabled.Should().BeTrue();
    }

    [Fact]
    public void TheCapsAreRead_AndNonsenseFallsBackRatherThanReachingZero()
    {
        var configured = From(
            ("COAI_CONSULT_TURNS", "3"),
            ("COAI_CONSULT_CALLS_PER_SESSION", "4"),
            ("COAI_CONSULT_IDLE_MINUTES", "30"));

        configured.ConsultTurns.Should().Be(3);
        configured.ConsultCallsPerSession.Should().Be(4);
        configured.ConsultIdle.Should().Be(TimeSpan.FromMinutes(30));

        // A cap of zero is a feature that refuses every call while the panel shows it switched on,
        // which is the one state nothing on screen could explain.
        var silly = From(
            ("COAI_CONSULT_TURNS", "0"),
            ("COAI_CONSULT_CALLS_PER_SESSION", "-2"),
            ("COAI_CONSULT_IDLE_MINUTES", "half an hour"));

        silly.ConsultTurns.Should().Be(5);
        silly.ConsultCallsPerSession.Should().Be(10);
        silly.ConsultIdle.Should().Be(TimeSpan.FromMinutes(15));
    }
}
