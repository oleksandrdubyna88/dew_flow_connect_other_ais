using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>
/// <c>/api/usage</c> answers rounds per 1 % of each gauge window (story 6.2 of todo/PLAN_every_round_is_counted.md) — the
/// shared library's figure over the server's own ledger, beside the round windows it is asked with.
/// </summary>
public sealed class RoundsPerPercentServedTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

    private const string Resets = "2026-10-15T07:00:00.0000000Z";

    /// <summary>A review line on slot <c>a</c>, written a second after the reading taken right after its launch.</summary>
    private static UsageLine Line(int minutesAgo, string round, double used, string vendor = "acme", string limit = "acme")
    {
        var observed = Now.AddMinutes(-minutesAgo);
        var gauge = new GaugeOnLine(observed.UtcDateTime.ToString("O"), "plus", [new GaugeWindowOnLine(limit, "primary", 10080, used, Resets)]);

        return new UsageLine(observed.AddSeconds(1), "dev@example.com", vendor, "m", "Architecture", "ok", 5, 100, 10, null)
        {
            Round = round,
            Job = round,
            Slot = "a",
            Gauge = gauge,
        };
    }

    /// <summary>
    /// Epic 6's code round: codex answers <c>rateLimitsByLimitId</c> with SEVERAL limits, and the slot keeps them all —
    /// so must the ledger line, or the derivation cannot see the second one. Through the real writer and reader.
    /// </summary>
    [Fact]
    public void EveryLimitOfAReading_SurvivesTheLedger_AndIsDerived()
    {
        var dir = Directory.CreateTempSubdirectory("coai-two-limits-").FullName;
        try
        {
            var ledger = new UsageLedger(dir);
            foreach (var (minutesAgo, round, used) in new[] { (30, "r0", 10.0), (20, "r1", 12.0) })
            {
                var reading = new GaugeReadingDto(DateTimeOffset.UtcNow.AddMinutes(-minutesAgo), "prolite", string.Empty, true, null,
                [
                    new GaugeWindowDto("codex", "primary", used, 10080, Resets),
                    new GaugeWindowDto("codex_other", "primary", used * 2, 300, "2026-10-12T15:00:00.0000000Z"),
                ]);
                ledger.RecordJob("dev@example.com", "acme", "m", "Architecture", "ok", TimeSpan.FromSeconds(1), CoaiMcp.Core.Findings.Usage.None,
                    "review", "TeamServer", round, "first", LaunchOptions.None, round, "a", reading.OnLine(), round: round);
            }

            var lines = new UsageReader(dir).Read(new UsageRange(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(1))).Lines;
            var derived = RoundsPerPercent.Derive(lines.Select(l => new GaugeLine(l.AtUtc, l.Vendor, l.Slot, l.Round, true, l.Gauge)));

            derived.Ratios.Select(r => (r.LimitId, r.WindowMins)).Should().BeEquivalentTo(
                [("codex", 10080L), ("codex_other", 300L)],
                "the second limit the vendor reported was on the slot, and the ledger line must not drop it");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static UsageDto Answer(string windows, UsageScope scope, params UsageLine[] ledger) =>
        UsageEndpoints.Answer(
            range => new UsageScan([.. ledger.Where(l => range.Contains(l.AtUtc))], 0),
            new UsageAsk(
                UsageWindow.Range("today", Now)!,
                scope,
                windows.Length == 0 ? [] : ((WindowsAsked.Read)UsageWindow.Several(windows, Now)).Windows),
            new Caller("boss@example.com", "", IsAdmin: true),
            Now);

    [Fact]
    public void AnAnswerWithRoundWindows_CarriesEachGaugeWindowsFigure()
    {
        var answer = Answer("5h,24h,week", UsageScope.Company,
            Line(30, "r0", 10), Line(20, "r1", 11), Line(20, "r2", 11), Line(10, "r3", 12));

        var acme = answer.Gauges.Should().ContainSingle().Subject;
        (acme.Vendor, acme.LimitId, acme.WindowMins, acme.Intervals, acme.Unavailable).Should().Be(("acme", "acme", 10080L, 2, string.Empty));
        acme.RoundsPerPercent.Should().BeApproximately(5.0 / 3, 1e-9, "two rounds over 1 %, then one over 1 % — weighted by rounds, (2 × 2 + 1 × 1) / 3");
    }

    [Fact]
    public void CodexsFigure_IsHiddenForTheConsumerCoaiCannotCount()
    {
        var answer = Answer("5h,24h,week", UsageScope.Company,
            Line(30, "r0", 10, "codex", "codex"), Line(20, "r1", 11, "codex", "codex"));

        var codex = answer.Gauges.Should().ContainSingle().Subject;
        codex.RoundsPerPercent.Should().BeNull("direct codex use spends the same limit and no ledger counts it");
        codex.MissingConsumer.Should().Be(GaugeConsumers.DirectCodexUse);
    }

    [Fact]
    public void TheFigure_ReachesBackPastTheNarrowestWindow()
    {
        // Asked only for the last five hours, the figure still reads the week's readings: a gauge window is a week long.
        var answer = Answer("5h", UsageScope.Me, Line(3 * 24 * 60, "r0", 10), Line(2 * 24 * 60, "r1", 12));

        answer.Gauges.Should().ContainSingle().Which.RoundsPerPercent.Should().Be(0.5);
    }

    [Fact]
    public void AnAnswerWithoutRoundWindows_CarriesNoGaugeKey()
    {
        var answer = Answer(string.Empty, UsageScope.Company, Line(30, "r0", 10), Line(20, "r1", 11));

        answer.Gauges.Should().BeNull("a client older than the field asks no windows, and gets today's shape unchanged");
    }
}

/// <summary><c>GET /api/usage?windows=…</c> carries <c>gauges</c> through the real host; <c>?window=</c> alone does not (6.2).</summary>
[Collection(ServerCollection.Name)]
public sealed class RoundsPerPercentEndpointTests
{
    private static object Entry(TimeSpan ago, string round, double used) => new
    {
        utc = (DateTimeOffset.UtcNow - ago).ToString("O"),
        provider = "codex",
        model = "m",
        role = "Architecture",
        stage = "TeamServer",
        seconds = 5.0,
        tokensIn = 100,
        tokensOut = 10,
        costUsd = (double?)null,
        outcome = "ok",
        email = $"dev@{TeamServer.Domain}",
        kind = "review",
        turn = round,
        attempt = "first",
        job = round,
        slot = "a",
        round,
        clientAttempt = "first",
        gaugeObservedUtc = (DateTimeOffset.UtcNow - ago - TimeSpan.FromSeconds(1)).ToString("O"),
        gaugePlan = "prolite",
        gaugeWindows = new[] { new { limitId = "codex", kind = "primary", mins = 10080, usedPercent = used, resetsUtc = "2026-10-15T07:00:00.0000000Z" } },
    };

    private static async Task<JsonNode> Get(TeamServer server, string query)
    {
        var response = await server.ClientFor($"dev@{TeamServer.Domain}").GetAsync($"/api/usage{query}");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
    }

    [Fact]
    public async Task TheWire_NamesTheWindow_AndWhyItsFigureIsHidden()
    {
        using var server = new TeamServer();
        File.WriteAllLines(Path.Combine(server.DataDir, "usage.jsonl"), new[]
        {
            Entry(TimeSpan.FromHours(2), "00000000000000000000000000000001", 10),
            Entry(TimeSpan.FromHours(1), "00000000000000000000000000000002", 12),
        }.Select(l => JsonSerializer.Serialize(l)));

        var answer = await Get(server, "?windows=5h,24h,week");

        var gauge = answer["gauges"]!.AsArray().Should().ContainSingle().Subject!;
        gauge["vendor"]!.GetValue<string>().Should().Be("codex");
        gauge["limitId"]!.GetValue<string>().Should().Be("codex");
        gauge["windowMins"]!.GetValue<long>().Should().Be(10080);
        gauge["roundsPerPercent"].Should().BeNull("hidden is null on the wire, never 0");
        gauge["unavailable"]!.GetValue<string>().Should().Be(GaugeReasons.ConsumerNotCounted);
        gauge["missingConsumer"]!.GetValue<string>().Should().Be(GaugeConsumers.DirectCodexUse);

        var older = await Get(server, "?window=week");
        older.AsObject().ContainsKey("gauges").Should().BeFalse("an older client's request is answered in today's shape");
    }
}
