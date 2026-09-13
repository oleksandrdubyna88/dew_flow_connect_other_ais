using System.Collections.Immutable;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Gate;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A finding the caller accepted, fixed, and was handed back — phase 2's instrument.
/// </summary>
/// <remarks>
/// <para>Not <c>re_raised</c>, and the distinction is the whole point: that one is a reviewer raising
/// something over a standing REJECTION — a disagreement the caller is defending. This is the
/// opposite and the more expensive one. Nobody is disagreeing; the fix did not take.</para>
/// <para>Nothing is called. The number exists so the question "should an automatic consultation fire
/// when a finding survives two rounds" can be answered from measurement rather than from an
/// impression of how often it happens.</para>
/// </remarks>
public sealed class StuckFindingsTests
{
    private static Finding Found(
        string title,
        string file = "src/Parser.cs",
        int line = 40,
        Category category = Category.Reliability) =>
        new(Severity.Major, category, file, line, title, title + ", and here is why", "and the fix", ["codex"]);

    private static EarlierDecision Accepted(int round, Finding finding) => new(round, finding, true);

    private static EarlierDecision Rejected(int round, Finding finding) => new(round, finding, false);

    [Fact]
    public void AFindingTheCallerAcceptedAndWasHandedBack_IsCounted()
    {
        var survivors = StuckFindings.SurvivedAcceptance(
            [Found("The parser drops the last token")],
            [Accepted(1, Found("The parser drops the last token"))]);

        survivors.Count.Should().Be(1);
        survivors.Rounds.Should().Equal([1]);
        survivors.Sentence.Should()
            .Contain("an automatic consultation could have fired here")
            .And.Contain("1 accepted finding(s) from round 1");
    }

    /// <summary>
    /// The product's OWN rule for "the same defect" decides, not a second one written here.
    /// </summary>
    /// <remarks>
    /// Same category, same file, lines within five, and as much wording overlap as those coordinates
    /// leave necessary — which is why a reviewer rephrasing the same defect four lines further down
    /// still counts, and a different defect in the same file does not.
    /// </remarks>
    [Fact]
    public void TheMatchIsTheProductsOwnSimilarityRule_NotAnExactString()
    {
        var rephrased = StuckFindings.SurvivedAcceptance(
            [Found("The parser drops the last token", line: 44)],
            [Accepted(2, Found("The parser drops the last token", line: 40))]);

        rephrased.Count.Should().Be(1, "four lines apart is the same defect by this product's rule");

        var different = StuckFindings.SurvivedAcceptance(
            [Found("The lock is taken after the record is read")],
            [Accepted(2, Found("The parser drops the last token"))]);

        different.Count.Should().Be(0);
    }

    [Fact]
    public void AFindingInAnotherFile_IsAnotherFinding()
    {
        StuckFindings.SurvivedAcceptance(
            [Found("The parser drops the last token", file: "src/Other.cs")],
            [Accepted(1, Found("The parser drops the last token"))])
            .Count.Should().Be(0);
    }

    /// <summary>
    /// One finding that survived is ONE finding, however many earlier rounds accepted something like it.
    /// </summary>
    /// <remarks>
    /// Counting it once per matching round would make a long session look worse than a short one for
    /// the same defect, which is the opposite of what this number is for.
    /// </remarks>
    [Fact]
    public void AFindingMatchingTwoEarlierRounds_IsCountedOnce()
    {
        var survivors = StuckFindings.SurvivedAcceptance(
            [Found("The parser drops the last token")],
            [
                Accepted(1, Found("The parser drops the last token")),
                Accepted(2, Found("The parser drops the last token", line: 41)),
            ]);

        survivors.Count.Should().Be(1);
        survivors.Rounds.Should().Equal([2], "the acceptance in force when it came back is the latest one");
    }

    /// <summary>
    /// Accepted, rejected, accepted again: the round NAMED is the last acceptance, not the first.
    /// </summary>
    /// <remarks>
    /// It decides which history a person is pointed at. Round 1's acceptance was superseded by a
    /// rejection and then replaced by round 3's; reporting round 1 would send somebody to read an
    /// argument that had already been had and settled the other way. (local, third code round.)
    /// </remarks>
    [Fact]
    public void ADefectAcceptedTwiceAcrossADisagreement_NamesTheLatestAcceptance()
    {
        var defect = Found("The parser drops the last token");

        var survivors = StuckFindings.SurvivedAcceptance(
            [defect],
            [Accepted(1, defect), Rejected(2, defect), Accepted(3, defect)]);

        survivors.Count.Should().Be(1);
        survivors.Rounds.Should().Equal([3]);
    }

    [Fact]
    public void SeveralSurvivors_NameEveryRoundTheyCameFrom()
    {
        var survivors = StuckFindings.SurvivedAcceptance(
            [Found("The parser drops the last token"), Found("The lock is taken too late", file: "src/Lock.cs")],
            [
                Accepted(1, Found("The parser drops the last token")),
                Accepted(3, Found("The lock is taken too late", file: "src/Lock.cs")),
            ]);

        survivors.Count.Should().Be(2);
        survivors.Rounds.Should().Equal([1, 3]);
        survivors.Sentence.Should().Contain("2 accepted finding(s) from round 1, 3");
    }

    /// <summary>
    /// Accepted, then REJECTED, then raised again: that is a disagreement, not a fix that did not take.
    /// </summary>
    /// <remarks>
    /// The caller's latest word about the defect is the rejection, and a reviewer pressing a standing
    /// rejection is `re_raised` — a different signal, recorded elsewhere, and the more interesting one
    /// for reading an argument. Counting it here would file a disagreement as a failure to fix.
    /// (codex, story 6's plan round.)
    /// </remarks>
    [Fact]
    public void ADefectAcceptedThenRejected_IsNotCountedWhenItComesBack()
    {
        var defect = Found("the parser drops the last token");

        StuckFindings.SurvivedAcceptance(
            [defect],
            [Accepted(1, defect), Rejected(2, defect)])
            .Count.Should().Be(0, "the caller's latest word about it is a disagreement they are defending");

        // And the other way round: rejected once, then accepted, then handed back IS this signal.
        StuckFindings.SurvivedAcceptance(
            [defect],
            [Rejected(1, defect), Accepted(2, defect)])
            .Should().Match<StuckFindings.Survivors>(s => s.Count == 1 && s.Rounds.Contains(2));
    }

    [Fact]
    public void NothingToCompare_IsNoSentenceAtAll()
    {
        StuckFindings.SurvivedAcceptance([], [Accepted(1, Found("x"))]).Sentence.Should().BeEmpty();
        StuckFindings.SurvivedAcceptance([Found("x")], []).Sentence.Should().BeEmpty();
        StuckFindings.SurvivedAcceptance([Found("x")], [Accepted(1, Found("y", file: "other.cs"))])
            .Sentence.Should().BeEmpty("a round where nothing survived says nothing, rather than saying zero");
    }

    /// <summary>
    /// The sentence says what COULD have happened, never what should.
    /// </summary>
    /// <remarks>
    /// This story measures; it calls nothing. A line that told an agent to consult would be phase 2
    /// arriving before its own evidence — which is precisely the decision the number exists to inform.
    /// </remarks>
    [Fact]
    public void TheSentenceMeasures_AndInstructsNobody()
    {
        var sentence = StuckFindings.SurvivedAcceptance(
            [Found("The parser drops the last token")],
            [Accepted(1, Found("The parser drops the last token"))]).Sentence;

        sentence.Should().Contain("could have fired");
        sentence.Should().NotContainAny("you should", "call `consult`", "consult now");
    }
}
