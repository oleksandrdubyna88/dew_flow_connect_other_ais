using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CoaiServer;
using FluentAssertions;
using Xunit;

namespace CoaiServer.Tests;

public sealed class UsageWindowTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 14, 30, 0, TimeSpan.Zero);

    [Fact]
    public void TodayStartsAtMidnightUtc() =>
        UsageWindow.Range("today", Now)!.FromUtc.Should().Be(new DateTimeOffset(2026, 9, 6, 0, 0, 0, TimeSpan.Zero));

    [Theory]
    [InlineData("week", 7)]
    [InlineData("month", 30)]
    [InlineData("year", 365)]
    public void TheTrailingWindowsSpanWhatTheySay(string name, int days)
    {
        var range = UsageWindow.Range(name, Now)!;

        (range.ToUtc - range.FromUtc).Days.Should().Be(days);
    }

    [Fact]
    public void AnUnknownWindowIsRefusedRatherThanDefaulted() =>
        // Falling back to "today" would answer a question nobody asked, and the wrong answer would
        // look like wrong data rather than a wrong request.
        UsageWindow.Range("fortnight", Now).Should().BeNull();

    [Fact]
    public void AMissingWindowMeansToday() =>
        UsageWindow.Range(null, Now)!.FromUtc.Should().Be(UsageWindow.Range("today", Now)!.FromUtc);

    [Fact]
    public void TheRangeIsHalfOpenSoNothingIsCountedTwice()
    {
        var range = new UsageRange(Now, Now.AddHours(1));

        range.Contains(Now).Should().BeTrue("the lower bound is included");
        range.Contains(Now.AddHours(1)).Should().BeFalse(
            "the upper bound is not — otherwise a run on the boundary belongs to two adjacent windows");
        range.Contains(Now.AddSeconds(-1)).Should().BeFalse();
    }

    [Fact]
    public void ARunThatFinishedDuringThisRequestIsInToday() =>
        // The exclusive upper bound is a second ahead of now for exactly this reason.
        UsageWindow.Range("today", Now)!.Contains(Now).Should().BeTrue();
}

