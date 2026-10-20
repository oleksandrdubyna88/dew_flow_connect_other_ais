using CoaiMcp.Core.Authors;
using FluentAssertions;
using Xunit;
using static CoaiMcp.Tests.AssignmentViews;

namespace CoaiMcp.Tests;

/// <summary>
/// A write into a submodule or a nested worktree under the gate's <c>repo_path</c> is <c>wrote here</c> — but its lines are not in the
/// gate checkout's diff, so S3 lists its root and call count apart (plan gate finding 0). The root stays in memory; E5 stores counts.
/// A write into the gate checkout ITSELF is never listed, also when its root arrives lower-cased.
/// </summary>
public sealed class AWriteIntoANestedCheckoutIsHereButOutsideTheGateCheckoutTests
{
    private const string X = AttributionCases.X;
    private const string Worktree = X + "/.claude/worktrees/y";

    private static AttributionScenario Window() => AttributionScenario.For().Gate("A", X).Opened(0).Code(1, 60, 70).Agent("a1", launchedAt: 4)
        .Repositories(X + "/sub", Worktree).Message("a1", 10).Message("a1", 11).Message("a1", 12);

    [Fact]
    public void ASubmoduleAndANestedWorktree_AreHereAndListed()
    {
        var authors = Window().Write("a1", 10, X + "/sub/a.cs").Write("a1", 11, Worktree + "/b.cs").Write("a1", 12, X + "/c.cs").AuthorsOf("A", "CodeReview", 1);

        authors.Rows.Should().ContainSingle().Which.Should().Match<AuthorRow>(row => row.Association == Association.WroteHere && row.WriteCalls == 3);
        authors.WritesOutsideTheGateCheckout.Should().Equal(new CheckoutWrites(Worktree, 1), new CheckoutWrites(X + "/sub", 1));
    }

    [Fact]
    public void AWriteIntoTheGateCheckoutItself_IsNotListed()
    {
        var authors = Window().Write("a1", 10, X + "/c.cs").Write("a1", 11, X + "/d.cs").AuthorsOf("A", "CodeReview", 1);

        authors.WritesOutsideTheGateCheckout.Should().BeEmpty();
    }

    /// <summary>E3's roots are lower-cased and the gate's <c>repo_path</c> is as typed: an ordinal compare would list every write into the gate checkout.</summary>
    [Fact]
    public void AGateRootArrivingLowerCased_IsStillTheGateCheckout()
    {
        var scenario = Window().Write("a1", 10, X + "/c.cs");
        var input = scenario.Build("A", "CodeReview", 1);
        var lowered = input with { Repositories = ["d:/x"] };

        var authors = RoundAuthorsBuilder.Build(lowered, RoundAttribution.Assign(lowered));

        authors.Rows.Single().WriteCalls.Should().Be(1, "the write is here");
        authors.WritesOutsideTheGateCheckout.Should().BeEmpty();
    }

    [Fact]
    public void AnUnobservedRoundListsNone()
    {
        var authors = Window().CallerUnobserved("transcript not found").AuthorsOf("A", "CodeReview", 1);

        authors.WritesOutsideTheGateCheckout.Should().BeEmpty();
    }
}
