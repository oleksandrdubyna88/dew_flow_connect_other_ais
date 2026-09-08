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
/// `open` touches nothing until it owns the session, a dead round's slot is released, and an empty
/// scope is resolved under the claim.
/// </summary>
/// <remarks>
/// <para><b>`open` pruned first and asked afterwards.</b> `PruneOursAsync` deletes every
/// `coai-wt-*` tree by name — including the one a review in another process is reading at that
/// moment — and it ran BEFORE the claim. So `open` could delete a live reviewer's checkout and only
/// then report that the session was busy: the refusal arrived after the damage.</para>
/// <para><b>A killed round held the session for ever.</b> Process death releases the claim, but the
/// locator row still said `running`, and the one-running-round index then refused every later round
/// of that session. Nothing expired it, because there is deliberately no lease.</para>
/// <para><b>The fallback scope was read outside the claim.</b> An empty `planText` falls back to the
/// scope the plan stage agreed, and that read happened before the claim — so a concurrent `resolve`
/// could change the session between the read and the round.</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class OpenTakesTheClaimFirstTests : IAsyncLifetime
{
    private const string Scope = """
        # SCOPE

        `open` must own the session before it deletes anything, a dead round must not hold a session
        for ever, and a round must review the scope its session actually carries. When it is done: a
        live worktree survives a concurrent open, a killed round reads back failed without any
        automatic retry, and a resolve racing a scope fallback cannot change what was reviewed.
        """;

    private const string Clean = """{"findings": []}""";

    private const string OneMajor = """
        {"findings": [
          {"severity": "major", "category": "security", "file": "app.cs", "line": 10,
           "title": "token compared with ==", "why": "timing side channel", "fix": "FixedTimeEquals"}
        ]}
        """;

    /// <summary>Counts reviewer launches, and can hold a round open until the test lets it finish.</summary>
    private sealed class GatedLauncher(IProcessLauncher inner, string reviewerExe) : IProcessLauncher
    {
        private int _reviewers;

        public int Reviewers => Volatile.Read(ref _reviewers);

        /// <summary>Set to make every reviewer wait; the test releases it when it is ready.</summary>
        public ManualResetEventSlim? Gate { get; set; }

        /// <summary>Signalled the moment the first reviewer actually starts.</summary>
        public ManualResetEventSlim Started { get; } = new(false);

        public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            if (!string.Equals(request.Executable, reviewerExe, StringComparison.OrdinalIgnoreCase))
            {
                return await inner.RunAsync(request, ct);
            }

            Interlocked.Increment(ref _reviewers);
            Started.Set();
            if (Gate is { } gate)
            {
                // A real round is minutes long. Holding it here is how a test gets a genuinely
                // concurrent `open` rather than one that races the process start.
                gate.Wait(TimeSpan.FromSeconds(60));
            }

            return await inner.RunAsync(request, ct);
        }
    }

    private readonly ProcessLauncher _git = new();
    private GatedLauncher _launcher = null!;
    private string _repo = string.Empty;
    private string _data = string.Empty;

    private static string FakeCliExe => Path.Combine(
        AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli");

    public async ValueTask InitializeAsync()
    {
        _repo = Directory.CreateTempSubdirectory("coai-open-repo-").FullName;
        _data = Directory.CreateTempSubdirectory("coai-open-data-").FullName;
        _launcher = new GatedLauncher(_git, FakeCliExe);
        await Git("init", "-b", "main");
        await File.WriteAllTextAsync(Path.Combine(_repo, "app.cs"), "v1\n");
        await Git("add", ".");
        await Git("commit", "-m", "base");
        await Git("checkout", "-b", "feature");
        await File.WriteAllTextAsync(Path.Combine(_repo, "app.cs"), string.Concat(Enumerable.Repeat("line\n", 100)));
        await Git("add", ".");
        await Git("commit", "-m", "the feature");
        Environment.SetEnvironmentVariable("FAKECLI_MODE", "vendor");
    }

    public ValueTask DisposeAsync()
    {
        foreach (var name in (string[])["FAKECLI_MODE", "FAKECLI_STDOUT", "FAKECLI_OUTFILE_TEXT", "FAKECLI_EXIT", "FAKECLI_STDERR"])
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        foreach (var dir in (string[])[_repo, _data])
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

    private async Task Git(params string[] args)
    {
        var result = await _git.RunAsync(new ProcessRequest(
            "git", ["-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", .. args], _repo));
        result.ExitCode.Should().Be(0, $"git {string.Join(' ', args)}: {result.StdErr}");
    }

    private PanelService Service() =>
        new(
            new PanelSettings
            {
                Providers = [new("codex") { ExecutablePath = FakeCliExe }],
                Rounds = PanelConfig.Uniform(3, 2, StagePolicy.Human),
                DataDir = _data,
                ReviewerTimeout = TimeSpan.FromSeconds(60),
                RateLimitBackoff = TimeSpan.FromMilliseconds(5),
            },
            VaultKeys.None("no vault in tests"),
            default,
            _launcher,
            Logger.None);

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private static bool IsError(string json) => Parse(json).TryGetProperty("error", out _);

    private async Task ReachTheCodeStageAsync(PanelService service)
    {
        await service.OpenAsync(_repo, "feature");
        Script(Clean);
        Parse(await service.ReviewPlanAsync(_repo, "feature", "the plan, and what it must achieve"))
            .GetProperty("verdict").GetString().Should().Be("proceed");
        await service.ResolveAsync(_repo, "feature", "[]");
    }

    private string[] LiveWorktrees() =>
        Directory.Exists(Path.Combine(_data, "worktrees"))
            ? Directory.GetDirectories(Path.Combine(_data, "worktrees"), "coai-wt-*")
            : [];

    // ============ 1. open touches nothing before it owns the session ============

    [Fact]
    public async Task AConcurrentOpen_CannotDeleteALiveReviewersWorktree()
    {
        var service = Service();
        await ReachTheCodeStageAsync(service);

        using var gate = new ManualResetEventSlim(false);
        _launcher.Gate = gate;
        // The plan round already set this; it has to mean THIS round's first reviewer.
        _launcher.Started.Reset();
        Script(OneMajor);
        var review = service.ReviewCodeAsync(_repo, "feature", "main", Scope);

        // Wait for a reviewer to be genuinely running, so a worktree exists AND is in use.
        _launcher.Started.Wait(TimeSpan.FromSeconds(30)).Should().BeTrue("a reviewer started");
        var live = LiveWorktrees();
        live.Should().ContainSingle("the round has a checkout of its own");
        var tree = live[0];
        File.Exists(Path.Combine(tree, "app.cs")).Should().BeTrue("the reviewer's checkout is real");

        // `open` while that round runs. It must refuse, and must not have pruned on the way.
        var opened = await Service().OpenAsync(_repo, "feature");

        IsError(opened).Should().BeTrue();
        Parse(opened).GetProperty("error").GetString().Should().Contain("already changing the session");
        Directory.Exists(tree).Should().BeTrue("the live worktree survived the refused open");
        File.Exists(Path.Combine(tree, "app.cs")).Should().BeTrue("and so did its contents");

        // The round finishes normally — the proof that nothing was taken from under it.
        gate.Set();
        var answer = await review;
        IsError(answer).Should().BeFalse(answer);
        Parse(answer).GetProperty("findings").GetArrayLength().Should().Be(1);

        // And once it is over, `open` works and the ordinary cleanup happens.
        var after = await Service().OpenAsync(_repo, "feature");
        IsError(after).Should().BeFalse(after);
        LiveWorktrees().Should().BeEmpty("the prune runs once the session is free");
    }

    // ============ 2. a killed round does not hold the session for ever ============

    [Fact]
    public async Task AKilledRunRound_IsReleasedAsFailed_WithNoAutomaticRetry()
    {
        var service = Service();
        await ReachTheCodeStageAsync(service);
        var reserved = Parse(await service.ReserveRoundAsync(_repo, "feature", "main", "token-1"));
        var locator = reserved.GetProperty("locator");
        var (provider, session, round) = (
            locator.GetProperty("providerId").GetString()!,
            locator.GetProperty("sessionId").GetString()!,
            locator.GetProperty("roundId").GetString()!);
        var dead = new RoundLocator(provider, session, round);

        // A REAL second process, holding the REAL session claim — the same file a running
        // `run_round` holds for its whole duration. Nothing in-process can stand in for it: the
        // mechanism under test is the KERNEL releasing that handle when a process dies, and an
        // in-process Dispose is an orderly release, which is the case that was never in doubt.
        var claimFile = SessionClaim.FileFor(_data, _repo, "feature");
        Directory.CreateDirectory(Path.GetDirectoryName(claimFile)!);
        var start = new ProcessStartInfo(FakeCliExe, ["hold", claimFile])
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        // The vendor mode answers before the verb switch is reached, and this fixture sets it.
        start.Environment["FAKECLI_MODE"] = string.Empty;
        using var owner = Process.Start(start)!;
        (await owner.StandardOutput.ReadLineAsync()).Should().Be("held", "the other process has the claim");

        // The round is marked running and OWNED BY THAT PROCESS. The row is written here rather
        // than by the child — the child is a stub that holds a handle, not a second coai-mcp — but
        // what lands on disk is byte-for-byte what a crashed `run_round` leaves: `running`, this
        // machine, that pid, and the claim held by a process that is about to stop existing.
        using (var db = RoundsDb.Open(_data, Logger.None)!)
        {
            db.Begin(dead, new RoundOwner(Environment.MachineName, owner.Id, "the-process-that-dies"))
                .Claim.Should().Be(RoundClaim.Claimed);
        }

        // While it lives, the session is genuinely busy and the slot is genuinely taken.
        IsError(await Service().OpenAsync(_repo, "feature")).Should().BeTrue("the owner is alive");
        using (var db = RoundsDb.Open(_data, Logger.None)!)
        {
            db.Read(dead)!.State.Should().Be(RoundLifecycle.Running);
            db.Read(dead)!.OwnerPid.Should().Be(owner.Id);
            db.Reserve(session, "token-2", "CodeReview", _repo, "feature", db.Read(dead)!.Subject, () => "r2", out _);
            db.Begin(new RoundLocator(provider, session, "r2"), RoundOwner.Here("someone"))
                .Claim.Should().Be(RoundClaim.SessionBusy, "the running round holds the one slot");
        }

        // Killed, not asked to exit: no graceful cleanup, no chance to tidy the row.
        owner.Kill(entireProcessTree: true);
        await owner.WaitForExitAsync();

        var reviewersBefore = _launcher.Reviewers;
        var roundsBefore = CodeRoundsInTrail(await service.StatusAsync(_repo, "feature"));

        // A new server opens the session. Taking the claim is what proves nothing is running.
        var afterRestart = Service();
        IsError(await afterRestart.OpenAsync(_repo, "feature")).Should().BeFalse();

        // Failed — never not_started, which would license a free retry of a call that may have run,
        // and never completed, which would invent an answer nobody produced.
        var status = Parse(await afterRestart.RoundStatusAsync(provider, session, round));
        status.GetProperty("state").GetString().Should().Be("failed");
        status.GetProperty("state").GetString().Should().NotBe("not_started");
        status.GetProperty("state").GetString().Should().NotBe("completed");
        status.TryGetProperty("review", out _).Should().BeFalse("a round that never answered has no findings");
        _launcher.Reviewers.Should().Be(reviewersBefore, "reconciliation launches nothing at all");

        // The unique running slot is free again.
        using (var db = RoundsDb.Open(_data, Logger.None)!)
        {
            db.RunningRounds(_repo, "feature").Should().BeEmpty("the slot the dead round held is released");
        }

        // A new round needs a NEW token and a deliberate run_round — nothing was retried for us.
        var again = Parse(await afterRestart.ReserveRoundAsync(_repo, "feature", "main", "token-3"));
        again.TryGetProperty("error", out var reserveError).Should().BeFalse(reserveError.ToString());
        var next = again.GetProperty("locator");
        Script(OneMajor);
        var ran = await afterRestart.RunRoundAsync(
            next.GetProperty("providerId").GetString()!,
            next.GetProperty("sessionId").GetString()!,
            next.GetProperty("roundId").GetString()!,
            Scope);

        IsError(ran).Should().BeFalse(ran);
        var launched = _launcher.Reviewers - reviewersBefore;
        launched.Should().BeGreaterThan(0);
        Parse(ran).GetProperty("reviewers").GetString()
            .Should().Contain($"all {launched} reviewers answered", "exactly one round's worth of launches");
        CodeRoundsInTrail(await afterRestart.StatusAsync(_repo, "feature"))
            .Should().Be(roundsBefore + 1, "exactly one round was dispatched");

        // And the dead round is still failed — recovery settled it, it did not re-run it.
        Parse(await afterRestart.RoundStatusAsync(provider, session, round))
            .GetProperty("state").GetString().Should().Be("failed");
    }

    private static int CodeRoundsInTrail(string status) =>
        Parse(status).GetProperty("rounds").EnumerateArray()
            .Count(r => r.GetProperty("stage").GetString() == "CodeReview"
                        && r.GetProperty("status").GetString() == "done");

    [Fact]
    public void AnotherMachinesRunningRound_IsLeftAlone()
    {
        using var db = RoundsDb.Open(_data, Logger.None)!;
        var subject = new SubjectAttestation(
            "d:/repo/.git", "main", new string('b', 40), new string('a', 40), new string('7', 40));
        db.Reserve("s1", "token-1", "CodeReview", _repo, "feature", subject, () => "r1", out _);
        var locator = new RoundLocator(RoundLocator.Provider, "s1", "r1");
        db.Begin(locator, new RoundOwner("some-other-machine", 4242, "elsewhere")).Claim
            .Should().Be(RoundClaim.Claimed);

        db.RunningRounds(_repo, "feature").Should().ContainSingle()
            .Which.OwnerMachine.Should().Be("some-other-machine");
    }

    [Fact]
    public async Task OpenLeavesAnotherMachinesRunningRoundExactlyWhereItIs()
    {
        var service = Service();
        await ReachTheCodeStageAsync(service);
        var reserved = Parse(await service.ReserveRoundAsync(_repo, "feature", "main", "token-1"));
        var locator = reserved.GetProperty("locator");
        var mine = new RoundLocator(
            locator.GetProperty("providerId").GetString()!,
            locator.GetProperty("sessionId").GetString()!,
            locator.GetProperty("roundId").GetString()!);

        using (var db = RoundsDb.Open(_data, Logger.None)!)
        {
            db.Begin(mine, new RoundOwner("some-other-machine", 4242, "elsewhere")).Claim
                .Should().Be(RoundClaim.Claimed);
        }

        // The claim is a file handle, and a handle proves liveness only where the filesystem is
        // local. `open` here owns the session on THIS machine, which proves nothing about a
        // process on another one — releasing that round would declare a live review dead.
        IsError(await Service().OpenAsync(_repo, "feature")).Should().BeFalse();

        Parse(await service.RoundStatusAsync(mine.ProviderId, mine.SessionId, mine.RoundId))
            .GetProperty("state").GetString().Should().Be("running", "it is not this server's to release");
    }

    // ============ 3. the fallback scope is read under the claim ============

    [Fact]
    public async Task AResolveRacingAnEmptyScope_CannotChangeWhatIsReviewed()
    {
        var service = Service();
        await service.OpenAsync(_repo, "feature");
        Script(Clean);
        // Past CodeScope.Floor: the code stage refuses a scope with no substance, and a short one
        // would never reach a reviewer at all.
        const string agreed = """
            # SCOPE — agreed by the plan stage

            The fallback scope a code round uses when the caller sends none must be read from the
            session under the claim that round holds, not before it. When it is done: a resolve
            racing the round cannot change what the reviewers were told the change was for, and the
            recorded plan text of the round is the text the plan stage agreed.
            """;
        Parse(await service.ReviewPlanAsync(_repo, "feature", agreed))
            .GetProperty("verdict").GetString().Should().Be("proceed");
        await service.ResolveAsync(_repo, "feature", "[]");

        using var gate = new ManualResetEventSlim(false);
        _launcher.Gate = gate;
        _launcher.Started.Reset();
        Script(OneMajor);

        // An empty planText: the scope has to come from the session, and it must be the session as
        // the round OWNS it — not as it was before the claim was taken.
        var review = service.ReviewCodeAsync(_repo, "feature", "main", planText: "  ");
        _launcher.Started.Wait(TimeSpan.FromSeconds(30)).Should().BeTrue();

        // A concurrent resolve, while the round holds the claim. It must not get in.
        var raced = await service.ResolveAsync(_repo, "feature", "[]");
        IsError(raced).Should().BeTrue("resolve waits its turn like every other mutating call");
        Parse(raced).GetProperty("error").GetString().Should().Contain("already changing the session");

        gate.Set();
        var answer = await review;
        IsError(answer).Should().BeFalse(answer);

        // The round reviewed the agreed scope, and the database records exactly that text.
        PlanTextOfLastRound().Should().Be(agreed, "the fallback was read under the claim");
    }

    [Fact]
    public async Task TheFallbackScopeIsReadAfterTheClaim_NotBefore()
    {
        var service = Service();
        await service.OpenAsync(_repo, "feature");
        Script(Clean);
        Parse(await service.ReviewPlanAsync(_repo, "feature", Scope))
            .GetProperty("verdict").GetString().Should().Be("proceed");
        await service.ResolveAsync(_repo, "feature", "[]");

        // The session's scope changes at the one instant that separates the two designs: the claim
        // is held and the session has not been read yet. A round that resolved its fallback BEFORE
        // the claim carries the old text; one that reads it after carries this.
        const string agreedLater = """
            # SCOPE - as the session actually carries it

            The scope a round runs with must be the one its session holds once the round owns
            that session. When it is done: a change that landed before the claim was taken is
            the change the reviewers are told about, and no read from before the claim can
            survive into the round's recorded plan text.
            """;
        service.AfterClaimTaken = () =>
        {
            service.AfterClaimTaken = null;
            var store = new SessionStore(_data);
            var current = store.Load(_repo, "feature")!;
            store.Save(current with { PlanText = agreedLater });
        };

        Script(OneMajor);
        IsError(await service.ReviewCodeAsync(_repo, "feature", "main", planText: "  ")).Should().BeFalse();

        PlanTextOfLastRound().Should().Be(agreedLater, "the fallback was read under the claim");
    }

    [Fact]
    public async Task AnExplicitScope_IsNeverReplacedByTheSessions()
    {
        var service = Service();
        await ReachTheCodeStageAsync(service);
        Script(OneMajor);

        IsError(await service.ReviewCodeAsync(_repo, "feature", "main", Scope)).Should().BeFalse();

        PlanTextOfLastRound().Should().Be(Scope, "an explicit scope is used exactly as sent");
    }

    private string PlanTextOfLastRound()
    {
        using var raw = new Microsoft.Data.Sqlite.SqliteConnection(
            $"Data Source={Path.Combine(_data, RoundsDb.FileName)};Pooling=False");
        raw.Open();
        using var read = raw.CreateCommand();
        read.CommandText = "SELECT plan_text FROM rounds WHERE stage = 'CodeReview' ORDER BY id DESC LIMIT 1";

        return (string)(read.ExecuteScalar() ?? string.Empty);
    }
}
