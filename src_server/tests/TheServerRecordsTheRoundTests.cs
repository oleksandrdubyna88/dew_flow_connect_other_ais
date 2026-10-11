using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>
/// The server records the round — story 4.2 of <c>todo/PLAN_every_round_is_counted.md</c>: a review request may carry
/// the client's <c>round</c> id, its <c>attempt</c> word and the <c>remainingSeconds</c> on its own deadline; the job
/// keeps all three and every ledger line of the job carries the round and the client's attempt.
/// </summary>
/// <remarks>
/// <para><b>Counting fields never fail a review</b> (risk amendment 3 of the plan). A plain nullable <c>int</c> or
/// <c>string</c> on the request rejects <c>"oops"</c>, <c>{}</c>, <c>1.5</c> or an overflow while the JSON is BOUND —
/// a 400 before any <c>Dropped</c> note could be written. So every value below goes through the REAL
/// <c>POST /api/reviews</c>, and every one is accepted: a malformed value is dropped with a note naming its field, an
/// absent one (and a JSON <c>null</c>) is an old client — stored empty, never defaulted, no note.</para>
/// <para>The line half runs the REAL <see cref="JobRunner"/> over the job the endpoint stored, with a fake launcher at
/// <see cref="IReviewLauncher"/> — the round is a runner rule, nothing below the launcher decides it.</para>
/// </remarks>
[Collection(ServerCollection.Name)]
public sealed class TheServerRecordsTheRoundTests : IDisposable
{
    /// <summary>A round id as the client mints it: 128 random bits, 32 lower-case hex characters.</summary>
    private const string Round = "0123456789abcdef0123456789abcdef";

    private const string Email = $"dev@{TeamServer.Domain}";

    private const string Vendors = """[{ "id": "codex", "runtime": "codex", "models": ["gpt-5.6-luna"], "slots": ["a", "b"] }]""";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coai-server-round-" + Guid.NewGuid().ToString("N"));

    public TheServerRecordsTheRoundTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A temp directory a vendor helper still holds is the OS's to clean, not this test's to fail on.
        }
    }

    // ---------- the value matrix, through the real endpoint ----------

    /// <summary>Every malformed value amendment 3 names, and a few a client could plausibly send, for each field.</summary>
    public static TheoryData<string, string> Malformed => new()
    {
        { "round", "0" },
        { "round", "-1" },
        { "round", "\"oops\"" },
        { "round", "{}" },
        { "round", "[]" },
        { "round", "1.5" },
        { "round", "true" },
        { "round", "\"\"" },
        { "round", "\"0123456789abcdef0123456789abcde\"" },
        { "round", "\"0123456789abcdef0123456789abcdef0\"" },
        { "round", "\"0123456789abcdef0123456789abcdeg\"" },
        { "round", "\"" + new string('a', 4096) + "\"" },
        { "round", "123456789012345678901234567890123" },
        { "attempt", "0" },
        { "attempt", "-1" },
        { "attempt", "\"oops\"" },
        { "attempt", "{}" },
        { "attempt", "1.5" },
        { "attempt", "\"\"" },
        { "attempt", "\"rotated\"" },
        { "attempt", "\"First\"" },
        { "attempt", "\"" + new string('r', 4096) + "\"" },
        { "remainingSeconds", "0" },
        { "remainingSeconds", "-1" },
        { "remainingSeconds", "\"oops\"" },
        { "remainingSeconds", "\"60\"" },
        { "remainingSeconds", "{}" },
        { "remainingSeconds", "1.5" },
        { "remainingSeconds", "60.0" },
        { "remainingSeconds", "2147483648" },
        { "remainingSeconds", "99999999999999999999" },
        { "remainingSeconds", "1e400" },
    };

    [Theory]
    [MemberData(nameof(Malformed))]
    public async Task AMalformedCountingField_IsAcceptedAndDroppedWithANote_NeverRefused(string field, string value)
    {
        using var server = Server();

        var (accepted, job) = await Post(server, $"\"{field}\": {value}");

        accepted.Dropped.Should().ContainSingle("only the malformed field is dropped, and it is SAID")
            .Which.Field.Should().Be(field);
        accepted.Dropped![0].Reason.Length.Should().BeLessThan(300, "a note names what was wrong; it never echoes a 4 KiB value back");
        (job.Round, job.ClientAttempt, job.StatedBudget.Seconds).Should().Be((string.Empty, string.Empty, 0),
            "a dropped value is stored EMPTY — never a guess at what the client meant");
        job.StatedBudget.State.Should().Be(field == "remainingSeconds" ? StatedBudgetState.Dropped : StatedBudgetState.Absent,
            "a dropped budget is its own fact on the record, not the same 0 an absent one is (the epic-4 code round)");
        job.StatedBudget.Reason.Should().Be(field == "remainingSeconds" ? accepted.Dropped[0].Reason : string.Empty,
            "the record keeps WHY it was dropped — the sentence the request's note carried");
    }

    [Theory]
    [InlineData("round", "")]
    [InlineData("round", "null")]
    [InlineData("attempt", "")]
    [InlineData("attempt", "null")]
    [InlineData("remainingSeconds", "")]
    [InlineData("remainingSeconds", "null")]
    public async Task AnAbsentOrNullField_IsAnOldClient_StoredEmpty_WithNoNote(string field, string value)
    {
        using var server = Server();

        var (accepted, job) = await Post(server, value.Length == 0 ? string.Empty : $"\"{field}\": {value}");

        accepted.Dropped.Should().BeNullOrEmpty("a client older than the field sent nothing, and must not be told on every review that it was dropped");
        (job.Round, job.ClientAttempt).Should().Be((string.Empty, string.Empty), "absent is stored empty, never defaulted");
        job.StatedBudget.Should().Be(StatedBudget.Absent, "an absent or null budget is an old client's — no deadline, no reason");
    }

    [Theory]
    [InlineData("round", "\"" + Round + "\"", Round, "", 0)]
    [InlineData("round", "\"0123456789ABCDEF0123456789ABCDEF\"", Round, "", 0)]
    [InlineData("attempt", "\"first\"", "", "first", 0)]
    [InlineData("attempt", "\"retry\"", "", "retry", 0)]
    [InlineData("attempt", "\"repair\"", "", "repair", 0)]
    [InlineData("attempt", "\"followUp\"", "", "followUp", 0)]
    [InlineData("remainingSeconds", "1", "", "", 1)]
    [InlineData("remainingSeconds", "50", "", "", 50)]
    [InlineData("remainingSeconds", "2147483647", "", "", int.MaxValue)]
    public async Task AWellFormedValue_IsTaken_WithNoNote(string field, string value, string round, string attempt, int remaining)
    {
        using var server = Server();

        var (accepted, job) = await Post(server, $"\"{field}\": {value}");

        accepted.Dropped.Should().BeNullOrEmpty();
        (job.Round, job.ClientAttempt, job.StatedBudget.Seconds).Should().Be((round, attempt, remaining),
            "a round id is kept in its canonical lower case, so one round is never two in a count");
        job.StatedBudget.State.Should().Be(remaining > 0 ? StatedBudgetState.Stated : StatedBudgetState.Absent);
    }

    [Fact]
    public async Task ARepeatedSubmit_WithLessTimeLeft_IsTheSameJob()
    {
        // A client retrying a submit whose answer was lost has spent seconds meanwhile: the remaining time is not part of
        // WHAT was asked, so it must not turn the retry into a 409 "a different request".
        using var server = Server();
        const string key = "\"idempotencyKey\": \"round-turn-1\"";

        var (first, _) = await Post(server, $"\"round\": \"{Round}\", \"attempt\": \"first\", \"remainingSeconds\": 300, {key}");
        var (second, _) = await Post(server, $"\"round\": \"{Round}\", \"attempt\": \"first\", \"remainingSeconds\": 280, {key}");

        second.Id.Should().Be(first.Id);
    }

    // ---------- the job and its lines ----------

    [Fact]
    public async Task AMalformedRound_IsDroppedWithANote_AndTheReviewRuns()
    {
        using var server = Server();
        var (accepted, job) = await Post(server, "\"round\": \"oops\", \"attempt\": \"repair\", \"remainingSeconds\": 50");
        accepted.Dropped.Should().ContainSingle().Which.Field.Should().Be("round");

        var (ran, lines) = await Run(job, new EveryServerLaunchIsWrittenTests.BySlot(), "b");

        ran.Status.Should().Be(JobStatus.Done, "a counting field never fails a review");
        var line = lines.Should().ContainSingle().Subject;
        Text(line, "round").Should().BeEmpty("the dropped round is stored empty, and the line says so — never a minted stand-in");
        Text(line, "clientAttempt").Should().Be(LaunchAttempts.Repair, "the well-formed fields of the same request are kept");
        Text(line, "attempt").Should().Be(LaunchAttempts.First, "the server's own launch attempt keeps its own meaning");
    }

    [Fact]
    public async Task AValidRound_ReachesEveryLineOfTheJob_BesideTheServersOwnAttempt()
    {
        using var server = Server();
        var (_, job) = await Post(server, $"\"round\": \"{Round}\", \"attempt\": \"retry\", \"remainingSeconds\": 50");

        // Slot a refuses with a rate limit and the job rotates onto b: two launches, two lines, one round.
        var (ran, lines) = await Run(job, new EveryServerLaunchIsWrittenTests.BySlot(), "a", "b");

        ran.Status.Should().Be(JobStatus.Done);
        lines.Should().HaveCount(2);
        lines.Select(l => Text(l, "round")).Should().OnlyContain(round => round == Round, "every launch of the round carries its id — the requeued one too");
        lines.Select(l => Text(l, "clientAttempt")).Should().OnlyContain(attempt => attempt == LaunchAttempts.Retry);
        lines.Select(l => Text(l, "attempt")).Should().OnlyContain(attempt => attempt == LaunchAttempts.Rotated,
            "the client's word does not overwrite the server's: this job met two accounts");
    }

    [Fact]
    public async Task AnOldClient_IsStoredEmpty_AndItsLineSaysNoRound()
    {
        using var server = Server();
        var (_, job) = await Post(server, string.Empty);

        var (_, lines) = await Run(job, new EveryServerLaunchIsWrittenTests.BySlot(), "b");

        var line = lines.Should().ContainSingle().Subject;
        (Text(line, "round"), Text(line, "clientAttempt")).Should().Be((string.Empty, string.Empty), "an old client's line names no round, empty rather than invented");
        Text(line, "attempt").Should().Be(LaunchAttempts.First);
    }

    // ---------- the reader ----------

    [Fact]
    public void AnOldLedgerLine_StillReads_AndANewOneKeepsItsRoundAndClientAttempt()
    {
        File.WriteAllText(
            Path.Combine(_dir, "usage.jsonl"),
            """{"utc":"2026-10-11T10:00:00.0000000Z","provider":"codex","model":"m","role":"Architecture","stage":"TeamServer","seconds":8.4,"tokensIn":14508,"tokensOut":5,"costUsd":null,"outcome":"ok","email":"dev@example.com","kind":"review","turn":"1760090400-abc","attempt":"first","endCause":""}""" + "\n"
            + """{"utc":"2026-10-11T10:00:01.0000000Z","provider":"codex","model":"m","role":"Architecture","stage":"TeamServer","seconds":1.0,"tokensIn":3,"tokensOut":1,"costUsd":null,"outcome":"ok","email":"dev@example.com","kind":"review","turn":"1760090400-def","attempt":"rotated","endCause":"","round":"0123456789abcdef0123456789abcdef","clientAttempt":"repair"}""" + "\n");
        var at = new DateTimeOffset(2026, 10, 11, 10, 0, 0, TimeSpan.Zero);

        var scan = new UsageReader(_dir).Read(new UsageRange(at.AddMinutes(-1), at.AddMinutes(1)));

        scan.Unreadable.Should().Be(0, "a line older than the fields is history, never damage");
        scan.Lines.Should().HaveCount(2);
        (scan.Lines[0].Round, scan.Lines[0].ClientAttempt).Should().Be((string.Empty, string.Empty), "an old line names no round, which is the truth about it");
        (scan.Lines[1].Round, scan.Lines[1].ClientAttempt, scan.Lines[1].Attempt).Should().Be((Round, "repair", "rotated"),
            "the reader dropped the fields the line holds");
    }

    // ---------- the harness ----------

    private static TeamServer Server()
    {
        var server = new TeamServer();
        File.WriteAllText(Path.Combine(server.DataDir, "vendors.json"), Vendors);

        return server;
    }

    /// <summary>
    /// A review request POSTed as RAW JSON — the only way to send what a typed client cannot — with
    /// <paramref name="members"/> added to an otherwise ordinary body; the 202's body and the job the store holds for it.
    /// </summary>
    private static async Task<(ReviewAcceptedDto Accepted, JobRecord Job)> Post(TeamServer server, string members)
    {
        var body = """{ "vendor": "codex", "model": "gpt-5.6-luna", "prompt": "review this", "role": "Architecture", "timeoutSeconds": 60"""
            + (members.Length == 0 ? string.Empty : ", " + members) + " }";
        var response = await server.ClientFor(Email).PostAsync("/api/reviews", new StringContent(body, Encoding.UTF8, "application/json"));
        response.StatusCode.Should().Be(HttpStatusCode.Accepted,
            $"a counting field never refuses a review — the server answered {await response.Content.ReadAsStringAsync()}");
        var accepted = (await response.Content.ReadFromJsonAsync<ReviewAcceptedDto>())!;

        return (accepted, server.Services.GetRequiredService<JobStore>().Find(accepted.Id, Email)!);
    }

    /// <summary>The stored job, run by the real runner on the named signed-in slots until it ends; its record and its lines.</summary>
    private async Task<(JobRecord Job, IReadOnlyList<JsonElement> Lines)> Run(JobRecord job, IReviewLauncher launcher, params string[] slots)
    {
        File.WriteAllText(Path.Combine(_dir, "vendors.json"),
            "[{ \"id\": \"codex\", \"runtime\": \"codex\", \"models\": [\"gpt-5.6-luna\"], \"slots\": [" + string.Join(", ", slots.Select(s => $"\"{s}\"")) + "] }]");
        var registry = new SlotRegistry(_dir, new JsonFileStore());
        foreach (var slot in slots)
        {
            registry.MarkSignedIn(registry.Read("codex", slot));
        }

        var jobs = new JobStore();
        var runner = new JobRunner(jobs, new VendorCatalogHost(_dir), new SlotRegistry(_dir, new JsonFileStore()), launcher, new UsageLedger(_dir));
        jobs.Submit(job);
        for (var pumps = 0; pumps < slots.Length && !jobs.All().Single().IsTerminal; pumps++)
        {
            (await runner.PumpAsync("codex", CancellationToken.None)).Should().BeTrue("a signed-in slot is free");
        }

        return (jobs.All().Single(), WithdrawnJob.Lines(_dir));
    }

    private static string Text(JsonElement line, string name) =>
        line.TryGetProperty(name, out var value) ? value.GetString() ?? string.Empty : "<absent>";
}