public sealed class UsageTotalsTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    private static UsageLine Line(
        string email = "dev@example.com", string vendor = "codex", string outcome = "ok",
        double? cost = null, long tokensIn = 100, long tokensOut = 10, double seconds = 5,
        JobKind kind = JobKind.Review) =>
        new(At, email, vendor, "m", "Architecture", outcome, seconds, tokensIn, tokensOut, cost, kind);

    [Fact]
    public void FailedRunsAreCountedAndTheirTokensTooBecauseTheyCostTheSame()
    {
        var totals = UsageTotals.ByVendor([Line(), Line(outcome: "RateLimited"), Line(outcome: "NonZeroExit")]);

        var codex = totals.Should().ContainSingle().Subject;
        codex.Runs.Should().Be(3);
        codex.Failed.Should().Be(2);
        codex.TokensIn.Should().Be(300, "a review that burned tokens and answered nothing spent them");
    }

    [Fact]
    public void AnUnknownPriceIsNeverShownAsZero()
    {
        var totals = UsageTotals.ByVendor([Line(), Line()]);

        var codex = totals.Single();
        codex.CostUsd.Should().BeNull("subscription CLIs price nothing, and null is not zero");
        codex.CostIsFloor.Should().BeFalse("nothing is known, so there is no floor to be above");
        codex.UnpricedRuns.Should().Be(2);
    }

    [Fact]
    public void AMixedGroupIsMarkedAFloorAndSaysHowMuchIsMissing()
    {
        var totals = UsageTotals.ByVendor([Line(cost: 1.5), Line(), Line()]);

        var codex = totals.Single();
        codex.CostUsd.Should().Be(1.5);
        codex.CostIsFloor.Should().BeTrue("some of what was spent is not in that number");
        codex.UnpricedRuns.Should().Be(2, "a bare flag cannot tell 'one of forty' from 'thirty-nine of forty'");
    }

    [Fact]
    public void AFullyPricedGroupIsNotAFloor()
    {
        UsageTotals.ByVendor([Line(cost: 1), Line(cost: 2)]).Single()
            .Should().Match<VendorTotal>(v => v.CostUsd == 3 && !v.CostIsFloor && v.UnpricedRuns == 0);
    }

    [Fact]
    public void APersonSeesOnlyTheirOwnLines()
    {
        IReadOnlyList<UsageLine> lines = [Line(), Line(), Line(email: "other@example.com")];

        UsageTotals.ByVendorFor(lines, "dev@example.com").Single().Runs.Should().Be(2);
        UsageTotals.ByVendorFor(lines, "other@example.com").Single().Runs.Should().Be(1);
    }

    [Fact]
    public void AnEmailIsMatchedWithoutRegardToCase()
    {
        // An identity provider may hand back Alice@Example.com where the ledger holds
        // alice@example.com. Matching exactly shows that person an empty page and splits them into two
        // rows in the company view. (codex, plan round.)
        IReadOnlyList<UsageLine> lines = [Line(email: "alice@example.com"), Line(email: "Alice@Example.COM")];

        UsageTotals.ByVendorFor(lines, "ALICE@example.com").Single().Runs.Should().Be(2);
        UsageTotals.ByPerson(lines).Should().ContainSingle("they are one person, not two");
    }

    [Fact]
    public void LinesWithNoEmailCountForTheCompanyAndForNobody()
    {
        // Lines written by a local run, before there was a Team server. The money was spent, so it is
        // in the company's totals; it is not anybody's here, so it is in no person's row.
        IReadOnlyList<UsageLine> lines = [Line(email: ""), Line()];

        UsageTotals.ByVendor(lines).Single().Runs.Should().Be(2);
        UsageTotals.ByPerson(lines).Should().ContainSingle().Which.Email.Should().Be("dev@example.com");
    }

    [Fact]
    public void VendorsAreOrderedByHowMuchTheyWereUsed()
    {
        var totals = UsageTotals.ByVendor([Line(vendor: "claude"), Line(), Line(), Line()]);

        totals.Select(v => v.Vendor).Should().ContainInOrder("codex", "claude");
    }

    [Fact]
    public void TheGateAndAskingAreCountedApart()
    {
        // The owner asked for this on 2026-09-08. Same vendors, two questions — "what did the gate
        // cost me" and "what did asking cost me" — and a single total answers neither.
        var kinds = UsageTotals.ByKind([
            Line(tokensIn: 1000, tokensOut: 100),
            Line(tokensIn: 1000, tokensOut: 100),
            Line(tokensIn: 50, tokensOut: 20, kind: JobKind.Chat),
        ]);

        kinds.Should().HaveCount(2);
        kinds[0].Kind.Should().Be("review");
        kinds[0].Runs.Should().Be(2);
        kinds[0].TokensIn.Should().Be(2000);
        kinds[1].Kind.Should().Be("chat");
        kinds[1].Runs.Should().Be(1);
        kinds[1].TokensIn.Should().Be(50);
    }

    [Fact]
    public void AWindowWithOnlyReviewsInItSaysSoByHavingOneRow()
    {
        // Not a row of zeroes: a zero is a measurement, and "we ran no conversations" is an absence.
        var kinds = UsageTotals.ByKind([Line(), Line()]);

        kinds.Should().ContainSingle().Which.Kind.Should().Be("review");
    }

    [Fact]
    public void AFailedConversationIsCountedNotFiltered()
    {
        // It burned the seconds and the tokens whatever it answered. Hiding it is the one thing a
        // spending record must not do — the same rule the vendor rows already follow.
        var kinds = UsageTotals.ByKind([Line(outcome: "TimedOut", kind: JobKind.Chat)]);

        kinds.Should().ContainSingle();
        kinds[0].Runs.Should().Be(1);
        kinds[0].Failed.Should().Be(1);
    }

    [Fact]
    public void OnePersonsConversationsAreTheirsAlone()
    {
        var lines = new[] { Line(kind: JobKind.Chat), Line(email: "someone@example.com", kind: JobKind.Chat) };

        UsageTotals.ByKindFor(lines, "dev@example.com").Should().ContainSingle()
            .Which.Runs.Should().Be(1);
    }
}

