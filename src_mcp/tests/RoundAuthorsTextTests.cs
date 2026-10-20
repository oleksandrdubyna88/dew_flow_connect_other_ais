using CoaiMcp.Store;
using FluentAssertions;
using Xunit;
using static CoaiMcp.Tests.RoundAuthorsFacts;
using static CoaiMcp.Tests.RoundAuthorsSeed;

namespace CoaiMcp.Tests;

/// <summary>
/// The <c>--table</c> rendering writes only printable text: a value that reaches it from a store row or from an <c>--add</c> file — a branch
/// name, a model, a platform word — cannot carry a terminal escape that clears the screen or rewrites the figures a person reads.
/// </summary>
public sealed class RoundAuthorsTextTests
{
    private const string Escape = "\u001b[2J\u001b[31m";

    private const string HostileCohort = "first\u001b]8;;http://x\u0007";

    [Fact]
    public void AControlCharacterInADisplayedValue_NeverReachesTheTerminal()
    {
        var rounds = new[] { Round(1, branch: "feat/" + Escape + "red", authors: Observed(MainRow(model: "claude-opus-5-5\u0007"), Coder("a1", "claude-sonnet-5-5"))) };
        var selection = Sel();
        var sums = RoundAuthorsCells.Count(RoundAuthorsPopulation.Of(rounds, selection), selection);
        var answer = new RoundAuthorsAnswer(1, selection, [new RoundAuthorsSource(new string('a', 32), "linux" + Escape, "2026-10-19T00:00:00Z\u001b]0;x\u0007", true, ["s"])], sums.Cells, sums.Exclusions, true);

        var text = RoundAuthorsText.Render(answer);

        text.Where(at => char.IsControl(at) && at != '\n').Should().BeEmpty("the table prints no control character but its own line breaks");
        text.Should().Contain("feat/", "the branch is still shown, with its control characters made visible");
    }

    /// <summary>
    /// The header is printed too: a cohort word comes from the report's own cell keys, and an <c>--add</c> file is attacker-controllable JSON, so
    /// a crafted key must not reach the terminal as an OSC 8 link or an escape — whichever line prints it.
    /// </summary>
    [Fact]
    public void ACohortWordFromAnAddFile_NeverReachesTheTerminalThroughTheHeader()
    {
        var selection = Sel(split: Stamp(T0.AddHours(2.5)));
        var sums = RoundAuthorsCells.Count(RoundAuthorsPopulation.Of([Round(1)], selection), selection);
        var crafted = sums.Cells.Select(cell => cell with { Key = cell.Key with { Cohort = HostileCohort } }).ToList();
        var answer = new RoundAuthorsAnswer(1, selection, [new RoundAuthorsSource(new string('a', 32), "linux", "2026-10-19T00:00:00Z", true, ["s"])], crafted, sums.Exclusions, true);

        var text = RoundAuthorsText.Render(answer);

        text.Where(at => char.IsControl(at) && at != '\n').Should().BeEmpty("every line of the table, header included, prints no control character but its own line breaks");
        text.Should().Contain("Cohort first?]8;;http://x?", "the header still names the cohort, with its control characters made visible");
    }

    /// <summary>
    /// A control character is not the only thing a reader cannot trust: a bidi override reorders a row (Trojan Source), a zero-width character hides
    /// in a word, a line or paragraph separator starts a line the table did not write. Every displayed value of a cell and of an exclusion is held to
    /// the same rule — branch, coder mix, coder effort, main model, reviewer set, the exclusion reason and what it names.
    /// </summary>
    [Fact]
    public void AValueWithABidiOverrideOrAZeroWidthCharacter_NeverReachesTheTerminal()
    {
        var selection = Sel();
        var sums = RoundAuthorsCells.Count(RoundAuthorsPopulation.Of([Round(1, branch: "feat/\u202Eevil")], selection), selection);
        var cells = sums.Cells.Select(cell => cell with
        {
            Key = cell.Key with { CoderMix = "sonnet\u200B", CoderEffort = "high\u2066", MainModel = "opus\uFEFF", ReviewerSet = "codex\u00AD" },
        }).ToList();
        var exclusions = sums.Exclusions.Select(count => count with { Reason = "x\u2028y\u2029", Because = [new NamedCount("n\u200Fm\u0085", 1)] }).ToList();
        var answer = new RoundAuthorsAnswer(1, selection, [new RoundAuthorsSource(new string('a', 32), "linux", "2026-10-19T00:00:00Z", true, ["s"])], cells, exclusions, true);

        var text = RoundAuthorsText.Render(answer);

        text.Where(at => at != '\n' && IsTerminalUnsafe(at)).Should().BeEmpty("the table prints nothing a terminal acts on or a reader cannot see, but its own line breaks");
        text.Should().Contain("feat/?evil", "the branch is still shown, with its bidi override made visible").And.Contain("sonnet?").And.Contain("x?y?");
    }

    [Theory]
    [InlineData("first\u009B2J", "first?2J")]
    [InlineData("a\u007Fb", "a?b")]
    [InlineData("\uFEFFfirst", "?first")]
    [InlineData("sonnet\u200B", "sonnet?")]
    [InlineData("feat/\u202Eevil", "feat/?evil")]
    [InlineData("x\u2028y\u2029z", "x?y?z")]
    [InlineData("x\u0085y\u000Cz", "x?y?z")]
    [InlineData("x\u00ADy\u2066z\u2069", "x?y?z?")]
    [InlineData("x\ny\rz", "x?y?z")]
    [InlineData("feat/émoji-😀 — ok", "feat/émoji-😀 — ok")]
    public void Printable_ShowsWhatATerminalWouldActOnAsAQuestionMark_AndKeepsTheRest(string value, string shown) =>
        RoundAuthorsText.Printable(value).Should().Be(shown);
}
