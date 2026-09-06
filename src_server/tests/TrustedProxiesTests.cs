using System.Net;
using FluentAssertions;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>
/// Who may claim, through <c>X-Forwarded-*</c>, to be speaking for somebody else.
/// </summary>
/// <remarks>
/// An earlier draft trusted every peer. Two reviewers found it on this story's code round, from
/// two angles: a direct caller could send <c>X-Forwarded-Proto: https</c> and walk past the HTTPS
/// check, or forge <c>X-Forwarded-For</c> and move into another person's rate-limit partition.
/// </remarks>
public sealed class TrustedProxiesTests
{
    [Fact]
    public void TheDefault_IsLoopbackAndThePrivateRanges_AndNothingPublic()
    {
        var trusted = TrustedProxies.Default;

        // IPNetwork normalises a base address to the network's own, so this is 127.0.0.0/8.
        trusted.Should().Contain(n => n.BaseAddress.Equals(IPAddress.Parse("127.0.0.0")));
        trusted.Should().Contain(n => n.BaseAddress.Equals(IPAddress.Parse("10.0.0.0")));
        trusted.Should().Contain(n => n.BaseAddress.Equals(IPAddress.Parse("192.168.0.0")));
        trusted.Should().NotContain(n => n.PrefixLength == 0,
            "a zero-length prefix is every address there is, which is what trusting nobody in "
            + "particular looks like written down");
    }

    [Fact]
    public void AConfiguredList_ReplacesTheDefault()
    {
        TrustedProxies.From("10.1.2.0/24, 192.168.5.0/24").Should().HaveCount(2);
    }

    /// <summary>
    /// All or nothing: a half-read set of trusted networks is a trust boundary nobody wrote, and
    /// this is the one place where failing open would mean trusting an address by accident.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-network")]
    [InlineData("10.1.2.0/24, nonsense")]
    public void AnythingItCannotReadWhole_FallsBackToTheDefault(string csv)
    {
        TrustedProxies.From(csv).Should().BeEquivalentTo(TrustedProxies.Default);
    }
}
