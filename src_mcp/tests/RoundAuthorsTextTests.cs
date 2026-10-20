using CoaiMcp.Store;
using FluentAssertions;
using Xunit;
using static CoaiMcp.Tests.RoundAuthorsFacts;

namespace CoaiMcp.Tests;

/// <summary>
/// The <c>--table</c> rendering writes only printable text: a value that reaches it from a store row or from an <c>--add</c> file — a branch
/// name, a model, a platform word — cannot carry a terminal escape that clears the screen or rewrites the figures a person reads.
/// </summary>
public sealed class RoundAuthorsTextTests
{
    private const string Escape = "\u001b[2J\u001b[31m";

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
}
