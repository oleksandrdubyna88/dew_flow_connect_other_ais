using System.Diagnostics;
using System.Text.Json;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog.Core;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A LOCAL review line that ends <c>Cancelled</c> says which token killed it — E3.S2 of
/// <c>todo/PLAN_the_round_counter_tail_E3.md</c>. The cause is read from the tokens the engine holds (the host's stopping token, the
/// caller's, the round's own clock), never from the failure's sentence, and it sits on the turn's LAST line only.
/// </summary>
/// <remarks>
/// <para>Driven through the real <see cref="PanelService"/> and the real git; only the reviewer's process is played by the test
/// (<see cref="Reviewers"/>), so "blocks until its token fires" costs no child process and no sleep.</para>
/// <para>The cause is the FIRST source to fire (code round of 2026-10-21; <see cref="RoundCancellation"/>): a stopping host may cancel the
/// in-flight request too, and whichever token fired first is the one that ended the turn — the later one, even before the line was written, did
/// not. Only tokens already fired when the round began have no order, and those follow D5: <c>hostStopped</c> over <c>withdrawn</c> over
/// <c>roundClock</c>. <c>unknown</c> is a cancelled turn when none fired.</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class ALocalCancellationCarriesItsSourceTests : IAsyncLifetime
{
    private const string Clean = """{"findings": []}""";

    private readonly ProcessLauncher _git = new();
    private string _repo = string.Empty;
    private string _data = string.Empty;

    private static string FakeCliExe => Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli");

    public async ValueTask InitializeAsync()
    {
        _repo = Directory.CreateTempSubdirectory("coai-cause-repo-").FullName;
        _data = Directory.CreateTempSubdirectory("coai-cause-data-").FullName;
        await Git("init", "-b", "main");
        await File.WriteAllTextAsync(Path.Combine(_repo, "app.cs"), "v1\n");
        await Git("add", ".");
        await Git("commit", "-m", "base");
        await Git("checkout", "-b", "feature");
        Environment.SetEnvironmentVariable("FAKECLI_MODE", "vendor");
    }

    public ValueTask DisposeAsync()
    {
        foreach (var name in (string[])["FAKECLI_MODE", "FAKECLI_STDOUT", "FAKECLI_OUTFILE_TEXT", "FAKECLI_EXIT", "FAKECLI_STDERR"])
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var dir in (string[])[_repo, _data])
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }

        return ValueTask.CompletedTask;
    }

    private async Task Git(params string[] args)
    {
        var result = await _git.RunAsync(new ProcessRequest(
            "git", ["-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", .. args], _repo));
        result.ExitCode.Should().Be(0, $"git {string.Join(' ', args)}: {result.StdErr}");
    }

    /// <summary>How the played reviewer answers each launch.</summary>
    public enum Play
    {
        /// <summary>Holds until the token it was handed fires, then reports the kill as a caller's cancellation.</summary>
        BlockUntilCancelled,

        /// <summary>
        /// Holds until its token fires, then keeps the answer back until the test lets go — so a second token can fire while the
        /// reviewer is already dead, which is the order the cause has to be read in.
        /// </summary>
        BlockThenHoldTheAnswer,

        /// <summary>Reports a cancellation at once, with no token having fired — a remote row's server ended it.</summary>
        CancelledWithNoTokenFired,

        /// <summary>Reports its own budget ran out.</summary>
        TimedOut,

        /// <summary>The vendor refuses with a usage limit.</summary>
        RateLimited,

        /// <summary>The real fake CLI answers a clean review.</summary>
        Answer,

        /// <summary>The first launch of a reviewer answers something unreadable (so it is repaired), every later one blocks.</summary>
        MalformedThenBlock,
    }

    /// <summary>git through the real launcher; the codex row's launches played as <see cref="Play"/> says.</summary>
    private sealed class Reviewers(IProcessLauncher inner, Play play) : IProcessLauncher
    {
        /// <summary>Completed when the first reviewer launch is under way — the moment a test may cancel.</summary>
        public TaskCompletionSource Arrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Completed when a held reviewer has seen its token fire (<see cref="Play.BlockThenHoldTheAnswer"/>).</summary>
        public TaskCompletionSource Killed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>The test completes this to let a held reviewer report.</summary>
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            if (!string.Equals(request.Executable, FakeCliExe, StringComparison.OrdinalIgnoreCase))
            {
                return await inner.RunAsync(request, ct);
            }

            if (request.Arguments is ["--version"])
            {
                return new ProcessResult(0, "codex-cli 0.131.0\n", string.Empty, TimedOut: false);
            }

            // A repair is the executor's second launch of the same reviewer, told apart by the instruction it appends to the prompt.
            var isRepair = request.StdIn.Contains(RepairInstruction.Text, StringComparison.Ordinal);
            Arrived.TrySetResult();

            return play switch
            {
                Play.Answer => await inner.RunAsync(request, ct),
                Play.TimedOut => new ProcessResult(-1, string.Empty, string.Empty, TimedOut: true),
                Play.RateLimited => new ProcessResult(1, string.Empty, "You've hit your usage limit.", TimedOut: false),
                Play.CancelledWithNoTokenFired => new ProcessResult(-1, string.Empty, string.Empty, TimedOut: true, Cancelled: true),
                Play.MalformedThenBlock when !isRepair => await inner.RunAsync(request, ct),
                Play.BlockThenHoldTheAnswer => await HeldAsync(ct),
                _ => await BlockedAsync(ct),
            };
        }

        private async Task<ProcessResult> HeldAsync(CancellationToken ct)
        {
            var result = await BlockedAsync(ct);
            Killed.TrySetResult();
            await Release.Task;

            return result;
        }

        private static async Task<ProcessResult> BlockedAsync(CancellationToken ct)
        {
            try
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
            catch (OperationCanceledException)
            {
                // The launcher's own answer for a kill the caller's token ordered.
            }

            return new ProcessResult(-1, string.Empty, string.Empty, TimedOut: true, Cancelled: true);
        }
    }

    private PanelService Service(IProcessLauncher launcher, TimeSpan roundTimeout = default, CancellationToken hostStopping = default) => new(
        new PanelSettings
        {
            Providers = [new("codex") { ExecutablePath = FakeCliExe }],
            Rounds = PanelConfig.Uniform(3, 2, StagePolicy.Human),
            DataDir = _data,
            ReviewerTimeout = TimeSpan.FromSeconds(30),
            RateLimitBackoff = TimeSpan.FromMilliseconds(5),
            RoundTimeout = roundTimeout,
        },
        VaultKeys.None("no vault in tests"), default, launcher, Logger.None, Noticing.None, hostStopping: hostStopping);

    private static void Script(string answer)
    {
        Environment.SetEnvironmentVariable("FAKECLI_STDOUT", answer);
        Environment.SetEnvironmentVariable("FAKECLI_OUTFILE_TEXT", answer);
        Environment.SetEnvironmentVariable("FAKECLI_EXIT", "0");
    }

    private async Task<string> RoundAsync(PanelService service, CancellationToken ct = default)
    {
        await service.OpenAsync(_repo, "feature");

        return await service.ReviewPlanAsync(_repo, "feature", "a plan", ct);
    }

    private static string Text(JsonElement line, string name) =>
        line.TryGetProperty(name, out var value) ? value.GetString() ?? string.Empty : string.Empty;

    private List<JsonElement> Lines() =>
        [.. File.ReadAllLines(Path.Combine(_data, "usage.jsonl")).Select(l => JsonDocument.Parse(l).RootElement.Clone())];

    private List<string> CausesOfCancelledLines() =>
        [.. Lines().Where(l => Text(l, "outcome") != "ok" && Text(l, "kind") != "chat").Select(l => Text(l, "endCause"))];

    private static long Now() => Stopwatch.GetTimestamp();

    [Fact]
    public void NothingFired_IsUnknown_AndTheFirstToFireIsTheCause()
    {
        using var host = new CancellationTokenSource();
        using var call = new CancellationTokenSource();
        using var round = new RoundCancellation(host.Token, call.Token, TimeSpan.FromMinutes(5));
        round.CauseAt(Now()).Should().Be(EndCauses.Unknown, "no token fired");

        call.Cancel();
        host.Cancel();

        round.Token.IsCancellationRequested.Should().BeTrue("the caller's token ends the round");
        round.CauseAt(Now()).Should().Be(EndCauses.Withdrawn, "the call's token fired FIRST: the host's stop that came after it did not make it a host stop");
    }

    [Fact]
    public void TheHostFirst_IsHostStopped_EvenWhenTheCallIsCancelledWithIt()
    {
        using var host = new CancellationTokenSource();
        using var call = new CancellationTokenSource();
        using var round = new RoundCancellation(host.Token, call.Token, TimeSpan.FromMinutes(5));

        host.Cancel();
        call.Cancel();

        round.CauseAt(Now()).Should().Be(EndCauses.HostStopped);
    }

    [Fact]
    public void WhenTheClockFiresBeforeTheCaller_TheCauseIsTheClock()
    {
        using var call = new CancellationTokenSource();
        using var round = new RoundCancellation(CancellationToken.None, call.Token, TimeSpan.FromMilliseconds(30));
        round.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(30)).Should().BeTrue("the 30 ms clock fires");

        call.Cancel();

        round.CauseAt(Now()).Should().Be(EndCauses.RoundClock, "the clock fired first; the person came after it");
    }

    [Fact]
    public void ACancellationThatLandedBeforeAnyTokenFired_IsUnknown_EvenIfTheClockFiresAfterwards()
    {
        // Taken BEFORE the round exists: a pause longer than the 30 ms clock between the two would otherwise land the outcome after it.
        var landed = Now();
        using var round = new RoundCancellation(CancellationToken.None, CancellationToken.None, TimeSpan.FromMilliseconds(30));
        round.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(30)).Should().BeTrue("the 30 ms clock fires after the outcome landed");

        round.CauseAt(landed).Should().Be(EndCauses.Unknown,
            "the reviewer ended Cancelled for its own reason before any token fired; a clock that fires later did not end it");
        round.CauseAt(Now()).Should().Be(EndCauses.RoundClock, "an outcome that lands after the clock fired is the clock's");
    }

    [Fact]
    public void TheHostStoppingAlone_EndsNoRound_SoItIsNotACause()
    {
        using var host = new CancellationTokenSource();
        using var round = new RoundCancellation(host.Token, CancellationToken.None, TimeSpan.FromMinutes(5));

        host.Cancel();

        round.Token.IsCancellationRequested.Should().BeFalse("the host's stop cancels nothing of the round: the round keeps running");
        round.CauseAt(Now()).Should().Be(EndCauses.Unknown, "a stop that ended nothing is not why anything ended");
    }

    [Fact]
    public void WhenOnlyTheHostFired_AndTheClockThenEndsTheRound_TheCauseIsTheClock()
    {
        // The live shape: no review tool passes the request token, so a client closing stdin fires the host's token and nothing else. The
        // round goes on; when its own clock then ends a reviewer, the clock is why — the host stop ended nothing.
        using var host = new CancellationTokenSource();
        using var round = new RoundCancellation(host.Token, CancellationToken.None, TimeSpan.FromMilliseconds(30));
        host.Cancel();
        round.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(30)).Should().BeTrue("the 30 ms clock ends the round");

        round.CauseAt(Now()).Should().Be(EndCauses.RoundClock, "the clock ended the round; a host stop that merely preceded it did not");
    }

    [Fact]
    public void TokensThatHadAlreadyFiredWhenTheRoundBegan_FollowThePrecedence()
    {
        using var host = new CancellationTokenSource();
        using var call = new CancellationTokenSource();
        host.Cancel();
        call.Cancel();
        using var round = new RoundCancellation(host.Token, call.Token, TimeSpan.FromMinutes(5));

        round.CauseAt(Now()).Should().Be(EndCauses.HostStopped,
            "tokens already fired when the round began have no order between them: the host's stop wins, then a withdrawal");
    }

    [Fact]
    public void TheLocalWords_AreKnown_AndTheServersAreNotMadeLocal()
    {
        EndCauses.LocalOnly.Should().BeEquivalentTo([EndCauses.RoundClock, EndCauses.Unknown]);
        EndCauses.Known.Should().HaveCount(7).And.Contain(EndCauses.LocalOnly);
    }

    [Fact]
    public async Task ALocalCancellation_CarriesItsSource_TheRoundsOwnClock()
    {
        var reviewers = new Reviewers(_git, Play.BlockUntilCancelled);

        await RoundAsync(Service(reviewers, roundTimeout: TimeSpan.FromSeconds(8)));

        CausesOfCancelledLines().Should().NotBeEmpty().And.OnlyContain(c => c == EndCauses.RoundClock,
            "only the round's own deadline fired: the caller never cancelled and the host never stopped");
    }

    [Fact]
    public async Task ALocalCancellation_CarriesItsSource_TheCallsToken()
    {
        var reviewers = new Reviewers(_git, Play.BlockUntilCancelled);
        using var call = new CancellationTokenSource();
        var service = Service(reviewers);
        var round = RoundAsync(service, call.Token);
        await reviewers.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);
        await call.CancelAsync();
        await round;

        CausesOfCancelledLines().Should().NotBeEmpty().And.OnlyContain(c => c == EndCauses.Withdrawn, "a person (the MCP client) withdrew the call");
    }

    [Fact]
    public async Task ALocalCancellation_CarriesItsSource_TheHostsStop_EvenWhenTheCallIsCancelledWithIt()
    {
        var reviewers = new Reviewers(_git, Play.BlockUntilCancelled);
        using var host = new CancellationTokenSource();
        using var call = new CancellationTokenSource();
        var round = RoundAsync(Service(reviewers, hostStopping: host.Token), call.Token);
        await reviewers.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);
        // A stopping host cancels the request it was serving as well: that is the host's doing, not a withdrawal.
        await host.CancelAsync();
        await call.CancelAsync();
        await round;

        CausesOfCancelledLines().Should().NotBeEmpty().And.OnlyContain(c => c == EndCauses.HostStopped);
    }

    [Fact]
    public async Task TheCallCancelledFirst_IsWithdrawn_EvenIfTheHostStopsBeforeTheReviewerReports()
    {
        var reviewers = new Reviewers(_git, Play.BlockThenHoldTheAnswer);
        using var host = new CancellationTokenSource();
        using var call = new CancellationTokenSource();
        var round = RoundAsync(Service(reviewers, hostStopping: host.Token), call.Token);
        await reviewers.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);
        await call.CancelAsync();
        await reviewers.Killed.Task.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);
        await host.CancelAsync();
        reviewers.Release.TrySetResult();
        await round;

        CausesOfCancelledLines().Should().NotBeEmpty().And.OnlyContain(c => c == EndCauses.Withdrawn,
            "the call's token killed the reviewer; the host stopping afterwards, before the line was written, did not");
    }

    [Fact]
    public async Task TheClockFiringFirst_IsTheRoundClock_EvenIfTheCallIsCancelledBeforeTheReviewerReports()
    {
        var reviewers = new Reviewers(_git, Play.BlockThenHoldTheAnswer);
        using var call = new CancellationTokenSource();
        var round = RoundAsync(Service(reviewers, roundTimeout: TimeSpan.FromSeconds(8)), call.Token);
        await reviewers.Killed.Task.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);
        await call.CancelAsync();
        reviewers.Release.TrySetResult();
        await round;

        CausesOfCancelledLines().Should().NotBeEmpty().And.OnlyContain(c => c == EndCauses.RoundClock,
            "the round's clock killed the reviewer; the person cancelling afterwards, before the line was written, did not");
    }

    [Fact]
    public async Task TheClockFiringFirst_IsSaidInTheSentenceToo_EvenIfTheCallIsCancelledBeforeTheReviewerReports()
    {
        // The ledger said roundClock but the sentence read "the caller did not cancel": two answers to one question.
        var reviewers = new Reviewers(_git, Play.BlockThenHoldTheAnswer);
        using var call = new CancellationTokenSource();
        var round = RoundAsync(Service(reviewers, roundTimeout: TimeSpan.FromSeconds(8)), call.Token);
        await reviewers.Killed.Task.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);
        await call.CancelAsync();
        reviewers.Release.TrySetResult();
        var reply = await round;

        CausesOfCancelledLines().Should().NotBeEmpty().And.OnlyContain(c => c == EndCauses.RoundClock);
        reply.Should().Contain("minute limit", "the clock fired first, so the deadline sentence and the ledger's cause agree");
    }

    [Fact]
    public async Task TheCallCancelledFirst_IsNotSaidToBeTheDeadline_EvenIfTheClockFiresBeforeTheReviewerReports()
    {
        var reviewers = new Reviewers(_git, Play.BlockThenHoldTheAnswer);
        using var call = new CancellationTokenSource();
        var round = RoundAsync(Service(reviewers, roundTimeout: TimeSpan.FromSeconds(8)), call.Token);
        await reviewers.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);
        await call.CancelAsync();
        await reviewers.Killed.Task.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);
        await Task.Delay(TimeSpan.FromSeconds(9), TestContext.Current.CancellationToken);
        reviewers.Release.TrySetResult();
        var reply = await round;

        CausesOfCancelledLines().Should().NotBeEmpty().And.OnlyContain(c => c == EndCauses.Withdrawn);
        reply.Should().NotContain("minute limit", "a person ended the round first; the clock striking afterwards is not why");
    }

    [Fact]
    public async Task ACancellationNobodyCanName_IsUnknown_NeverEmpty()
    {
        await RoundAsync(Service(new Reviewers(_git, Play.CancelledWithNoTokenFired)));

        CausesOfCancelledLines().Should().NotBeEmpty().And.OnlyContain(c => c == EndCauses.Unknown,
            "a cancellation whose token nobody above the executor can name is said so, never left blank");
    }

    [Fact]
    public async Task OnlyTheTurnsLastLine_CarriesTheCause()
    {
        Script("this is not JSON, so the review is repaired");
        var reviewers = new Reviewers(_git, Play.MalformedThenBlock);

        await RoundAsync(Service(reviewers, roundTimeout: TimeSpan.FromSeconds(10)));

        var turns = Lines().GroupBy(l => Text(l, "turn")).Where(g => g.Count() > 1).ToList();
        turns.Should().NotBeEmpty("a malformed first launch is repaired, so a turn has two lines");
        foreach (var turn in turns)
        {
            turn.SkipLast(1).Select(l => Text(l, "endCause")).Should().OnlyContain(c => c.Length == 0, "only the turn's last line says why it was cancelled");
            Text(turn.Last(), "endCause").Should().Be(EndCauses.RoundClock, string.Join(" // ", turn.Select(l => $"{Text(l, "attempt")} | {Text(l, "outcome")} | {Text(l, "endCause")}")));
        }
    }

    [Theory]
    [InlineData(Play.Answer)]
    [InlineData(Play.TimedOut)]
    [InlineData(Play.RateLimited)]
    public async Task ATurnThatWasNotCancelled_CarriesNoCause(Play play)
    {
        Script(Clean);

        await RoundAsync(Service(new Reviewers(_git, play)));

        Lines().Should().NotBeEmpty().And.OnlyContain(l => Text(l, "endCause").Length == 0);
    }
}
