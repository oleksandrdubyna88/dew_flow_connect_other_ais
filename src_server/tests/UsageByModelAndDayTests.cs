using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>
/// The thirty-day launches-per-day chart under the company answer — its own UTC calendar range,
/// whatever window the summary was asked for. Story 2.2 of <c>PLAN_team_usage_by_person.md</c>.
/// </summary>
public sealed class UsageDailyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 14, 30, 0, TimeSpan.Zero);

    private static UsageLine Line(DateTimeOffset at, string vendor = "codex") =>
        new(at, "dev@example.com", vendor, "m", "Architecture", "ok", 5, 100, 10, null);

    [Fact]
    public void TheChartRangeIsThirtyUtcCalendarDays_EndingWithToday()
    {
        var range = UsageDaily.Range(Now);

        range.FromUtc.Should().Be(new DateTimeOffset(2026, 8, 8, 0, 0, 0, TimeSpan.Zero));
        range.ToUtc.Should().Be(new DateTimeOffset(2026, 9, 7, 0, 0, 0, TimeSpan.Zero), "half-open: tomorrow's midnight is not in it");
        (range.ToUtc - range.FromUtc).Days.Should().Be(UsageDaily.Days);
    }

    [Fact]
    public void EveryDayIsPresent_IncludingTheEmptyOnes()
    {
        // DENSE, so the client draws thirty bars without inventing the missing ones — and a day with
        // no launches is a bar of zero height, which is a fact, not an absence.
        var daily = UsageDaily.Over([], UsageDaily.Range(Now));

        daily.Days.Should().HaveCount(UsageDaily.Days);
        daily.Days[0].Day.Should().Be("2026-08-08");
        daily.Days[^1].Day.Should().Be("2026-09-06");
        daily.FromUtc.Should().Be(UsageDaily.Range(Now).FromUtc);
        daily.ToUtc.Should().Be(UsageDaily.Range(Now).ToUtc);
    }

    [Fact]
    public void ADayIsAUtcCalendarDay_AndItsBarsSumToItsLaunches()
    {
        var daily = UsageDaily.Over(
            [
                Line(new DateTimeOffset(2026, 9, 5, 23, 59, 59, TimeSpan.Zero)),
                Line(new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero)),
                Line(new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero), vendor: "claude"),
                Line(new DateTimeOffset(2026, 9, 6, 1, 0, 0, TimeSpan.Zero)),
            ],
            UsageDaily.Range(Now));

        var fifth = daily.Days.Single(d => d.Day == "2026-09-05");
        fifth.Vendors.Select(v => (v.Vendor, v.Runs)).Should().Equal(("claude", 1), ("codex", 2));
        var sixth = daily.Days.Single(d => d.Day == "2026-09-06");
        sixth.Vendors.Select(v => (v.Vendor, v.Runs)).Should().Equal(
            [("claude", 0), ("codex", 1)],
            "every vendor seen in the range has a row on every day, so a stacked chart needs no lookup");
        daily.Days.Single(d => d.Day == "2026-08-08").Vendors.Should().OnlyContain(v => v.Runs == 0);
    }

    [Fact]
    public void ALaunchOutsideTheThirtyDaysIsNotInTheChart()
    {
        // The scan handed in is the UNION of the summary's window and the chart's range, so the chart
        // must apply its own range rather than trust what it was given.
        var daily = UsageDaily.Over([Line(Now.AddDays(-60)), Line(Now)], UsageDaily.Range(Now));

        daily.Days.Sum(d => d.Vendors.Sum(v => v.Runs)).Should().Be(1);
    }
}

