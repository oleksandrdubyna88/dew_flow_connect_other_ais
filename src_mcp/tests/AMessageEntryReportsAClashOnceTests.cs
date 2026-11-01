using CoaiMcp.Store;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// E3.S4 (S4-14, G14): two PRESENT values of one field that differ are ONE inconsistency, reported by the merge that first made it
/// visible and by no later merge; an absent value never differs. Pins <c>MessageEntry.Clashes</c> before and after its
/// non-short-circuit <c>|</c> chain became three named reads (SonarCloud S2178).
/// </summary>
public sealed class AMessageEntryReportsAClashOnceTests
{
    private static readonly DateTime Start = new(2026, 10, 19, 10, 0, 0, DateTimeKind.Utc);

    private static LineFacts Line(string model, string requested, string effort) => new(
        LineKind.Assistant, Start, string.Empty, false, "msg_1", new MessageValues(false, model, requested, effort, 0), new TokenUsage(1, 0, 0, 1), true, []);

    private static MessageEntry Entry(string model, string requested, string effort) => MessageEntry.First(0, "main", Start, Line(model, requested, effort));

    private static bool Differs(MessageEntry entry, string model, string requested, string effort, int second = 1) =>
        entry.Merge(0, Start.AddSeconds(second), Line(model, requested, effort)).ValuesDiffer;

    [Theory]
    [InlineData("opus", "", "", "sonnet", "", "")]
    [InlineData("", "opus", "", "", "sonnet", "")]
    [InlineData("", "", "low", "", "", "high")]
    [InlineData("opus", "opus", "low", "sonnet", "sonnet", "high")]
    public void A_field_whose_two_present_values_differ_is_reported(string model, string requested, string effort, string model2, string requested2, string effort2)
    {
        var entry = Entry(model, requested, effort);

        Differs(entry, model2, requested2, effort2).Should().BeTrue("two present values of one field that differ are an inconsistency");
    }

    [Fact]
    public void A_clash_is_reported_by_the_first_merge_that_shows_it_and_by_no_later_one()
    {
        var entry = Entry("opus", string.Empty, "low");

        var first = Differs(entry, "sonnet", string.Empty, "high");
        var second = Differs(entry, "haiku", string.Empty, "medium", second: 2);

        first.Should().BeTrue();
        second.Should().BeFalse("the clash is already counted: it is one inconsistency, not one per line");
    }

    [Fact]
    public void An_absent_value_never_differs_and_an_equal_one_does_not_either()
    {
        var entry = Entry("opus", string.Empty, "low");

        Differs(entry, string.Empty, "opus", string.Empty).Should().BeFalse("an absent value never differs (G14)");
        Differs(entry, "opus", string.Empty, "low", second: 2).Should().BeFalse("equal values agree");
    }

    [Fact]
    public void A_field_first_present_on_a_later_line_is_still_compared_after_that_line()
    {
        var entry = Entry(string.Empty, string.Empty, string.Empty);

        Differs(entry, "opus", string.Empty, "low").Should().BeFalse("the first present value only sets what later lines are compared with");
        Differs(entry, "opus", string.Empty, "high", second: 2).Should().BeTrue();
    }
}
