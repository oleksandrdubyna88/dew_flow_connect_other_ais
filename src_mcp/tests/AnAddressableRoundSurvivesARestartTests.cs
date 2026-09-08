using System.Text.Json;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog.Core;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The whole addressable path over a real repository and scripted CLIs: reserve, run, lose the
/// answer, restart, and read it back without spending a second round.
/// </summary>
/// <remarks>
/// <para>The unit tests hold the store's constraints still. This holds the PATH: that the locator a
/// caller stores before dispatch is the one the round completes under, that the answer it recovers
/// after a restart is the same document it lost, and — the expensive half — that recovering it runs
/// no reviewer at all.</para>
/// <para>A "restart" here is a second <see cref="PanelService"/> over the same data directory,
/// which is exactly what a restarted <c>coai-mcp</c> is: the sessions and the database are on disk
/// and nothing else survives.</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class AnAddressableRoundSurvivesARestartTests : IAsyncLifetime
{
    private const string Scope = """
        # SCOPE

        A code round must be nameable before it runs, so a caller whose answer is lost can ask about
        that round and no other. When it is done: the locator is issued before any reviewer starts,
        the answer is stored whole against it, and a read-back after a restart returns the same
        findings. Constraint: the existing review_code call keeps its exact behaviour.
        """;

    private const string Clean = """{"findings": []}""";

    private const string TwoMajors = """
        {"findings": [
          {"severity": "major", "category": "security", "file": "app.cs", "line": 10,
           "title": "token compared with ==", "why": "timing side channel", "fix": "FixedTimeEquals"},
          {"severity": "major", "category": "reliability", "file": "app.cs", "line": 40,
           "title": "no timeout on the outbound call", "why": "a hung peer hangs the request", "fix": "add a timeout"}
        ]}
        """;

    private readonly ProcessLauncher _launcher = new();
    private string _repo = string.Empty;
    private string _data = string.Empty;

    private static string FakeCliExe => Path.Combine(
        AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli");

    public async ValueTask InitializeAsync()
    {
        _repo = Directory.CreateTempSubdirectory("coai-loc-repo-").FullName;
        _data = Directory.CreateTempSubdirectory("coai-loc-data-").FullName;
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
        var result = await _launcher.RunAsync(new ProcessRequest(
            "git", ["-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", .. args], _repo));
        result.ExitCode.Should().Be(0, $"git {string.Join(' ', args)}: {result.StdErr}");
    }

    /// <summary>A server over this data directory. A second one IS a restart.</summary>
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

    /// <summary>Opens the session and gets the plan stage past the gate the code stage needs.</summary>
    private async Task ReachTheCodeStageAsync(PanelService service)
    {
        await service.OpenAsync(_repo, "feature");
        Script(Clean);
        var plan = Parse(await service.ReviewPlanAsync(_repo, "feature", "the plan, and what it must achieve"));
        plan.GetProperty("verdict").GetString().Should().Be("proceed");
        await service.ResolveAsync(_repo, "feature", "[]");
    }

    /// <summary>How many rounds the session's own trail records — the count a second review would move.</summary>
    private async Task<int> CodeRoundsRunAsync(PanelService service) =>
        Parse(await service.StatusAsync(_repo, "feature"))
            .GetProperty("rounds")
            .EnumerateArray()
            .Count(r => r.GetProperty("stage").GetString() == "CodeReview");

    [Fact]
    public async Task TheWholePath_Reserve_Run_Restart_ReadBack()
    {
        var service = Service();
        await ReachTheCodeStageAsync(service);

        // 1. The locator exists BEFORE anything runs, with what it will read pinned to it.
        var reserved = Parse(await service.ReserveRoundAsync(_repo, "feature", "main", "agent-relay-round-1"));
        reserved.GetProperty("state").GetString().Should().Be("not_started");
        reserved.GetProperty("alreadyReserved").GetBoolean().Should().BeFalse();
        var locator = reserved.GetProperty("locator");
        var providerId = locator.GetProperty("providerId").GetString()!;
        var sessionId = locator.GetProperty("sessionId").GetString()!;
        var roundId = locator.GetProperty("roundId").GetString()!;
        providerId.Should().Be(RoundLocator.Provider);
        roundId.Should().NotBeNullOrWhiteSpace();

        var attestation = reserved.GetProperty("attestation");
        var headSha = attestation.GetProperty("headSha").GetString()!;
        var treeSha = attestation.GetProperty("treeSha").GetString()!;
        var subjectHash = attestation.GetProperty("subjectHash").GetString()!;
        headSha.Should().HaveLength(40);
        treeSha.Should().HaveLength(40);
        subjectHash.Should().HaveLength(64);

        // Reserving spends nothing: no reviewer has run and the trail is untouched.
        (await CodeRoundsRunAsync(service)).Should().Be(0);

        // 2. Running it produces the review, carrying the same locator and the same attestation.
        Script(TwoMajors);
        var answer = Parse(await service.RunRoundAsync(providerId, sessionId, roundId, Scope));
        answer.GetProperty("findings").GetArrayLength().Should().Be(2);
        answer.GetProperty("locator").GetProperty("roundId").GetString().Should().Be(roundId);
        answer.GetProperty("attestation").GetProperty("subjectHash").GetString().Should().Be(subjectHash);
        (await CodeRoundsRunAsync(service)).Should().Be(1);

        // 3. A restart, and the round is read back whole — from a server that has never seen it.
        var afterRestart = Service();
        var status = Parse(await afterRestart.RoundStatusAsync(providerId, sessionId, roundId));
        status.GetProperty("state").GetString().Should().Be("completed");
        status.GetProperty("attestation").GetProperty("subjectHash").GetString().Should().Be(subjectHash);
        status.GetProperty("attestation").GetProperty("headSha").GetString().Should().Be(headSha);
        var recovered = status.GetProperty("review");
        recovered.GetProperty("findings").GetArrayLength().Should().Be(2);
        recovered.GetProperty("verdict").GetString().Should().Be(answer.GetProperty("verdict").GetString());
        recovered.GetProperty("gatingCount").GetInt32().Should().Be(answer.GetProperty("gatingCount").GetInt32());
        recovered.GetProperty("findings")[0].GetProperty("title").GetString()
            .Should().Be(answer.GetProperty("findings")[0].GetProperty("title").GetString());

        // And reading it back ran nothing.
        (await CodeRoundsRunAsync(afterRestart)).Should().Be(1);
    }

    [Fact]
    public async Task ALostAnswerIsRecoveredAfterARestart_WithoutASecondReview()
    {
        var service = Service();
        await ReachTheCodeStageAsync(service);
        var reserved = Parse(await service.ReserveRoundAsync(_repo, "feature", "main", "agent-relay-round-1"));
        var locator = reserved.GetProperty("locator");
        var (providerId, sessionId, roundId) = (
            locator.GetProperty("providerId").GetString()!,
            locator.GetProperty("sessionId").GetString()!,
            locator.GetProperty("roundId").GetString()!);

        // The round runs and the caller never sees the reply — the crash window this exists for.
        Script(TwoMajors);
        _ = await service.RunRoundAsync(providerId, sessionId, roundId, Scope);
        var roundsAfterTheOnlyReview = await CodeRoundsRunAsync(service);
        roundsAfterTheOnlyReview.Should().Be(1);

        // A new server. All it has is the locator the caller wrote down before dispatching.
        var afterRestart = Service();
        var status = Parse(await afterRestart.RoundStatusAsync(providerId, sessionId, roundId));

        status.GetProperty("state").GetString().Should().Be("completed");
        status.GetProperty("review").GetProperty("findings").GetArrayLength().Should().Be(2);
        // The whole point: the findings came back and the round budget did not move.
        (await CodeRoundsRunAsync(afterRestart)).Should().Be(roundsAfterTheOnlyReview);
    }

    [Fact]
    public async Task RunningTheSameLocatorTwice_ReplaysTheAnswerAndStartsNothing()
    {
        var service = Service();
        await ReachTheCodeStageAsync(service);
        var reserved = Parse(await service.ReserveRoundAsync(_repo, "feature", "main", "token-1"));
        var locator = reserved.GetProperty("locator");
        var (providerId, sessionId, roundId) = (
            locator.GetProperty("providerId").GetString()!,
            locator.GetProperty("sessionId").GetString()!,
            locator.GetProperty("roundId").GetString()!);

        Script(TwoMajors);
        var first = await service.RunRoundAsync(providerId, sessionId, roundId, Scope);
        var afterFirst = await CodeRoundsRunAsync(service);

        // The caller retried because it did not see the first reply. It must not buy a second round.
        var second = await service.RunRoundAsync(providerId, sessionId, roundId, Scope);

        second.Should().Be(first, "a completed locator replays its stored answer verbatim");
        (await CodeRoundsRunAsync(service)).Should().Be(afterFirst);
    }

    [Fact]
    public async Task ReservingTwiceWithOneToken_IsTheSameRound()
    {
        var service = Service();
        await ReachTheCodeStageAsync(service);

        var first = Parse(await service.ReserveRoundAsync(_repo, "feature", "main", "token-1"));
        var again = Parse(await service.ReserveRoundAsync(_repo, "feature", "main", "token-1"));
        var other = Parse(await service.ReserveRoundAsync(_repo, "feature", "main", "token-2"));

        again.GetProperty("alreadyReserved").GetBoolean().Should().BeTrue();
        again.GetProperty("locator").GetProperty("roundId").GetString()
            .Should().Be(first.GetProperty("locator").GetProperty("roundId").GetString());

        // A different token on the same repo+branch is a different round — the case a session id
        // alone could never separate.
        other.GetProperty("locator").GetProperty("roundId").GetString()
            .Should().NotBe(first.GetProperty("locator").GetProperty("roundId").GetString());
        other.GetProperty("locator").GetProperty("sessionId").GetString()
            .Should().Be(first.GetProperty("locator").GetProperty("sessionId").GetString());
    }

    [Fact]
    public async Task AlocatorThisServerNeverIssued_IsUnknown_NeverNotStarted()
    {
        var service = Service();
        await ReachTheCodeStageAsync(service);
        var reserved = Parse(await service.ReserveRoundAsync(_repo, "feature", "main", "token-1"));
        var sessionId = reserved.GetProperty("locator").GetProperty("sessionId").GetString()!;

        var madeUp = Parse(await service.RoundStatusAsync(RoundLocator.Provider, sessionId, "never-issued"));
        madeUp.GetProperty("state").GetString().Should().Be("unknown");
        madeUp.GetProperty("instruction").GetString().Should().Contain("not evidence");
        madeUp.TryGetProperty("review", out _).Should().BeFalse();

        // Right ids, wrong server: the namespace is part of the name.
        Parse(await service.RoundStatusAsync("some-other-server", sessionId,
                reserved.GetProperty("locator").GetProperty("roundId").GetString()!))
            .GetProperty("state").GetString().Should().Be("unknown");

        // And running a locator that does not exist refuses rather than starting a round.
        Parse(await service.RunRoundAsync(RoundLocator.Provider, sessionId, "never-issued", Scope))
            .GetProperty("error").GetString().Should().Contain("no such round");
        (await CodeRoundsRunAsync(service)).Should().Be(0);
    }

    [Fact]
    public async Task AHalfLocatorNeverReachesADispatch()
    {
        var service = Service();
        await ReachTheCodeStageAsync(service);

        foreach (var (provider, session, round) in ((string, string, string)[])
                 [(RoundLocator.Provider, "s", ""), (RoundLocator.Provider, "", "r"), ("", "s", "r")])
        {
            Parse(await service.RunRoundAsync(provider, session, round, Scope))
                .GetProperty("error").GetString().Should().Contain("all required");
            Parse(await service.RoundStatusAsync(provider, session, round))
                .GetProperty("error").GetString().Should().Contain("all required");
        }

        (await CodeRoundsRunAsync(service)).Should().Be(0);
    }

    [Fact]
    public async Task AMovedBranchRefusesTheRound_AndLeavesItReserved()
    {
        var service = Service();
        await ReachTheCodeStageAsync(service);
        var reserved = Parse(await service.ReserveRoundAsync(_repo, "feature", "main", "token-1"));
        var locator = reserved.GetProperty("locator");
        var (providerId, sessionId, roundId) = (
            locator.GetProperty("providerId").GetString()!,
            locator.GetProperty("sessionId").GetString()!,
            locator.GetProperty("roundId").GetString()!);
        var pinnedHead = reserved.GetProperty("attestation").GetProperty("headSha").GetString()!;

        // The developer commits underneath the reservation.
        await File.WriteAllTextAsync(Path.Combine(_repo, "app.cs"), "something else entirely\n");
        await Git("add", ".");
        await Git("commit", "-m", "moved underneath");

        Script(TwoMajors);
        var refused = Parse(await service.RunRoundAsync(providerId, sessionId, roundId, Scope));

        refused.GetProperty("error").GetString().Should().Contain("moved since this round was reserved");
        refused.GetProperty("error").GetString().Should().Contain(pinnedHead);
        // Nothing ran, and the reservation is still spendable on the subject it named.
        (await CodeRoundsRunAsync(service)).Should().Be(0);
        Parse(await service.RoundStatusAsync(providerId, sessionId, roundId))
            .GetProperty("state").GetString().Should().Be("not_started");
    }

    [Fact]
    public async Task ReserveRefusesWithoutASession_AndWithoutAToken()
    {
        var service = Service();

        // No session: the same refusal every other tool gives, and no row written.
        Parse(await service.ReserveRoundAsync(_repo, "feature", "main", "token-1"))
            .GetProperty("error").GetString().Should().Contain("call open first");

        await ReachTheCodeStageAsync(service);
        Parse(await service.ReserveRoundAsync(_repo, "feature", "main", "  "))
            .GetProperty("error").GetString().Should().Contain("clientToken is required");

        // A base that does not resolve is refused before anything is reserved: an attestation with
        // a blank in it could never be checked.
        Parse(await service.ReserveRoundAsync(_repo, "feature", "no-such-base", "token-1"))
            .GetProperty("error").GetString().Should().Contain("cannot resolve");
    }

    [Fact]
    public async Task TheOldReviewCodeCall_IsUnchanged_AndNeedsNoLocator()
    {
        var service = Service();
        await ReachTheCodeStageAsync(service);

        // Exactly the call every existing caller makes, with no reservation anywhere.
        Script(TwoMajors);
        var answer = Parse(await service.ReviewCodeAsync(_repo, "feature", "main", Scope));

        answer.GetProperty("findings").GetArrayLength().Should().Be(2);
        answer.GetProperty("verdict").GetString().Should().NotBeNullOrEmpty();
        // The old shape gains nothing it did not have: the locator and attestation belong to the
        // tools that promise them.
        answer.TryGetProperty("locator", out _).Should().BeFalse();
        answer.TryGetProperty("attestation", out _).Should().BeFalse();
        (await CodeRoundsRunAsync(service)).Should().Be(1);

        // And `status` still answers about the session, in the shape it always has.
        var status = Parse(await service.StatusAsync(_repo, "feature"));
        status.GetProperty("sessionId").GetString().Should().NotBeNullOrEmpty();
        status.GetProperty("rounds").GetArrayLength().Should().BeGreaterThan(0);
        status.TryGetProperty("locator", out _).Should().BeFalse();
    }
}
