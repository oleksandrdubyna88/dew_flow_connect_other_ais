using System.Diagnostics;
using System.Text.Json;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Serilog.Core;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// One session, one mutating call — and a finished round always reaches its session file.
/// </summary>
/// <remarks>
/// <para>Two blockers the addressable round left behind.</para>
/// <para><b>The session could be left behind.</b> The answer commits to SQLite and the round machine
/// lives in a JSON file; nothing writes both at once. Ordering SQLite first stopped losing the
/// ANSWER and started losing the SESSION: a crash between them left a round that read back
/// `completed` while `resolve` saw no pending findings and the next round was refused. The update is
/// now decided inside the answer's transaction and applied afterwards, so a crash costs a catch-up
/// rather than a stranded round.</para>
/// <para><b>The guard covered one path.</b> `run_round` takes a running slot in `round_locators`;
/// `review_plan` and `review_code` take nothing. So a legacy round and an addressable one on the
/// same repo+branch ran together, both computing the next ordinal from the same file. The claim is
/// an OS file handle every mutating call takes — released by the kernel when a process dies, so
/// there is no lease to expire under a live review.</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class OneSessionOneMutatingCallTests : IAsyncLifetime
{
    private const string Scope = """
        # SCOPE

        A finished round must always reach its session, and one session must never have two mutating
        calls in flight. When it is done: a crash between the answer and the session is caught up
        exactly once, resolve sees the findings, the next round runs — and a second caller on the
        same session is refused before any reviewer starts.
        """;

    private const string Clean = """{"findings": []}""";

    /// <summary>Enough gating findings to leave the stage open, so a NEXT round is permitted.</summary>
    private const string ThreeMajors = """
        {"findings": [
          {"severity": "major", "category": "security", "file": "app.cs", "line": 10,
           "title": "token compared with ==", "why": "timing side channel", "fix": "FixedTimeEquals"},
          {"severity": "major", "category": "reliability", "file": "app.cs", "line": 40,
           "title": "no timeout on the outbound call", "why": "a hung peer hangs the request", "fix": "add a timeout"},
          {"severity": "major", "category": "architecture", "file": "app.cs", "line": 70,
           "title": "the parser reaches into the transport", "why": "layers cross", "fix": "invert it"}
        ]}
        """;

    private const string OneMajor = """
        {"findings": [
          {"severity": "major", "category": "security", "file": "app.cs", "line": 10,
           "title": "token compared with ==", "why": "timing side channel", "fix": "FixedTimeEquals"}
        ]}
        """;

    private sealed class CountingLauncher(IProcessLauncher inner, string reviewerExe) : IProcessLauncher
    {
        private int _reviewers;

        public int Reviewers => Volatile.Read(ref _reviewers);

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            if (string.Equals(request.Executable, reviewerExe, StringComparison.OrdinalIgnoreCase))
            {
                Interlocked.Increment(ref _reviewers);
            }

            return inner.RunAsync(request, ct);
        }
    }

    private readonly ProcessLauncher _git = new();
    private CountingLauncher _launcher = null!;
    private string _repo = string.Empty;
    private string _other = string.Empty;
    private string _data = string.Empty;

    private static string FakeCliExe => Path.Combine(
        AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli");

    public async ValueTask InitializeAsync()
    {
        _repo = Directory.CreateTempSubdirectory("coai-claim-repo-").FullName;
        _other = Directory.CreateTempSubdirectory("coai-claim-other-").FullName;
        _data = Directory.CreateTempSubdirectory("coai-claim-data-").FullName;
        _launcher = new CountingLauncher(_git, FakeCliExe);
        foreach (var repo in (string[])[_repo, _other])
        {
            await Git(repo, "init", "-b", "main");
            await File.WriteAllTextAsync(Path.Combine(repo, "app.cs"), "v1\n");
            await Git(repo, "add", ".");
            await Git(repo, "commit", "-m", "base");
            await Git(repo, "checkout", "-b", "feature");
            await File.WriteAllTextAsync(Path.Combine(repo, "app.cs"), string.Concat(Enumerable.Repeat("line\n", 100)));
            await Git(repo, "add", ".");
            await Git(repo, "commit", "-m", "the feature");
        }

        Environment.SetEnvironmentVariable("FAKECLI_MODE", "vendor");
    }

    public ValueTask DisposeAsync()
    {
        foreach (var name in (string[])["FAKECLI_MODE", "FAKECLI_STDOUT", "FAKECLI_OUTFILE_TEXT", "FAKECLI_EXIT", "FAKECLI_STDERR"])
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        foreach (var dir in (string[])[_repo, _other, _data])
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        return ValueTask.CompletedTask;
    }

    private void Script(string answer)
    {
        Environment.SetEnvironmentVariable("FAKECLI_STDOUT", answer);
        Environment.SetEnvironmentVariable("FAKECLI_OUTFILE_TEXT", answer);
        Environment.SetEnvironmentVariable("FAKECLI_EXIT", "0");
        Environment.SetEnvironmentVariable("FAKECLI_STDERR", "");
    }

    private async Task Git(string repo, params string[] args)
    {
        var result = await _git.RunAsync(new ProcessRequest(
            "git", ["-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", .. args], repo));
        result.ExitCode.Should().Be(0, $"git {string.Join(' ', args)}: {result.StdErr}");
    }

    private PanelService Service() =>
        new(
            new PanelSettings
            {
                Providers = [new("codex") { ExecutablePath = FakeCliExe }],
                Rounds = PanelConfig.Uniform(3, 2, StagePolicy.Human),
                DataDir = _data,
                ReviewerTimeout = TimeSpan.FromSeconds(30),
                RateLimitBackoff = TimeSpan.FromMilliseconds(5),
            },
            VaultKeys.None("no vault in tests"),
            default,
            _launcher,
            Logger.None);

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private static bool IsError(string json) => Parse(json).TryGetProperty("error", out _);

    private async Task ReachTheCodeStageAsync(PanelService service, string repo)
    {
        await service.OpenAsync(repo, "feature");
        Script(Clean);
        Parse(await service.ReviewPlanAsync(repo, "feature", "the plan, and what it must achieve"))
            .GetProperty("verdict").GetString().Should().Be("proceed");
        await service.ResolveAsync(repo, "feature", "[]");
    }

    private async Task<(string Provider, string Session, string Round)> ReserveAsync(PanelService service, string token)
    {
        var reserved = Parse(await service.ReserveRoundAsync(_repo, "feature", "main", token));
        reserved.TryGetProperty("error", out var err).Should().BeFalse(err.ToString());
        var locator = reserved.GetProperty("locator");

        return (
            locator.GetProperty("providerId").GetString()!,
            locator.GetProperty("sessionId").GetString()!,
            locator.GetProperty("roundId").GetString()!);
    }

    // ================= 1. the session catches up after a crash =================

    [Fact]
    public async Task ACrashBeforeTheSessionSave_IsCaughtUpOnce_AndTheRoundGoesOnNormally()
    {
        var service = Service();
        await ReachTheCodeStageAsync(service, _repo);
        var (provider, session, round) = await ReserveAsync(service, "token-1");

        // The answer is committed and the process dies before the session file is touched.
        service.AfterDurableCompletion = () => throw new IOException("died holding the session update");
        // Three gating findings, so the verdict is `revise` and the stage stays open — otherwise
        // `resolve` finishes the session and "the next round runs" has nothing to prove.
        Script(ThreeMajors);
        IsError(await service.RunRoundAsync(provider, session, round, Scope)).Should().BeTrue();
        var reviewersSpent = _launcher.Reviewers;

        // The session really is behind. `LiveRound` left the round in the trail as `running` —
        // that is how the panel shows a round while it happens — but nothing settled it: no
        // verdict, and nothing to resolve.
        var stranded = Parse(await service.StatusAsync(_repo, "feature"));
        stranded.GetProperty("awaitingResolve").GetBoolean().Should().BeFalse("the crash landed before the save");
        stranded.GetProperty("roundsRunThisStage").GetInt32().Should().Be(0);
        stranded.GetProperty("rounds").EnumerateArray()
            .Where(r => r.GetProperty("stage").GetString() == "CodeReview")
            .Should().OnlyContain(r => r.GetProperty("status").GetString() == "running");

        // A new server. `open` is the mutating recovery path.
        var afterRestart = Service();
        var reopened = Parse(await afterRestart.OpenAsync(_repo, "feature"));

        reopened.TryGetProperty("error", out _).Should().BeFalse();
        var trail = Parse(await afterRestart.StatusAsync(_repo, "feature"));
        trail.GetProperty("rounds").EnumerateArray()
            .Count(r => r.GetProperty("stage").GetString() == "CodeReview" && r.GetProperty("status").GetString() == "done")
            .Should().Be(1, "the owed update was applied exactly once");
        trail.GetProperty("roundsRunThisStage").GetInt32().Should().Be(1);
        trail.GetProperty("awaitingResolve").GetBoolean().Should().BeTrue();

        // The locator still reads back completed, with its whole answer.
        Parse(await afterRestart.RoundStatusAsync(provider, session, round))
            .GetProperty("state").GetString().Should().Be("completed");

        // `resolve` sees the full pending list — the thing the stranded session could not do.
        var pending = Parse(await afterRestart.RoundStatusAsync(provider, session, round))
            .GetProperty("review").GetProperty("findings").GetArrayLength();
        pending.Should().Be(3);
        var resolved = Parse(await afterRestart.ResolveAsync(
            _repo, "feature",
            JsonSerializer.Serialize(Enumerable.Range(0, pending).Select(i => new { finding = i, action = "accept" }))));
        resolved.TryGetProperty("error", out var resolveError).Should().BeFalse(resolveError.ToString());
        resolved.GetProperty("recordedDecisions").GetInt32().Should().Be(pending,
            "the recovered session carried the WHOLE pending list, not a count");

        // And the state machine allows the next round it was always going to allow.
        Script(Clean);
        var next = Parse(await afterRestart.ReviewCodeAsync(_repo, "feature", "main", Scope));
        next.TryGetProperty("error", out var nextError).Should().BeFalse(nextError.ToString());
        next.GetProperty("verdict").GetString().Should().Be("proceed");
        _launcher.Reviewers.Should().BeGreaterThan(reviewersSpent, "the next round really ran");
    }

    [Fact]
    public async Task RepeatingRecovery_AddsNothing_AndSpendsNothing()
    {
        var service = Service();
        await ReachTheCodeStageAsync(service, _repo);
        var (provider, session, round) = await ReserveAsync(service, "token-1");
        service.AfterDurableCompletion = () => throw new IOException("died holding the session update");
        Script(OneMajor);
        await service.RunRoundAsync(provider, session, round, Scope);

        var afterRestart = Service();
        await afterRestart.OpenAsync(_repo, "feature");
        var afterFirst = await afterRestart.StatusAsync(_repo, "feature");
        var reviewersAfterFirst = _launcher.Reviewers;

        // Settled by the FIRST catch-up, not eventually. A debt that is paid but still recorded as
        // owed would be retried on every open — harmless only until the session moves, at which
        // point an already-applied update starts reporting a conflict that never happened.
        using (var afterOne = RoundsDb.Open(_data, Logger.None)!)
        {
            afterOne.UnappliedCommits(_repo, "feature").Should().BeEmpty("one catch-up is enough");
        }

        // Three more opens, and a third server for good measure.
        await afterRestart.OpenAsync(_repo, "feature");
        await afterRestart.OpenAsync(_repo, "feature");
        await Service().OpenAsync(_repo, "feature");

        var afterMany = await afterRestart.StatusAsync(_repo, "feature");
        Parse(afterMany).GetProperty("rounds").GetArrayLength()
            .Should().Be(Parse(afterFirst).GetProperty("rounds").GetArrayLength(), "no round was duplicated");
        Parse(afterMany).GetProperty("roundsRunThisStage").GetInt32()
            .Should().Be(Parse(afterFirst).GetProperty("roundsRunThisStage").GetInt32(), "no budget was spent");
        _launcher.Reviewers.Should().Be(reviewersAfterFirst, "recovery starts no reviewer");

        using var db = RoundsDb.Open(_data, Logger.None)!;
        db.UnappliedCommits(_repo, "feature").Should().BeEmpty("the debt was paid, once");
    }

    [Fact]
    public async Task ASessionThatMovedUnderneath_IsNotOverwritten()
    {
        var service = Service();
        await ReachTheCodeStageAsync(service, _repo);
        var (provider, session, round) = await ReserveAsync(service, "token-1");
        service.AfterDurableCompletion = () => throw new IOException("died holding the session update");
        Script(OneMajor);
        await service.RunRoundAsync(provider, session, round, Scope);

        // Somebody else advances the session while the finished round still owes it an update —
        // through the store, so it is a real session document and not a hand-edit that `open`
        // would normalise away.
        var theirStore = new SessionStore(_data);
        var theirs = theirStore.Load(_repo, "feature")!;
        theirs.UsedPrompts.Add("someone-elses-edit");
        theirStore.Save(theirs);

        var afterRestart = Service();
        await afterRestart.OpenAsync(_repo, "feature");

        // Fail closed: their edit survives, the update is NOT applied, and the reason is recorded.
        theirStore.Load(_repo, "feature")!.UsedPrompts.Should().Contain("someone-elses-edit");
        using var db = RoundsDb.Open(_data, Logger.None)!;
        var owed = db.CommitFor(new RoundLocator(provider, session, round))!;
        owed.Applied.Should().BeFalse();
        owed.Conflict.Should().Contain("the session moved");

        // The answer is still readable by its locator — the round was not lost, only its session.
        Parse(await afterRestart.RoundStatusAsync(provider, session, round))
            .GetProperty("state").GetString().Should().Be("completed");
    }

    // ================= 2. one mutating call per session =================

    [Fact]
    public async Task AnAddressableRoundAndALegacyReviewCode_ProduceExactlyOneFanOut()
    {
        var service = Service();
        await ReachTheCodeStageAsync(service, _repo);
        var (provider, session, round) = await ReserveAsync(service, "token-1");
        var before = _launcher.Reviewers;

        Script(OneMajor);
        var both = await Task.WhenAll(
            service.RunRoundAsync(provider, session, round, Scope),
            service.ReviewCodeAsync(_repo, "feature", "main", Scope));

        both.Count(r => !IsError(r)).Should().Be(1, "one mutating call per session");
        var answered = Parse(both.First(r => !IsError(r))).GetProperty("reviewers").GetString()!;
        answered.Should().Contain($"all {_launcher.Reviewers - before} reviewers answered",
            "every launch belongs to the one round that ran");
    }

    [Fact]
    public async Task TwoLegacyReviewCodeCalls_ProduceExactlyOneFanOut()
    {
        var service = Service();
        await ReachTheCodeStageAsync(service, _repo);
        var before = _launcher.Reviewers;

        Script(OneMajor);
        var both = await Task.WhenAll(
            service.ReviewCodeAsync(_repo, "feature", "main", Scope),
            service.ReviewCodeAsync(_repo, "feature", "main", Scope));

        // Neither call takes a locator, so nothing but the session claim stands between them.
        both.Count(r => !IsError(r)).Should().Be(1);
        Parse(both.First(IsError)).GetProperty("error").GetString()
            .Should().Contain("already changing the session");
        Parse(both.First(r => !IsError(r))).GetProperty("reviewers").GetString()
            .Should().Contain($"all {_launcher.Reviewers - before} reviewers answered");
    }

    [Fact]
    public async Task TwoDifferentSessionsRunAtTheSameTime()
    {
        var service = Service();
        await ReachTheCodeStageAsync(service, _repo);
        await ReachTheCodeStageAsync(service, _other);
        var before = _launcher.Reviewers;

        Script(OneMajor);
        var both = await Task.WhenAll(
            service.ReviewCodeAsync(_repo, "feature", "main", Scope),
            service.ReviewCodeAsync(_other, "feature", "main", Scope));

        // Different repositories are different sessions, and must not queue behind each other.
        both.Should().OnlyContain(r => !IsError(r), "different sessions never contend");
        var launched = _launcher.Reviewers - before;
        var perRound = int.Parse(
            Parse(both[0]).GetProperty("reviewers").GetString()!.Split(' ')[1]);
        launched.Should().Be(perRound * 2, "both rounds really ran");
    }

    [Fact]
    public async Task WhenTheClaimHolderIsKilled_TheNextProcessTakesIt()
    {
        var claimFile = SessionClaim.FileFor(_data, _repo, "feature");
        Directory.CreateDirectory(Path.GetDirectoryName(claimFile)!);

        // A REAL second process holding the claim exactly as this server would.
        var start = new ProcessStartInfo(FakeCliExe, ["hold", claimFile])
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        // The vendor mode answers before the verb switch is reached, and this fixture sets it.
        start.Environment["FAKECLI_MODE"] = string.Empty;
        using var holder = Process.Start(start)!;
        (await holder.StandardOutput.ReadLineAsync()).Should().Be("held", "the other process has it");

        // While it lives, this process cannot take the claim, and a mutating call refuses.
        SessionClaim.TryTake(_data, _repo, "feature").Should().BeNull();
        var service = Service();
        Parse(await service.OpenAsync(_repo, "feature")).GetProperty("error").GetString()
            .Should().Contain("already changing the session");

        // Killed, not asked to exit: the kernel closes the handle, which is the whole reason this
        // is an OS lock and not a lease. There is nothing to expire and nothing to repair.
        holder.Kill(entireProcessTree: true);
        await holder.WaitForExitAsync();

        using var taken = SessionClaim.TryTake(_data, _repo, "feature");
        taken.Should().NotBeNull("a dead owner holds nothing");
        taken!.Dispose();
        Parse(await service.OpenAsync(_repo, "feature")).TryGetProperty("error", out _).Should().BeFalse();
    }

    [Fact]
    public async Task ReadOnlyCallsNeverTakeTheClaim()
    {
        var service = Service();
        await ReachTheCodeStageAsync(service, _repo);
        var (provider, session, round) = await ReserveAsync(service, "token-1");

        // Held by somebody else for the duration.
        using var held = SessionClaim.TryTake(_data, _repo, "feature");
        held.Should().NotBeNull();

        // Reading must never queue behind a round that takes minutes.
        Parse(await service.StatusAsync(_repo, "feature")).TryGetProperty("error", out _).Should().BeFalse();
        Parse(await service.RoundStatusAsync(provider, session, round))
            .GetProperty("state").GetString().Should().Be("not_started");
        Parse(await service.ProvidersAsync()).TryGetProperty("providers", out _).Should().BeTrue();
    }
}
