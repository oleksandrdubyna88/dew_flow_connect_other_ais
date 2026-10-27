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
using Serilog;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>The three lines one round writes about a Kimi reviewer, and the outcome and launch count they came from.</summary>
internal sealed record TheRound(ReviewerOutcome Outcome, string Summary, string Ledger, string Log, int Shims)
{
    /// <summary>The three lines with their names, for an assertion that must hold for each of them.</summary>
    public IEnumerable<(string Where, string Line)> Lines => [("round summary", Summary), ("ledger line", Ledger), ("log line", Log)];
}

/// <summary>
/// ONE round with a Kimi reviewer, from the vendor's answer to the three lines a person reads — the summary, the ledger and the log — with nothing the
/// product writes typed by hand: a fake agent answers <c>session/prompt</c> with a JSON-RPC error, the REAL shim (<c>AskAcpMode</c>, Kimi's own failure
/// table) turns it into its exit, report and tagged stderr, and the real runtime, executor, scheduler, ledger and round audit read those.
/// </summary>
internal sealed class AKimiRound(TempDir dir)
{
    private static BoundedScheduler Scheduler() => new(retryLadder: [TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1)]);

    private static KimiRuntime Kimi() => new("kimi", HostKind.Windows, new KimiSeams(
        root => new KimiPlaces(root, "persons", "persons-bin", [], string.Empty, "kimi.exe"), () => ("coai-mcp.exe", [])));

    private ReviewerInvocation Invocation() =>
        new("kimi", RoleCatalog.ArchitectureRole, new ProcessRequest("coai-mcp.exe", ["--ask-acp"], dir) { Timeout = TimeSpan.FromSeconds(30) }, string.Empty, new KimiReadSide(Kimi()), Model: "kimi-code/k3");

    /// <summary>What the real shim leaves for a turn whose agent answers with <paramref name="promptError"/>: exit, stdout report, and stderr as the parent captures it (tagged).</summary>
    public static async Task<ProcessResult> TheShimSaysAsync(string promptError, string stderr = "")
    {
        using var agent = new FakeAcpAgent(stderr.Length > 0 ? [FakeAcpAgent.StdErrText(stderr), FakeAcpAgent.Chunk("x")] : [FakeAcpAgent.Chunk("x")], $$""","promptError":"{{promptError}}" """);
        var notes = new List<string>();
        var stdout = new StringWriter();

        var code = await AskAcpMode.RunAsync(
            ["--ask-acp", "--profile", "fake", "--model", "m-1", "--cwd", agent.Cwd, "--deadline-seconds", "30"],
            new StringReader("say hello"), notes.Add, stdout, [agent.Profile(failures: new KimiAcpProfile().KnownFailures)]);

        return new ProcessResult(code, stdout.ToString(), string.Join("\n", notes.Select(note => ShimNotes.Prefix + note)), TimedOut: false);
    }

    /// <summary>One round whose agent answers the turn with <paramref name="promptError"/>.</summary>
    public async Task<TheRound> RunAsync(string promptError, string stderr = "") => await RunAsync(await TheShimSaysAsync(promptError, stderr));

    /// <summary>One round whose launch ends as <paramref name="said"/> says — for a launch the real shim cannot produce (a vendor line with no tag).</summary>
    public async Task<TheRound> RunAsync(ProcessResult said)
    {
        var kimi = new ScriptedKimi(_ => said);
        var invocation = Invocation();
        var sink = new ListSink();
        var audit = new RoundAudit(new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger(), "CodeReview", 1);
        var ledger = new UsageLedger(dir.At("quota"));

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
}
