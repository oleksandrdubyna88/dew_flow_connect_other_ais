using CoaiMcp.Core.Authors;
using FluentAssertions;
using Xunit;
using static CoaiMcp.Tests.AssignmentViews;

namespace CoaiMcp.Tests;

/// <summary>
/// Local completeness over ROWS (E4.S3): in every observed window Σ row turns + ambiguous + elsewhere + claimed-by-peer turns is the
/// window's distinct message ids, and the same per token term. Turns, launches and tokens partition the window; agent counts do not.
/// </summary>
public sealed class EveryWindowAccountsForEveryMessageTests
{
    public static TheoryData<string> Cases => AttributionCases.Names;

    [Theory]
    [MemberData(nameof(Cases))]
    public void TurnsAndTokensPartitionTheWindow(string name)
    {
        var scenario = AttributionCases.Named(name);
        var every = scenario.AssignEvery();
        var checkedWindows = 0;

        every.Should().NotBeEmpty();
        foreach (var one in every)
        {
            var authors = RoundAuthorsBuilder.Build(scenario.Build(one.Gate, one.Stage, one.Number), one.Assignment);
            authors.Windows.Should().HaveCount(one.Assignment.Windows.Length, "one entry per window, empty ones included");
            for (var at = 0; at < authors.Windows.Length; at++)
            {
                checkedWindows += Account(name, one.Assignment, authors, at);
            }
        }

        checkedWindows.Should().BeGreaterThan(0, "an accounting that looked at no window proves nothing");
    }

    private static int Account(string name, WindowAssignment assignment, RoundAuthors authors, int index)
    {
        var window = assignment.Windows[index];
        var totals = authors.Windows[index];
        if (totals.State == ObservationKinds.Unobserved)
        {
            totals.Ambiguous.Should().Be(ActivityTotals.Unknown, $"{name}: an unobserved window is never a zero");

            return 0;
        }

        var placed = assignment.Messages.Where(placement => placement.Window == window).ToList();
        var rows = authors.Rows.Where(row => row.Window == window.Kind).ToList();
        var turns = rows.Sum(row => row.Turns) + totals.Ambiguous.Turns + totals.Elsewhere.Turns + totals.ClaimedByPeer.Turns;

        turns.Should().Be(placed.Count, $"{name}: {window.Kind} of {window.Gate} {window.Stage} {window.Number}");
        Sum(rows.Select(row => row.Tokens).Concat([totals.Ambiguous.Tokens, totals.Elsewhere.Tokens, totals.ClaimedByPeer.Tokens]))
            .Should().Be(Sum(placed.Select(placement => placement.Tokens)), $"{name}: tokens per term");

        return 1;
    }

    private static TokenTerms Sum(IEnumerable<TokenTerms> terms) =>
        terms.Aggregate(new TokenTerms(0, 0, 0, 0), (sum, next) => new TokenTerms(sum.Input + next.Input, sum.Output + next.Output, sum.CacheCreation + next.CacheCreation, sum.CacheRead + next.CacheRead));

    /// <summary>G10: the two-repositories agent claimed by a peer is in TWO agent counts while its turns are in one.</summary>
    [Fact]
    public void TheAgentCountsAreNotAPartition()
    {
        var authors = AttributionCases.TwoRepositoriesOneAgent().AuthorsOf("B", "CodeReview", 1);

        (authors.Ambiguous.Agents + authors.ClaimedByPeer.Agents).Should().Be(2);
        (authors.Ambiguous.Turns + authors.ClaimedByPeer.Turns).Should().Be(3);
    }
}
