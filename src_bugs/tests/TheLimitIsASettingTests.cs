using FluentAssertions;
using Xunit;

namespace CoaiBugs.Tests;

/// <summary>
/// <c>COAI_BUGS_RATE_PER_MINUTE</c>: a setting, with a default, an off switch, and a cap that
/// refuses rather than clamps.
/// </summary>
/// <remarks>
/// The refusal at the cap is the reviewer's arithmetic — an unvalidated rate times the caller
/// ceiling is a billion timestamps — and it is a REFUSAL because the doctrine says an illegal value
/// fails naming the legal ones. That the server exits 78 on one is proved over the real binary in
/// <c>TheBuiltBinariesTests</c>; this covers every shape of the value cheaply.
/// </remarks>
public sealed class TheLimitIsASettingTests
{
    private static RatePerMinute Rate(string? raw) =>
        RatePerMinute.Parse(raw).Should().BeOfType<RatePerMinute.Parsed.Rate>().Subject.Value;

    private static string Refusal(string? raw) =>
        RatePerMinute.Parse(raw).Should().BeOfType<RatePerMinute.Parsed.Refused>().Subject.Why;

    /// <summary>Only an ABSENT variable is the default. A present-but-empty one is a value, and a wrong one.</summary>
    /// <remarks>
    /// The first version treated blank as unset, and a code round pointed out what that hides: an
    /// operator who writes <c>COAI_BUGS_RATE_PER_MINUTE=</c> in the unit's environment file, meaning
    /// to fill it in, gets the default silently instead of a refusal naming the range.
    /// </remarks>
    [Fact]
    public void UnsetMeansTheDefault()
    {
        var rate = Rate(null);

        rate.Value.Should().Be(RatePerMinute.Default);
        rate.Disabled.Should().BeFalse();
    }

    /// <summary>Zero switches the limit off — a limit nobody can switch off takes the service down at 03:00.</summary>
    [Fact]
    public void ZeroDisablesTheLimit()
    {
        var rate = Rate("0");

        rate.Disabled.Should().BeTrue();
        rate.Value.Should().Be(0);
    }

    [Fact]
    public void TheCapItselfIsAccepted() => Rate("1000").Value.Should().Be(RatePerMinute.Most);

    [Theory]
    [InlineData("1001")]
    [InlineData("999999999999")]
    [InlineData("-1")]
    [InlineData("+5")]
    [InlineData("5.0")]
    [InlineData(" 5")]
    [InlineData("ten")]
    [InlineData("")]
    [InlineData("   ")]
    public void AnythingElseIsRefusedNamingTheLegalValues(string raw)
    {
        var why = Refusal(raw);

        // An EMPTY value is refused too — that is the point of this case — but `Contain("")` throws
        // in FluentAssertions rather than passing vacuously, so the sentence is checked for the word
        // that describes it instead of for the value itself.
        if (raw.Length == 0)
        {
            why.Should().Contain("''", "a variable set to nothing is refused, not silently defaulted, and the sentence shows the nothing it read");
        }
        else
        {
            why.Should().Contain(raw, "the sentence must say what it read");
        }

        why.Should().Contain(RatePerMinute.Variable, "and which variable it read it from");
        why.Should().Contain("0 (no limit)").And.Contain("1000", "and the range that would have been accepted");
    }
}
