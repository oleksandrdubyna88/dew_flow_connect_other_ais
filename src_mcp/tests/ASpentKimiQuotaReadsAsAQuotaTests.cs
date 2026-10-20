using System.Text.Json;
using CoaiMcp.Acp;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Acp;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using FluentAssertions.Execution;
using Serilog;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// S7 of <c>todo/PLAN_a_spent_kimi_quota_is_not_a_lost_sign_in.md</c> — ONE round with a Kimi reviewer whose account has spent its 5-hour quota,
/// from the vendor's answer to the three lines a person reads: the round's summary, the ledger and the server log.
/// </summary>
/// <remarks>
/// <para>Nothing is typed by hand where the product writes it. The fake agent answers <c>session/prompt</c> with a JSON-RPC error (the real envelope of
/// <c>AcpClient</c>), the REAL shim (<c>AskAcpMode</c>, with Kimi's own failure table) turns that into its exit, report and stderr, and the REAL
/// executor and scheduler read those. So the test fails when any one of the stories' seams is missing — the table row (story 1), the
/// reviewer's reason (story 1), the vendor's line kept after the cure (story 2) — and a unit test of each seam would not.</para>
/// <para>The sentence the vendor said is the owner's CLI line of the plan's section 1, wrapped in the sign-in prefix of its section 2 hypothesis:
/// the real ACP envelope is NOT measured yet (the plan's capture step); when it is, <see cref="PromptError"/> is replaced by the real inner text.</para>
/// </remarks>
public sealed class ASpentKimiQuotaReadsAsAQuotaTests : IDisposable
{
    /// <summary>What the agent answers the turn with — constructed from the owner's CLI line, wrapped in the sign-in prefix (S1's hypothesis).</summary>
    private const string PromptError = "Authentication required: 429 You've reached your 5-hour usage limit. Your quota will reset at 14:00 UTC.";

    private readonly TempDir _dir = TempDir.For("testsToDelete-kimi-quota-round-");

    public void Dispose() => _dir.Dispose();

    private static BoundedScheduler Scheduler() => new(retryLadder: [TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1)]);

    private static KimiRuntime Kimi() => new("kimi", HostKind.Windows, new KimiSeams(
        root => new KimiPlaces(root, "persons", "persons-bin", [], string.Empty, "kimi.exe"), () => ("coai-mcp.exe", [])));

    private ReviewerInvocation Invocation() =>
        new("kimi", RoleCatalog.ArchitectureRole, new ProcessRequest("coai-mcp.exe", ["--ask-acp"], _dir) { Timeout = TimeSpan.FromSeconds(30) }, string.Empty, new KimiReadSide(Kimi()), Model: "kimi-code/k3");

    /// <summary>What the real shim leaves for a turn whose agent answers with <paramref name="promptError"/>: exit, stdout report, and stderr as the parent captures it (tagged).</summary>
    private static async Task<ProcessResult> TheShimSaysAsync(string promptError)
    {
        using var agent = new FakeAcpAgent([FakeAcpAgent.Chunk("x")], $$""","promptError":"{{promptError}}" """);
        var notes = new List<string>();
        var stdout = new StringWriter();

        var code = await AskAcpMode.RunAsync(
            ["--ask-acp", "--profile", "fake", "--model", "m-1", "--cwd", agent.Cwd, "--deadline-seconds", "30"],
            new StringReader("say hello"), notes.Add, stdout, [agent.Profile(failures: new KimiAcpProfile().KnownFailures)]);

        return new ProcessResult(code, stdout.ToString(), string.Join("\n", notes.Select(note => ShimNotes.Prefix + note)), TimedOut: false);
    }

    /// <summary>The three lines one round writes about the reviewer, and the outcome they came from.</summary>
    private sealed record TheRound(ReviewerOutcome Outcome, string Summary, string Ledger, string Log, int Shims);

    private async Task<TheRound> OneRoundAsync(string promptError)
    {
        var said = await TheShimSaysAsync(promptError);
        var kimi = new ScriptedKimi(_ => said);
        var invocation = Invocation();
        var sink = new ListSink();
        var audit = new RoundAudit(new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger(), "CodeReview", 1);
        var ledger = new UsageLedger(_dir.At("quota"));

        var results = await Scheduler().RunAllAsync(
            [new ReviewerWork(invocation)], new ReviewerExecutor(kimi), TestContext.Current.CancellationToken,
            onProgress: progress =>
            {
                audit.Moved(progress);
                if (progress.Outcome is { } outcome)
                {
                    ledger.Record(invocation, outcome, "kimi-code/k3", "CodeReview", progress.Elapsed);
                }
            });

        var written = File.ReadAllLines(ledger.Path).Select(line => JsonSerializer.Deserialize(line, LedgerJsonContext.Default.UsageEntry)!).Single();

        return new TheRound(
            results.Single().Outcome, ReviewerSummaryFactory.From(results).Failures.Single(), written.Outcome,
            sink.Lines.Single(line => line.Contains("FAILED", StringComparison.Ordinal)), kimi.Shims);
    }

    private static void ReadsAsAQuota(string where, string line)
    {
        line.Should().Contain(KimiQuotaWords.Opening, $"the {where} says the quota is used up");
        line.Should().Contain(KimiQuotaWords.ResetOpening, $"the {where} quotes the reset clause Kimi gave");
        line.Should().NotContain("kimi login", $"the {where} never sends a person to sign in again — it would not help");
        line.Should().NotContain("exit 77", $"the {where} is not a sign-in refusal");
        line.Should().Contain(KimiQuotaWords.Opening, Exactly.Once(), $"the {where} says the cure once — two seams quoting it is an integration bug");
    }

    [Fact]
    public async Task ASpentQuota_ReadsAsAQuota_InTheSummary_TheLedger_AndTheLog_AfterOneLaunch()
    {
        var round = await OneRoundAsync(PromptError);

        using var scope = new AssertionScope();
        ReadsAsAQuota("round summary", round.Summary);
        ReadsAsAQuota("ledger line", round.Ledger);
        ReadsAsAQuota("log line", round.Log);
        var limited = round.Outcome.Should().BeOfType<ReviewerOutcome.RateLimited>("a spent quota is a rate limit, not a failed sign-in").Subject;
        limited.Attempts.Should().Be(1);
        RateLimit.Hopeless(limited.Reason).Should().BeTrue("the vendor's own words say the allowance is spent: " + limited.Reason);
        round.Shims.Should().Be(1, "a spent quota is never climbed: the ladder does not start");
    }

    [Fact]
    public async Task AnAccountIdInTheVendorsSentence_ReachesNoneOfTheThreeLines()
    {
        var round = await OneRoundAsync(PromptError.Replace(" Your quota", " Your account org-0a1b2c3d4e5f6a7b <ak-9z8y7x6w5v4u3t2s>. Your quota", StringComparison.Ordinal));

        using var scope = new AssertionScope();
        foreach (var (where, line) in new[] { ("round summary", round.Summary), ("ledger line", round.Ledger), ("log line", round.Log) })
        {
            line.Should().NotContain("0a1b2c3d4e5f6a7b", $"the {where} must not carry the account id").And.NotContain("9z8y7x6w5v4u3t2s");
            line.Should().Contain(KimiQuotaWords.Opening, $"the {where} still says what happened");
        }
    }
}
