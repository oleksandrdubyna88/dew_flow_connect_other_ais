using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>
/// An old client that asks <c>?window=</c> reads TODAY's answer (story 5.1 of todo/PLAN_every_round_is_counted.md): every key
/// the answer had before the round counts, in the same order, with the same values — the counts are ADDED to each vendor
/// row, and the several-window block is absent unless it was asked for.
/// </summary>
/// <remarks>
/// The golden text below was captured from the server BEFORE story 5.1 (the same lines, the same fixed instant), so it is
/// the deployed shape, not this build's reading of it. A test that compared the new answer with itself could not notice
/// a renamed or reordered key; this one fails on either.
/// </remarks>
public sealed class TheWindowAnswerKeepsItsBytesTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The keys story 5.1 adds to a vendor total — the ONLY difference an old client may see.</summary>
    private static readonly string[] Added =
        ["rounds", "runsWithoutRound", "retries", "repairs", "followUps", "cancelled", "noBudget", "launchesByKind"];

    private static UsageLine Line(string email, string vendor, string model, string outcome, int minutesAgo, JobKind kind = JobKind.Review) =>
        new(At.AddMinutes(-minutesAgo), email, vendor, model, "Architecture", outcome, 4.5, 1000, 100, null, kind);

    private static IReadOnlyList<UsageLine> Lines { get; } =
    [
        Line("dev@example.com", "codex", "gpt-5.6-luna", "ok", 10),
        Line("dev@example.com", "codex", "gpt-5.6-luna", "RateLimited", 20),
        Line("boss@example.com", "gemini", "", "ok", 30, JobKind.Chat),
    ];

    /// <summary>The company answer over a window, built as the endpoint builds it — less the thirty-day chart.</summary>
    /// <remarks>
    /// The chart is left out of the golden text because story 5.1 does not touch it and thirty dense days would bury the
    /// keys this test is about; it stays covered by <c>UsageByModelAndDayTests</c> and <c>http/usage/usage.http</c>.
    /// </remarks>
    private static UsageDto CompanyAnswer()
    {
        var range = UsageWindow.Range("week", At)!;

        return new UsageDto(
            range.FromUtc,
            range.ToUtc,
            "company",
            UsageTotals.ByVendor(Lines),
            UsageTotals.ByPerson(Lines),
            0,
            UsageTotals.ByKind(Lines));
    }

    /// <summary>The answer's text with every key story 5.1 added taken out again.</summary>
    private static string WithoutTheCounts(string json)
    {
        var node = JsonNode.Parse(json)!;
        foreach (var vendor in VendorObjects(node))
        {
            foreach (var key in Added)
            {
                vendor.Remove(key);
            }
        }

        // Relaxed escaping on the way back out, so `+` in an offset reads as `+` on both sides of the comparison: the
        // server escapes it (`+`), and a golden text that held the escape was unescaped by the tooling that wrote it.
        return node.ToJsonString(new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    private static IEnumerable<JsonObject> VendorObjects(JsonNode answer) =>
    [
        .. answer["vendors"]!.AsArray().Select(v => v!.AsObject()),
        .. answer["people"]!.AsArray().SelectMany(p => p!["vendors"]!.AsArray()).Select(v => v!.AsObject()),
    ];

    [Fact]
    public void AWindowAnswer_IsTodaysBytes_WithTheCountsOnlyAdded()
    {
        var json = JsonSerializer.Serialize(CompanyAnswer(), ServerJsonContext.Default.UsageDto);

        WithoutTheCounts(json).Should().Be(Golden,
            "an old client asking ?window= must read every key it read before, in the same order, with the same values");
    }

    [Fact]
    public void AWindowAnswer_CarriesNoSeveralWindowBlock_UnlessItWasAsked()
    {
        var json = JsonSerializer.Serialize(CompanyAnswer(), ServerJsonContext.Default.UsageDto);

        JsonNode.Parse(json)!.AsObject().ContainsKey("windows").Should().BeFalse(
            "a key nobody asked for is not today's shape — absent, not null, as `daily` is on a personal answer");
    }

    /// <summary>Captured from the server before story 5.1, over <see cref="Lines"/> at <see cref="At"/>.</summary>
    private const string Golden =
        """{"fromUtc":"2026-10-04T12:00:01+00:00","toUtc":"2026-10-11T12:00:01+00:00","scope":"company","vendors":[{"vendor":"codex","runs":2,"failed":1,"tokensIn":2000,"tokensOut":200,"seconds":9,"costUsd":null,"costIsFloor":false,"unpricedRuns":2,"models":[{"model":"gpt-5.6-luna","runs":2,"failed":1,"tokensIn":2000,"tokensOut":200,"costUsd":null,"costIsFloor":false,"unpricedRuns":2}]},{"vendor":"gemini","runs":1,"failed":0,"tokensIn":1000,"tokensOut":100,"seconds":4.5,"costUsd":null,"costIsFloor":false,"unpricedRuns":1,"models":[{"model":"","runs":1,"failed":0,"tokensIn":1000,"tokensOut":100,"costUsd":null,"costIsFloor":false,"unpricedRuns":1}]}],"people":[{"email":"boss@example.com","vendors":[{"vendor":"gemini","runs":1,"failed":0,"tokensIn":1000,"tokensOut":100,"seconds":4.5,"costUsd":null,"costIsFloor":false,"unpricedRuns":1,"models":[{"model":"","runs":1,"failed":0,"tokensIn":1000,"tokensOut":100,"costUsd":null,"costIsFloor":false,"unpricedRuns":1}]}]},{"email":"dev@example.com","vendors":[{"vendor":"codex","runs":2,"failed":1,"tokensIn":2000,"tokensOut":200,"seconds":9,"costUsd":null,"costIsFloor":false,"unpricedRuns":2,"models":[{"model":"gpt-5.6-luna","runs":2,"failed":1,"tokensIn":2000,"tokensOut":200,"costUsd":null,"costIsFloor":false,"unpricedRuns":2}]}]}],"unreadableLines":0,"kinds":[{"kind":"review","runs":2,"failed":1,"tokensIn":2000,"tokensOut":200,"seconds":9,"costUsd":null,"costIsFloor":false,"unpricedRuns":2},{"kind":"chat","runs":1,"failed":0,"tokensIn":1000,"tokensOut":100,"seconds":4.5,"costUsd":null,"costIsFloor":false,"unpricedRuns":1}]}""";
}
