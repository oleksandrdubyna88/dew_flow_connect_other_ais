using System.Diagnostics;
using CoaiMcp.Core.Context;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Context;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog.Core;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// <see cref="RoundSizer"/>'s time (E2 code gate): the independent measurements of one sizing run side by side, and the whole
/// sizing answers within ONE deadline of its own — never the round's token — after which the unfinished scopes read
/// <c>unavailable: git failed</c> and the round goes on. A cancellation of the ROUND still propagates.
/// </summary>
public sealed class TheSizingIsConcurrentAndBoundedTests : IAsyncLifetime
{
    private readonly ProcessLauncher _git = new();
    private string _repo = string.Empty;
    private string _first = string.Empty;
    private string _sha = string.Empty;
    private CollectedDiff _collected = null!;
    private ShapedDiff _shaped = null!;
    private List<RoundRecord> _earlier = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _repo = Directory.CreateTempSubdirectory("coai-sizing-").FullName;
        await Run("init", "-b", "main");
        await Write("app.cs", "v1\n");
        await Commit("base");
        await Run("checkout", "-b", "feature");
        await Write("app.cs", string.Concat(Enumerable.Repeat("line\n", 100)));
        await Commit("the feature");
        _first = await Run("rev-parse", "HEAD");
        await Write("app.cs", string.Concat(Enumerable.Repeat("line\n", 100)) + "the fix\n");
        await Commit("the fix");
        _sha = await Run("rev-parse", "HEAD");
        _collected = await new ContextAssembler(_git).CollectAsync(_repo, "main", _sha, ct: Ct);
        _shaped = DiffShaper.Shape(_collected.Files);
        _earlier =
        [
            new RoundRecord(nameof(Stage.CodeReview), 1, "proceed", 0, "ok", DateTime.UtcNow)
            {
                Status = RoundRecord.Done,
                Sha = _first,
                Size = new RoundSize(ScopeSize.ObservedZero(_collected.ComparedAgainst), ScopeSize.Unavailable(ScopeState.FirstRound)),
            },
        ];
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            Directory.Delete(_repo, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A leftover temp directory is not a failing test (git objects are read-only on Windows).
        }

