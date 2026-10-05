using System.Text.RegularExpressions;
using CoaiMcp.Core.Context;
using CoaiMcp.Core.Security;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A pattern that ran out of time on one file is not run again on the rest of that classification — epic 2's code round
/// (coai session 589a145b): 32 patterns each spending the match timeout on each of 16 files is over eight minutes
/// before a round can say its detection was incomplete.
/// </summary>
/// <remarks>
/// What it would have matched on the later files is then not known, so those files say their detection is INCOMPLETE —
/// never a quiet "no match".
/// </remarks>
public sealed class ATimedOutPatternRunsOncePerClassificationTests
{
    // A backtracking engine and a 1 ms budget, so the first file's catastrophic text times out every time; the
    // second file's text matches at once — if the pattern is run on it at all.
    private static SignalTable Table() => new(
        new Dictionary<string, SignalMatcher>
        {
            ["slow"] = new([], [new Regex("(a+)+$|MATCHME", RegexOptions.None, TimeSpan.FromMilliseconds(1))]),
        },
        []);

    [Fact]
    public void TheLaterFile_IsNotScannedAgain_AndSaysItsDetectionIsIncomplete()
    {
        var files = SecuritySignals.Classify(
            [new FileDiff("a.cs", new string('a', 40) + "!", false), new FileDiff("b.cs", "MATCHME", false)],
            Table());

        files[0].DetectionIncomplete.Should().BeTrue("the pattern ran out of time on it");
        files[1].Signals.Should().NotContain("slow", "the pattern that timed out is not run a second time");
        files[1].DetectionIncomplete.Should().BeTrue("what the skipped pattern would have found there is not known");
    }

    [Fact]
    public void ANewClassification_StartsFresh()
    {
        SecuritySignals.Classify([new FileDiff("a.cs", new string('a', 40) + "!", false)], Table());

        SecuritySignals.Classify([new FileDiff("b.cs", "MATCHME", false)], Table())[0].Signals.Should().Contain("slow");
    }
}