/// <summary>
/// The two additive answers on the wire — <c>models[]</c> under every vendor and <c>daily</c> under
/// the company answer — read as RAW JSON, because what a client of an older or a newer server sees is
/// the property's presence, not a deserialised default.
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class UsageByModelAndDayEndpointTests
{
    private static string Dev => $"dev@{TeamServer.Domain}";

    private static string Admin => $"boss@{TeamServer.Domain}";

    /// <summary>A ledger line stamped at <paramref name="at"/>; <c>null</c> leaves the model column off, as an old line has it.</summary>
    private static string LedgerLine(string email, DateTimeOffset at, string vendor = "codex", string? model = "m") =>
        JsonSerializer.Serialize(new
        {
            utc = at.ToString("O"),
            provider = vendor,
            model,
            role = "Architecture",
            stage = "TeamServer",
            seconds = 5.0,
            tokensIn = 100,
            tokensOut = 10,
            costUsd = (double?)null,
            outcome = "ok",
            email,
        }, new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });

    private static void Ledger(TeamServer server, params string[] lines) =>
        File.WriteAllLines(Path.Combine(server.DataDir, "usage.jsonl"), lines);

    private static async Task<JsonElement> AnswerFor(TeamServer server, string email, string query)
    {
        using var client = server.ClientFor(email);
        var response = await client.GetAsync("/api/usage" + query, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement.Clone();
    }

    private static int Launches(JsonElement daily) =>
        daily.GetProperty("days").EnumerateArray()
            .Sum(day => day.GetProperty("vendors").EnumerateArray().Sum(v => v.GetProperty("runs").GetInt32()));

    [Fact]
    public async Task CompanyToday_CountsTodayInTheSummary_AndBothDaysInTheChart()
    {
        // The reader used to keep only the SELECTED window, so a chart built from a "today" read
        // showed one day. The read covers the union of the window and the chart's range, and the two
        // are aggregated from it separately.
        using var server = new TeamServer();
        var now = DateTimeOffset.UtcNow;
        Ledger(server, LedgerLine(Dev, now), LedgerLine(Dev, now.AddHours(-24)));

        var answer = await AnswerFor(server, Admin, "?scope=company&window=today");

        answer.GetProperty("vendors").EnumerateArray().Single().GetProperty("runs").GetInt32().Should().Be(1, "yesterday is not today");
        Launches(answer.GetProperty("daily")).Should().Be(2, "the chart's thirty days hold both");
    }

    [Fact]
    public async Task CompanyYear_IncludesASixtyDayOldLaunch_ThatTheChartLeavesOut()
    {
        using var server = new TeamServer();
        Ledger(server, LedgerLine(Dev, DateTimeOffset.UtcNow.AddDays(-60)));

        var answer = await AnswerFor(server, Admin, "?scope=company&window=year");

        answer.GetProperty("vendors").EnumerateArray().Single().GetProperty("runs").GetInt32().Should().Be(1);
        var daily = answer.GetProperty("daily");
        daily.GetProperty("days").GetArrayLength().Should().Be(UsageDaily.Days, "dense: every day is present even when nothing happened");
        Launches(daily).Should().Be(0);
    }

    [Fact]
    public async Task TheDailyRangeIsNamed_AndItsDaysAreCalendarStrings()
    {
        using var server = new TeamServer();

        var daily = (await AnswerFor(server, Admin, "?scope=company")).GetProperty("daily");

        daily.GetProperty("fromUtc").GetDateTimeOffset().Should().BeBefore(daily.GetProperty("toUtc").GetDateTimeOffset());
        foreach (var day in daily.GetProperty("days").EnumerateArray())
        {
            // A string the client prints as it is — never through `new Date()`, which would shift it
            // into the viewer's local day.
            day.GetProperty("day").GetString().Should().MatchRegex(@"^\d{4}-\d{2}-\d{2}$");
            day.GetProperty("vendors").ValueKind.Should().Be(JsonValueKind.Array);
        }
    }

    [Fact]
    public async Task TheMeAnswer_HasNoDaily()
    {
        // The chart is the company view's. Absent — not null — so a client can read "no daily" as
        // "this is not the company answer, or this server is older than the chart".
        using var server = new TeamServer();
        Ledger(server, LedgerLine(Dev, DateTimeOffset.UtcNow));

        var mine = await AnswerFor(server, Dev, "");

        mine.TryGetProperty("daily", out _).Should().BeFalse();
    }

    [Fact]
    public async Task ModelsAreOnTheWire_ForBothScopes()
    {
        using var server = new TeamServer();
        Ledger(server, LedgerLine(Dev, DateTimeOffset.UtcNow, model: "gpt-5"));

        var mine = await AnswerFor(server, Dev, "");
        var all = await AnswerFor(server, Admin, "?scope=company");

        foreach (var answer in new[] { mine, all })
        {
            var models = answer.GetProperty("vendors").EnumerateArray().Single().GetProperty("models");
            var model = models.EnumerateArray().Should().ContainSingle().Subject;
            model.GetProperty("model").GetString().Should().Be("gpt-5");
            model.GetProperty("runs").GetInt32().Should().Be(1);
            model.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
                ["model", "runs", "failed", "tokensIn", "tokensOut", "costUsd", "costIsFloor", "unpricedRuns"]);
        }
    }

    [Fact]
    public async Task AnOldLineWithoutAModel_GroupsUnderAnEmptyModel()
    {
        using var server = new TeamServer();
        Ledger(server, LedgerLine(Dev, DateTimeOffset.UtcNow, model: null));

        var all = await AnswerFor(server, Admin, "?scope=company");

        all.GetProperty("vendors").EnumerateArray().Single().GetProperty("models").EnumerateArray()
            .Should().ContainSingle().Which.GetProperty("model").GetString().Should().BeEmpty("the client labels it unknown");
    }
}