public sealed class UsageReaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coai-usage-" + Guid.NewGuid().ToString("N"));

    public UsageReaderTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static readonly UsageRange Everything =
        new(DateTimeOffset.MinValue, DateTimeOffset.MaxValue);

    private void WriteLedger(params string[] lines) =>
        File.WriteAllText(Path.Combine(_dir, "usage.jsonl"), string.Join("\n", lines) + "\n");

    private static string Entry(string email, string outcome = "ok", string? utc = null) =>
        JsonSerializer.Serialize(new
        {
            utc = utc ?? DateTimeOffset.UtcNow.ToString("O"),
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
        });

    [Fact]
    public void AnAbsentLedgerIsAnEmptyReportAndNotAFault()
    {
        var scan = new UsageReader(_dir).Read(Everything);

        scan.Lines.Should().BeEmpty();
        scan.Unreadable.Should().Be(0);
    }

    [Fact]
    public void AGoodLedgerParses()
    {
        WriteLedger(Entry("dev@example.com"), Entry("other@example.com"));

        new UsageReader(_dir).Read(Everything).Lines.Should().HaveCount(2);
    }

    [Fact]
    public void ATornLastLineIsCountedRatherThanThrown()
    {
        // The file is appended by a process that can be killed mid-write, so this is an expected
        // state. A usage page that answers 500 over one bad line is worse than one that is honest.
        WriteLedger(Entry("dev@example.com"), "{\"utc\":\"2026-09-06T12:00:00", Entry("dev@example.com"));

        var scan = new UsageReader(_dir).Read(Everything);

        scan.Lines.Should().HaveCount(2, "the good lines are still counted");
        scan.Unreadable.Should().Be(1);
    }

    [Fact]
    public void ALineFromBeforeTheEmailColumnIsValidRatherThanDamage()
    {
        // The column is trailing and defaulted, so an old line has no email — which is the truth about
        // it. Reporting every historical row as unreadable would be a page full of false alarm.
        WriteLedger("""{"utc":"2026-09-06T12:00:00.000Z","provider":"codex","model":"m","role":"Architecture","stage":"CodeReview","seconds":5,"tokensIn":1,"tokensOut":2,"costUsd":null,"outcome":"ok"}""");

        var scan = new UsageReader(_dir).Read(Everything);

        scan.Unreadable.Should().Be(0);
        scan.Lines.Should().ContainSingle().Which.Email.Should().BeEmpty();
    }

    [Fact]
    public void BlankLinesAreNotDamage()
    {
        WriteLedger(Entry("dev@example.com"), "", "   ");

        new UsageReader(_dir).Read(Everything).Unreadable.Should().Be(0);
    }

    [Fact]
    public void OnlyTheWindowIsReturned()
    {
        WriteLedger(
            Entry("dev@example.com", utc: "2026-09-06T12:00:00.0000000Z"),
            Entry("dev@example.com", utc: "2020-01-01T12:00:00.0000000Z"));

        var scan = new UsageReader(_dir).Read(
            new UsageRange(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), DateTimeOffset.MaxValue));

        scan.Lines.Should().ContainSingle();
    }

    [Fact]
    public void TheLedgerCanBeAppendedToWhileItIsBeingRead()
    {
        // The default share mode for a read forbids writers, so a review finishing while somebody
        // looked at the usage page would fail to append its own line and the record would silently
        // lose a row. (gemini, plan round.)
        WriteLedger(Entry("dev@example.com"));
        var path = Path.Combine(_dir, "usage.jsonl");

        using var reading = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var append = () =>
        {
            using var writer = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            writer.Write("x"u8);
        };

        append.Should().NotThrow();
        new UsageReader(_dir).Read(Everything).Lines.Should().NotBeEmpty();
    }
}

