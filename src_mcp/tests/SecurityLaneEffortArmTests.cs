using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The pure half of the security-lane effort measurement (epic 2 of todo/PLAN_local_effort_reaches_every_local_launch.md,
/// S2.1): reading an arm from the environment, the safe twin of the planted fixture, and the check that every cell's
/// launch carried its arm's effort and ceiling — so a cell that did not differ can never join the arm it claims.
/// </summary>
public sealed class SecurityLaneEffortArmTests
{
    private static Func<string, string?> Env(params (string Name, string Value)[] set) =>
        name => set.FirstOrDefault(pair => pair.Name == name).Value;

    [Fact]
    public void WithNothingSet_TheArmIsTodaysProduct_NoneAt8192_ThreeRepeats_TheUnsafeFixture()
    {
        var arm = SecurityLaneEffortArm.From(Env());

        arm.Should().Be(new SecurityLaneEffortArm("none", 8192, 3, Safe: false));
        arm.Name.Should().Be("none-8192-unsafe");
    }

    [Fact]
    public void EveryInputIsRead()
    {
        var arm = SecurityLaneEffortArm.From(Env(
            ("COAI_SECURITY_CALIBRATION_EFFORT", "high"),
            ("COAI_SECURITY_CALIBRATION_MAX_TOKENS", "16384"),
            ("COAI_SECURITY_CALIBRATION_REPEATS", "1"),
            ("COAI_SECURITY_CALIBRATION_FIXTURE", "safe")));

        arm.Should().Be(new SecurityLaneEffortArm("high", 16384, 1, Safe: true));
        arm.Name.Should().Be("high-16384-safe");
    }

    [Theory]
    [InlineData("COAI_SECURITY_CALIBRATION_EFFORT", "max", "none, low, medium, high")]
    [InlineData("COAI_SECURITY_CALIBRATION_MAX_TOKENS", "0", "1 to 131072")]
    [InlineData("COAI_SECURITY_CALIBRATION_MAX_TOKENS", "lots", "1 to 131072")]
    [InlineData("COAI_SECURITY_CALIBRATION_REPEATS", "11", "1 to 10")]
    [InlineData("COAI_SECURITY_CALIBRATION_FIXTURE", "both", "safe, unsafe")]
    public void AnInputOutsideItsRange_IsRefused_NamingWhatIsLegal(string name, string value, string legal)
    {
        var read = () => SecurityLaneEffortArm.From(Env((name, value)));

        read.Should().Throw<ArgumentException>().WithMessage($"*{name}*{legal}*");
    }

    [Fact]
    public void ACellLaunchedWithItsArmsEffortAndCeiling_HasNoMismatch()
    {
        var arm = new SecurityLaneEffortArm("low", 8192, 1, Safe: false);

        arm.Mismatches([["--ask-local", "--max-tokens", "8192", "--reasoning-effort", "low"]]).Should().BeEmpty();
    }

    [Fact]
    public void ACellThatDidNotDiffer_IsNamed_SoItCannotJoinTheArm()
    {
        var arm = new SecurityLaneEffortArm("low", 8192, 1, Safe: false);

        arm.Mismatches([
            ["--ask-local", "--max-tokens", "8192", "--reasoning-effort", "none"],
            ["--ask-local", "--max-tokens", "4096", "--reasoning-effort", "low"],
            ["--ask-local", "--max-tokens", "8192"],
        ]).Should().Equal(
            "cell 1 sent --reasoning-effort none, the arm is low",
            "cell 2 sent --max-tokens 4096, the arm is 8192",
            "cell 3 sent no --reasoning-effort, the arm is low");
    }

    [Fact]
    public void NoCellAtAll_IsAMismatch_NotAnEmptyPass()
    {
        new SecurityLaneEffortArm("none", 8192, 1, Safe: false).Mismatches([]).Should().Equal("no --ask-local launch was recorded");
    }

    [Fact]
    public void TheSafeTwin_KeepsTheGuardAndTheParameters_AndStillChangesTheQueryLine()
    {
        var safe = SecurityLaneCalibrationTests.SafeHead();
        var baseline = SecurityLaneCalibrationTests.Source(unsafeChange: false);

        safe.Should().NotBe(baseline, "a head equal to its base is a diff with nothing to review");
        safe.Should().Contain("throw new UnauthorizedAccessException()", "the tenant guard stays");
        safe.Should().Contain("@search").And.Contain("ORDER BY Id").And.NotContain("'{search}'", "the search text stays a parameter");
        safe.Replace(" ORDER BY Id", string.Empty).Should().Be(baseline, "the only change is the ordering");
    }
}
