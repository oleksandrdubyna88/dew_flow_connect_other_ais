using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>
/// The server counts rounds (story 5.1 of todo/PLAN_every_round_is_counted.md): per vendor, the DISTINCT round ids its review
/// lines carry, the review lines that carry none apart, the client's retries, repairs and follow-ups, the cancelled and the
/// budget-refused, and the launches per kind — over <c>vendors[]</c> and <c>people[].vendors[]</c> alike.
/// </summary>
public sealed class RoundCountsTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    private const string R1 = "0123456789abcdef0123456789abcdef";
    private const string R2 = "fedcba9876543210fedcba9876543210";

    private static UsageLine Line(
        string round = "", string clientAttempt = "", string job = "", string outcome = "ok", string attempt = "first",
        JobKind kind = JobKind.Review, string role = "Architecture", string vendor = "codex", string email = "dev@example.com") =>
        new(At, email, vendor, "m", role, outcome, 5, 100, 10, null, kind, Turn: job, Attempt: attempt)
        {
            Round = round,
            ClientAttempt = clientAttempt,
            Job = job,
        };

    private static VendorTotal Codex(params UsageLine[] lines) =>
        UsageTotals.ByVendor(lines).Single(v => v.Vendor == "codex");

    [Fact]
    public void FourRolesOfOneRound_CountAsOneRound()
    {
        var codex = Codex(
            Line(R1, "first", "j1", role: "Architecture"),
            Line(R1, "first", "j2", role: "Security"),
            Line(R1, "first", "j3", role: "Tests"),
            Line(R1, "first", "j4", role: "Performance"));

        codex.Rounds.Should().Be(1, "one round launches four reviewers on a code stage, and it is still one round");
        codex.Runs.Should().Be(4, "the launches are what they were");
        codex.RunsWithoutRound.Should().Be(0);
    }

    [Fact]
    public void ARetryAndARepair_AddNoRound_AndAreCountedOncePerRequest()
    {
        var codex = Codex(
            Line(R1, "first", "j1"),
            // A client retry is a whole new job; this one met a refusing account first, so it wrote two lines.
            Line(R1, "retry", "j2", outcome: "RateLimited", attempt: "rotated"),
            Line(R1, "retry", "j2", attempt: "rotated"),
            Line(R1, "repair", "j3"));

        codex.Rounds.Should().Be(1, "a retry and a repair belong to the round they repair");
        codex.Retries.Should().Be(1, "one retry REQUEST, however many accounts its job met");
        codex.Repairs.Should().Be(1);
        codex.FollowUps.Should().Be(0);
    }

    [Fact]
    public void AClientFollowUp_IsCounted_AndTheServersOwnFollowUpAndRotationAreNot()
    {
        var codex = Codex(
            // The server's own continuation of a first request: its two lines say followUp in the SERVER's word only.
            Line(R1, "first", "j1"),
            Line(R1, "first", "j1", attempt: "followUp"),
            // The client's follow-up request.
            Line(R2, "followUp", "j2"));

        codex.FollowUps.Should().Be(1, "only the CLIENT's word counts a follow-up; the server's continuation is a launch inside one request");
        codex.Retries.Should().Be(0, "a rotation is the server's, never a client retry");
        codex.Runs.Should().Be(3);
    }

    [Fact]
    public void LinesWithoutARound_AreCountedApart_NeverFoldedIntoAGuess()
    {
        var codex = Codex(
            Line(),
            Line(),
            Line(R1, "first", "j1"),
            Line(kind: JobKind.Chat));

        codex.Rounds.Should().Be(1);
        codex.RunsWithoutRound.Should().Be(2, "two review lines from a client too old to send a round");
    }

    [Fact]
    public void TheCanary_IsNotARound_AndNotARunWithoutARound()
    {
        var codex = Codex(
            Line(R1, "first", "c1", kind: JobKind.Canary),
            Line(kind: JobKind.Canary),
            Line(R2, "first", "j1"));

        codex.Rounds.Should().Be(1, "the deploy's check is not a person's round");
        codex.RunsWithoutRound.Should().Be(0, "nor is it a run from an old client");
        codex.LaunchesByKind.Should().Equal(new Dictionary<string, int> { ["canary"] = 2, ["review"] = 1 });
    }

    [Fact]
    public void CancelledAndBudgetRefusedLines_AreCountedByTheirOutcome()
    {
        var codex = Codex(
            Line(R1, "first", "j1", outcome: "Cancelled"),
            Line(R1, "first", "j2", outcome: "NoBudget"),
            Line(R1, "first", "j3", outcome: "NoBudget"),
            Line(R1, "first", "j4"));

        codex.Cancelled.Should().Be(1);
        codex.NoBudget.Should().Be(2);
        codex.Rounds.Should().Be(1, "a refused request still belonged to the round that sent it");
    }

    [Fact]
    public void LaunchesPerKind_LeaveOutWhatNeverLaunched()
    {
        var codex = Codex(
            Line(R1, "first", "j1"),
            Line(R1, "first", "j2", outcome: "NoBudget"),
            Line(kind: JobKind.Chat));

        codex.LaunchesByKind.Should().Equal(new Dictionary<string, int> { ["chat"] = 1, ["review"] = 1 },
            "a job ended at claim for want of budget started no vendor — a line, an honest zero, and no launch");
        codex.Runs.Should().Be(3, "runs stays what it always was: the ledger's lines");
    }

    [Fact]
    public void ALineFromBeforeTheRoundFields_IsARunWithoutARound()
    {
        // Written before story 4.2: no round, no client attempt, no job.
        var old = new UsageLine(At, "dev@example.com", "codex", "m", "Architecture", "ok", 5, 100, 10, null);

        var codex = Codex(old, old);

        codex.RunsWithoutRound.Should().Be(2);
        codex.Rounds.Should().Be(0);
        codex.Retries.Should().Be(0);
    }

    [Fact]
    public void AClientWordWithNoJob_IsOneRequestPerLine()
    {
        // A line with neither job nor turn (no server writes one, but the reader must not merge strangers).
        var codex = Codex(Line(R1, "retry", job: ""), Line(R1, "retry", job: ""));

        codex.Retries.Should().Be(2);
    }

    [Fact]
    public void EachPersonsVendors_CarryTheSameCounts()
    {
        var lines = new[]
        {
            Line(R1, "first", "j1"),
            Line(R1, "retry", "j2"),
            Line(R2, "first", "j3", email: "other@example.com"),
            Line(email: "other@example.com"),
        };

        var people = UsageTotals.ByPerson(lines);

        var dev = people.Single(p => p.Email == "dev@example.com").Vendors.Single();
        var other = people.Single(p => p.Email == "other@example.com").Vendors.Single();
        (dev.Rounds, dev.Retries, dev.RunsWithoutRound).Should().Be((1, 1, 0));
        (other.Rounds, other.Retries, other.RunsWithoutRound).Should().Be((1, 0, 1));
        UsageTotals.ByVendor(lines).Single().Rounds.Should().Be(2, "the company's row counts both people's rounds");
    }
}

