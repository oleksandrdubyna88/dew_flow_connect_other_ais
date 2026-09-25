using CoaiMcp.Core.Consultation;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What a consultation is FOR — being stuck, a group of epics, or a risky piece — read from the three
/// arguments <c>consult</c> gained in <c>todo/PLAN_consult_on_a_cadence.md</c>, epic 2.
/// </summary>
/// <remarks>
/// One canonical path from the caller's spelling to the identity the gate matches on (epic 2's plan round,
/// codex): <c>research\PLAN_x.md</c> and <c>todo/PLAN_x.md</c> are one plan, and <c>04-6</c> and
/// <c>4-6</c> are one group. Without it a consultation could be taken and never count.
/// </remarks>
public sealed class ConsultAimTests
{
    [Theory]
    [InlineData("")]
    [InlineData("stuck")]
    [InlineData(" STUCK ")]
    public void NoKindOrStuck_IsAStuckConsultation_WhateverElseIsSent(string kind)
    {
        var (aim, refusal) = ConsultAim.Parse(kind, "todo/PLAN_x.md", "4-6");

        refusal.Should().BeEmpty();
        aim.IsStuck.Should().BeTrue();
        aim.Kind.Should().Be(ConsultKinds.Stuck);
        aim.Epics.Should().BeEmpty("a stuck consultation is about no group");
    }

    [Theory]
    [InlineData("4-6", "4-6")]
    [InlineData("04-06", "4-6")]
    [InlineData(" 7 ", "7")]
    [InlineData("7-7", "7")]
    public void ACadenceGroup_IsReadIntoTheRangeTheGateMatches(string epics, string canonical)
    {
        var (aim, refusal) = ConsultAim.Parse("cadence", "todo/PLAN_x.md", epics);

        refusal.Should().BeEmpty();
        aim.Kind.Should().Be(ConsultKinds.Cadence);
        aim.Epics.Should().Be(canonical);
    }

    [Theory]
    [InlineData("7", "7")]
    [InlineData("7/7.2", "7/7.2")]
    [InlineData(" 07 / 7.2 ", "7/7.2")]
    public void ARiskItem_IsReadIntoTheKeyTheGateMatches(string epics, string canonical) =>
        ConsultAim.Parse("risk", "todo/PLAN_x.md", epics).Aim.Epics.Should().Be(canonical);

    [Fact]
    public void ThePlan_IsKeptAsWrittenAndKeyedByItsFileName()
    {
        var (aim, _) = ConsultAim.Parse("cadence", @"research\PLAN_X.md", "1-3");

        aim.Plan.Should().Be("research/PLAN_X.md");
        aim.PlanKey.Should().Be(ConsultAim.Parse("cadence", "todo/plan_x.md", "1-3").Aim.PlanKey);
    }

    [Theory]
    [InlineData("cadence", "", "1-3", "plan")]
    [InlineData("cadence", "todo/PLAN_x.md", "", "epics")]
    [InlineData("risk", "", "", "plan")]
    public void ACadenceOrRiskConsultation_WithoutItsPlanOrEpics_IsRefused(string kind, string plan, string epics, string missing) =>
        ConsultAim.Parse(kind, plan, epics).Refusal.Should().Contain(kind).And.Contain($"no {missing}");

    [Theory]
    [InlineData("cadence", "6-4")]
    [InlineData("cadence", "4/6")]
    [InlineData("cadence", "٤-٦")]
    [InlineData("risk", "7-9")]
    [InlineData("risk", "7/x")]
    // Epic 3's code round (gemini): a story of another epic is a contradiction, not a key.
    [InlineData("risk", "2/7.2")]
    public void AMalformedEpics_IsRefusedNamingTheShape(string kind, string epics) =>
        ConsultAim.Parse(kind, "todo/PLAN_x.md", epics).Refusal.Should().Contain($"'{epics}'");

    [Theory]
    // Epic 2's code round (gemini): the plan is a repo-relative path, and a key or an audit record built
    // from '../../elsewhere.md' or an absolute path would name something outside the repository.
    [InlineData("../../secret.md")]
    [InlineData("todo/../../x.md")]
    [InlineData("/etc/PLAN_x.md")]
    [InlineData(@"C:\work\PLAN_x.md")]
    public void APlanOutsideTheRepository_IsRefused(string plan) =>
        ConsultAim.Parse("cadence", plan, "1-3").Refusal.Should().Contain("repo-relative").And.Contain(plan);

    [Fact]
    public void AnUnknownKind_IsRefusedNamingTheThree() =>
        ConsultAim.Parse("urgent", "", "").Refusal.Should().Contain("stuck").And.Contain("cadence").And.Contain("risk").And.Contain("'urgent'");

    [Theory]
    [InlineData("stuck", "consult")]
    [InlineData("cadence", "consult-cadence")]
    [InlineData("risk", "consult-risk")]
    public void EachKindHasItsOwnPrompt(string kind, string prompt) =>
        ConsultKinds.PromptId(kind).Should().Be(prompt);

    [Fact]
    public void AKindThisBuildDoesNotKnow_IsPromptedAsStuck() =>
        ConsultKinds.PromptId("a-newer-kind").Should().Be("consult", "a record from a newer build still gets a consultant");
}
