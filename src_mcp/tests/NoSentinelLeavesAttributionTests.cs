using System.Text.Json;
using CoaiMcp.Core.Authors;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// E1 replaced every text field of every fixture with one sentinel. Nothing attribution produces — the rows, the totals, the contributors,
/// the assignment itself — may carry it (E4 §2, No text). The positive half is what makes the negative one mean something: the sentinel IS
/// in the bytes this test read, so a fixture without sentinels cannot pass vacuously.
/// </summary>
public sealed class NoSentinelLeavesAttributionTests
{
    public static TheoryData<string> Dirs => [.. RecordedCell.All.Select(cell => cell.Case).Distinct(StringComparer.Ordinal)];

    [Theory]
    [MemberData(nameof(Dirs))]
    public void NothingAttributionProducesCarriesTheSentinel(string dir)
    {
        using var recorded = RecordedSession.Open(dir);
        var sentinel = FixtureManifest.Sentinel;

        // the positive half
        (recorded.Fixture.MainFile.Length == 0 || File.ReadAllText(recorded.Fixture.MainFile).Contains(sentinel, StringComparison.Ordinal))
            .Should().BeTrue($"{dir}: the sentinel is in the fixture bytes the test read");

        var text = recorded.Rounds().SelectMany(round =>
        {
            var assignment = recorded.Assign(round.Gate, round.Number);
            var authors = recorded.Authors(round.Gate, round.Number);

            return new[] { Json(assignment), Json(authors), Json(Contributors.Of([authors], withBeforeOpen: true)), assignment.ToString(), authors.ToString() };
        }).ToList();

        text.Should().NotBeEmpty();
        text.Should().OnlyContain(item => !item.Contains(sentinel, StringComparison.Ordinal) && !item.Contains("SENTINEL", StringComparison.OrdinalIgnoreCase), dir);
    }

    private static string Json(object value) => JsonSerializer.Serialize(value);
}
