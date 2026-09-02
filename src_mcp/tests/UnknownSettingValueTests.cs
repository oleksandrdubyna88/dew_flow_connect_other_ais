using Xunit;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using FluentAssertions;

namespace CoaiMcp.Tests;

/// <summary>
/// A setting whose VALUE this build does not know is reported, never silently defaulted.
/// </summary>
/// <remarks>
/// <para>Reported from the panel: <i>"I set this and it still keeps asking me — looks like the
/// settings are not applied without a restart again."</i> They were applied. The settings file said
/// <c>COAI_ON_EXHAUSTED: good_enough</c>, the environment overrode nothing, and the reload worked —
/// the RUNNING SERVER was a build from the day before <c>good_enough</c> existed. It read the value,
/// did not recognise it, and fell through to <c>Human</c>. So the gate kept asking a person, which
/// is precisely the behaviour that had been switched off.</para>
///
/// <para>The diagnosis cost twenty minutes and three wrong hypotheses, all of which the server could
/// have ruled out in one line. An unrecognised value is a MISMATCH between two halves of a product
/// that are versioned separately, and it is the one thing the older half knows and the newer half
/// cannot see.</para>
///
/// <para>The fallback itself stays: refusing to start because a future panel wrote a future policy
/// would be worse than doing the conservative thing. What changes is that it says so.</para>
/// </remarks>
public class UnknownSettingValueTests
{
    private static Func<string, string?> With(string value) =>
        name => name == "COAI_ON_EXHAUSTED" ? value : null;

    [Fact]
    public void AKnownPolicyIsTakenAndNothingIsReported()
    {
        var settings = PanelSettings.FromEnvironment(With("good_enough"));

        settings.Rounds.OnExhausted.Should().Be(StagePolicy.GoodEnough);
        settings.Unrecognised.Should().BeEmpty();
    }

    [Theory]
    [InlineData("continue", StagePolicy.Continue)]
    [InlineData("escalate", StagePolicy.Escalate)]
    [InlineData("goodenough", StagePolicy.GoodEnough)]
    [InlineData("GOOD_ENOUGH", StagePolicy.GoodEnough)]
    [InlineData("human", StagePolicy.Human)]
    public void EveryPolicyThisBuildKnows(string value, StagePolicy expected)
    {
        PanelSettings.FromEnvironment(With(value)).Rounds.OnExhausted.Should().Be(expected);
    }

    [Fact]
    public void AnUnknownPolicyStillFallsBackToAskingAPerson()
    {
        // The safe end of the range: a policy this build cannot honour must not silently proceed
        // over open findings, so the fallback is the one that stops.
        PanelSettings.FromEnvironment(With("ship_it_anyway")).Rounds.OnExhausted
            .Should().Be(StagePolicy.Human);
    }

    [Fact]
    public void AnUnknownPolicySAYSSo_NamingTheSettingAndTheValue()
    {
        var settings = PanelSettings.FromEnvironment(With("ship_it_anyway"));

        settings.Unrecognised.Should().ContainSingle()
            .Which.Should().Contain("COAI_ON_EXHAUSTED")
            .And.Contain("ship_it_anyway")
            .And.Contain("asking a person");
    }

    [Fact]
    public void TheMessageTellsSomebodyWhatToDoAboutIt()
    {
        // The actual case: the panel is newer than the server. "Unknown value" alone would have sent
        // the same twenty minutes into the settings file rather than into the binary's date.
        var settings = PanelSettings.FromEnvironment(With("good_enough_but_typoed"));

        settings.Unrecognised.Single().Should().Contain("update");
    }

    [Fact]
    public void AnEmptyOrAbsentValueIsNotAComplaint()
    {
        // Absent means "nobody set it", which is the normal state of most settings and not a
        // mismatch to report.
        PanelSettings.FromEnvironment(_ => null).Unrecognised.Should().BeEmpty();
        PanelSettings.FromEnvironment(With(string.Empty)).Unrecognised.Should().BeEmpty();
    }
}