/// <summary>The trailing 5 h and 24 h windows, and several windows answered from one scan (story 5.1).</summary>
public sealed class SeveralWindowsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("5h", 5)]
    [InlineData("24h", 24)]
    public void TheHourWindows_AreTrailing(string name, int hours)
    {
        var range = UsageWindow.Range(name, Now)!;

        (range.ToUtc - range.FromUtc).Should().Be(TimeSpan.FromHours(hours));
        range.Contains(Now).Should().BeTrue("a run that finished during this request is in every window");
        range.Contains(Now.AddHours(-hours)).Should().BeFalse(
            "the window ends a second ahead of now, so its start is a second after now − span: the run exactly at now − span is the window before");
        range.Contains(Now.AddHours(-hours).AddSeconds(1)).Should().BeTrue();
    }

    [Fact]
    public void TheFiveHourWindow_IsTrailing_NotSinceMidnight()
    {
        var early = new DateTimeOffset(2026, 10, 11, 2, 0, 0, TimeSpan.Zero);

        UsageWindow.Range("5h", early)!.FromUtc.Should().Be(early.AddSeconds(1).AddHours(-5),
            "at 02:00 UTC the last five hours reach into yesterday, which a calendar box would not");
    }

    [Fact]
    public void SeveralNames_AreReadInTheOrderAsked_EachOnce()
    {
        var asked = UsageWindow.Several("5h, 24h,week,5H,", Now);

        asked.Should().BeOfType<WindowsAsked.Read>().Which.Windows.Select(w => w.Name)
            .Should().Equal(["5h", "24h", "week"], "trimmed, lower-cased, an empty item ignored and a repeat answered once");
    }

    [Fact]
    public void AnUnknownName_AmongSeveral_IsRefusedByName()
    {
        UsageWindow.Several("5h,fortnight", Now).Should().BeOfType<WindowsAsked.Refused>()
            .Which.Name.Should().Be("fortnight");
    }

    [Fact]
    public void NoNames_IsNoWindows()
    {
        UsageWindow.Several(null, Now).Should().BeOfType<WindowsAsked.Read>().Which.Windows.Should().BeEmpty();
        UsageWindow.Several(" , ", Now).Should().BeOfType<WindowsAsked.Read>().Which.Windows.Should().BeEmpty();
    }

    private static UsageLine At(int minutesAgo, string round) =>
        new(Now.AddMinutes(-minutesAgo), "dev@example.com", "codex", "m", "Architecture", "ok", 5, 100, 10, null)
        {
            Round = round,
            ClientAttempt = "first",
            Job = round,
        };

    [Fact]
    public void SeveralWindows_OneScan()
    {
        var scans = new List<UsageRange>();
        IReadOnlyList<UsageLine> ledger =
        [
            At(3 * 60, "00000000000000000000000000000001"),
            At(10 * 60, "00000000000000000000000000000002"),
            At(3 * 24 * 60, "00000000000000000000000000000003"),
        ];
        UsageScan Read(UsageRange range)
        {
            scans.Add(range);

            return new UsageScan([.. ledger.Where(l => range.Contains(l.AtUtc))], 0);
        }
        var windows = ((WindowsAsked.Read)UsageWindow.Several("5h,24h,week", Now)).Windows;
        var ask = new UsageAsk(UsageWindow.Range("today", Now)!, UsageScope.Company, windows);

        var answer = UsageEndpoints.Answer(Read, ask, new Caller("boss@example.com", "", IsAdmin: true), Now);

        scans.Should().ContainSingle("every window, the summary and the chart are answered from ONE read of the ledger");
        answer.Windows!.Select(w => (w.Window, w.Vendors.Single().Rounds)).Should().Equal(
            [("5h", 1), ("24h", 2), ("week", 3)]);
        answer.Windows!.Select(w => w.People.Single().Vendors.Single().Rounds).Should().Equal([1, 2, 3],
            "an admin's windows carry each person's vendors with the same counts");
    }

    [Fact]
    public void APersonalAnswer_WithSeveralWindows_IsTheCallersOwn()
    {
        var mine = new UsageLine(Now.AddMinutes(-5), "dev@example.com", "codex", "m", "Architecture", "ok", 5, 1, 1, null) { Round = "00000000000000000000000000000001" };
        var theirs = mine with { Email = "other@example.com", Round = "00000000000000000000000000000002" };
        var windows = ((WindowsAsked.Read)UsageWindow.Several("5h", Now)).Windows;

        var answer = UsageEndpoints.Answer(
            _ => new UsageScan([mine, theirs], 0),
            new UsageAsk(UsageWindow.Range("today", Now)!, UsageScope.Me, windows),
            new Caller("dev@example.com", "", IsAdmin: false),
            Now);

        var five = answer.Windows!.Single();
        five.Vendors.Single().Rounds.Should().Be(1, "a person's windows count their own rounds, never a colleague's");
        five.People.Should().BeEmpty("the personal view is not a directory of colleagues, in a window either");
    }

    [Fact]
    public void APersonalAnswer_ReadsAsFarBackAsItsWidestWindow()
    {
        // The top of a personal answer is TODAY and it has no thirty-day chart to widen its read, so only the windows
        // themselves can make the one scan reach three days back.
        var threeDaysAgo = At(3 * 24 * 60, "00000000000000000000000000000003");
        var windows = ((WindowsAsked.Read)UsageWindow.Several("week", Now)).Windows;

        var answer = UsageEndpoints.Answer(
            range => new UsageScan([.. new[] { threeDaysAgo }.Where(l => range.Contains(l.AtUtc))], 0),
            new UsageAsk(UsageWindow.Range("today", Now)!, UsageScope.Me, windows),
            new Caller("dev@example.com", "", IsAdmin: false),
            Now);

        answer.Windows!.Single().Vendors.Single().Rounds.Should().Be(1, "the week's round was read, not cut off at today's midnight");
        answer.Vendors.Should().BeEmpty("and the top of the answer is still today's window alone");
    }
}

