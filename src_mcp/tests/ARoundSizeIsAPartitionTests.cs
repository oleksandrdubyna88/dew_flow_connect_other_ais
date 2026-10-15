using Xunit;
using CoaiMcp.Core.Context;
using CoaiMcp.Runners.Context;
using FluentAssertions;

namespace CoaiMcp.Tests;

/// <summary>
/// E2.S1 §S1.4 — the numstats a round already has, turned into coverage × kind cells. Pure: the examples are
/// §S1.6's numbers, and a helper builds the records from <c>"added removed path [← from]"</c> so a test reads like
/// the table.
/// </summary>
public sealed class ARoundSizeIsAPartitionTests
{
    private const string From = "abc1234";

    private const string NotInUnfiltered = "a reviewable file is not in the unfiltered numstat";
    private const string TwoMatchOne = "two unfiltered records match one reviewable file";
    private const string ElidedNotReviewable = "an elided path is not a reviewable file";

    private static readonly string[] Sentences = [NotInUnfiltered, TwoMatchOne, ElidedNotReviewable];

    /// <summary><c>"2 1 src/new.cs ← src/old.cs"</c> · <c>"- - logo.png"</c> → the record the reader would produce.</summary>
    private static CountedChange Rec(string line)
    {
        var parts = line.Split(" ← ");
        var columns = parts[0].Split(' ', 3);
        var binary = columns[0] == "-";
        var added = binary ? 0 : int.Parse(columns[0], System.Globalization.CultureInfo.InvariantCulture);
        var deleted = binary ? 0 : int.Parse(columns[1], System.Globalization.CultureInfo.InvariantCulture);

        return new CountedChange(new NumstatChange(columns[2], parts.Length > 1 ? parts[1] : string.Empty, binary), added, deleted);
    }

    private static CountedChange[] Recs(params string[] lines) => [.. lines.Select(Rec)];

    private static ScopeSize Partition(string[] unfiltered, string[] filtered, params string[] elided) =>
        ChangePartition.Of(Recs(filtered), Recs(unfiltered), elided, From);

    private static string Show(SizeCell c) =>
        $"{c.Coverage}/{c.Kind} {c.Files} {c.Added?.ToString() ?? "null"} {c.Removed?.ToString() ?? "null"}";

    private static string[] Shown(ScopeSize size) => [.. size.Cells.Select(Show)];

    private static readonly string[] Example1Unfiltered =
        ["1 1 app.cs", "1 0 package-lock.json", "1 0 dist/bundle.js", "- - logo.png"];

    private static readonly string[] Example1Filtered = ["1 1 app.cs", "- - logo.png"];

    [Fact]
    public void EveryUnfilteredFile_LandsInExactlyOneCell_AndTheFilesSumToTheRecordCount()
    {
        var size = Partition(Example1Unfiltered, Example1Filtered);

        size.State.Should().Be(ScopeState.Recorded);
        size.Cells.Should().HaveCount(4);
        size.Cells.Sum(c => c.Files).Should().Be(Example1Unfiltered.Length);
        size.Cells.Select(c => (c.Coverage, c.Kind)).Should().OnlyHaveUniqueItems();
        size.Cells.Should().OnlyContain(c => c.Files >= 1);
        size.Cells.Should().OnlyContain(c => c.Added.HasValue != (c.Coverage == ChangeCoverage.Binary));
    }

    [Fact]
    public void AnExcludedFileIsCountedApart_NeverAsShown()
    {
        var size = Partition(Example1Unfiltered, Example1Filtered);

        Shown(size).Should().Equal(
            "shown/code 1 1 1",
            "excluded/code 1 1 0",
            "excluded/config 1 1 0",
            "binary/unknown 1 null null");
    }

    [Fact]
    public void ABinaryHasNoLineCount_WhateverTheReaderGave()
    {
        var example1 = Partition(Example1Unfiltered, Example1Filtered);
        var handBuilt = new CountedChange(new NumstatChange("logo.png", string.Empty, IsBinary: true), 7, 7);

        var size = ChangePartition.Of([handBuilt], [handBuilt], [], From);

        example1.Cells.Where(c => c.Coverage == ChangeCoverage.Binary).Should().ContainSingle()
            .Which.Added.Should().BeNull("git's dash reads as 0 in the reader, and 0 is a line count");
        size.Cells.Should().ContainSingle();
        size.Cells[0].Added.Should().BeNull("a binary has no line count — 7 is a number the reader invented");
        size.Cells[0].Removed.Should().BeNull();
    }

