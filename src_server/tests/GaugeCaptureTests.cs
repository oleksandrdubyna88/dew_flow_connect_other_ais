using System.Net.Http.Json;
using System.Text.Json;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>
/// The Team server captures the codex gauge, safely — story 6.1a of <c>todo/PLAN_every_round_is_counted.md</c>: inside
/// the slot's lease, after the job, behind the expiry guard, suspended on an authentication error, never changing the
/// job's result, never logging what the reader said.
/// </summary>
/// <remarks>
/// <para>Through the REAL <see cref="JobRunner"/>, <see cref="SlotRegistry"/> on disk and <see cref="GaugeCapture"/>,
/// with two fakes for the two processes a test cannot have: a reviewer (a fake <see cref="IReviewLauncher"/>, the seam
/// every runner test uses) and the reader (a fake <see cref="IProcessLauncher"/> answering the MEASURED app-server
/// line, research/RESULTS_vendor_quota_gauges.md §2.2). The containment against a real process is
/// <see cref="ReaderBarrierTests"/>'s and the crash harness's.</para>
/// <para>Every credential is fake and disposable: a token set in codex's shape under a temp directory, a JWT signed by
/// nobody whose one claim is its expiry.</para>
/// <para>In the server collection because two of these start the in-process <see cref="TeamServer"/>, whose
/// configuration is process-global environment variables: run beside another class's server, the catalog read
/// another test's data directory — seen once in the full suite (1 of 572) and never alone.</para>
/// </remarks>
[Collection(ServerCollection.Name)]
public sealed class GaugeCaptureTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coai-gauge-" + Guid.NewGuid().ToString("N"));

    private readonly List<string> _notes = [];

    /// <summary>Far enough that the guard passes by days, not minutes.</summary>
    private static readonly DateTimeOffset FarExpiry = DateTimeOffset.UtcNow.AddDays(9);

    public GaugeCaptureTests()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(
            Path.Combine(_dir, "vendors.json"),
            """[{ "id": "codex", "runtime": "codex", "models": ["m"], "slots": ["a"] }]""");
        var registry = new SlotRegistry(_dir, new JsonFileStore());
        registry.MarkSignedIn(registry.Read("codex", "a"));
        GaugeFixtures.WriteAuth(Slot().Directory, FarExpiry);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A temp directory a stray handle still holds is the OS's to clean, not this test's to fail on.
        }
    }

    [Fact]
    public async Task AFixtureGaugeAnswer_IsCapturedAfterTheJob_StoredOnTheSlot_AndOnTheJobsLedgerLine()
    {
        var reader = new GaugeFixtures.Reader(GaugeFixtures.Notifications + GaugeFixtures.Answer + "\n");
        var (jobs, runner) = Build(new GaugeFixtures.Reviewer(), reader.For);
        var job = Job();
        jobs.Submit(job);

        (await runner.PumpAsync("codex", CancellationToken.None)).Should().BeTrue();

        jobs.All().Single().Status.Should().Be(JobStatus.Done, "the job's result is the vendor's, whatever the gauge did");
        var stored = Slot().Gauge;
        stored.Should().NotBeNull("the reading is the slot's one overwritten snapshot, read back from state.json");
        stored!.PlanType.Should().Be("prolite");
        stored.OrdinaryUsageAllowed.Should().BeTrue();
        stored.ResetCreditsAvailable.Should().Be(1);
        stored.RateLimitReachedType.Should().BeEmpty();
        stored.Stale.Should().BeFalse();
        var window = stored.Windows.Should().ContainSingle("the measured plan reports one window, the weekly one").Subject;
        window.LimitId.Should().Be("codex");
        window.Kind.Should().Be("primary");
        window.UsedPercent.Should().Be(40);
        window.WindowDurationMins.Should().Be(10080);
        window.ResetsAtUtc.Should().Be("2026-10-14T07:48:11.0000000Z");

        var line = Lines().Single();
        line.GetProperty("gaugePlan").GetString().Should().Be("prolite");
        var onLine = line.GetProperty("gaugeWindows").EnumerateArray().Should().ContainSingle().Subject;
        onLine.GetProperty("limitId").GetString().Should().Be("codex");
        onLine.GetProperty("kind").GetString().Should().Be("primary");
        onLine.GetProperty("usedPercent").GetDouble().Should().Be(40);
        onLine.GetProperty("mins").GetInt64().Should().Be(10080);
        onLine.GetProperty("resetsUtc").GetString().Should().Be("2026-10-14T07:48:11.0000000Z");
        line.GetProperty("gaugeObservedUtc").GetString().Should().EndWith("Z");
        line.GetProperty("job").GetString().Should().Be(job.Id, "the reading sits on the job's own line");

        var read = new UsageReader(_dir).Read(new UsageRange(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(1))).Lines.Single();
        read.Gauge.Windows.Should().ContainSingle().Which.Should().Be(
            new GaugeWindowOnLine("codex", "primary", 10080, 40, "2026-10-14T07:48:11.0000000Z"),
            "the server's reader reads the writer's own record back");
    }

    [Fact]
    public async Task TheReaderIsAsked_OnlyTheRead_InsideTheSlotsOwnHome_Confined()
    {
        var reader = new GaugeFixtures.Reader(GaugeFixtures.Answer + "\n");
        var (jobs, runner) = Build(new GaugeFixtures.Reviewer(), reader.For);
        jobs.Submit(Job());

        await runner.PumpAsync("codex", CancellationToken.None);

        var request = reader.Requests.Should().ContainSingle("one read per job").Subject;
        request.Arguments.Should().Equal("app-server");
        request.StdIn.Should().Contain("account/rateLimits/read");
        request.StdIn.Should().NotContain("consume").And.NotContain("login", "a read reads: never a reset credit, never a sign-in");
        request.StdIn.Split('\n', StringSplitOptions.RemoveEmptyEntries).Should().HaveCount(3, "initialize, initialized, the read — and nothing else");
        request.Environment["CODEX_HOME"].Should().Be(Path.Combine(Slot().Directory, ".codex"), "the slot's own token set, no copy");
        request.InheritsEnvironment.Should().BeFalse("confined, as every launch on this box is");
        request.EndsEarly.Should().BeTrue("a server is ended on its answer, not waited out");
        request.Timeout.Should().Be(GaugeCapture.ReaderLifetime);
        request.TrackAs.Should().Be(GaugeCapture.ReaderLabel);
    }

    [Fact]
    public async Task ANearExpiryToken_SpawnsNothing()
    {
        GaugeFixtures.WriteAuth(Slot().Directory, DateTimeOffset.UtcNow + GaugeCapture.ReaderLifetime + GaugeCapture.ExpiryMargin - TimeSpan.FromSeconds(30));
        var counting = new GaugeFixtures.Counting();
        var (jobs, runner) = Build(new GaugeFixtures.Reviewer(), counting.For);
        jobs.Submit(Job());

        await runner.PumpAsync("codex", CancellationToken.None);

        counting.Launches.Should().Be(0, "a token this close to its expiry is one codex would refresh on the read");
        _notes.Should().ContainSingle(note => note.Contains(GaugeCodes.TokenNearExpiry, StringComparison.Ordinal));
        State().Reader.Should().BeNull("no marker is written for a reader that is not spawned");
        Lines().Single().TryGetProperty("gaugeWindows", out _).Should().BeFalse("no reading, no window — never 0");
    }

    [Fact]
    public async Task AnUnknownExpiry_SpawnsNothing()
    {
        File.WriteAllText(CodexAuthExpiry.AuthFileOf(Slot().Directory), """{"auth_mode":"chatgpt","tokens":{"access_token":"not-a-jwt"},"last_refresh":"2026-10-11T00:00:00Z"}""");
        var counting = new GaugeFixtures.Counting();
        var (jobs, runner) = Build(new GaugeFixtures.Reviewer(), counting.For);
        jobs.Submit(Job());

        await runner.PumpAsync("codex", CancellationToken.None);

        counting.Launches.Should().Be(0, "an expiry nobody can read is an expiry that may be now");
        _notes.Should().ContainSingle(note => note.Contains(CodexAuthExpiry.NotAJwt, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(GaugeFixtures.AuthError, GaugeCodes.AuthRefused)]
    [InlineData(GaugeFixtures.RefreshRequest, GaugeCodes.RefreshRequested)]
    public async Task AnAuthError_SuspendsCaptureOnly_TheNextJobStillSchedules_AndNeedsSignInIsUntouched(string said, string code)
    {
        var reader = new GaugeFixtures.Reader(said + "\n");
        var reviewer = new GaugeFixtures.Reviewer();
        var (jobs, runner) = Build(reviewer, reader.For);
        jobs.Submit(Job());

        await runner.PumpAsync("codex", CancellationToken.None);

        var slot = Slot();
        slot.GaugeSuspended.Should().Be(code, "its OWN state on the slot");
        slot.NeedsSignIn.Should().BeFalse("never needs-sign-in, which would make the recovery launch unschedulable");
        slot.IsReady(DateTimeOffset.UtcNow).Should().BeTrue();
        jobs.All().Single().Status.Should().Be(JobStatus.Done, "the capture's failure is not the job's");

        // The next job schedules — and FAILS at the vendor, which is no proof about the token set: still suspended, no read.
        reviewer.Fails = true;
        jobs.Submit(Job());
        (await runner.PumpAsync("codex", CancellationToken.None)).Should().BeTrue("the next job still schedules on this account");
        reviewer.Environments.Should().HaveCount(2);
        reader.Requests.Should().ContainSingle("no read while suspended: the second job spawned no reader");
        Slot().GaugeSuspended.Should().Be(code, "a launch that failed lifts nothing");

        // The third job SUCCEEDS on this token set, which lifts the suspension; the capture after it reads again.
        reviewer.Fails = false;
        jobs.Submit(Job());
        await runner.PumpAsync("codex", CancellationToken.None);
        reader.Requests.Should().HaveCount(2, "a later launch on the slot that succeeds lifts the suspension");
    }

    [Fact]
    public async Task ACaptureFailure_KeepsTheJobsSuccess_AndMarksThePreviousReadingStale()
    {
        var good = new GaugeFixtures.Reader(GaugeFixtures.Answer + "\n");
        var (jobs, runner) = Build(new GaugeFixtures.Reviewer(), good.For);
        jobs.Submit(Job());
        await runner.PumpAsync("codex", CancellationToken.None);
        Slot().Gauge!.Stale.Should().BeFalse();

        var (jobs2, runner2) = Build(new GaugeFixtures.Reviewer(), new GaugeFixtures.Throwing().For);
        jobs2.Submit(Job());
        await runner2.PumpAsync("codex", CancellationToken.None);

        jobs2.All().Single().Status.Should().Be(JobStatus.Done, "a capture failure never changes the finished job's result");
        var slot = Slot();
        slot.Gauge.Should().NotBeNull("the previous reading is kept");
        slot.Gauge!.Stale.Should().BeTrue("and marked as the last the vendor said");
        slot.GaugeSuspended.Should().BeEmpty("an infrastructure failure is not an authentication error");
        Lines().Should().HaveCount(2);
        Lines()[1].GetProperty("gaugeObservedUtc").GetString().Should().BeEmpty("the second job's line carries no reading");
    }

    [Fact]
    public async Task TheLog_CarriesNoRawMessage_NoAccountId_NoMachineName()
    {
        var reader = new GaugeFixtures.Reader(GaugeFixtures.Notifications + GaugeFixtures.Answer + "\n");
        var (jobs, runner) = Build(new GaugeFixtures.Reviewer(), reader.For);
        jobs.Submit(Job());

        await runner.PumpAsync("codex", CancellationToken.None);

        _notes.Should().ContainSingle().Which.Should().Contain("40").And.Contain("10080");
        var everything = string.Join("\n", _notes);
        everything.Should().NotContain(GaugeFixtures.AccountMarker).And.NotContain(GaugeFixtures.MachineMarker)
            .And.NotContain("\"id\":2", "a log line is codes and numbers, never a line of the stream");
    }

    [Fact]
    public async Task AnAbsentSecondaryWindow_StaysAbsent_AndAReportedOneIsKept()
    {
        var (jobs, runner) = Build(new GaugeFixtures.Reviewer(), new GaugeFixtures.Reader(GaugeFixtures.Answer + "\n").For);
        jobs.Submit(Job());
        await runner.PumpAsync("codex", CancellationToken.None);

        Slot().Gauge!.Secondary.Should().BeNull("the measured plan reports no five-hour window");
        var line = Lines().Single();
        line.GetProperty("gaugeWindows").EnumerateArray().Select(w => w.GetProperty("kind").GetString()).Should().Equal(
            ["primary"], "an absent window is not on the line at all, never a 0 %");

        var (jobs2, runner2) = Build(new GaugeFixtures.Reviewer(), new GaugeFixtures.Reader(GaugeFixtures.AnswerWithSecondary + "\n").For);
        jobs2.Submit(Job());
        await runner2.PumpAsync("codex", CancellationToken.None);

        var secondary = Slot().Gauge!.Secondary;
        secondary.Should().NotBeNull();
        secondary!.UsedPercent.Should().Be(12.5);
        secondary.WindowDurationMins.Should().Be(300);
        var kept = Lines()[1].GetProperty("gaugeWindows").EnumerateArray().Single(w => w.GetProperty("kind").GetString() == "secondary");
        kept.GetProperty("usedPercent").GetDouble().Should().Be(12.5);
        kept.GetProperty("mins").GetInt64().Should().Be(300);
    }

    [Fact]
    public async Task TheNextLaunchAfterACapture_UsesTheSlotsCredentialsAsTheReaderLeftThem()
    {
        var rewritten = GaugeFixtures.AuthJson(FarExpiry.AddDays(1), refreshToken: "fake-refresh-token-ROTATED");
        var reader = new GaugeFixtures.Reader(GaugeFixtures.Answer + "\n")
        {
            // A refresh, as a reader would leave it: the token set rewritten in place, under the home it was given.
            SideEffect = request => File.WriteAllText(Path.Combine(request.Environment["CODEX_HOME"]!, "auth.json"), rewritten),
        };
        var reviewer = new GaugeFixtures.Reviewer();
        var (jobs, runner) = Build(reviewer, reader.For);
        jobs.Submit(Job());
        await runner.PumpAsync("codex", CancellationToken.None);

        jobs.Submit(Job());
        await runner.PumpAsync("codex", CancellationToken.None);

        var next = reviewer.Environments[1];
        next["CODEX_HOME"].Should().Be(reader.Requests[0].Environment["CODEX_HOME"], "the same home the reader ran in — no copy, no restore");
        File.ReadAllText(Path.Combine(next["CODEX_HOME"]!, "auth.json")).Should().Be(rewritten,
            "the resulting credentials are what the next launch finds: exclusive ownership of the token set is the safety");
    }

    [Fact]
    public async Task TheCatalog_ServesTheLatestReadingPerVendor_NamingNoAccount()
    {
        using var server = new TeamServer();
        File.WriteAllText(Path.Combine(server.DataDir, "vendors.json"), """[{ "id": "codex", "runtime": "codex", "models": ["m"], "slots": ["a", "b"] }]""");
        var registry = new SlotRegistry(server.DataDir, new JsonFileStore());
        var older = Reading(new DateTimeOffset(2026, 10, 11, 10, 0, 0, TimeSpan.Zero), 35);
        var newer = Reading(new DateTimeOffset(2026, 10, 11, 11, 0, 0, TimeSpan.Zero), 40);
        registry.SaveGauge(registry.Read("codex", "b"), newer);
        registry.SaveGauge(registry.Read("codex", "a"), older);
        var client = server.ClientFor($"dev@{TeamServer.Domain}");

        var body = await client.GetStringAsync("/api/catalog");
        var catalog = JsonSerializer.Deserialize(body, ServerJsonContext.Default.CatalogDto)!;

        var gauge = catalog.Vendors.Single().Slots.Gauge;
        gauge.Should().NotBeNull();
        gauge!.ObservedUtc.Should().Be(newer.ObservedUtc, "the latest reading among the vendor's accounts, whichever read it");
        gauge.Windows.Single().UsedPercent.Should().Be(40);
        body.Should().NotContain("\"name\"").And.NotContain("@", "the reading only: never an account's name, never an email");
        body.Should().Contain("\"gauge\":");
    }

    [Fact]
    public async Task TheCatalog_OmitsTheGauge_WhenNothingWasEverRead()
    {
        using var server = new TeamServer();
        File.WriteAllText(Path.Combine(server.DataDir, "vendors.json"), """[{ "id": "codex", "runtime": "codex", "models": ["m"], "slots": ["a"] }]""");

        var body = await server.ClientFor($"dev@{TeamServer.Domain}").GetStringAsync("/api/catalog");

        body.Should().NotContain("\"gauge\"", "omitted, not null: the same absence an older server has, which the client reads as unavailable");
    }

    [Fact]
    public void AnOldLedgerLine_ReadsNoGauge_AndTheGuardIsArithmetic()
    {
        File.WriteAllText(Path.Combine(_dir, "usage.jsonl"), """{"utc":"2026-10-11T10:00:00.0000000Z","provider":"codex","model":"m","role":"r","stage":"TeamServer","seconds":1,"tokensIn":1,"tokensOut":1,"costUsd":null,"outcome":"ok"}""" + "\n");

        var line = new UsageReader(_dir).Read(new UsageRange(new DateTimeOffset(2026, 10, 11, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 12, 0, 0, 0, TimeSpan.Zero))).Lines.Single();

        line.Gauge.Should().Be(GaugeOnLine.None, "a line older than the fields says nothing about the gauge");
        var now = new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);
        GaugeCapture.Guard(new Expiry.Known(now + GaugeCapture.ReaderLifetime + GaugeCapture.ExpiryMargin + TimeSpan.FromSeconds(1)), now).Should().BeNull("clears the margin");
        GaugeCapture.Guard(new Expiry.Known(now + GaugeCapture.ReaderLifetime + GaugeCapture.ExpiryMargin), now).Should().BeOfType<GaugeCaptured.Skipped>("exactly the margin is not past it");
        GaugeCapture.Guard(new Expiry.Known(now.AddDays(-1)), now).Should().BeOfType<GaugeCaptured.Skipped>();
        GaugeCapture.Guard(new Expiry.Unknown("x"), now).Should().BeOfType<GaugeCaptured.Skipped>();
    }

    private static GaugeReadingDto Reading(DateTimeOffset observed, double used) =>
        new(observed, "prolite", string.Empty, true, 1, [new GaugeWindowDto("codex", "primary", used, 10080, "2026-10-14T07:48:11.0000000Z")]);

    private (JobStore Jobs, JobRunner Runner) Build(IReviewLauncher reviewer, Func<IProcessTracker, IProcessLauncher> launchers)
    {
        var jobs = new JobStore();
        var registry = new SlotRegistry(_dir, new JsonFileStore());
        var gauge = new GaugeCapture(registry, launchers, "0.0.0-test", _notes.Add);
        var runner = new JobRunner(jobs, new VendorCatalogHost(_dir), registry, reviewer, new UsageLedger(_dir), gauge: gauge);

        return (jobs, runner);
    }

    private AccountSlot Slot() => new SlotRegistry(_dir, new JsonFileStore()).Read("codex", "a");

    private SlotStateDto State() =>
        new JsonFileStore().Read(Path.Combine(Slot().Directory, "state.json"), ServerJsonContext.Default.SlotStateDto)!;

    private static JobRecord Job() =>
        new(JobId.New(), "dev@example.com", "codex", "m", "Architecture", "review this",
            JobStatus.Queued, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(10), TimeSpan.FromSeconds(60));

    private IReadOnlyList<JsonElement> Lines() =>
        [.. File.ReadAllLines(Path.Combine(_dir, "usage.jsonl")).Select(l => JsonDocument.Parse(l).RootElement.Clone())];
}
