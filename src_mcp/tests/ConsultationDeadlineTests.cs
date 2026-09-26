using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The turn deadline derived from the launch's own budget — a backstop, always past the launch.
/// </summary>
public sealed class ConsultationDeadlineTests
{
    [Fact]
    public void ItIsLongerThanTheLaunch_SoTheLaunchersOwnKillWinsAndTheTurnStaysResumable()
    {
        var reviewerTimeout = TimeSpan.FromMinutes(10);

        ConsultationDeadline.For(reviewerTimeout).Should().BeGreaterThan(reviewerTimeout,
            "a turn that outran the launch must still be bounded, and by more than the launch itself");
    }

    [Fact]
    public void TheGraceIsNeverLessThanTheDrainTheLauncherGrantsAKilledChild()
    {
        // A grace under the launcher's drain would fire mid-kill and lose the handle a killed-but-
        // accepted turn needs to resume. A one-second timeout's quarter is well under the drain.
        ConsultationDeadline.GraceFor(TimeSpan.FromSeconds(1)).Should().Be(ProcessRequest.DefaultDrainGrace);
    }

    [Fact]
    public void ForALongTimeout_TheGraceIsAQuarterOfIt()
    {
        ConsultationDeadline.GraceFor(TimeSpan.FromMinutes(20)).Should().Be(TimeSpan.FromMinutes(5));
        ConsultationDeadline.For(TimeSpan.FromMinutes(20)).Should().Be(TimeSpan.FromMinutes(25));
    }
}
