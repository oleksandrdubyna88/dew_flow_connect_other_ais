using System.Diagnostics;
using CoaiMcp.Core.Context;
using CoaiMcp.Runners.Context;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// <see cref="ChangePartition"/> on a range the size of a vendored import (E2 code gate): matching an unfiltered record to its
/// reviewable twin must not be a scan of the whole filtered list, or a 20 000-file range costs minutes inside the round.
/// </summary>
public sealed class APartitionOfAHugeRangeIsLinearTests
{
    private const int Reviewable = 15_000;
    private const int Hidden = 5_000;

    private static CountedChange Record(string path, string renamedFrom = "") => new(new NumstatChange(path, renamedFrom, false), 3, 1);

    [Fact]
    public void TwentyThousandUnfilteredAndFifteenThousandFilteredRecords_ArePartitionedInUnderASecond()
    {
        var filtered = Enumerable.Range(0, Reviewable).Select(i => Record($"src/module{i / 100}/file{i}.cs")).ToList();
        var unfiltered = filtered.Select(f => Record(f.Change.Path))
            .Concat(Enumerable.Range(0, Hidden).Select(i => Record($"dist/bundle{i}.js")))
            .ToList();
        _ = ChangePartition.Of(filtered[..10], unfiltered[..10], [], "base");

        var watch = Stopwatch.StartNew();
        var scope = ChangePartition.Of(filtered, unfiltered, [], "base");
        watch.Stop();

        scope.State.Should().Be(ScopeState.Recorded, "every reviewable record has its twin");
        scope.Cells.Sum(c => c.Files).Should().Be(Reviewable + Hidden, "every changed file is in exactly one cell");
        scope.Cells.Should().Contain(c => c.Coverage == ChangeCoverage.Excluded && c.Files == Hidden);
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1), "matching is a lookup, not a scan of the filtered list");
    }

    [Fact]
    public void TheDisagreementOfAHugeRange_IsFoundInUnderASecondToo()
    {
        var filtered = Enumerable.Range(0, Reviewable).Select(i => Record($"src/file{i}.cs")).ToList();
        var unfiltered = filtered.Skip(1).Select(f => Record(f.Change.Path)).ToList();

        var watch = Stopwatch.StartNew();
        var why = ChangePartition.Disagreement(filtered, unfiltered, []);
        watch.Stop();

        why.Should().Be("a reviewable file is not in the unfiltered numstat");
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
    }
}
