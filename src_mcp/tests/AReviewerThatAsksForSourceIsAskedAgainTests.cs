using CoaiMcp.Core.Feature;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Normalizer;
using CoaiMcp.Runners.Collecting;
using CoaiMcp.Runners.Feature;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The turn loop (plan §4.9, story S3.2): a feature reviewer that asks for source is served it and asked
/// again — one reviewer, one conversation, one terminal outcome — through the REAL scheduler, the real
/// codex adapter's argv, the fake CLI switching its answer by turn, and a real repository the source is
/// read from.
/// </summary>
/// <remarks>
/// <para>The continuation is built here the way the roster builds it — the base prompt with the tail
/// appended for the launch, the base with the tail and the repair instruction for the repair — so what
/// is driven is the runners' whole loop and nothing of the server; the server's composition is held by
/// <c>AFeatureIsReviewedEndToEndTests</c>.</para>
/// <para>The fake CLI reads which turn it is off the prompt (<c>FAKECLI_TURN_MARKER</c>), never off a
/// counter: two reviewers of one round interleave on any counter, and the per-provider test runs three.</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class AReviewerThatAsksForSourceIsAskedAgainTests : IAsyncLifetime
{
    private const string Base =
        "You review a feature.\n\n## What you have\n\nAn outline. Ask for source in `sourceRequests`.\n\n## The context\n\nan outline of src/Cart.cs\n";

    private const string Clean = """{"findings": [], "notes": "nothing to add", "sourceRequests": []}""";

    private const string AsksForAdd =
        """{"findings": [], "notes": "waiting", "sourceRequests": [{"file": "src/Cart.cs", "symbol": "Cart.Add", "startLine": null, "endLine": null, "why": "does Add check?"}]}""";

    private const string AsksForTheBigFile =
        """{"findings": [], "notes": "waiting", "sourceRequests": [{"file": "src/Big.cs", "symbol": null, "startLine": 1, "endLine": 400, "why": "all of it"}]}""";

    private const string FindingAndAsk =
        """
        {"findings": [{"severity": "major", "category": "reliability", "file": "src/Cart.cs", "line": 28, "title": "Add never checks", "why": "it may not", "fix": "check"}],
         "notes": "one so far", "sourceRequests": [{"file": "src/Cart.cs", "symbol": "Cart.Add", "startLine": null, "endLine": null, "why": "to be sure"}]}
        """;

    /// <summary>
    /// What the fake writes to STDOUT: the usage the codex adapter reads there — and a clean review as
    /// well, so a bare invocation that answers from stdout (the reviewer queued behind a slot) parses.
    /// </summary>
    private const string OneThousandIn = """{"findings": [], "input_tokens": 1000, "output_tokens": 50}""";

    private const string TenIn = """{"findings": [], "input_tokens": 10, "output_tokens": 5}""";

    /// <summary>The usage of a launch whose ANSWER (the outfile) is not the schema's JSON — billed all the same.</summary>
    private const string SevenHundredIn = """{"input_tokens": 700, "output_tokens": 30}""";

    private readonly ProcessLauncher _launcher = new();
    private readonly ReviewerExecutor _executor = new(new ProcessLauncher());
    private readonly TempDir _dir = TempDir.For("coai-turns-");
    private TempGitRepo _git = null!;
    private string _head = string.Empty;
    private string _record = string.Empty;

    private static string CartSource =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "outline", "csharp.cs.txt")).ReplaceLineEndings("\n");

    public async ValueTask InitializeAsync()
    {
        _git = await TempGitRepo.InitAsync(_launcher, "coai-turns-repo-");
        Directory.CreateDirectory(Path.Combine(_git.Path, "src"));
        await _git.WriteAsync("src/Cart.cs", CartSource);
        // 400 lines of 350 bytes: one slice past the reviewer's whole allowance, so asking for it spends the budget.
        await _git.WriteAsync("src/Big.cs", string.Concat(Enumerable.Range(0, 400).Select(i => $"// {i} {new string('x', 340)}\n")));
        await _git.CommitAsync("the feature");
        _head = await _git.HeadAsync();
        _record = Directory.CreateDirectory(_dir.At("record")).FullName;

        Environment.SetEnvironmentVariable("FAKECLI_MODE", "vendor");
        Environment.SetEnvironmentVariable("FAKECLI_RECORD_DIR", _record);
        Environment.SetEnvironmentVariable("FAKECLI_TURN_MARKER", TurnTail.HeadingPrefix + "{n} of");
        Environment.SetEnvironmentVariable("FAKECLI_REPAIR_MARKER", "YOUR PREVIOUS ATTEMPT");
        Environment.SetEnvironmentVariable("FAKECLI_STDOUT", TenIn);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var name in Environment.GetEnvironmentVariables().Keys.Cast<string>().Where(n => n.StartsWith("FAKECLI_", StringComparison.Ordinal)))
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        await _git.DisposeAsync();
        _dir.Dispose();
    }

    // ---------- the fixture: the roster's composition, by hand ----------

    private static void Turn(int n, string answer, string stdout = "", int exit = 0, string stderr = "", int sleepMs = 0, bool repair = false)
    {
        var prefix = n == 1 && !repair ? "FAKECLI_" : $"FAKECLI_TURN{n}_{(repair ? "REPAIR_" : string.Empty)}";
        Environment.SetEnvironmentVariable(prefix + "OUTFILE_TEXT", answer);
        Environment.SetEnvironmentVariable(prefix + "STDOUT", stdout.Length > 0 ? stdout : null);
        Environment.SetEnvironmentVariable(prefix + "EXIT", exit == 0 ? null : exit.ToString());
        Environment.SetEnvironmentVariable(prefix + "STDERR", stderr.Length > 0 ? stderr : null);
        Environment.SetEnvironmentVariable(prefix + "SLEEP_MS", sleepMs == 0 ? null : sleepMs.ToString());
    }

    private SourceResolver Resolver() =>
        new(new GitHistory(_launcher), new TreeSitterOutliner(), _git.Path, _head);

    /// <summary>One reviewer's work, composed as `RosterBuilder.Add` composes it: base + tail, and the repair from the same.</summary>
    private ReviewerWork Work(string provider = "codex", int followUps = 3, TimeSpan? timeout = null, SourceResolver? resolver = null)
    {
        var runtime = new CodexRuntime(provider);
        var settings = new ReviewerSettings(provider) { ExecutablePath = FakeCliInvocations.Exe, Timeout = timeout ?? TimeSpan.FromMinutes(1) };
        var schema = SchemaFile.Ensure(_dir, SchemaShape.Feature);
        var launchDir = Directory.CreateDirectory(_dir.At("launch-" + Guid.NewGuid().ToString("N")[..6])).FullName;
        var repairDir = Directory.CreateDirectory(_dir.At("repair-" + Guid.NewGuid().ToString("N")[..6])).FullName;
        var outputDir = Directory.CreateDirectory(_dir.At("out-" + Guid.NewGuid().ToString("N")[..6])).FullName;
        ReviewerWork Build(string tail) => new(
            runtime.Build(RoleCatalog.FeatureRole, Base + tail, launchDir, schema, outputDir, settings),
            runtime.Build(RoleCatalog.FeatureRole, Base + tail + RepairInstruction.Text, repairDir, schema, outputDir, settings),
            "feature-review",
            System.Text.Encoding.UTF8.GetByteCount(Base + tail));
        var turns = new SourceTurns.On(resolver ?? Resolver(), followUps);

        return Build(string.Empty) with { Continue = new SourceConversation(turns, Build) };
    }

    private async Task<(ReviewerOutcome Outcome, List<ReviewerProgress> Progress)> RunAsync(
        ReviewerWork work, BoundedScheduler? scheduler = null, CancellationToken ct = default)
    {
        var progress = new List<ReviewerProgress>();
        var results = await (scheduler ?? new BoundedScheduler()).RunAllAsync(
            [work], _executor, ct, p => { lock (progress) { progress.Add(p); } });

        return (results.Single().Outcome, progress);
    }

    /// <summary>Every recorded launch's prompt (the last NUL-separated field), oldest first.</summary>
    private IReadOnlyList<string> Prompts() =>
        [.. Directory.GetFiles(_record, "*.argv").OrderBy(File.GetCreationTimeUtc).Select(f => LaunchRecords.Read(f).Split('\0')[^1])];

    // ---------- the prefix, the served code, the note ----------

    [Fact]
    public async Task TheSecondTurnsPrompt_IsTheFirstTurnsPromptByteForByte_WithTheServedSymbolAppended()
    {
        Turn(1, AsksForAdd);
        Turn(2, Clean);

        var (outcome, _) = await RunAsync(Work(), ct: TestContext.Current.CancellationToken);

        var prompts = Prompts();
        prompts.Should().HaveCount(2, "a request buys one follow-up turn");
        prompts[1].Should().StartWith(prompts[0], "the base prompt is resent unchanged, so a vendor's prompt cache can reuse it (D25)");
        var tail = prompts[1][prompts[0].Length..];
        tail.Should().StartWith("\n\n" + TurnTail.HeadingPrefix + "2 of 4", "the turn count lives in the tail alone");
        tail.Should().Contain("### What you asked for").And.Contain("src/Cart.cs `Cart.Add` — does Add check?");
        tail.Should().Contain("### src/Cart.cs lines 28-35 of 49 @ " + _head + " — Add", "the symbol is served fenced with its path, lines and commit");
        tail.Should().Contain("secretAdd(item);", "the served text is the declaration's body");
        tail.Should().Contain(TurnTail.OnlyThisTurnCounts).And.Contain(TurnTail.AskAgain).And.NotContain("FINAL:");
        var ok = outcome.Should().BeOfType<ReviewerOutcome.Ok>().Subject;
        ok.Turns.Should().Be(2);
        ok.Served.Should().Contain("turn 2: served src/Cart.cs Add (28-35 of 49)");
        ok.Review.Notes.Should().Be("nothing to add", "the LAST turn's answer is the reviewer's answer");
    }

    [Fact]
    public async Task AReviewerThatAsksForNothing_IsNotAskedAgain()
    {
        Turn(1, Clean);

        var (outcome, progress) = await RunAsync(Work(), ct: TestContext.Current.CancellationToken);

        Prompts().Should().ContainSingle();
        var ok = outcome.Should().BeOfType<ReviewerOutcome.Ok>().Subject;
        ok.Turns.Should().Be(1);
        ok.Served.Should().BeEmpty();
        ok.EarlierTurns.Should().BeEmpty();
        progress.Single(p => p.Outcome is not null).Note.Should().BeEmpty("a single-turn reviewer's note is what it always was");
    }

    // ---------- the three stops ----------

    [Fact]
    public async Task TheFollowUpCap_EndsTheConversation_AndTheLastTurnWasToldFinal()
    {
        Turn(1, AsksForAdd);
        Turn(2, AsksForAdd);
        Turn(3, AsksForAdd);
        Turn(4, AsksForAdd);

        var (outcome, progress) = await RunAsync(Work(followUps: 2), ct: TestContext.Current.CancellationToken);

        var prompts = Prompts();
        prompts.Should().HaveCount(3, "one turn plus two follow-ups, and a FINAL turn's requests are ignored");
        prompts[1].Should().NotContain(TurnTail.Final);
        prompts[2].Should().Contain(TurnTail.HeadingPrefix + "3 of 3").And.Contain(TurnTail.Final);
        outcome.Should().BeOfType<ReviewerOutcome.Ok>().Which.Turns.Should().Be(3);
        progress.Single(p => p.Outcome is not null).Note.Should().StartWith("3 turns; source:");
    }

    [Fact]
    public async Task ASpentSourceBudget_MakesTheNextTurnFinal_WhateverTheCapSays()
    {
        Turn(1, AsksForTheBigFile);
        Turn(2, AsksForAdd);
        Turn(3, Clean);

        var (outcome, _) = await RunAsync(Work(followUps: 3), ct: TestContext.Current.CancellationToken);

        var prompts = Prompts();
        prompts.Should().HaveCount(2, "a turn whose request spent the reviewer's allowance is told FINAL, and its own request is then ignored");
        prompts[1].Should().Contain("not served: src/Big.cs — budget spent").And.Contain(TurnTail.Final);
        outcome.Should().BeOfType<ReviewerOutcome.Ok>().Which.Turns.Should().Be(2);
    }

    // ---------- the repair ----------

    [Fact]
    public async Task AMalformedSecondTurn_IsRepairedAgainstTheSecondTurnsPrompt_NotTheFirsts()
    {
        Turn(1, AsksForAdd);
        Turn(2, "this is not JSON at all");
        Turn(2, Clean, repair: true);

        var (outcome, _) = await RunAsync(Work(), ct: TestContext.Current.CancellationToken);

        var prompts = Prompts();
        prompts.Should().HaveCount(3, "turn 1, turn 2, and turn 2's repair");
        prompts[2].Should().StartWith(prompts[1], "the repair is composed from the turn it repairs — the served source included");
        prompts[2].Should().EndWith(RepairInstruction.Text);
        var ok = outcome.Should().BeOfType<ReviewerOutcome.Ok>().Subject;
        ok.Repaired.Should().BeTrue();
        ok.Turns.Should().Be(2);
    }

    // ---------- usage survives a failed later turn ----------

    [Fact]
    public async Task AFailedSecondTurn_KeepsTheFirstTurnsCost_OnTheOutcomeAndInTheLedger()
    {
        Turn(1, AsksForAdd, stdout: OneThousandIn);
        Turn(2, Clean, exit: 1, stderr: "the model fell over");

        var (outcome, _) = await RunAsync(Work(), ct: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ReviewerOutcome.NonZeroExit>("a failed later turn is a failed reviewer");
        var earlier = outcome.EarlierTurns.Should().ContainSingle().Subject;
        earlier.Turn.Should().Be(1);
        earlier.Usage.TokensIn.Should().Be(1000, "turn 1 answered and was billed, whatever turn 2 did");
        outcome.EarlierUsage.TokensIn.Should().Be(1000);

        var ledger = new UsageLedger(_dir.At("ledger"));
        ledger.Record(new ReviewerInvocation("codex", RoleCatalog.FeatureRole, new ProcessRequest("x", [], ".")), outcome, "m", "FeatureReview", TimeSpan.FromSeconds(4));
        var lines = File.ReadAllLines(ledger.Path);
        lines.Should().HaveCount(2, "one ledger line per turn");
        lines[0].Should().Contain("\"tokensIn\":1000").And.Contain("\"outcome\":\"ok\"");
        // The fake's default stdout (ten tokens in) is what this failed launch REPORTED, and since
        // 2026-09-26 a failed launch keeps what it reported — see the next test for the whole claim.
        lines[1].Should().Contain("\"tokensIn\":10").And.Contain("exit 1");
    }

    /// <summary>
    /// A failed SECOND turn that the vendor billed keeps BOTH turns' spend: turn 1's on the base, its own
    /// on the terminal outcome — and the ledger writes both.
    /// </summary>
    /// <remarks>
    /// The first trial's reasoning-only answers arrived on turn 1 and on turn 2 alike (Qwen3.8-max,
    /// 2026-09-26); with the failed turn's usage dropped at the launch, a two-turn reviewer that fell
    /// over on its second call was written down at roughly half of what it cost.
    /// </remarks>
    [Fact]
    public async Task AFailedSecondTurn_ThatWasBilled_KeepsBothTurnsSpend()
    {
        Turn(1, AsksForAdd, stdout: OneThousandIn);
        Turn(2, Clean, stdout: """{"findings": [], "input_tokens": 1100, "output_tokens": 16382}""", exit: 70, stderr: "no message content: 16382 reasoning tokens");

        var (outcome, _) = await RunAsync(Work(), ct: TestContext.Current.CancellationToken);

        var failed = outcome.Should().BeOfType<ReviewerOutcome.NonZeroExit>().Subject;
        failed.Usage.TokensIn.Should().Be(1100, "turn 2 was billed before the shim refused its answer");
        failed.Usage.TokensOut.Should().Be(16382);
        outcome.EarlierUsage.TokensIn.Should().Be(1000);

        var ledger = new UsageLedger(_dir.At("ledger"));
        ledger.Record(new ReviewerInvocation("codex", RoleCatalog.FeatureRole, new ProcessRequest("x", [], ".")), outcome, "m", "FeatureReview", TimeSpan.FromSeconds(4));
        var lines = File.ReadAllLines(ledger.Path);
        lines.Should().HaveCount(2);
        lines[0].Should().Contain("\"tokensIn\":1000").And.Contain("\"outcome\":\"ok\"");
        lines[1].Should().Contain("\"tokensIn\":1100").And.Contain("\"tokensOut\":16382").And.Contain("exit 70");
    }

    [Fact]
    public async Task ACancellationDuringTheSecondTurn_KeepsTheFirstTurnsCost()
    {
        Turn(1, AsksForAdd, stdout: OneThousandIn);
        Turn(2, Clean, sleepMs: 20_000);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var progress = new List<ReviewerProgress>();

        var results = await new BoundedScheduler().RunAllAsync([Work()], _executor, cts.Token, p =>
        {
            lock (progress) { progress.Add(p); }
            if (p.Note.StartsWith("turn 1 answered", StringComparison.Ordinal))
            {
                cts.CancelAfter(TimeSpan.FromMilliseconds(300));
            }
        });

        var outcome = results.Single().Outcome;
        // The launcher reports a caller's cancellation mid-launch as a killed tree (`TimedOut` with
        // `Cancelled`), and the executor names that `TimedOut`; a cancellation that lands between two
        // turns — while the source is being served — throws and is the scheduler's "cancelled while it
        // was running". Either way it is not an answer, and turn 1's cost survives it.
        outcome.Should().NotBeOfType<ReviewerOutcome.Ok>("the round ended before the conversation did");
        outcome.EarlierTurns.Should().ContainSingle().Which.Usage.TokensIn.Should().Be(1000, "the round ended, and turn 1 still cost what it cost");
        progress.Count(p => p.Outcome is not null).Should().Be(1, "one terminal outcome, reported once");
    }

    [Fact]
    public async Task AFailedLaterTurn_IsAFailedReviewer_NeverAFallbackToTheFirstAnswer()
    {
        Turn(1, FindingAndAsk);
        Turn(2, Clean, exit: 1, stderr: "gone");

        var (outcome, _) = await RunAsync(Work(), ct: TestContext.Current.CancellationToken);

        outcome.Should().NotBeOfType<ReviewerOutcome.Ok>("turn 1's finding was made while waiting for source, and standing on it could give a false proceed");
        ReviewerSummaryFactory.From([(Work().Invocation, outcome)]).Sentence.Should().Contain("0 of 1");
    }

    // ---------- time ----------

    [Fact]
    public async Task ATurnThatTimesOut_IsOneTerminalFailure_AndTheSlotIsReleased()
    {
        Turn(1, AsksForAdd, stdout: OneThousandIn);
        Turn(2, Clean, sleepMs: 10_000);
        var scheduler = new BoundedScheduler(globalCap: 1, perProviderCap: 1);
        var progress = new List<ReviewerProgress>();
        var slow = Work(timeout: TimeSpan.FromSeconds(2));
        var next = new ReviewerWork(FakeCliInvocations.Invoke("codex", ["emit", FakeCliInvocations.CleanReview]));

        var results = await scheduler.RunAllAsync([slow, next], _executor, TestContext.Current.CancellationToken, p => { lock (progress) { progress.Add(p); } });

        var timedOut = results[0].Outcome;
        timedOut.Should().BeOfType<ReviewerOutcome.TimedOut>("each turn runs under the reviewer's own deadline");
        timedOut.EarlierTurns.Should().ContainSingle().Which.Usage.TokensIn.Should().Be(1000, "every turn's usage is kept");
        progress.Count(p => p.Role == RoleCatalog.FeatureRole && p.Outcome is not null).Should().Be(1, "one terminal outcome, so the stand-down count moves once");
        results[1].Outcome.Should().BeOfType<ReviewerOutcome.Ok>("the reviewer queued behind the slot got it after the timeout");
    }

    /// <summary>
    /// A turn whose first launch answered junk and whose REPAIR then timed out: the malformed attempt
    /// completed and was billed, and its cost rides on the turn's terminal outcome — one ledger line for
    /// that turn, not two and not none (found by the epic 3 risk consultation 159f0397).
    /// </summary>
    [Fact]
    public async Task ATurnWhoseRepairTimesOut_KeepsTheMalformedAttemptsCost_AsOneTerminalFailure_AndReleasesTheSlot()
    {
        Turn(1, AsksForAdd, stdout: OneThousandIn);
        Turn(2, "this is not JSON at all", stdout: SevenHundredIn);
        Turn(2, Clean, sleepMs: 10_000, repair: true);
        var scheduler = new BoundedScheduler(globalCap: 1, perProviderCap: 1);
        var progress = new List<ReviewerProgress>();
        var slow = Work(timeout: TimeSpan.FromSeconds(2));
        var next = new ReviewerWork(FakeCliInvocations.Invoke("codex", ["emit", FakeCliInvocations.CleanReview]));

        var results = await scheduler.RunAllAsync([slow, next], _executor, TestContext.Current.CancellationToken, p => { lock (progress) { progress.Add(p); } });

        var timedOut = results[0].Outcome;
        timedOut.Should().BeOfType<ReviewerOutcome.TimedOut>("turn 2's repair ran out of the turn's deadline");
        timedOut.EarlierTurns.Should().ContainSingle().Which.Usage.TokensIn.Should().Be(1000, "turn 1 answered and was billed");
        timedOut.LastTurnUsage.TokensIn.Should().Be(700, "turn 2's malformed first launch is the failed turn's own cost");
        timedOut.TotalUsage.TokensIn.Should().Be(1700, "both attempts' usage, each exactly once");
        progress.Count(p => p.Role == RoleCatalog.FeatureRole && p.Outcome is not null).Should().Be(1, "one terminal outcome, reported once");
        results[1].Outcome.Should().BeOfType<ReviewerOutcome.Ok>("the reviewer queued behind the slot got it after the timeout");

        var ledger = new UsageLedger(_dir.At("ledger-repair"));
        ledger.Record(slow.Invocation, timedOut, "m", "FeatureReview", TimeSpan.FromSeconds(4));
        var lines = File.ReadAllLines(ledger.Path);
        lines.Should().HaveCount(2, "turn 1's line and the failed turn's — the malformed attempt is on that turn's line, never a third");
        lines[0].Should().Contain("\"tokensIn\":1000").And.Contain("\"outcome\":\"ok\"");
        lines[1].Should().Contain("\"tokensIn\":700", "turn 2's first launch completed and reported its usage; only its answer was malformed")
            .And.Contain("timeout");
    }

    /// <summary>
    /// A turn that CONTINUES keeps every launch it paid for. Turn 1's first launch answered junk and was
    /// billed, its repair was rate limited, and the ladder's retry answered and asked for source: the
    /// malformed launch rode forward on the retry's outcome as that turn's earlier launch — and the loop
    /// wrote down only the retry's own usage, so turn 1's ledger line and the round total lost it
    /// (found by epic 3's code round, 2026-09-26).
    /// </summary>
    [Fact]
    public async Task AContinuingTurnsEarlierLaunches_AreOnItsLedgerLine_AndInTheRoundTotal_Once()
    {
        Turn(1, AsksForAdd, stdout: OneThousandIn);
        Turn(2, Clean, stdout: TenIn);
        var launches = new TwoDecidedThenReal(_launcher);
        var work = Work();

        var results = await new BoundedScheduler(retryLadder: [TimeSpan.FromMilliseconds(1)])
            .RunAllAsync([work], new ReviewerExecutor(launches), TestContext.Current.CancellationToken);

        var ok = results.Single().Outcome.Should().BeOfType<ReviewerOutcome.Ok>().Subject;
        ok.Turns.Should().Be(2);
        launches.Count.Should().Be(4, "turn 1's malformed launch, its refused repair, the ladder's retry, then turn 2");
        var first = ok.EarlierTurns.Should().ContainSingle().Subject;
        first.Usage.TokensIn.Should().Be(1700, "turn 1 is its malformed launch (700) AND the retry that answered (1000)");
        ok.LastTurnUsage.TokensIn.Should().Be(10, "turn 2 had one launch");
        ok.TotalUsage.TokensIn.Should().Be(1710, "every launch of every turn, each exactly once");

        var ledger = new UsageLedger(_dir.At("ledger-continuing"));
        ledger.Record(work.Invocation, ok, "m", "FeatureReview", TimeSpan.FromSeconds(4));
        var lines = File.ReadAllLines(ledger.Path);
        lines.Should().HaveCount(2, "one ledger line per turn");
        lines[0].Should().Contain("\"tokensIn\":1700", "turn 1's line carries every launch of turn 1");
        lines[1].Should().Contain("\"tokensIn\":10");

        var live = new LiveRound(new SessionStore(_dir.At("store-continuing")), new PersistedSession(new SessionState("s", "D:/r", "main", new PanelConfig()), []), 1, [work], "", Noticing.None);
        live.Finish("proceed", 0, "all 1 reviewers answered", [(work.Invocation, ok)]).TokensIn.Should().Be(1710, "the round total is the same sum");
    }

    /// <summary>
    /// Decides the first two launches without running them — a billed launch with no answer file (so it is
    /// malformed), then a refused repair — and hands every later launch to the fake CLI. Counted in order,
    /// which is sound because one reviewer's launches never overlap.
    /// </summary>
    private sealed class TwoDecidedThenReal(IProcessLauncher inner) : IProcessLauncher
    {
        private int _count;

        public int Count => _count;

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default) =>
            Interlocked.Increment(ref _count) switch
            {
                1 => Task.FromResult(new ProcessResult(0, SevenHundredIn, string.Empty, TimedOut: false)),
                2 => Task.FromResult(new ProcessResult(1, string.Empty, "429 Too Many Requests", TimedOut: false)),
                _ => inner.RunAsync(request, ct),
            };
    }

    /// <summary>
    /// The cap on the WHOLE conversation — <c>reviewerTimeout × (1 + follow-ups)</c> — driven by a
    /// continuation that never stops on its own, since the feature stage's stops at the cap itself.
    /// </summary>
    [Fact]
    public async Task AConversationThatOutlivesItsCap_IsOneTerminalTimeout_WithEveryTurnsUsageKept()
    {
        Environment.SetEnvironmentVariable("FAKECLI_STDOUT", """{"findings": [], "input_tokens": 7, "output_tokens": 1}""");
        Environment.SetEnvironmentVariable("FAKECLI_SLEEP_MS", "400");
        var turn = new ReviewerWork(FakeCliInvocations.Invoke("gemini", [], timeout: TimeSpan.FromMilliseconds(700)));
        var work = turn with { Continue = new Endless(turn, followUps: 1) };
        var progress = new List<ReviewerProgress>();

        var results = await new BoundedScheduler().RunAllAsync([work], _executor, TestContext.Current.CancellationToken, p => { lock (progress) { progress.Add(p); } });

        var outcome = results.Single().Outcome;
        outcome.Should().BeOfType<ReviewerOutcome.TimedOut>("a conversation past 700 ms × 2 is ended by the cap, as one failed reviewer");
        outcome.EarlierTurns.Should().NotBeEmpty("the turns that answered before the cap are kept").And.OnlyContain(t => t.Usage.TokensIn == 7);
        progress.Count(p => p.Outcome is not null).Should().Be(1, "one terminal outcome");
    }

    /// <summary>
    /// A cap set on the WORK itself — the feature stage's whole-review limit for an api reviewer (the
    /// operator's 20 minutes, 2026-09-27: every turn of one reviewer's conversation, launch to final
    /// answer) — ends the conversation before the derived <c>timeout × (1 + follow-ups)</c> would.
    /// </summary>
    [Fact]
    public async Task AConversationCapOnTheWork_EndsTheConversation_BeforeTheDerivedOneWould()
    {
        Environment.SetEnvironmentVariable("FAKECLI_STDOUT", """{"findings": [], "input_tokens": 7, "output_tokens": 1}""");
        Environment.SetEnvironmentVariable("FAKECLI_SLEEP_MS", "300");
        var turn = new ReviewerWork(FakeCliInvocations.Invoke("gemini", [], timeout: TimeSpan.FromSeconds(2)));
        var work = turn with { Continue = new Endless(turn, followUps: 3), ConversationCap = TimeSpan.FromMilliseconds(900) };

        var results = await new BoundedScheduler().RunAllAsync([work], _executor, TestContext.Current.CancellationToken);

        var outcome = results.Single().Outcome;
        outcome.Should().BeOfType<ReviewerOutcome.TimedOut>("the work's own 900 ms cap ended it; the derived 2 s × 4 never came into it");
        outcome.EarlierTurns.Count.Should().BeInRange(1, 3, "at ~300 ms a turn, 900 ms allows two or three turns — never the twenty the derived cap would");
    }

    /// <summary>A continuation that always asks for another turn — the same launch again.</summary>
    private sealed class Endless(ReviewerWork again, int followUps) : IReviewerContinuation
    {
        public int FollowUps => followUps;

        public Task<TurnDecision> AfterAsync(int turn, ReviewerOutcome.Ok answered, CancellationToken ct) =>
            Task.FromResult<TurnDecision>(new TurnDecision.Next(again with { Continue = this }, $"turn {turn + 1}: again"));
    }

    [Fact]
    public async Task TwoTurnReviewers_DoNotRaiseThePerProviderPeak()
    {
        Turn(1, AsksForAdd);
        Turn(2, Clean);
        var scheduler = new BoundedScheduler(globalCap: 3, perProviderCap: 2);

        var results = await scheduler.RunAllAsync([Work(), Work(), Work()], _executor, TestContext.Current.CancellationToken);

        scheduler.PeakPerProvider["codex"].Should().BeLessThanOrEqualTo(2, "a reviewer's slot is held across its turns, never taken twice");
        results.Should().OnlyContain(r => r.Outcome is ReviewerOutcome.Ok && ((ReviewerOutcome.Ok)r.Outcome).Turns == 2);
    }

    // ---------- a resolver that cannot answer is a refusal, never a crash ----------

    [Fact]
    public async Task AResolverThatTimesOut_RefusesThatRequest_AndTheTurnGoesOnWithTheRest()
    {
        var slow = new SlowShow(_launcher, TimeSpan.FromSeconds(5), "src/Big.cs");
        var resolver = new SourceResolver(new GitHistory(slow), new TreeSitterOutliner(), _git.Path, _head, readDeadline: TimeSpan.FromMilliseconds(300));
        Turn(1, """{"findings": [], "notes": "w", "sourceRequests": [{"file": "src/Big.cs", "symbol": null, "startLine": 1, "endLine": 3, "why": "a"}, {"file": "src/Cart.cs", "symbol": "Cart.Add", "startLine": null, "endLine": null, "why": "b"}]}""");
        Turn(2, Clean);

        var (outcome, _) = await RunAsync(Work(resolver: resolver), ct: TestContext.Current.CancellationToken);

        var tail = Prompts()[1];
        tail.Should().Contain("not served: src/Big.cs — timed out", "a slow git show is that request's refusal, said as such");
        tail.Should().Contain("### src/Cart.cs lines 28-35", "the turn goes on with what was served");
        outcome.Should().BeOfType<ReviewerOutcome.Ok>().Which.Turns.Should().Be(2);
    }

    /// <summary>The real launcher, with `git show` of one path delayed past the resolver's deadline.</summary>
    private sealed class SlowShow(IProcessLauncher inner, TimeSpan delay, string path) : IProcessLauncher
    {
        public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            if (request.Arguments.Count > 1 && request.Arguments[0] == "show" && request.Arguments[1].EndsWith(":" + path, StringComparison.Ordinal))
            {
                await Task.Delay(delay, ct);
            }

            return await inner.RunAsync(request, ct);
        }
    }
}