        return ValueTask.CompletedTask;
    }

    private async Task<string> Run(params string[] args)
    {
        var result = await _git.RunAsync(
            new ProcessRequest("git", ["-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", .. args], _repo), Ct);
        result.ExitCode.Should().Be(0, $"git {string.Join(' ', args)}: {result.StdErr}");

        return result.StdOut.Trim();
    }

    private Task Write(string path, string text) => File.WriteAllTextAsync(Path.Combine(_repo, path), text, Ct);

    private async Task Commit(string message)
    {
        await Run("add", ".");
        await Run("commit", "-m", message);
    }

    private Task<MeasuredSize> Measure(IProcessLauncher launcher, CancellationToken round, TimeSpan? total = null) =>
        RoundSizer.MeasureAsync(new ContextAssembler(launcher), _repo, _sha, _collected, _shaped, _earlier, Logger.None, round, total);

    [Fact]
    public async Task TheCallsOfOneSizing_RunSideBySide()
    {
        var stamps = new Stamping(_git, TimeSpan.FromMilliseconds(250));
        var watch = Stopwatch.StartNew();

        var measured = await Measure(stamps, Ct);
        watch.Stop();

        measured.Size!.Delta.State.Should().Be(ScopeState.Recorded, "the same answers as when they ran one after another");
        stamps.Calls.Should().HaveCount(5, "unfiltered numstat, --raw, is-ancestor, and the delta's two numstats");
        var whole = stamps.Single(a => a.Contains("--numstat") && !a.Contains(" -- ") && a.Contains($"{_collected.ComparedAgainst}..{_sha}"));
        var raw = stamps.Single(a => a.Contains("--raw"));
        var ancestor = stamps.Single(a => a.Contains("--is-ancestor"));
        var filtered = stamps.Single(a => a.Contains("--numstat") && a.Contains(" -- ") && a.Contains($"{_first}..{_sha}"));
        var unfiltered = stamps.Single(a => a.Contains("--numstat") && !a.Contains(" -- ") && a.Contains($"{_first}..{_sha}"));

        whole.Overlaps(raw).Should().BeTrue("the whole scope and the gitlink count are independent");
        whole.Overlaps(ancestor).Should().BeTrue("and so is the delta");
        filtered.Overlaps(unfiltered).Should().BeTrue("the delta's two numstats are asked together");
        ancestor.End.Should().BeLessThanOrEqualTo(filtered.Start, "but the ancestry question still comes first: a rebased head runs no numstat");
        ancestor.End.Should().BeLessThanOrEqualTo(unfiltered.Start);
        watch.Elapsed.Should().BeLessThan(stamps.SumOfDurations, "overlapping calls take less than their sum");
    }

    [Fact]
    public async Task ASizingThatOutlastsItsDeadline_ReturnsGitFailedScopes_WithinIt()
    {
        var slow = new Stamping(_git, TimeSpan.FromSeconds(4));

        var measured = await Measure(slow, Ct, total: TimeSpan.FromMilliseconds(300));
        var returned = slow.MarkReturned();
        await slow.AllCallsEndedAsync();

        // The claim is an ORDER of two events on the stand-in's own stopwatch — the return, and the end of the slowest call's hold —
        // never a distance from a fixed number: a loaded machine delays both marks together and leaves the order alone.
        returned.HeldToTheEnd.Should().BeEmpty("the sizing answers at its deadline, not when its slowest call is done");
        slow.Calls.Should().NotBeEmpty().And.OnlyContain(c => c.Cancelled, "the deadline reached every call: none ran its hold to the end");
        measured.Size!.Whole.State.Should().Be(ScopeState.GitFailed);
        measured.Size.Whole.From.Should().Be(_collected.ComparedAgainst, "the base was chosen before the failure");
        measured.Size.Delta.State.Should().Be(ScopeState.GitFailed);
        measured.Size.Delta.From.Should().Be(_first);
        measured.Gitlinks.Should().BeNull("an unfinished count is no count");
    }

    [Fact]
    public async Task ALauncherThatIgnoresItsToken_CannotHoldTheSizingPastItsDeadline()
    {
        var deaf = new Stamping(_git, TimeSpan.FromSeconds(4), honourTheToken: false);

        var measured = await Measure(deaf, Ct, total: TimeSpan.FromMilliseconds(300));
        var returned = deaf.MarkReturned();

        returned.Ended.Should().BeEmpty("the wait is bounded, not only the calls: the sizing returned while every deaf call was still holding");
        measured.Size!.Whole.State.Should().Be(ScopeState.GitFailed);
    }

    [Fact]
    public async Task ACancellationOfTheRound_StillPropagates()
    {
        using var round = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var slow = new Stamping(_git, TimeSpan.FromSeconds(30));
        var sizing = Measure(slow, round.Token);
        await Task.Delay(200, Ct);

        await round.CancelAsync();

        await sizing.Awaiting(s => s).Should().ThrowAsync<OperationCanceledException>("the round token is the round's — a size is never a reason to swallow it");
    }

    [Fact]
    public async Task ACancelWhileTheRoundIsLiveAndTheDeadlineIsFar_IsOnlyAFailedCall()
    {
        var odd = new Throwing();

        var measured = await Measure(odd, Ct);

        measured.Size!.Whole.State.Should().Be(ScopeState.GitFailed, "an OCE that is neither the round's nor the deadline's is one call failing");
    }

    /// <summary>
    /// Runs the real git, then holds the call for <paramref name="hold"/>, and stamps when it ran. One stopwatch marks every start, every
    /// end and the sizing's return (<see cref="MarkReturned"/>), so a claim about the sizing's time is an order of marks, not a distance.
    /// A launcher that does not <paramref name="honourTheToken"/> ignores it altogether, the real git included.
    /// </summary>
    private sealed class Stamping(IProcessLauncher inner, TimeSpan hold, bool honourTheToken = true) : IProcessLauncher
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly object _gate = new();
        private readonly List<Call> _calls = [];
        private readonly TaskCompletionSource _allEnded = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _open;

        public IReadOnlyList<Call> Calls
        {
            get
            {
                lock (_gate)
                {
                    return [.. _calls];
                }
            }
        }

        public TimeSpan SumOfDurations => TimeSpan.FromTicks(Calls.Sum(c => (c.End - c.Start).Ticks));

        public Call Single(Func<string, bool> pick) => Calls.Single(c => pick(c.Arguments));

        /// <summary>The sizing has just returned: what the stopwatch read, and which calls had ended by then — the ones that ran their hold to the end apart.</summary>
        public Returned MarkReturned()
        {
            lock (_gate)
            {
                return new Returned(_clock.Elapsed, [.. _calls], [.. _calls.Where(c => c.HeldToTheEnd)]);
            }
        }

        /// <summary>Completes when every call that began has ended (an honoured token ends a hold at once); the suite's guard names a call that never saw its token.</summary>
        public async Task AllCallsEndedAsync()
        {
            try
            {
                await _allEnded.Task.WaitAsync(Ct);
            }
            catch (OperationCanceledException) when (Ct.IsCancellationRequested)
            {
                throw new InvalidOperationException("a call of the sizing never ended: the deadline's cancellation did not reach it.");
            }
        }

        public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            var start = _clock.Elapsed;
            var token = honourTheToken ? ct : CancellationToken.None;
            Begin();
            var cancelled = false;
            var held = false;
            try
            {
                var result = await inner.RunAsync(request, token);
                await Task.Delay(hold, token);
                held = true;

                return result;
            }
            catch (OperationCanceledException)
            {
                cancelled = true;

                throw;
            }
            finally
            {
                End(new Call(string.Join(' ', request.Arguments), start, _clock.Elapsed, cancelled, held));
            }
        }

        private void Begin()
        {
            lock (_gate)
            {
                _open++;
            }
        }

        private void End(Call call)
        {
            lock (_gate)
            {
                _calls.Add(call);
                if (--_open == 0)
                {
                    _allEnded.TrySetResult();
                }
            }
        }
    }

    /// <summary>The sizing's return on the stand-in's stopwatch: when, which calls had ended, and which of them had run their hold to its end.</summary>
    private sealed record Returned(TimeSpan At, IReadOnlyList<Call> Ended, IReadOnlyList<Call> HeldToTheEnd);

    /// <summary><paramref name="Cancelled"/>: the call's token was seen cancelled. <paramref name="HeldToTheEnd"/>: the call ran its whole hold.</summary>
    private sealed record Call(string Arguments, TimeSpan Start, TimeSpan End, bool Cancelled, bool HeldToTheEnd)
    {
        public bool Overlaps(Call other) => Start < other.End && other.Start < End;
    }

    private sealed class Throwing : IProcessLauncher
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default) => throw new OperationCanceledException();
    }
}
