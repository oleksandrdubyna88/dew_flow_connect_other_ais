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
/// One locator runs exactly one review, at exactly the commit it attested, and its answer survives
/// a crash between finishing and replying.
/// </summary>
/// <remarks>
/// <para>Three failures found by review of the first cut, all of them invisible to the tests that
/// shipped with it:</para>
/// <para><b>Both callers dispatched.</b> The claim was read back as a STATE after the update, and
/// the winner and the loser both see <c>running</c> — so a race launched two fan-outs of a
/// non-idempotent call. Proved here by COUNTING reviewer launches, because a timestamp comparison
/// cannot see a second process.</para>
/// <para><b>The pin did not reach the worktree.</b> The subject was checked, then the legacy code
/// path resolved the branch again — so a commit landing in between was reviewed and published under
/// the previous commit's attestation.</para>
/// <para><b>The answer was written twice.</b> The round was recorded, then the locator was completed
/// by a second call; a crash in between left a finished round whose locator had nothing to give
/// back.</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class OneLocatorRunsOnceTests : IAsyncLifetime
{
    private const string Scope = """
        # SCOPE

        A dispatched round must run exactly once, at exactly the commit its locator attested, and its
        answer must survive the reply being lost. When it is done: two racing callers produce one
        review, a branch that moves mid-round changes nothing that is reviewed or published, and a
        restart recovers the whole answer. Constraint: the existing review_code call is untouched.
        """;

    private const string OneMajor = """
        {"findings": [
          {"severity": "major", "category": "security", "file": "app.cs", "line": 10,
           "title": "token compared with ==", "why": "timing side channel", "fix": "FixedTimeEquals"}
        ]}
        """;

    private const string Clean = """{"findings": []}""";

    /// <summary>
    /// A launcher that counts what it actually started.
    /// </summary>
    /// <remarks>
    /// The only honest way to answer "how many reviews ran". The session trail records one round
    /// per completed stage, so a doubled fan-out that raced on the same trail can leave ONE round
    /// recorded while six CLIs ran. Processes are what cost money and consume a vendor's quota, so
    /// processes are what this counts.
    /// </remarks>
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
    private string _data = string.Empty;

    private static string FakeCliExe => Path.Combine(
        AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli");

    public async ValueTask InitializeAsync()
    {
        _repo = Directory.CreateTempSubdirectory("coai-once-repo-").FullName;
        _data = Directory.CreateTempSubdirectory("coai-once-data-").FullName;
        _launcher = new CountingLauncher(_git, FakeCliExe);
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

    private async Task<string> HeadOfAsync(string rev)
    {
        var result = await _git.RunAsync(new ProcessRequest("git", ["rev-parse", "--verify", $"{rev}^{{commit}}"], _repo));
        result.ExitCode.Should().Be(0);

        return result.StdOut.Trim();
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

    private async Task ReachTheCodeStageAsync(PanelService service)
    {
        await service.OpenAsync(_repo, "feature");
        Script(Clean);
        Parse(await service.ReviewPlanAsync(_repo, "feature", "the plan, and what it must achieve"))
            .GetProperty("verdict").GetString().Should().Be("proceed");
        await service.ResolveAsync(_repo, "feature", "[]");
    }

    private async Task<(string Provider, string Session, string Round, JsonElement Attestation)> ReserveAsync(
        PanelService service, string token)
    {
        var reserved = Parse(await service.ReserveRoundAsync(_repo, "feature", "main", token));
        reserved.TryGetProperty("error", out var err).Should().BeFalse(err.ToString());
        var locator = reserved.GetProperty("locator");

        return (
            locator.GetProperty("providerId").GetString()!,
            locator.GetProperty("sessionId").GetString()!,
            locator.GetProperty("roundId").GetString()!,
            reserved.GetProperty("attestation"));
    }

    // ---------- 1. two callers, one locator ----------

    [Fact]
    public async Task TwoCallersRacingOnOneLocator_RunExactlyOneReview()
    {
        var service = Service();
        await ReachTheCodeStageAsync(service);
        var (provider, session, round, _) = await ReserveAsync(service, "token-1");
        var beforeAnyReview = _launcher.Reviewers;

        Script(OneMajor);
        // Started together on purpose. Both see a reserved round; only one may dispatch.
        var both = await Task.WhenAll(
            service.RunRoundAsync(provider, session, round, Scope),
            service.RunRoundAsync(provider, session, round, Scope));

        var reviews = both.Where(r => !IsError(r)).ToList();
        var refusals = both.Where(IsError).ToList();
        reviews.Should().HaveCount(1, "exactly one caller may dispatch a reserved round");
        refusals.Should().HaveCount(1);
        // Either guard is a correct refusal, and which one fires depends on how the two calls
        // interleave: the session claim turns the loser away before it reaches the round at all,
        // and the round's own claim catches it if it gets that far. Both refuse before a reviewer.
        Parse(refusals[0]).GetProperty("error").GetString()
            .Should().MatchRegex("already changing the session|already running|could not be started|another round");

        // The measurement that matters: reviewer PROCESSES. A doubled fan-out costs real quota and
        // can still leave one round in the trail, so counting rounds would miss it.
        var answered = Parse(reviews[0]).GetProperty("reviewers").GetString()!;
        var launched = _launcher.Reviewers - beforeAnyReview;
        launched.Should().BeGreaterThan(0, "a review really ran");
        answered.Should().Contain($"all {launched} reviewers answered",
            "every launch belongs to the one round that was claimed — a second dispatch would double this");
    }

    // ---------- 4. two locators, one session ----------

    [Fact]
    public async Task TwoLocatorsOfOneSessionRacing_RunOneReviewAndKeepTheirOwnRounds()
    {
        var service = Service();
        await ReachTheCodeStageAsync(service);
        var mine = await ReserveAsync(service, "token-mine");
        var theirs = await ReserveAsync(service, "token-theirs");
        mine.Round.Should().NotBe(theirs.Round);
        var before = _launcher.Reviewers;

        Script(OneMajor);
        var both = await Task.WhenAll(
            service.RunRoundAsync(mine.Provider, mine.Session, mine.Round, Scope),
            service.RunRoundAsync(theirs.Provider, theirs.Session, theirs.Round, Scope));

        var reviews = both.Where(r => !IsError(r)).ToList();
        reviews.Should().HaveCount(1, "one round at a time per session — the trail is one document");
        var launched = _launcher.Reviewers - before;
        Parse(reviews[0]).GetProperty("reviewers").GetString().Should().Contain($"all {launched} reviewers answered");

        // Whichever won, it owns its OWN local round and the loser owns none — no locator inherits
        // another's result, and the loser is still spendable.
        using var db = RoundsDb.Open(_data, Logger.None)!;
        var winner = Parse(reviews[0]).GetProperty("locator").GetProperty("roundId").GetString()!;
        var loser = winner == mine.Round ? theirs : mine;
        var won = db.Read(new RoundLocator(RoundLocator.Provider, mine.Session, winner))!;
        var lost = db.Read(new RoundLocator(RoundLocator.Provider, loser.Session, loser.Round))!;

        won.State.Should().Be(RoundLifecycle.Completed);
        won.RoundRef.Should().NotBeNull();
        lost.State.Should().Be(RoundLifecycle.Reserved, "the loser dispatched nothing");
        lost.RoundRef.Should().BeNull("and it inherited nobody's round");
        lost.ResultJson.Should().BeEmpty();
    }

    // ---------- 2. the branch moves after the last check ----------

    [Fact]
    public async Task ABranchMovingAfterTheLastCheck_ChangesNothingThatIsReviewedOrPublished()
    {
        var service = Service();
        await ReachTheCodeStageAsync(service);
        var (provider, session, round, attestation) = await ReserveAsync(service, "token-1");
        var pinned = attestation.GetProperty("headSha").GetString()!;

        // Exactly the window: past every check RunRoundAsync makes, and before the checkout.
        var moved = string.Empty;
        service.BeforeWorktree = async () =>
        {
            service.BeforeWorktree = null;
            await File.WriteAllTextAsync(Path.Combine(_repo, "app.cs"), "something else entirely\n");
            await Git("add", ".");
            await Git("commit", "-m", "landed underneath the round");
            moved = await HeadOfAsync("feature");
        };

        Script(OneMajor);
        var answer = Parse(await service.RunRoundAsync(provider, session, round, Scope));

        moved.Should().NotBeNullOrEmpty().And.NotBe(pinned, "the branch really did move inside the window");
        answer.GetProperty("attestation").GetProperty("headSha").GetString().Should().Be(pinned);

        // And the SHA that actually reached the worktree is the pinned one, not the new tip. This
        // is the assertion the old shape fails: it published the old attestation over new code.
        using var db = RoundsDb.Open(_data, Logger.None)!;
        var stored = db.Read(new RoundLocator(provider, session, round))!;
        stored.Subject.HeadSha.Should().Be(pinned);
        HeadShaOfRoundRow(stored.RoundRef!.Value).Should().Be(pinned, "the round read what it attested");
    }

    private string HeadShaOfRoundRow(long roundRef)
    {
        // Read straight out of the projection: `rounds.head_sha` is the SHA handed to the worktree.
        using var raw = new Microsoft.Data.Sqlite.SqliteConnection(
            $"Data Source={Path.Combine(_data, RoundsDb.FileName)};Pooling=False");
        raw.Open();
        using var read = raw.CreateCommand();
        read.CommandText = $"SELECT head_sha FROM rounds WHERE id = {roundRef}";

        return (string)(read.ExecuteScalar() ?? string.Empty);
    }

    // ---------- 3. the crash between finishing and replying ----------

    [Fact]
    public async Task ACrashBetweenFinishingAndReplying_LeavesTheWholeAnswerRecoverable()
    {
        var service = Service();
        await ReachTheCodeStageAsync(service);
        var (provider, session, round, attestation) = await ReserveAsync(service, "token-1");
        var subjectHash = attestation.GetProperty("subjectHash").GetString()!;

        // The process dies the instant the answer is durable and before the caller sees it.
        service.AfterDurableCompletion = () => throw new IOException("the server died holding the reply");

        Script(OneMajor);
        var lost = await service.RunRoundAsync(provider, session, round, Scope);
        IsError(lost).Should().BeTrue("the caller never got its answer");
        var reviewersSpent = _launcher.Reviewers;

        // A new server over the same data directory. All it has is the locator.
        var afterRestart = Service();
        var status = Parse(await afterRestart.RoundStatusAsync(provider, session, round));

        status.GetProperty("state").GetString().Should().Be("completed");
        status.GetProperty("attestation").GetProperty("subjectHash").GetString().Should().Be(subjectHash);
        var recovered = status.GetProperty("review");
        recovered.GetProperty("findings").GetArrayLength().Should().Be(1);
        recovered.GetProperty("findings")[0].GetProperty("title").GetString().Should().Be("token compared with ==");
        recovered.GetProperty("locator").GetProperty("roundId").GetString().Should().Be(round);

        // Recovering it ran nothing, and running it again replays rather than reviews.
        _launcher.Reviewers.Should().Be(reviewersSpent);
        var replay = await afterRestart.RunRoundAsync(provider, session, round, Scope);
        IsError(replay).Should().BeFalse();
        Parse(replay).GetProperty("findings").GetArrayLength().Should().Be(1);
        _launcher.Reviewers.Should().Be(reviewersSpent, "a completed locator replays and starts nothing");
    }
}
