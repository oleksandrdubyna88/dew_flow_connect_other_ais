using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Which ladder a running server climbs, given what the operator has actually set.
/// </summary>
/// <remarks>
/// The precedence exists because of a real hazard the plan round named twice, from two vendors: a
/// deployment that had deliberately set <c>COAI_RATE_LIMIT_BACKOFF_SECONDS=45</c> would silently
/// stop meaning "one retry at 45 seconds" and start meaning four retries at the shipped ladder, and
/// nothing anywhere would say so. A setting somebody wrote down outranks a default.
/// </remarks>
public sealed class RetryLadderSettingsTests
{
    private static Func<string, string?> Env(params (string Name, string Value)[] set) =>
        name => set.FirstOrDefault(e => e.Name == name).Value;

    [Fact]
    public void NothingConfigured_ClimbsTheShippedLadder()
    {
        PanelSettings.FromEnvironment(Env()).RetryLadder.Should().Equal(RetryLadder.Default);
    }

    [Fact]
    public void TheLegacyBackoffAlone_StaysOneRetryAtThatNumber()
    {
        var settings = PanelSettings.FromEnvironment(Env(("COAI_RATE_LIMIT_BACKOFF_SECONDS", "45")));

        settings.RetryLadder.Should().Equal(TimeSpan.FromSeconds(45));
        settings.RateLimitBackoff.Should().Be(TimeSpan.FromSeconds(45), "the panel's own setting is unchanged");
        settings.Unrecognised.Should().BeEmpty("nothing here is unknown — it is the older way of saying it");
    }

    [Fact]
    public void TheLadderWins_WhenBothAreSet()
    {
        PanelSettings.FromEnvironment(Env(
                ("COAI_RETRY_BACKOFF", "5,30"),
                ("COAI_RATE_LIMIT_BACKOFF_SECONDS", "45")))
            .RetryLadder.Should().Equal(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// A ladder nobody can read falls back — and SAYS so, in the one place a person already looks
    /// when a setting seems not to have applied.
    /// </summary>
    [Fact]
    public void ALadderThatCannotBeRead_FallsBackAndIsReported()
    {
        var settings = PanelSettings.FromEnvironment(Env(("COAI_RETRY_BACKOFF", "5,nonsense,60")));

        settings.RetryLadder.Should().Equal(RetryLadder.Default);
        settings.Unrecognised.Should().ContainSingle()
            .Which.Should().Contain("COAI_RETRY_BACKOFF").And.Contain("5,nonsense,60");
    }

    [Fact]
    public void AnUnreadableLadder_StillDefersToTheLegacyBackoff()
    {
        var settings = PanelSettings.FromEnvironment(Env(
            ("COAI_RETRY_BACKOFF", "nonsense"),
            ("COAI_RATE_LIMIT_BACKOFF_SECONDS", "45")));

        settings.RetryLadder.Should().Equal(
            [TimeSpan.FromSeconds(45)],
            "an unreadable new setting must not overrule a readable old one");
        settings.Unrecognised.Should().ContainSingle();
    }
}
