using System.Reflection;
using CoaiMcp.Core.Authors;
using FluentAssertions;
using Xunit;
using static CoaiMcp.Tests.AssignmentViews;

namespace CoaiMcp.Tests;

/// <summary>
/// Association is evidence, not inference: what an agent is called, what it requested and when it launched say nothing about which
/// repository it worked in, and no input member is a working directory (Q8: a subagent's <c>cwd</c> is the session's folder, and its
/// writes land elsewhere).
/// </summary>
/// <remarks>
/// <c>NoInputMemberIsACwd</c> is a positive: a shell passes it, and its break-it — a <c>Cwd</c> member that <c>SessionRung</c> reads — is what
/// proves it (§S2.2b G12d).
/// </remarks>
public sealed class EvidenceNotInferenceTests
{
    private const string X = AttributionCases.X;

    private static AttributionScenario Window() => AttributionScenario.For().Gate("A", X).Opened(0).Code(1, 60, 70);

    [Theory]
    [InlineData("coder")]
    [InlineData("Explore")]
    [InlineData("feature-dev:code-reviewer")]
    public void AnAgentTypeNamedCoder_AssociatesNothing(string type)
    {
        var scenario = Window().Agent("a1", launchedAt: 4, type: type, requested: "coder").Message("a1", 10);

        scenario.Assign("A", "CodeReview", 1).Msg("a1", 10).Should().Match<Placement>(p => p.Association == Association.Ambiguous && p.Rung == Rung.NoEvidence);
    }

    [Fact]
    public void ALaunchInsideTheWindow_AssociatesNothing()
    {
        var assignment = Window().Agent("a1", launchedAt: 4).Assign("A", "CodeReview", 1);

        assignment.Launch("a1").Should().Match<LaunchPlacement>(l => l.Association == Association.Ambiguous && l.Rung == Rung.NoEvidence);
    }

    [Fact]
    public void AnAgentWhoseOnlyWritesAreOutside_HasNoEvidence()
    {
        var assignment = Window().Agent("a1", launchedAt: 4).Message("a1", 10).WriteOutside("a1", 10).Assign("A", "CodeReview", 1);

        assignment.Msg("a1", 10).Rung.Should().Be(Rung.NoEvidence);
        assignment.Writes.Should().ContainSingle(write => write.Where == WriteWhere.Outside && write.Root.Length == 0);
    }

    [Fact]
    public void NoInputMemberIsACwd()
    {
        var records = typeof(AttributionInput).Assembly.GetTypes().Where(type => type.Namespace == typeof(AttributionInput).Namespace && type.IsPublic);
        var members = records.SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)).Select(property => property.Name);

        records.Should().NotBeEmpty();
        members.Should().NotContain(name => name.Contains("cwd", StringComparison.OrdinalIgnoreCase), "no rule may read a working directory");
        members.Should().Contain(nameof(ObservedAgent.FirstLineAt), "the scan did see the input records");
    }
}