    [Fact]
    public void ABinaryThatIsAlsoExcluded_IsBinary() =>
        Shown(Partition(["- - bun.lockb"], [])).Should().Equal("binary/unknown 1 null null");

    [Fact]
    public void ABinaryIsNeverElided_AssertedAgainstTheShaper()
    {
        var shaped = DiffShaper.Shape([new FileDiff("x.png", string.Empty, IsBinary: true, BinaryBytes: 9)], maxBytes: 0);

        shaped.Elided.Should().BeEmpty("the shaper names a binary outside its budget");

        Shown(Partition(["- - logo.png"], ["- - logo.png"], "logo.png")).Should().Equal(
            "binary/unknown 1 null null");
    }

    [Fact]
    public void AnElidedFileIsElided_WithItsLinesCounted() =>
        Shown(Partition(["900 0 src/big.cs", "3 1 src/small.cs"], ["900 0 src/big.cs", "3 1 src/small.cs"], "src/big.cs"))
            .Should().Equal("shown/code 1 3 1", "elided/code 1 900 0");

    [Fact]
    public void ARenameKeptWhole_IsOneShownFile() =>
        Shown(Partition(["2 1 src/new.cs ← src/old.cs"], ["2 1 src/new.cs ← src/old.cs"]))
            .Should().Equal("shown/code 1 2 1");

    [Fact]
    public void ARenameOutOfAnExcludedFolder_IsShownWithTheLinesTheReviewersSaw() =>
        Shown(Partition(["2 1 src/a.js ← dist/a.js"], ["40 0 src/a.js"]))
            .Should().Equal("shown/code 1 40 0");

    [Fact]
    public void ARenameIntoAnExcludedFolder_IsShownAsTheDeletionTheReviewersSaw() =>
        Shown(Partition(["2 1 dist/a.js ← src/a.js"], ["0 40 src/a.js"]))
            .Should().Equal("shown/code 1 0 40");

    [Fact]
    public void ASwapOfTwoFiles_MatchesEachByItsNewName()
    {
        string[] both = ["3 0 b ← a", "0 3 a ← b"];

        var size = Partition(both, both);

        size.State.Should().Be(ScopeState.Recorded);
        Shown(size).Should().Equal("shown/unknown 2 3 3");
    }

    [Fact]
    public void AModeOnlyChange_IsAFileWithZeroLines() =>
        Shown(Partition(["0 0 tools/run.sh"], ["0 0 tools/run.sh"])).Should().Equal("shown/unknown 1 0 0");

    [Fact]
    public void ASubmodulePointer_IsOneUnknownFile() =>
        Shown(Partition(["1 1 .agents/conventions"], ["1 1 .agents/conventions"])).Should().Equal("shown/unknown 1 1 1");

    [Fact]
    public void ADeletedFile_IsKindedByItsOnlyName() =>
        Shown(Partition(["0 25 src_mcp/tests/OldTests.cs"], ["0 25 src_mcp/tests/OldTests.cs"]))
            .Should().Equal("shown/test 1 0 25");

    [Fact]
    public void AnEmptyRange_IsAnObservedZero_NotUnavailable()
    {
        var size = Partition([], []);

        size.State.Should().Be(ScopeState.Recorded);
        size.Cells.Should().BeEmpty();
        size.From.Should().Be(From);
    }

    [Fact]
    public void AReviewableFileAbsentFromTheUnfilteredRange_IsReportedNeverDropped()
    {
        var size = Partition([], ["1 0 src/x.cs"]);

        size.State.Should().Be(ScopeState.NumstatsDisagree);
        size.Cells.Should().BeEmpty();
        size.From.Should().Be(From);
    }

    [Fact]
    public void TwoUnfilteredRecordsClaimingOneReviewableFile_IsADisagreement()
    {
        var size = Partition(["0 10 a", "12 0 b"], ["2 0 b ← a"]);

        size.State.Should().Be(ScopeState.NumstatsDisagree);
        size.Cells.Should().BeEmpty();
    }

    [Fact]
    public void AnElidedPathThatIsNoReviewableFile_IsADisagreement()
    {
        var size = Partition(["3 0 src/Big.cs"], ["3 0 src/Big.cs"], "src/big.cs");

        size.State.Should().Be(ScopeState.NumstatsDisagree);
        size.Cells.Should().BeEmpty();
    }

