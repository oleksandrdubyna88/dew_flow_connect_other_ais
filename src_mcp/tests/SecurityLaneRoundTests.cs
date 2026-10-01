using System.Text.Json;
using CoaiMcp.Core.Context;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Notices;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Core.Security;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>Real round engine, git and SQLite; only the paid reviewer is replaced by FakeCli.</summary>
public sealed class SecurityLaneRoundTests : IAsyncLifetime
{
    private readonly ProcessLauncher _real = new();
    private readonly string _data = Directory.CreateTempSubdirectory("coai-security-test-").FullName;
    private TempGitRepo _repo = null!;
    private static string Exe => Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli");
    private static readonly FileDiff[] Files = [new("Query.cs", "@@ -1 +1 @@\n-old\n+database.Query(value);")];

    internal sealed class Reviewers(ProcessLauncher real, bool failOrdinary, bool failLane) : IProcessLauncher
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            if (request.Executable != Exe) return real.RunAsync(request, ct);
            var lane = request.Arguments.Any(a => a.Contains("finding-schema-security", StringComparison.Ordinal));
            var env = request.Environment.ToDictionary();
            env["FAKECLI_MODE"] = "vendor";
            env["FAKECLI_STDOUT"] = lane ? "{\"status\":\"SECURE\",\"findings\":[]}" : "{\"findings\":[]}";
            env["FAKECLI_OUTFILE_TEXT"] = env["FAKECLI_STDOUT"];
            env["FAKECLI_EXIT"] = (lane ? failLane : failOrdinary) ? "7" : "0";
            return real.RunAsync(request with { Environment = env }, ct);
        }
    }

    public async ValueTask InitializeAsync()
    {
        _repo = await TempGitRepo.InitAsync(_real);
        await _repo.WriteAsync("Query.cs", "class Query { int F() => 1; }");
        await _repo.CommitAsync("fixture");
        Directory.CreateDirectory(Path.Combine(_data, "prompts"));
        await File.WriteAllTextAsync(Path.Combine(_data, "prompts", "redteam-general.md"), "Fixture: return an empty findings array.");
    }

    public async ValueTask DisposeAsync()
    {
        await _repo.DisposeAsync();
        Directory.Delete(_data, true);
    }

    private PanelService Service(bool failOrdinary = false, bool failLane = false, Noticing? noticing = null,
        TimeSpan roundTimeout = default)
    {
        var lane = SecurityLaneSetting.Parse("""
            {"enabled":true,"prompts":[{"id":"redteam-general"}],"runs":[{"vendor":"codex","prompt":"redteam-general"}]}
            """, [new("codex")]);
        var settings = new PanelSettings
        {
            DataDir = _data,
            Providers = [new("codex") { ExecutablePath = Exe }],
            SecurityLane = lane,
            Rounds = new PanelConfig() { SecurityLane = lane.Gate },
            ReviewerTimeout = TimeSpan.FromSeconds(15),
            RoundTimeout = roundTimeout,
            CodeWorkspace = "none",
        };
        return new(settings, VaultKeys.None("fixture"), default, new Reviewers(_real, failOrdinary, failLane),
            Serilog.Core.Logger.None, noticing ?? Noticing.None);
    }

    private Task<string> Run(PanelService service, bool laneOnly = false)
    {
        var stage = new StageRun(RoundMachine.BeginCodeRound, false, Stage.CodeReview, false,
            (session, path, _, _) => Task.FromResult(service.Roster.BuildWork(laneOnly ? [] : [RoleCatalog.ArchitectureRole], path,
                "A committed fixture change", laneOnly ? 2 : 1, Stage.CodeReview, false, securityFiles: Files)))
        {
            Session = new SessionRule.CreateIfAbsent(() => new PersistedSession(
                new SessionState("security", _repo.Path, "main", service.Settings.Rounds)
                { Stage = Stage.CodeReview, PlanProceeded = true, RoundsRunThisStage = laneOnly ? 1 : 0 }, [])
            { PlanText = "Verify the committed fixture change without executing evidence." }),
        };
        return service.Engine.RunStageAsync(_repo.Path, "main", "Verify the committed fixture change.", stage, CancellationToken.None);
    }

    [Fact]
    public async Task A_failed_lane_only_round_does_not_pass_after_ordinary_budgets_are_spent()
    {
        using var answer = JsonDocument.Parse(await Run(Service(failLane: true), laneOnly: true));
        answer.RootElement.TryGetProperty("error", out _).Should().BeFalse(answer.RootElement.ToString());
        answer.RootElement.GetProperty("verdict").GetString().Should().Be("call_human");
    }

    [Fact]
    public async Task A_clean_lane_only_second_round_passes_after_ordinary_budgets_are_spent()
    {
        using var answer = JsonDocument.Parse(await Run(Service(), laneOnly: true));
        answer.RootElement.TryGetProperty("error", out _).Should().BeFalse(answer.RootElement.ToString());
        answer.RootElement.GetProperty("verdict").GetString().Should().Be("proceed");
    }

    [Fact]
    public async Task The_round_deadline_cancels_context_collection_before_reviewers_start()
    {
        var service = Service(roundTimeout: TimeSpan.FromSeconds(1));
        var cancelled = false;
        var stage = new StageRun(RoundMachine.BeginCodeRound, false, Stage.CodeReview, false,
            async (_, _, _, token) =>
            {
                try { await Task.Delay(TimeSpan.FromSeconds(3), token); }
                catch (OperationCanceledException) { cancelled = token.IsCancellationRequested; throw; }
                return new RoundWork([], []);
            })
        {
            Session = new SessionRule.CreateIfAbsent(() => new PersistedSession(
                new SessionState("deadline", _repo.Path, "main", service.Settings.Rounds)
                { Stage = Stage.CodeReview, PlanProceeded = true }, [])),
        };
        using var answer = JsonDocument.Parse(await service.Engine.RunStageAsync(_repo.Path, "main",
            "Verify cancellation of slow context collection.", stage, CancellationToken.None));
        cancelled.Should().BeTrue("the source builder must receive the round's deadline token");
        answer.RootElement.TryGetProperty("error", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Ordinary_failure_cannot_be_rescued_by_a_clean_lane_answer()
    {
        using var answer = JsonDocument.Parse(await Run(Service(failOrdinary: true)));
        answer.RootElement.TryGetProperty("error", out _).Should().BeFalse(answer.RootElement.ToString());
        answer.RootElement.GetProperty("verdict").GetString().Should().Be("call_human");
    }

    [Fact]
    public async Task Silent_lane_is_named_in_the_reply_persisted_summary_and_notice()
    {
        var notices = new List<ServerNotice>();
        using var answer = JsonDocument.Parse(await Run(Service(failLane: true,
            noticing: new(n => { notices.Add(n); return true; }, Serilog.Core.Logger.None))));
        answer.RootElement.TryGetProperty("error", out _).Should().BeFalse(answer.RootElement.ToString());
        answer.RootElement.GetProperty("instruction").GetString().Should().StartWith("Security lane incomplete");
        answer.RootElement.GetProperty("verdict").GetString().Should().Be("proceed");
        var session = new SessionStore(_data).Load(_repo.Path, "main")!;
        session.Rounds.Last().Reviewers.Should().Contain("Security lane incomplete");
        notices.Should().Contain(n => n.Code == ServerNoticeCodes.SecurityLaneIncomplete);
    }

    [Fact]
    public void Pairs_keep_their_own_identity_and_rerun_when_ordinary_budgets_are_spent()
    {
        var work = Service().Roster.BuildWork([], _repo.Path, "fixture", 2, Stage.CodeReview, false, securityFiles: Files);
        work.Reviewers.Should().ContainSingle().Which.Invocation.Role.Should().Be("redteam-general");
        work.Reviewers[0].IsSecurity.Should().BeTrue();
        var spent = Service().Roster.BuildWork([], _repo.Path, "fixture", 3, Stage.CodeReview, false, securityFiles: Files);
        spent.Reviewers.Should().BeEmpty();
        spent.NotAsked.Should().Contain(s => s.Reason.Contains("budget spent"));
    }

    [Fact]
    public void Ordinary_history_does_not_claim_security_evidence()
    {
        using var db = RoundsDb.Open(_data, Serilog.Core.Logger.None)!;
        var found = new Finding(Severity.Minor, Category.Security, "Query.cs", 1,
            "Fixture", "Evidence", "Correction", ["codex"]);
        db.RecordRound(new("s", _repo.Path, "main", new()), new("CodeReview", 1, "proceed", 0, "fixture", DateTime.UtcNow), [found]);
        RoundsQuery.FindingsOf(_data, "s", "CodeReview", 1).Findings.Single().SecurityEvidence.Should().BeNull();
    }

    [Fact]
    public void Security_evidence_survives_the_history_database_and_read_side()
    {
        using var db = RoundsDb.Open(_data, Serilog.Core.Logger.None)!;
        var found = SecurityEvidence.Attribute(new Finding(Severity.Major, Category.Security, "Query.cs", 1,
            "Fixture", "Evidence", "Correction", ["codex"])
        { AttackEvidence = new("Fixture request", "Missing fixture check", "Other tenant's fixture row") }, "codex", "redteam-general");
        db.RecordRound(new("s", _repo.Path, "main", new()), new("CodeReview", 1, "proceed", 0, "fixture", DateTime.UtcNow), [found]);
        var read = RoundsQuery.FindingsOf(_data, "s", "CodeReview", 1).Findings.Single();
        read.SecurityEvidence!.CapReason.Should().NotBeEmpty();
        read.SecurityEvidence.AlsoSeenBy.Single().Prompt.Should().Be("redteam-general");
        read.SecurityEvidence.AttackEvidence.Should().Be(found.AttackEvidence);
        read.SecurityEvidence.AlsoSeenBy.Single().AttackEvidence.Should().Be(found.AttackEvidence);
    }
}
