using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>
/// A gauge reading is written as its windows and nothing derived from them (story 6.2's report, 2026-10-12).
/// </summary>
/// <remarks>
/// <c>GaugeReadingDto.Primary</c> and <c>Secondary</c> are computed views over <c>Windows</c>. System.Text.Json writes every
/// public getter, so each reading reached the catalog answer and the slot's <c>state.json</c> twice — the windows, and
/// again as <c>primary</c> / <c>secondary</c> — a second copy no reader asked for, which a later change could let drift.
/// </remarks>
public sealed class TheGaugeIsWrittenOnceTests
{
    private static readonly GaugeReadingDto Reading = new(
        new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero),
        "prolite",
        string.Empty,
        OrdinaryUsageAllowed: true,
        ResetCreditsAvailable: 1,
        Windows: [new GaugeWindowDto("codex", "primary", 40, 10080, "2026-10-14T07:48:11Z")]);

    [Fact]
    public void AReading_IsWrittenAsItsWindows_NotAgainAsPrimaryAndSecondary()
    {
        var json = JsonSerializer.Serialize(Reading, ServerJsonContext.Default.GaugeReadingDto);

        using var document = JsonDocument.Parse(json);
        var keys = document.RootElement.EnumerateObject().Select(property => property.Name).ToList();
        keys.Should().Contain("windows");
        keys.Should().NotContain(["primary", "secondary"], "the windows are the reading; primary/secondary are views over them");
    }

    [Fact]
    public void AReadReading_StillKnowsItsPrimaryWindow()
    {
        var json = JsonSerializer.Serialize(Reading, ServerJsonContext.Default.GaugeReadingDto);

        var back = JsonSerializer.Deserialize(json, ServerJsonContext.Default.GaugeReadingDto)!;

        back.Primary!.UsedPercent.Should().Be(40, "the view is computed again from the windows it was written with");
    }
}
