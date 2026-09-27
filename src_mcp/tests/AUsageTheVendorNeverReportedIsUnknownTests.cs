using System.Text.Json;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A call that ENDS before the vendor returns its usage — killed on the reviewer's deadline, cancelled
/// with the round, or an <c>api</c> call whose connection dropped — records its usage as UNKNOWN, never
/// as zero tokens or $0 (the plan round's accepted finding on the API-vendor calibration branch).
/// </summary>
/// <remarks>
/// <para>Why it matters: the request may well have been read and billed — a hosted reasoning model
/// killed at minute nineteen has spent nineteen minutes of tokens — and a ledger line that reads
/// <c>"tokensIn":0</c> with no cost, beside a row that HAS a price, is priced by every reader at $0.
/// The unknown is carried the way an unpriced metered run already is: a flag on <see cref="Usage"/>,
/// words on the ledger line and the round record, and a sentence in the audit.</para>
/// <para>And the other direction, which is what keeps the marker worth reading: a request that was
/// never SENT — refused by the shim before any socket opened — consumed nothing, and says nothing.</para>
/// </remarks>
public sealed class AUsageTheVendorNeverReportedIsUnknownTests : IDisposable
{
    private const string Key = "sk-unknown-0123456789abcdefghijklmnopqrstuv";

    private static readonly TokenPrice Grok = new(new TokenRates(2.00, 0.50, 6.00), 0, TokenRates.None);

    private readonly ApiEndpointStub _stub = ApiEndpointStub.Start();
    private readonly string _dir = Directory.CreateTempSubdirectory("coai-usage-unknown-").FullName;