/// <summary>
/// What the WRITER produces is what the READER parses.
/// </summary>
/// <remarks>
/// The ledger's shape is declared twice: <c>UsageEntry</c> lives with the writer in Runners, and
/// <c>UsageEntryDto</c> here with the reader. That is deliberate — the writer's type is that binary's
/// business and this one is defensive by construction — but it means a rename on either side would
/// leave the reader silently producing empty groups and zero totals, which is the worst way for a
/// page about money to be wrong. This test is the seam: it writes through the real ledger and reads
/// through the real reader, so a drift is a red test rather than a quiet zero. (codex, code round.)
/// </remarks>
public sealed class LedgerRoundTripTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coai-roundtrip-" + Guid.NewGuid().ToString("N"));

    public LedgerRoundTripTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void EveryFieldTheWriterWritesIsAFieldTheReaderReads()
    {
        new CoaiMcp.Runners.Reviewers.UsageLedger(_dir).RecordJob(
            "dev@example.com", "codex", "gpt-5.6-luna", "Architecture", "ok",
            TimeSpan.FromSeconds(12.5), tokensIn: 4321, tokensOut: 765, costUsd: 0.25);

        var line = new UsageReader(_dir)
            .Read(new UsageRange(DateTimeOffset.MinValue, DateTimeOffset.MaxValue))
            .Lines.Should().ContainSingle().Subject;

        line.Email.Should().Be("dev@example.com");
        line.Vendor.Should().Be("codex");
        line.Model.Should().Be("gpt-5.6-luna");
        line.Role.Should().Be("Architecture");
        line.Outcome.Should().Be("ok");
        line.Failed.Should().BeFalse();
        line.Seconds.Should().Be(12.5);
        line.TokensIn.Should().Be(4321);
        line.TokensOut.Should().Be(765);
        line.CostUsd.Should().Be(0.25);
        line.AtUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void AFailedJobRoundTripsAsFailed()
    {
        new CoaiMcp.Runners.Reviewers.UsageLedger(_dir).RecordJob(
            "dev@example.com", "codex", "m", "Architecture", "RateLimited",
            TimeSpan.FromSeconds(90), tokensIn: 10, tokensOut: 0);

        var totals = UsageTotals.ByVendor(
            new UsageReader(_dir).Read(new UsageRange(DateTimeOffset.MinValue, DateTimeOffset.MaxValue)).Lines);

        // Ninety seconds and no answer cost the same as ninety seconds and an answer.
        totals.Single().Failed.Should().Be(1);
        totals.Single().Seconds.Should().Be(90);
    }

    [Fact]
    public void TheKindTravelsFromTheWriterToTheReader()
    {
        var ledger = new CoaiMcp.Runners.Reviewers.UsageLedger(_dir);
        ledger.RecordJob("dev@example.com", "codex", "m", "", "ok", TimeSpan.FromSeconds(4), 1, 2, kind: "chat");
        ledger.RecordJob("dev@example.com", "codex", "m", "Architecture", "ok", TimeSpan.FromSeconds(9), 3, 4, kind: "review");

        var kinds = UsageTotals.ByKind(
            new UsageReader(_dir).Read(new UsageRange(DateTimeOffset.MinValue, DateTimeOffset.MaxValue)).Lines);

        kinds.Select(k => k.Kind).Should().BeEquivalentTo(["review", "chat"]);
    }

    [Fact]
    public void ALineWrittenBeforeTheKindExistedIsReadAsAReview()
    {
        // The file is APPEND-ONLY and years old. Every line already on disk was written by a build
        // that had no such field, and every one of them was a review — there was nothing else to be.
        // Reading them as anything else, or refusing them, would put a hole in a spending record.
        File.WriteAllText(
            Path.Combine(_dir, "usage.jsonl"),
            $$"""
            {"utc":"{{DateTime.UtcNow:O}}","provider":"codex","model":"m","role":"Architecture","stage":"TeamServer","seconds":3.0,"tokensIn":5,"tokensOut":6,"costUsd":null,"outcome":"ok","email":"dev@example.com"}
            """);

        var lines = new UsageReader(_dir)
            .Read(new UsageRange(DateTimeOffset.MinValue, DateTimeOffset.MaxValue)).Lines;

        lines.Should().ContainSingle();
        lines[0].Kind.Should().Be(JobKind.Review);
        lines[0].TokensIn.Should().Be(5, "an old line is read whole, not merely tolerated");
    }

    [Fact]
    public void AKindFromAServerNewerThanThisOneIsCountedRatherThanDropped()
    {
        // History that already happened and already cost money. Dropping it because a newer server
        // wrote a word this build has not heard of would HIDE SPENDING, which is the one thing this
        // file must never do.
        File.WriteAllText(
            Path.Combine(_dir, "usage.jsonl"),
            $$"""
            {"utc":"{{DateTime.UtcNow:O}}","provider":"codex","model":"m","role":"","stage":"TeamServer","seconds":3.0,"tokensIn":5,"tokensOut":6,"costUsd":null,"outcome":"ok","email":"dev@example.com","kind":"rehearsal"}
            """);

        var scan = new UsageReader(_dir).Read(new UsageRange(DateTimeOffset.MinValue, DateTimeOffset.MaxValue));

        scan.Unreadable.Should().Be(0);
        scan.Lines.Should().ContainSingle().Which.Kind.Should().Be(JobKind.Review);
    }
}

[Collection(ServerCollection.Name)]
public sealed class UsageEndpointTests
{
    private static void Seed(TeamServer server, params (string Email, string Outcome)[] runs) =>
        File.WriteAllLines(
            Path.Combine(server.DataDir, "usage.jsonl"),
            runs.Select(r => JsonSerializer.Serialize(new
            {
                utc = DateTimeOffset.UtcNow.ToString("O"),
                provider = "codex",
                model = "m",
                role = "Architecture",
                stage = "TeamServer",
                seconds = 5.0,
                tokensIn = 100,
                tokensOut = 10,
                costUsd = (double?)null,
                outcome = r.Outcome,
                email = r.Email,
            })));

