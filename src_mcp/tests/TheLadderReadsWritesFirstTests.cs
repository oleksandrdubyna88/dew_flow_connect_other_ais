using CoaiMcp.Core.Authors;
using FluentAssertions;
using Xunit;
using static CoaiMcp.Tests.AssignmentViews;

namespace CoaiMcp.Tests;

/// <summary>
/// The first rung of the ladder (E4 §S2.4): an agent's own writes in a window. Here, elsewhere, both (ambiguous, two repositories),
/// or none that count (outside every checkout) — and a shell command is never a write.
/// </summary>
/// <remarks><c>ABashCallIsNotAWrite</c> is a positive: nothing but its plant (the builder admitting a shell call as a write) can fail it.</remarks>
public sealed class TheLadderReadsWritesFirstTests
{
    private static AttributionScenario Window() => AttributionScenario.For().Gate("A", AttributionCases.X).Opened(0).Code(1, 60, 70).Agent("a1", launchedAt: 4);

    private static WindowAssignment Round(AttributionScenario scenario) => scenario.Assign("A", "CodeReview", 1);

    [Fact]
    public void AWriteUnderRepoPath_IsWroteHere()
    {
        var assignment = Round(Window().Message("a1", 10).Write("a1", 10, AttributionCases.X + "/src/a.cs"));

        assignment.Msg("a1", 10).Should().Match<Placement>(p => p.Association == Association.WroteHere && p.Rung == Rung.Write && p.OwnerGate == "A");
    }

    [Fact]
    public void AWriteIntoASubmoduleUnderRepoPath_IsWroteHere()
    {
        var scenario = Window().Message("a1", 10).Write("a1", 10, AttributionCases.X + "/sub/a.cs").Repositories(AttributionCases.X + "/sub");

        Round(scenario).Msg("a1", 10).Association.Should().Be(Association.WroteHere, "the submodule root is inside the gate's repo_path");
        Round(scenario).Writes.Single().Should().Match<WritePlacement>(w => w.Root == AttributionCases.X + "/sub" && w.Where == WriteWhere.Here);
    }

    [Fact]
    public void AWriteIntoAnotherCheckout_IsWroteElsewhere_WithItsRoot()
    {
        var scenario = Window().Message("a1", 10).Write("a1", 10, AttributionCases.Y + "/b.cs").Repositories(AttributionCases.Y);
        var assignment = Round(scenario);

        assignment.Msg("a1", 10).Association.Should().Be(Association.WroteElsewhere);
        assignment.Msg("a1", 10).OwnerGate.Should().BeEmpty("nobody of this gate owns it");
        assignment.AssociationOf("a1", assignment.RoundWindow()).ElsewhereRoots.Should().Equal(AttributionCases.Y);
        assignment.Writes.Single().Where.Should().Be(WriteWhere.Elsewhere);
    }

    [Fact]
    public void AWriteOutsideEveryCheckout_IsNoEvidence()
    {
        var assignment = Round(Window().Message("a1", 10).WriteOutside("a1", 10));

        assignment.Msg("a1", 10).Should().Match<Placement>(p => p.Association == Association.Ambiguous && p.Rung == Rung.NoEvidence, "it falls through to the session rung, which has no destination");
        assignment.Writes.Single().Where.Should().Be(WriteWhere.Outside);
    }

    [Fact]
    public void WritesHereAndElsewhere_AreAmbiguousTwoRepositories()
    {
        var scenario = Window().Message("a1", 10).Message("a1", 20).Write("a1", 10, AttributionCases.X + "/a.cs").Write("a1", 20, AttributionCases.Y + "/b.cs").Repositories(AttributionCases.Y);
        var assignment = Round(scenario);

        assignment.Msg("a1", 10).Should().Match<Placement>(p => p.Association == Association.Ambiguous && p.Rung == Rung.TwoRepositories);
        assignment.AssociationOf("a1", assignment.RoundWindow()).Should().Match<AgentAssociation>(a => a.Rung == Rung.TwoRepositories);
    }

    [Fact]
    public void ABashCallIsNotAWrite()
    {
        var scenario = Window().Message("a1", 10).ShellCall("a1", 10, AttributionCases.X + "/a.cs");

        scenario.Observation().Writes.Should().BeEmpty("the reader never counts a shell command as a write");
        Round(scenario).Msg("a1", 10).Should().Match<Placement>(p => p.Association == Association.Ambiguous && p.Rung == Rung.NoEvidence);
    }

    [Fact]
    public void AWriteIsEvidenceOnlyInTheWindowItsMessageIsIn()
    {
        var scenario = AttributionScenario.For().Gate("A", AttributionCases.X).Opened(0).Code(1, 60, 70).Code(2, 120, 130)
            .Agent("a1", launchedAt: 4).Message("a1", 10).Message("a1", 90).Write("a1", 10, AttributionCases.X + "/a.cs");

        scenario.Assign("A", "CodeReview", 2).Msg("a1", 90).Rung.Should().Be(Rung.NoEvidence, "the write of the first window says nothing of the second");
    }
}