/// <summary><c>GET /api/usage?windows=…</c> through the real host (story 5.1).</summary>
[Collection(ServerCollection.Name)]
public sealed class RoundCountsEndpointTests
{
    private const string R1 = "0123456789abcdef0123456789abcdef";

    private static void Seed(TeamServer server, params object[] lines) =>
        File.WriteAllLines(Path.Combine(server.DataDir, "usage.jsonl"), lines.Select(l => JsonSerializer.Serialize(l)));

    private static object Entry(TimeSpan ago, string email, string round, string clientAttempt, string job, string outcome = "ok", string kind = "review") => new
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
        outcome,
        email,
        kind,
        turn = job,
        attempt = "first",
        job,
        round,
        clientAttempt,
    };

    private static async Task<JsonNode> Get(TeamServer server, string email, string query)
    {
        var response = await server.ClientFor(email).GetAsync($"/api/usage{query}");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
    }

    [Fact]
    public async Task SeveralWindows_AreAnsweredByName_EachWithItsRangeAndCounts()
    {
        using var server = new TeamServer();
        var dev = $"dev@{TeamServer.Domain}";
        Seed(server,
            Entry(TimeSpan.FromHours(1), dev, R1, "first", "j1"),
            Entry(TimeSpan.FromHours(1), dev, R1, "retry", "j2"),
            Entry(TimeSpan.FromHours(10), dev, "", "", "j3"),
            Entry(TimeSpan.FromDays(3), dev, "", "", "j4", outcome: "Cancelled"));

        var answer = await Get(server, dev, "?windows=5h,24h,week");

        var windows = answer["windows"]!.AsArray();
        windows.Select(w => (string)w!["window"]!).Should().Equal(["5h", "24h", "week"]);
        var five = windows[0]!["vendors"]![0]!;
        ((int)five["rounds"]!, (int)five["retries"]!, (int)five["runsWithoutRound"]!).Should().Be((1, 1, 0));
        ((int)windows[1]!["vendors"]![0]!["runsWithoutRound"]!).Should().Be(1);
        ((int)windows[2]!["vendors"]![0]!["cancelled"]!).Should().Be(1);
        var span = DateTimeOffset.Parse((string)windows[0]!["toUtc"]!) - DateTimeOffset.Parse((string)windows[0]!["fromUtc"]!);
        span.Should().Be(TimeSpan.FromHours(5), "each window names the range it counted");
        ((string)answer["scope"]!).Should().Be("me");
    }

    [Fact]
    public async Task AWindowAnswer_StaysTodaysShape_WithNoSeveralWindowBlock()
    {
        using var server = new TeamServer();
        var dev = $"dev@{TeamServer.Domain}";
        Seed(server, Entry(TimeSpan.FromMinutes(5), dev, R1, "first", "j1"));

        var answer = await Get(server, dev, "?window=week");

        answer.AsObject().ContainsKey("windows").Should().BeFalse("an old client asking ?window= reads today's shape");
        ((int)answer["vendors"]![0]!["rounds"]!).Should().Be(1, "the counts are added to today's vendor rows too");
    }

    [Fact]
    public async Task AnUnknownWindowAmongSeveral_IsRefusedNamingTheLegalOnes()
    {
        using var server = new TeamServer();

        var refused = await server.ClientFor($"dev@{TeamServer.Domain}").GetAsync("/api/usage?windows=5h,fortnight");

        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = (await refused.Content.ReadFromJsonAsync<ErrorDto>())!.Error;
        error.Should().Contain("'fortnight'").And.Contain("5h").And.Contain("24h").And.Contain("week");
    }

    [Fact]
    public async Task AnAdminsWindows_CarryEachPersonsVendorsWithTheCounts()
    {
        using var server = new TeamServer();
        Seed(server,
            Entry(TimeSpan.FromHours(1), $"dev@{TeamServer.Domain}", R1, "first", "j1"),
            Entry(TimeSpan.FromHours(1), $"other@{TeamServer.Domain}", "", "", "j2", kind: "canary"));

        var answer = await Get(server, $"boss@{TeamServer.Domain}", "?scope=company&windows=5h");

        var five = answer["windows"]![0]!;
        var people = five["people"]!.AsArray();
        people.Select(p => (string)p!["email"]!).Should().Equal([$"dev@{TeamServer.Domain}", $"other@{TeamServer.Domain}"]);
        ((int)people[0]!["vendors"]![0]!["rounds"]!).Should().Be(1);
        var canaryRow = people[1]!["vendors"]![0]!;
        ((int)canaryRow["rounds"]!, (int)canaryRow["runsWithoutRound"]!).Should().Be((0, 0), "a canary is neither");
        ((int)canaryRow["launchesByKind"]!["canary"]!).Should().Be(1);
    }

    [Fact]
    public async Task AnEmptyWindowsParameter_IsNoWindows()
    {
        using var server = new TeamServer();

        var answer = await Get(server, $"dev@{TeamServer.Domain}", "?windows=");

        answer.AsObject().ContainsKey("windows").Should().BeFalse("`?windows=` is a client saying nothing, as `?window=` is");
    }
}