    [Fact]
    public async Task UsageIsRefusedWithoutASignIn() =>
        (await new TeamServer().CreateClient().GetAsync("/api/usage")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task EverybodySeesTheirOwnSpendingAndNobodyElsesOnMeScope()
    {
        using var server = new TeamServer();
        Seed(server, ($"dev@{TeamServer.Domain}", "ok"), ($"dev@{TeamServer.Domain}", "ok"), ($"other@{TeamServer.Domain}", "ok"));

        var mine = await server.ClientFor($"dev@{TeamServer.Domain}").GetFromJsonAsync<UsageDto>("/api/usage");

        mine!.Scope.Should().Be("me");
        mine.Vendors.Should().ContainSingle().Which.Runs.Should().Be(2);
        mine.People.Should().BeEmpty("the personal view is not a directory of colleagues");
    }

    [Fact]
    public async Task CompanyScopeIsForAdminsAndSaysWhereToAsk()
    {
        using var server = new TeamServer();

        var refused = await server.ClientFor($"dev@{TeamServer.Domain}").GetAsync("/api/usage?scope=company");

        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await refused.Content.ReadFromJsonAsync<ErrorDto>())!.Error.Should().Contain("Coai:Admins");
    }

    [Fact]
    public async Task AnAdminSeesEverybodyAndEachPersonSeparately()
    {
        using var server = new TeamServer();
        Seed(server, ($"dev@{TeamServer.Domain}", "ok"), ($"other@{TeamServer.Domain}", "RateLimited"));

        var all = await server.ClientFor($"boss@{TeamServer.Domain}")
            .GetFromJsonAsync<UsageDto>("/api/usage?scope=company");

        all!.Scope.Should().Be("company");
        all.Vendors.Single().Runs.Should().Be(2);
        all.Vendors.Single().Failed.Should().Be(1);
        all.People.Should().HaveCount(2);
    }

    [Fact]
    public async Task AnUnknownWindowIsRefusedNamingTheLegalOnes()
    {
        using var server = new TeamServer();

        var refused = await server.ClientFor($"dev@{TeamServer.Domain}").GetAsync("/api/usage?window=fortnight");

        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await refused.Content.ReadFromJsonAsync<ErrorDto>();
        error!.Error.Should().Contain("today").And.Contain("week").And.Contain("month").And.Contain("year");
    }

    [Fact]
    public async Task AMistypedScopeIsRefusedRatherThanAnsweredWithThePersonalTotal()
    {
        using var server = new TeamServer();

        // `scope=compnay` used to answer 200 with the caller’s OWN numbers. An admin reading that as
        // the company’s total would make a spending decision from one person’s data. (codex, code round.)
        var refused = await server.ClientFor($"boss@{TeamServer.Domain}").GetAsync("/api/usage?scope=compnay");

        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await refused.Content.ReadFromJsonAsync<ErrorDto>())!.Error.Should().Contain("company");
    }

    [Fact]
    public async Task AnEmptyWindowMeansTodayRatherThanAnUnknownName()
    {
        using var server = new TeamServer();

        // `?window=` is a client saying nothing, not naming something wrong.
        var answer = await server.ClientFor($"dev@{TeamServer.Domain}")
            .GetFromJsonAsync<UsageDto>("/api/usage?window=");

        answer!.FromUtc.Should().Be(new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero));
    }

    [Fact]
    public async Task ScopeMeIsAcceptedExplicitlyAsWellAsByOmission()
    {
        using var server = new TeamServer();

        (await server.ClientFor($"dev@{TeamServer.Domain}")
            .GetFromJsonAsync<UsageDto>("/api/usage?scope=me"))!.Scope.Should().Be("me");
    }

    [Fact]
    public async Task ATornLineIsReportedToAnAdminAndToNobodyElse()
    {
        using var server = new TeamServer();
        File.WriteAllText(Path.Combine(server.DataDir, "usage.jsonl"), "{ not json\n");

        var mine = await server.ClientFor($"dev@{TeamServer.Domain}").GetFromJsonAsync<UsageDto>("/api/usage");
        var all = await server.ClientFor($"boss@{TeamServer.Domain}")
            .GetFromJsonAsync<UsageDto>("/api/usage?scope=company");

        // A torn line from months ago would otherwise sit on every person's page for ever, telling
        // them their record is damaged — about something they cannot see, fix, or have caused.
        mine!.UnreadableLines.Should().BeNull();
        all!.UnreadableLines.Should().Be(1);
    }

    [Fact]
    public async Task TheAnswerNamesTheRangeItUsed()
    {
        using var server = new TeamServer();

        var week = await server.ClientFor($"dev@{TeamServer.Domain}")
            .GetFromJsonAsync<UsageDto>("/api/usage?window=week");

        // "week" is the last seven days rather than the current ISO week, so the answer says which
        // seven and nobody has to guess.
        (week!.ToUtc - week.FromUtc).Days.Should().Be(7);
    }
}
