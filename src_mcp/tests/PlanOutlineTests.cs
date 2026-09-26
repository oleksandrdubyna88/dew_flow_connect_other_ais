using CoaiMcp.Core.Commands;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The epics and stories a plan names in its own headings — what the gate counts before it guesses
/// (<c>research/PLAN_consult_on_a_cadence.md</c>, D8).
/// </summary>
/// <remarks>
/// Measured on 2026-09-25 against private repo A's two plans: the 10-epic one has 5 numbered build
/// steps and the 4-epic one has 31, and the longer file is the smaller plan. Neither signal the reader
/// had could tell them apart; the headings can.
/// </remarks>
public sealed class PlanOutlineTests
{
    private static string PlanWith(IEnumerable<int> epics, int storiesEach, string hashes = "###")
    {
        var text = new System.Text.StringBuilder("# PLAN — generated\r\n\r\n> Status: plan only.\r\n\r\n");
        foreach (var epic in epics)
        {
            text.Append(System.Globalization.CultureInfo.InvariantCulture, $"{hashes} Epic {epic} — the {epic}th piece\r\n\r\n");
            for (var story = 1; story <= storiesEach; story++)
            {
                text.Append(System.Globalization.CultureInfo.InvariantCulture, $"{hashes}# Story {epic}.{story} — a story\r\n\r\ntext\r\n\r\n");
            }
        }

        return text.ToString();
    }

    [Fact]
    public void APlanWithoutHeadings_HasNoOutline()
    {
        var outline = PlanOutlineReader.Of("# PLAN\n\n## Build order\n\n1. one\n2. two\n");

        outline.HasEpics.Should().BeFalse();
        outline.Epics.Should().BeEmpty();
        outline.Stories.Should().Be(0);
    }

    [Theory]
    [InlineData("##")]
    [InlineData("###")]
    [InlineData("####")]
    public void EpicHeadingsAreCounted_AtEveryLevelAPlanUsesThem(string hashes)
    {
        var outline = PlanOutlineReader.Of(PlanWith(Enumerable.Range(1, 4), storiesEach: 2, hashes));

        outline.Epics.Select(e => e.Number).Should().Equal(1, 2, 3, 4);
        outline.Stories.Should().Be(8);
    }

    [Fact]
    public void TheTitleIsWhatFollowsTheNumber_WithoutTheCarriageReturn()
    {
        var outline = PlanOutlineReader.Of(PlanWith([5], storiesEach: 0));

        outline.Epics.Should().ContainSingle().Which.Should().Be(new OutlineEpic(5, "the 5th piece"));
    }

    [Theory]
    // Epic 1's plan round (gemini): authors write hyphens, colons and en-dashes as often as em-dashes.
    [InlineData("### Epic 3 — the title", "the title")]
    [InlineData("### Epic 3 – the title", "the title")]
    [InlineData("### Epic 3 - the title", "the title")]
    [InlineData("### Epic 3: the title", "the title")]
    [InlineData("### Epic 3. the title", "the title")]
    [InlineData("### epic 3 the title", "the title")]
    [InlineData("### Epic 3", "")]
    [InlineData("### Epic 3 —   the title   ", "the title")]
    public void AnyUsualSeparator_BetweenTheNumberAndTheTitle(string heading, string title) =>
        PlanOutlineReader.Of($"# PLAN\n\n{heading}\n\ntext\n").Epics
            .Should().ContainSingle().Which.Should().Be(new OutlineEpic(3, title));

    [Fact]
    public void Epic30_IsNotEpic3() =>
        PlanOutlineReader.Of("## Epic 30 — far\n").Epics.Should().ContainSingle().Which.Number.Should().Be(30);

    [Fact]
    public void AContinuingPlan_KeepsItsOwnNumbers()
    {
        var outline = PlanOutlineReader.Of(PlanWith(Enumerable.Range(5, 10), storiesEach: 5));

        outline.Epics.Should().HaveCount(10);
        outline.LastNumber.Should().Be(14);
        outline.Stories.Should().Be(50);
    }

    [Fact]
    public void AnEpicNamedTwice_CountsOnce()
    {
        // A plan that says "### Epic 3" in its build order and again in a deviations section is still
        // a plan with one epic 3.
        var outline = PlanOutlineReader.Of(PlanWith([1, 2, 3], 0) + "## Epic 3 — revisited\n");

        outline.Epics.Select(e => e.Number).Should().Equal(1, 2, 3);
        outline.Epics[2].Title.Should().Be("the 3th piece", "the first heading names it");
    }

    [Theory]
    // Epic 1's code round (codex): a plan that SHOWS an example outline must not be sized by it.
    [InlineData("```")]
    [InlineData("~~~")]
    [InlineData("````markdown")]
    public void HeadingsInsideAFencedBlock_AreNotThePlans(string fence)
    {
        var closing = fence.TrimEnd('m', 'a', 'r', 'k', 'd', 'o', 'w', 'n');
        var text = $"# PLAN\n\n### Epic 1 — real\n\n{fence}\n### Epic 2 — an example\n#### Story 2.1 — example\n{closing}\n\n### Epic 3 — real too\n";

        var outline = PlanOutlineReader.Of(text);

        outline.Epics.Select(e => e.Number).Should().Equal(1, 3);
        outline.Stories.Should().Be(0);
    }

    [Fact]
    public void AFenceLineWithAnInfoString_DoesNotCloseAnOpenBlock()
    {
        // CodeRabbit on #540: CommonMark lets a closing fence carry only whitespace after its marker, so a
        // ```js line inside an open block is part of the block, not its end.
        var text = "### Epic 1 — real\n```\nexample:\n```js\n### Epic 2 — inside the example\n```\n### Epic 3 — real\n";

        PlanOutlineReader.Of(text).Epics.Select(e => e.Number).Should().Equal(1, 3);
    }

    [Fact]
    public void ABacktickRunWithABacktickInItsInfoString_IsNotAFence() =>
        // Also CommonMark: the info string after backticks may not contain a backtick, so this line is
        // prose and hides nothing after it.
        PlanOutlineReader.Of("``` not `a` fence\n### Epic 1 — real\n").Epics.Select(e => e.Number).Should().Equal(1);

    [Fact]
    public void ATildeFenceMayCarryBackticksInItsInfoString() =>
        PlanOutlineReader.Of("~~~ `x`\n### Epic 1 — inside\n~~~\n### Epic 2 — real\n").Epics.Select(e => e.Number).Should().Equal(2);

    [Fact]
    public void AnUnclosedFence_HidesTheRestOfThePlan_AsMarkdownDoes() =>
        PlanOutlineReader.Of("### Epic 1 — a\n```\n### Epic 2 — b\n").Epics.Select(e => e.Number).Should().Equal(1);

    [Fact]
    public void NonAsciiDigits_AreNotAnEpicNumber() =>
        PlanOutlineReader.Of("### Epic ٥ — five\n").HasEpics.Should().BeFalse("and must not throw");

    [Fact]
    public void ProseThatMentionsAnEpic_IsNotAHeading() =>
        PlanOutlineReader.Of("# PLAN\n\nEpic 4 is the one that matters. ### Epic 5 inline\n- Epic 6\n")
            .HasEpics.Should().BeFalse();

    [Fact]
    public void TheTitlesOfAGroup_AreWhatAnOrderQuotes() =>
        PlanOutlineReader.Of(PlanWith([4, 5, 6, 7], 0)).TitlesOf(4, 6)
            .Should().Be("Epic 4 — the 4th piece; Epic 5 — the 5th piece; Epic 6 — the 6th piece");
}
