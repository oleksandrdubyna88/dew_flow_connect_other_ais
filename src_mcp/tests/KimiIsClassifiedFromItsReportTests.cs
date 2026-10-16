using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Acp;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A Kimi failure is classified from the shim's REPORT — its exit and its structured <c>failure</c> field — never from text (S4.5–S4.8 of
/// <c>todo/PLAN_vibe_and_kimi_runtimes_E4.md</c>, the risk consultation <c>ba5f661a</c>). The shim prints the whole trace of a turn on stdout, file
/// paths and tool titles included, and a path named <c>quota.cs</c> is not a rate limit. Through the real executor AND the real scheduler.
/// </summary>
public sealed class KimiIsClassifiedFromItsReportTests : IDisposable
{
    private readonly TempDir _dir = TempDir.For("testsToDelete-kimi-classify-");

    public void Dispose() => _dir.Dispose();

    private static BoundedScheduler Scheduler() => new(retryLadder: [TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1)]);

    private static KimiRuntime Kimi() => new("kimi", HostKind.Windows, new KimiSeams(
        root => new KimiPlaces(root, "persons", "persons-bin", [], string.Empty, "kimi.exe"), () => ("coai-mcp.exe", [])));

    private ReviewerInvocation Invocation(IReviewerRuntime? adapter = null) =>
        new("kimi", RoleCatalog.ArchitectureRole, new ProcessRequest("coai-mcp.exe", ["--ask-acp"], _dir) { Timeout = TimeSpan.FromSeconds(30) }, string.Empty, adapter ?? new KimiReadSide(Kimi()), Model: "kimi-code/k3");

    private async Task<ReviewerOutcome> ThroughTheSchedulerAsync(ScriptedKimi launcher) =>
        (await Scheduler().RunAllAsync([new ReviewerWork(Invocation())], new ReviewerExecutor(launcher), TestContext.Current.CancellationToken)).Single().Outcome;

    private async Task<ReviewerOutcome> ThroughTheExecutorAsync(ScriptedKimi launcher) =>
        await new ReviewerExecutor(launcher).RunAsync(Invocation(), ct: TestContext.Current.CancellationToken);

    private static ProcessResult Spent() => KimiShimScripts.Ended(
        AcpWords.SpentExit, AcpEnd.AgentFailed, "provider.api_error: 429 Your account is suspended due to insufficient balance",
        failure: "balance_spent", stderr: "balance_spent: provider.api_error: 429 Your account is suspended due to insufficient balance — the Kimi allowance is spent (D14)");

    // ---------- S4.5 ----------

    [Fact]
    public async Task ASpentBalance_IsOneLaunch_NamedSpent_ThroughTheExecutor()
    {
        var launcher = new ScriptedKimi(_ => Spent());

        var outcome = await ThroughTheExecutorAsync(launcher);

        var limited = outcome.Should().BeOfType<ReviewerOutcome.RateLimited>().Subject;
        RateLimit.Hopeless(limited.Reason).Should().BeTrue("the reason names a spent allowance, so the ladder stops (D14): " + limited.Reason);
        outcome.LastTurnUsage.Should().Be(Usage.Unknown, "a spent launch keeps `notCaptured` rather than a fabricated zero");
        launcher.Shims.Should().Be(1);
    }

    [Fact]
    public async Task ASpentBalance_IsOneLaunch_NamedSpent_ThroughTheScheduler()
    {
        var launcher = new ScriptedKimi(_ => Spent());

        var outcome = await ThroughTheSchedulerAsync(launcher);

        var limited = outcome.Should().BeOfType<ReviewerOutcome.RateLimited>().Subject;
        RateLimit.Hopeless(limited.Reason).Should().BeTrue(limited.Reason);
        limited.Attempts.Should().Be(1);
        launcher.Shims.Should().Be(1, "a spent allowance is not climbed: the ladder never starts");
        outcome.LastTurnUsage.Should().Be(Usage.Unknown);
    }

    // ---------- S4.6 ----------

    private static ProcessResult SignInRefusalWhoseTraceSaysQuota() => KimiShimScripts.Ended(
        77, AcpEnd.AgentFailed, "Authentication required", failure: "not_signed_in",
        stderr: "not_signed_in: Authentication required — run `kimi login` in a terminal and try again",
        reads: [new AcpReadRecord("src/quota.cs", Served: true, string.Empty)],
        calls: [new AcpToolCall("t1", "check rate limit", "execute", "completed")]);

    [Fact]
    public async Task ASignInRefusalWhoseTraceSaysQuota_IsNotARateLimit_ThroughTheExecutor()
    {
        var launcher = new ScriptedKimi(_ => SignInRefusalWhoseTraceSaysQuota());

        var outcome = await ThroughTheExecutorAsync(launcher);

        outcome.Should().BeOfType<ReviewerOutcome.NonZeroExit>().Which.ExitCode.Should().Be(77);
        ReviewerSummaryFactory.Describe(outcome).Should().Contain("kimi login", "the shim's own failure name is diagnosed");
        launcher.Shims.Should().Be(1);
    }

    [Fact]
    public async Task ASignInRefusalWhoseTraceSaysQuota_IsNotARateLimit_ThroughTheScheduler()
    {
        var launcher = new ScriptedKimi(_ => SignInRefusalWhoseTraceSaysQuota());

        var outcome = await ThroughTheSchedulerAsync(launcher);

        outcome.Should().BeOfType<ReviewerOutcome.NonZeroExit>().Which.ExitCode.Should().Be(77, "never RateLimited, never retried up the ladder");
        launcher.Shims.Should().Be(1);
    }

    // ---------- S4.7 ----------

    [Fact]
    public async Task ACleanSignInRefusal_IsOneLaunch()
    {
        var launcher = new ScriptedKimi(_ => KimiShimScripts.Ended(77, AcpEnd.AgentFailed, "Authentication required", failure: "not_signed_in", stderr: "not_signed_in: Authentication required — run `kimi login`"));

        var outcome = await ThroughTheSchedulerAsync(launcher);

        outcome.Should().BeOfType<ReviewerOutcome.NonZeroExit>().Which.ExitCode.Should().Be(77);
        launcher.Shims.Should().Be(1);
    }

    // ---------- S4.8 ----------

    [Fact]
    public async Task EveryOtherAdapter_IsClassifiedAsBefore()
    {
        var launcher = new ScriptedKimi(_ => new ProcessResult(1, string.Empty, "Error: rate limit exceeded", TimedOut: false));
        var invocation = Invocation(new PlainAdapter("codex")) with { Provider = "codex" };

        var outcome = await new ReviewerExecutor(launcher).RunAsync(invocation, ct: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ReviewerOutcome.RateLimited>().Which.Reason.Should().Contain("rate limit");
        outcome.LastTurnUsage.Should().Be(Usage.None, "the default body of the seam is today's: no usage on a limit read off text");
    }

    // ---------- the other exits the shim documents ----------

    [Fact]
    public async Task AnotherKimiHoldingTheSignIn_IsExit69_AndSaysSo_NeverARateLimit()
    {
        const string busy = "another Kimi started on this sign-in is still running (C:\\locks\\k.tree.lock is held) — it ends when its agent does; the turn is given up";
        var launcher = new ScriptedKimi(_ => KimiShimScripts.Ended(69, AcpEnd.NotStarted, busy, stderr: busy));

        var outcome = await ThroughTheExecutorAsync(launcher);

        var failed = outcome.Should().BeOfType<ReviewerOutcome.NonZeroExit>().Subject;
        failed.ExitCode.Should().Be(69);
        ReviewerSummaryFactory.Describe(outcome).Should().Contain("still running");
        failed.Usage.Should().Be(Usage.None, "an agent that never started consumed nothing");
    }

    [Fact]
    public async Task AModelTheSessionDoesNotOffer_IsExit65_NamedInCoaisWords()
    {
        const string said = "the agent's session offers kimi-code/k3-256k, kimi-code/k3 for 'model'; 'kimi-code/kimi-for-coding' is not one of them";
        var launcher = new ScriptedKimi(_ => KimiShimScripts.Ended(65, AcpEnd.ModelNotOffered, said, stderr: said));

        var outcome = await ThroughTheExecutorAsync(launcher);

        outcome.Should().BeOfType<ReviewerOutcome.NonZeroExit>().Which.ExitCode.Should().Be(65);
        ReviewerSummaryFactory.Describe(outcome).Should().Contain("does not offer this model");
    }

    [Fact]
    public async Task AnAnsweredReport_IsAnAnswer_WithItsTokens()
    {
        var launcher = new ScriptedKimi(_ => KimiShimScripts.Answered());

        var outcome = await ThroughTheExecutorAsync(launcher);

        outcome.Should().BeOfType<ReviewerOutcome.Ok>().Which.Usage.Should().Be(new Usage(19973, 31, null));
    }
}
