using System.Globalization;
using System.Text.Json;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A stated reset is read locally and written down — story 5.3b of <c>todo/PLAN_every_round_is_counted.md</c>, the
/// writer's half: the refusal's stated reset becomes the ledger line's <c>resetsUtc</c>, read by the one parser both
/// halves use, so the extension shows it without a second parser of vendor sentences.
/// </summary>
/// <remarks>
/// <para><b>What was missing.</b> The Team server parks an exhausted slot until the instant the vendor stated; this
/// machine wrote the refusal's words into <c>usage.jsonl</c> and nothing else, so the only way to know that Gemini comes
/// back in 88 hours was to read a ledger line by eye.</para>
/// <para><b>What must hold.</b> The LAST line of a turn that ended refused carries the instant the vendor named, in UTC;
/// a refusal that named none, an answer, and every earlier launch of the turn carry nothing — never the server's guess.
/// The real executor runs over <see cref="ScriptedResults"/>, and the real ledger writes the file that is read back.</para>
/// </remarks>
public sealed class AStatedResetIsWrittenLocallyTests : IDisposable
{
    private const string AgyQuotaLine =
        "error: Individual quota reached. Please upgrade your subscription to increase your limits. Resets in 88h49m9s.";

    private readonly TempDir _dir = TempDir.For("coai-stated-reset-");

    public void Dispose() => _dir.Dispose();

    private static ReviewerInvocation Agy() =>
        new("antigravity", RoleCatalog.ArchitectureRole, new ProcessRequest("agy", [], ".") { Timeout = TimeSpan.FromMinutes(1) });

    private static ProcessResult Exited(int code, string stdOut, string stdErr = "") => new(code, stdOut, stdErr, TimedOut: false);

    private IReadOnlyList<UsageEntry> Recorded(ReviewerOutcome outcome, string under)
    {
        var ledger = new UsageLedger(_dir.At(under));
        ledger.Record(Agy(), outcome, "gemini-3.8-flash-low", "CodeReview", TimeSpan.FromSeconds(4));

        return [.. File.ReadAllLines(ledger.Path).Select(l => JsonSerializer.Deserialize(l, LedgerJsonContext.Default.UsageEntry)!)];
    }

    private static DateTimeOffset Instant(string utc) =>
        DateTimeOffset.Parse(utc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    [Fact]
    public async Task AgysRefusal_IsWrittenWithTheInstantItStated()
    {
        var before = DateTimeOffset.UtcNow;
        var outcome = await new ReviewerExecutor(new ScriptedResults(Exited(3, string.Empty, AgyQuotaLine)))
            .RunAsync(Agy(), null, TestContext.Current.CancellationToken);

        var line = Recorded(outcome, "agy").Should().ContainSingle().Subject;

        line.ResetsUtc.Should().NotBeEmpty("agy stated when its quota comes back, and the ledger is where the local half keeps it");
        line.ResetsUtc.Should().EndWith("Z", "an instant written to storage is UTC");
        Instant(line.ResetsUtc).Should().BeCloseTo(before + new TimeSpan(88, 49, 9), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task ARefusalThatStatesNoReset_WritesNone_NeverTheGuess()
    {
        var outcome = await new ReviewerExecutor(new ScriptedResults(Exited(1, string.Empty, "429 Too Many Requests")))
            .RunAsync(Agy(), null, TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ReviewerOutcome.RateLimited>();
        Recorded(outcome, "throttle").Single().ResetsUtc.Should().BeEmpty("the vendor named no time — the server's 30-minute guess is not a statement");
    }

    [Fact]
    public void AnAnswer_WritesNone() =>
        Recorded(new ReviewerOutcome.Ok(new NormalisedReview([], []), false, new Usage(300, 9, null)), "answer")
            .Single().ResetsUtc.Should().BeEmpty();

    [Fact]
    public void OnlyTheTurnsLastLine_CarriesTheReset()
    {
        // A first launch that answered junk, whose repair was then refused with a stated reset: the refusal is the turn's
        // ending, and the junk answer before it stated nothing.
        var outcome = new ReviewerOutcome.RateLimited(AgyQuotaLine)
        {
            Launches =
            [
                new LaunchPart(LaunchAttempts.First, new Usage(700, 4, null), TimeSpan.FromSeconds(2), "unparseable: x"),
                new LaunchPart(LaunchAttempts.Repair, Usage.None, TimeSpan.FromSeconds(1), "rate limited"),
            ],
            EarlierLaunches = new Usage(700, 4, null),
        };

        var lines = Recorded(outcome, "repair");

        lines.Should().HaveCount(2);
        lines[0].ResetsUtc.Should().BeEmpty("the junk answer stated no reset");
        lines[1].ResetsUtc.Should().NotBeEmpty("the refused repair is what the turn last saw");
    }

    [Fact]
    public void AnOldLine_WithoutTheField_ReadsAsNone()
    {
        const string Old = """{"utc":"2026-10-01T10:00:00.0000000Z","provider":"gemini","model":"m","role":"r","stage":"CodeReview","seconds":1,"tokensIn":1,"tokensOut":1,"costUsd":null,"outcome":"ok"}""";

        JsonSerializer.Deserialize(Old, LedgerJsonContext.Default.UsageEntry)!.ResetsUtc.Should().BeEmpty();
    }
}
