using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The two settings an idle server's cost depends on (<c>todo/PLAN_an_idle_server_is_idle_and_starts_at_once.md</c>):
/// read through the settings like every other, defaulted when unset or unusable, and clamped — a zero sweep interval
/// would be a busy loop (plan round 1, codex).
/// </summary>
public sealed class ServerPaceTests
{
    private static ServerPace Read(string? sweep, string? reuse) => PanelSettings.FromEnvironment(name => name switch
    {
        ServerPace.SweepKey => sweep,
        ServerPace.ConsultantsReuseKey => reuse,
        _ => null,
    }).Pace;

    [Fact]
    public void Unset_IsAMinute_AndFiveMinutes()
    {
        Read(null, null).Should().Be(new ServerPace(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(300)));
        ServerPace.Default.Should().Be(Read(null, null));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("soon")]
    [InlineData("")]
    public void ZeroNegativeOrJunk_IsTheDefault(string value)
    {
        Read(value, value).Should().Be(ServerPace.Default);
    }

    [Theory]
    [InlineData("5", 10, 30)]
    [InlineData("120", 120, 120)]
    [InlineData("99999", 3_600, 86_400)]
    [InlineData("200000", 3_600, 86_400)]
    public void AValueIsTaken_WithinItsRange(string value, int sweepSeconds, int reuseSeconds)
    {
        var pace = Read(value, value);

        pace.SweepEvery.Should().Be(TimeSpan.FromSeconds(sweepSeconds));
        pace.ConsultantsReuse.Should().Be(TimeSpan.FromSeconds(reuseSeconds));
    }
}
