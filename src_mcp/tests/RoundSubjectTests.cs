using CoaiMcp.Core.Rounds;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What a round is CALLED in the list a person scans. Derived from the plan, because that is the
/// only description of the work anybody has already written down.
/// </summary>
public sealed class RoundSubjectTests
{
    private static string Subject(string planText, Func<string, bool>? exists = null) =>
        RoundSubject.From(planText, exists ?? (_ => false));

    [Fact]
    public void APlanPassedAsAPath_IsNamedByItsFile()
    {
        // The tools take the plan's TEXT, but a path is the honest mistake to make, and the file
        // name is more useful to a reader than the path is.
        Subject(@"D:\rsd\dew_flow_creds_for_devs\todo\PLAN_payment_instruments.md", exists: _ => true)
            .Should().Be("PLAN_payment_instruments.md");
    }

    [Fact]
    public void APathThatDoesNotExist_IsTreatedAsText_NotAsAFileName()
    {
        Subject(@"D:\gone\PLAN_missing.md").Should().Be(@"D:\gone\PLAN_missing.md");
    }

    [Fact]
    public void APlanWithATitle_IsNamedByItsTitle()
    {
        Subject("# PLAN — payment instruments\n\n> Status: plan only.\n\nSome body text.")
            .Should().Be("PLAN — payment instruments");
    }

    [Fact]
    public void ATitleFurtherDown_IsStillFound_BecauseAPlanOftenOpensWithAQuote()
    {
        Subject("> a note from the author\n\n## The corpus variants plan\n\nbody")
            .Should().Be("The corpus variants plan");
    }

    [Fact]
    public void NoTitleAtAll_FallsBackToTheFirstLineThatSaysSomething()
    {
        Subject("\n\n   \nAdd a payment record type with three shapes.\nmore text")
            .Should().Be("Add a payment record type with three shapes.");
    }

    [Fact]
    public void ALongTitle_IsCutOnAWordBoundary()
    {
        var subject = Subject(
            "# A plan whose title goes on and on describing every single thing it intends to do to the codebase");

        subject.Should().EndWith("…");
        subject.Length.Should().BeLessThan(70);
        subject.Should().NotContain("  ");
        // Cutting mid-word reads as corruption; cutting at a space reads as brevity.
        subject.TrimEnd('…').Should().NotEndWith(" ");
        subject.Should().StartWith("A plan whose title");
    }

    [Fact]
    public void AnEmptyPlan_HasNoSubject_RatherThanAnInventedOne()
    {
        Subject("   \n  ").Should().BeEmpty();
    }

    [Theory]
    [InlineData("PlanReview", "plan review")]
    [InlineData("CodeReview", "code review")]
    public void TheStage_IsSaidTheWayAPersonWouldSayIt(string stage, string spoken) =>
        RoundSubject.StageName(stage).Should().Be(spoken);
}