    [Fact]
    public void TheDisagreementSentencesCarryNoPath()
    {
        var absent = ChangePartition.Disagreement(Recs("1 0 src/x.cs"), [], []);
        var twice = ChangePartition.Disagreement(Recs("2 0 src/renamed_to.cs ← src/renamed_from.cs"), Recs("0 10 src/renamed_from.cs", "12 0 src/renamed_to.cs"), []);
        var stray = ChangePartition.Disagreement(Recs("3 0 src/Big.cs"), Recs("3 0 src/Big.cs"), ["src/big.cs"]);

        absent.Should().Be(NotInUnfiltered);
        twice.Should().Be(TwoMatchOne);
        stray.Should().Be(ElidedNotReviewable);
        new[] { absent, twice, stray }.Should().OnlyContain(s => Sentences.Contains(s));
        absent.Should().NotContain("src/x.cs");
        twice.Should().NotContain("renamed");
        stray.Should().NotContain("Big").And.NotContain("big.cs");
    }

    [Fact]
    public void AnAgreeingRangeHasNoDisagreement() =>
        ChangePartition.Disagreement(Recs(Example1Filtered), Recs(Example1Unfiltered), []).Should().BeEmpty();

    [Fact]
    public void CellsAreOrderedByCoverageThenKind_AndNeverRepeatAPair()
    {
        var example13 = Partition(
            ["5 0 research/x.md", "1 1 README.md", "3 0 Directory.Packages.props", "2 0 .gitignore", "7 0 src/Foo.cs", "4 0 tests/FooTests.cs"],
            ["5 0 research/x.md", "1 1 README.md", "3 0 Directory.Packages.props", "2 0 .gitignore", "7 0 src/Foo.cs", "4 0 tests/FooTests.cs"]);
        var mixed = Partition(
            ["1 0 tests/aTests.cs", "1 0 dist/b.js", "- - logo.png", "9 0 src/big.cs"],
            ["1 0 tests/aTests.cs", "- - logo.png", "9 0 src/big.cs"],
            "src/big.cs");

        Shown(example13).Should().Equal("shown/code 1 7 0", "shown/test 1 4 0", "shown/docs 2 6 1", "shown/config 2 5 0");
        Shown(mixed).Should().Equal("shown/test 1 1 0", "elided/code 1 9 0", "excluded/code 1 1 0", "binary/unknown 1 null null");
        example13.Cells.Select(c => (c.Coverage, c.Kind)).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void TheDeltaOfAFixThatReturnedAFileToTheBase_CountsItShown()
    {
        string[] delta = ["0 4 src/a.cs", "2 0 src/b.cs"];

        var size = ChangePartition.OfDelta(Recs(delta), Recs(delta), [], From);

        size.State.Should().Be(ScopeState.Recorded);
        Shown(size).Should().Equal("shown/code 2 2 4");
    }

    [Fact]
    public void PathsMatchOrdinally_ACaseTwinIsNotTheSameFile()
    {
        var size = Partition(["1 0 A.cs"], ["1 0 a.cs"]);

        size.State.Should().Be(ScopeState.NumstatsDisagree);
        size.Cells.Should().BeEmpty("an unavailable scope has no cells");
    }

    [Fact]
    public void TheFromIsKeptOnEveryAnswer()
    {
        Partition(Example1Unfiltered, Example1Filtered).From.Should().Be(From);
        Partition([], []).From.Should().Be(From);
        Partition([], ["1 0 src/x.cs"]).From.Should().Be(From);
    }

    [Fact]
    public void ADeltaThatLeftAnElidedFileAlone_IsRecorded_NotADisagreement()
    {
        string[] delta = ["1 0 src/small.cs"];

        var size = ChangePartition.OfDelta(Recs(delta), Recs(delta), ["src/big.cs"], From);

        size.State.Should().Be(ScopeState.Recorded);
        Shown(size).Should().Equal("shown/code 1 1 0");
    }

    [Fact]
    public void AnElidedFileTheDeltaTouched_IsElidedInTheDelta()
    {
        string[] delta = ["900 0 src/big.cs", "1 0 src/small.cs"];

        var size = ChangePartition.OfDelta(Recs(delta), Recs(delta), ["src/big.cs", "src/other.cs"], From);

        Shown(size).Should().Equal("shown/code 1 1 0", "elided/code 1 900 0");
    }
}
