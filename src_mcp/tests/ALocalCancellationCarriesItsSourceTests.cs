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
/// <para>Decision D5: <c>hostStopped</c> over <c>withdrawn</c> over <c>roundClock</c> over <c>unknown</c>. A stopping host may cancel the
/// in-flight request too, so the call's token firing as well must not make the host's stop a person's withdrawal.</para>
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
                _ => await BlockedAsync(ct),
            };
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

    [Theory]
    [InlineData(true, true, true, EndCauses.HostStopped)]
    [InlineData(true, true, false, EndCauses.HostStopped)]
    [InlineData(true, false, true, EndCauses.HostStopped)]
    [InlineData(true, false, false, EndCauses.HostStopped)]
    [InlineData(false, true, true, EndCauses.Withdrawn)]
    [InlineData(false, true, false, EndCauses.Withdrawn)]
    [InlineData(false, false, true, EndCauses.RoundClock)]
    [InlineData(false, false, false, EndCauses.Unknown)]
    public void TheCause_IsReadFromWhichTokenFired(bool host, bool caller, bool clock, string cause)
    {
        EndCauses.OfLocal(host, caller, clock).Should().Be(cause);
        EndCauses.Known.Should().Contain(cause);
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