    public void Dispose()
    {
        _stub.Dispose();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    // ---------- the ledger ----------

    [Fact]
    public void A_killed_turns_ledger_line_reads_usage_not_captured_never_zero_tokens_or_free()
    {
        new UsageLedger(_dir).Record(Build(Grok), new ReviewerOutcome.TimedOut(), "grok-4.7", "FeatureReview", TimeSpan.FromSeconds(1200));

        var line = LedgerLine();
        line.GetProperty("usageNote").GetString().Should().Be(
            "usage not captured", "the turn was killed before the vendor could say what it consumed — unknown, not zero");
        line.GetProperty("costUsd").ValueKind.Should().Be(JsonValueKind.Null, "a priced row must not work an unknown out as $0");
        line.GetProperty("outcome").GetString().Should().Be("timeout");
    }

    [Fact]
    public void A_killed_repair_keeps_the_attempt_that_was_counted_and_says_the_rest_is_unknown()
    {
        var outcome = new ReviewerOutcome.TimedOut { EarlierLaunches = new Usage(700, 10, null) };

        new UsageLedger(_dir).Record(Build(Grok), outcome, "grok-4.7", "FeatureReview", TimeSpan.FromSeconds(30));

        var line = LedgerLine();
        line.GetProperty("tokensIn").GetInt64().Should().Be(700, "the malformed first launch completed and reported its usage");
        line.GetProperty("usageNote").GetString().Should().Be("usage not captured", "and the killed repair's own share is not in that number");
    }

    [Fact]
    public void An_answered_turn_carries_no_marker()
    {
        new UsageLedger(_dir).Record(
            Build(Grok), new ReviewerOutcome.Ok(new NormalisedReview([], []), false, new Usage(100, 10, 0.00026)),
            "grok-4.7", "FeatureReview", TimeSpan.FromSeconds(3));

        LedgerLine().GetProperty("usageNote").GetString().Should().BeEmpty("a usage the vendor reported is known");
    }

    // ---------- the api shim's exit ----------

    [Fact]
    public void An_api_call_that_ended_unreachable_is_unknown_and_one_never_sent_is_nothing()
    {
        var runtime = new ApiRuntime("grok", _stub.Endpoint);
        var invocation = Build(Grok);

        runtime.ReadUsage(invocation, new ProcessResult(69, string.Empty, "the API did not finish in time", false))
            .NotCaptured.Should().BeTrue("exit 69 is a call that ended before an answer — a dropped connection or the shim's own deadline");
        runtime.ReadUsage(invocation, new ProcessResult(65, string.Empty, "needs --prompt-file", false))
            .NotCaptured.Should().BeFalse("exit 65 is a request the shim refused to send, and it consumed nothing");
        runtime.ReadUsage(invocation, new ProcessResult(0, "{\"tokensIn\":10,\"tokensOut\":2}", string.Empty, false))
            .NotCaptured.Should().BeFalse("a usage line is a usage the vendor reported");
    }

    /// <summary>The shim run as the REAL binary against an endpoint that does not answer inside the deadline.</summary>
    [Fact]
    public async Task An_api_call_whose_deadline_struck_before_the_answer_is_recorded_as_unknown()
    {
        _stub.Answers = _ =>
        {
            Thread.Sleep(TimeSpan.FromSeconds(3));
            return new ApiEndpointStub.Answer(200, ApiEndpointStub.Completion("{\"findings\":[]}", 900, 40));
        };

        var invocation = Build(Grok);
        var arguments = invocation.Request.Arguments.SkipWhile(a => a != "--ask-api").ToList();
        arguments[arguments.IndexOf("--timeout-seconds") + 1] = "1";
        var result = await new ProcessLauncher().RunAsync(
            new ProcessRequest(ServerBinary.Path, arguments, _dir) { Environment = invocation.Request.Environment, Timeout = TimeSpan.FromMinutes(1) },
            TestContext.Current.CancellationToken);
        var usage = new ApiRuntime("grok", _stub.Endpoint).ReadUsage(invocation, result);
        new UsageLedger(_dir).Record(
            invocation, new ReviewerOutcome.NonZeroExit(result.ExitCode, result.StdErr) { Usage = usage }, "grok-4.7", "FeatureReview", TimeSpan.FromSeconds(1));

        result.ExitCode.Should().Be(69, $"stderr said: {result.StdErr}");
        usage.NotCaptured.Should().BeTrue();
        var line = LedgerLine();
        line.GetProperty("usageNote").GetString().Should().Be("usage not captured");
        line.GetProperty("costUsd").ValueKind.Should().Be(JsonValueKind.Null);
    }

    // ---------- the round: status and the audit line ----------

    [Fact]
    public void Adding_an_unknown_to_a_known_usage_is_a_floor_that_says_so()
    {
        var total = new Usage(100, 10, 0.5).Add(Usage.Unknown);

        total.NotCaptured.Should().BeTrue("a total that folds in an unknown share is a floor");
        total.TokensIn.Should().Be(100);
        new Usage(100, 10, 0.5).Add(new Usage(1, 1, null)).NotCaptured.Should().BeFalse();
    }

    [Fact]
    public void A_round_with_a_killed_reviewer_records_usage_not_captured_beside_its_total()
    {
        var store = new SessionStore(_dir);
        var session = new PersistedSession(new SessionState("s", "D:/r", "main", new PanelConfig()), []);
        var live = new LiveRound(store, session, 1, [new ReviewerWork(Build(Grok))], "", Noticing.None);

        var record = live.Finish("revise", 1, "1 of 2", [
            (Build(Grok), new ReviewerOutcome.Ok(new NormalisedReview([], []), false, new Usage(100, 10, null))),
            (Build(Grok), new ReviewerOutcome.TimedOut()),
        ]);

        record.TokensIn.Should().Be(100, "what was counted is still counted");
        record.UsageNote.Should().Be("usage not captured", "status reads this record, and its total leaves the killed reviewer out");
    }

    [Fact]
    public void Status_carries_the_rounds_usage_note_beside_its_tokens()
    {
        var record = new RoundRecord("FeatureReview", 1, "revise", 1, "0 of 1", DateTime.UtcNow)
        {
            TokensIn = 100,
            UsageNote = CostText.UsageNotCaptured,
        };

        var json = JsonSerializer.Serialize(
            new SessionAnswer("s", "FeatureReview", 1, false, true, 5, 3, [record]), ServerJsonContext.Default.SessionAnswer);

        var round = JsonDocument.Parse(json).RootElement.GetProperty("rounds")[0];
        round.GetProperty("tokensIn").GetInt64().Should().Be(100);
        round.GetProperty("usageNote").GetString().Should().Be("usage not captured", "status is what a resumed conversation reads the round from");
    }

    [Fact]
    public void The_audit_names_a_killed_reviewers_usage_and_the_rounds_total_as_not_captured()
    {
        var sink = new ListSink();
        var log = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(sink).CreateLogger();
        var audit = new RoundAudit(log, "FeatureReview", 1);

        audit.Moved(new ReviewerProgress(
            "grok", RoleCatalog.FeatureRole, ReviewerState.Failed, new ReviewerOutcome.TimedOut(), TimeSpan.FromSeconds(1200)));
        audit.Closing("revise", 1, "0 of 1", new RoundRecord("FeatureReview", 1, "revise", 1, "0 of 1", DateTime.UtcNow) { StartedUtc = DateTime.UtcNow.AddMinutes(-20), UsageNote = CostText.UsageNotCaptured });

        sink.Lines.Should().HaveCount(2);
        sink.Lines[0].Should().Contain("usage not captured", "the reviewer's own line says its usage is unknown");
        sink.Lines[1].Should().Contain("usage not captured", "and the round's total says it is a floor");
    }

    private ReviewerInvocation Build(TokenPrice price)
    {
        var schema = Path.Combine(_dir, "schema.json");
        File.WriteAllText(schema, FindingSchema.Json);

        return new ApiRuntime("grok", _stub.Endpoint).Build(
            RoleCatalog.FeatureRole, "review this", _dir, schema, _dir,
            new ReviewerSettings("grok") { Model = "grok-4.7", ApiKey = Key, Price = price, Timeout = TimeSpan.FromMinutes(2) });
    }

    private JsonElement LedgerLine()
    {
        var lines = File.ReadAllLines(Path.Combine(_dir, "usage.jsonl"));

        return JsonDocument.Parse(lines.Should().ContainSingle().Subject).RootElement;
    }
}
