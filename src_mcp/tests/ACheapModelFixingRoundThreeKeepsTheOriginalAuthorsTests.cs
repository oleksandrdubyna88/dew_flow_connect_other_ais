using CoaiMcp.Core.Authors;
using FluentAssertions;
using Xunit;
using static CoaiMcp.Tests.RoundAuthorsFixtures;

namespace CoaiMcp.Tests;

/// <summary>
/// The consultant's case (E4.S4, parent §6): a cheap model fixing round three must NOT relabel the cumulative diff as the fixer's. The
/// contributors to the change round N reviewed are the union of the associated activity of the windows up to N, each tagged with the
/// rounds it was active in; the fixers of round N are those active in N's own window.
/// </summary>
public sealed class ACheapModelFixingRoundThreeKeepsTheOriginalAuthorsTests
{
    private const string Caller = "c0";

    private static IReadOnlyList<RoundAuthors> Journal() =>
    [
        Observed("CodeReview", 1, Caller, Row("opus-coder"), Row("main", association: Association.SameSession, writeCalls: 0)),
        Observed("CodeReview", 2, Caller),
        Observed("CodeReview", 3, Caller, Row("sonnet-fixer", number: 3, model: "claude-sonnet-5-5", requested: "sonnet")),
    ];

    [Fact]
    public void RoundOneHasTheOriginalAuthors() =>
        Contributors.Of(Journal().Take(1).ToList(), withBeforeOpen: false).Items.Select(item => item.Agent.Id).Should().Equal("main", "opus-coder");

    [Fact]
    public void RoundThreeHasTheOriginalAuthorsAndTheFixer()
    {
        var set = Contributors.Of(Journal(), withBeforeOpen: false);

        set.Items.Select(item => item.Agent.Id).Should().Equal("main", "opus-coder", "sonnet-fixer");
        set.Items.Single(item => item.Agent.Id == "sonnet-fixer").ActiveIn.Should().Equal(new RoundRef("CodeReview", 3, WindowKind.Round));
    }

    [Fact]
    public void TheOpusCodersActiveInDoesNotIncludeRoundThree()
    {
        var coder = Contributors.Of(Journal(), withBeforeOpen: false).Items.Single(item => item.Agent.Id == "opus-coder");

        coder.ActiveIn.Should().Equal(new RoundRef("CodeReview", 1, WindowKind.Round));
        coder.Models.Should().Equal("claude-opus-5-5");
    }

    [Fact]
    public void FixersOfRoundThree_AreTheFixerAlone() =>
        Contributors.FixersOf(Journal(), "CodeReview", 3, withBeforeOpen: false).Items.Select(item => item.Agent.Id).Should().Equal("sonnet-fixer");

    [Fact]
    public void TheFirstCodeRoundHasNoFixers() =>
        Contributors.FixersOf(Journal(), "CodeReview", 1, withBeforeOpen: false).Items.Should().BeEmpty("there is no earlier diff for a delta to be measured against");

    [Fact]
    public void ARoundNobodyWroteIn_HasNoFixers() =>
        Contributors.FixersOf(Journal(), "CodeReview", 2, withBeforeOpen: false).Items.Should().BeEmpty();
}
