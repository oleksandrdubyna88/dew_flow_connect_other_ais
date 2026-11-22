using System.Text.Json;
using CoaiMcp.Core.Context;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Notices;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Core.Security;
using CoaiMcp.Normalizer;
using CoaiMcp.Runners.Collecting;
using CoaiMcp.Runners.Feature;
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
    private readonly string _data = TempDir.For("coai-security-test-").Path;
    private TempGitRepo _repo = null!;
    private static string Exe => Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli");
    private static readonly FileDiff[] Files = [new("Query.cs", "@@ -1 +1 @@\n-old\n+database.Query(value);")];

    internal sealed class Reviewers(ProcessLauncher real, bool failOrdinary, bool failLane, string? laneAnswer = null) : IProcessLauncher
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            if (request.Executable != Exe) return real.RunAsync(request, ct);
            var lane = request.Arguments.Any(a => a.Contains("finding-schema-security", StringComparison.Ordinal));
            var env = request.Environment.ToDictionary();
            env["FAKECLI_MODE"] = "vendor";
            env["FAKECLI_STDOUT"] = StdoutOf(lane, laneAnswer);
            env["FAKECLI_OUTFILE_TEXT"] = env["FAKECLI_STDOUT"];
            env["FAKECLI_EXIT"] = (lane ? failLane : failOrdinary) ? "7" : "0";
            return real.RunAsync(request with { Environment = env }, ct);
        }

        private static string StdoutOf(bool lane, string? laneAnswer) =>
            lane ? laneAnswer ?? "{\"status\":\"SECURE\",\"findings\":[]}" : "{\"findings\":[]}";
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
        TimeSpan roundTimeout = default, SecurityLaneSetting? laneSetting = null, PanelConfig? rounds = null,
        bool providerEnabled = true, ProviderSettings? secondRow = null, string? laneAnswer = null)
    {
        var lane = laneSetting ?? SecurityLaneSetting.Parse("""
            {"enabled":true,"prompts":[{"id":"redteam-general"}],"runs":[{"vendor":"codex","prompt":"redteam-general"}]}
            """, [new("codex")]);
        var settings = new PanelSettings
        {
            DataDir = _data,
            Providers = [new("codex") { ExecutablePath = Exe, Feature = true, Enabled = providerEnabled },
                .. secondRow is null ? Array.Empty<ProviderSettings>() : [secondRow]],
            SecurityLane = lane,
            Rounds = (rounds ?? new PanelConfig()) with { SecurityLane = lane.Gate },
            ReviewerTimeout = TimeSpan.FromSeconds(15),
            RoundTimeout = roundTimeout,
            CodeWorkspace = "none",
        };
        return new(settings, VaultKeys.None("fixture"), default, new Reviewers(_real, failOrdinary, failLane, laneAnswer),
            Serilog.Core.Logger.None, noticing ?? Noticing.None);
    }

    private RoundWork SqlWork(FileDiff[] files)
    {
        var lane = SecurityLaneSetting.Parse("""
            {"enabled":true,"runs":[{"vendor":"codex","prompt":"redteam-sql"}]}
            """, [new("codex")]);
        return Service(laneSetting: lane).Roster.BuildWork([RoleCatalog.ArchitectureRole], _repo.Path,
            "fixture", 1, Stage.CodeReview, false, securityFiles: files, codexTiers: CodexTiers.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Uninspected_changes_cannot_be_reported_as_a_safe_trigger_skip(bool fileCountLimit)
    {
        FileDiff[] files = fileCountLimit
            ? [.. Enumerable.Range(0, SecuritySignals.MaxFiles).Select(i => new FileDiff($"f{i}.cs", "+return 42;")),
                new("z.cs", "+database.Query(value);")]
            : [new("large.cs", new string('a', SecuritySignals.MaxFileCharacters) + "\n+database.Query(value);")];
        var work = SqlWork(files);
        work.Excluded.Should().Contain(e => e.Role == "redteam-sql" && e.Reason.Contains("trigger coverage incomplete"));
        work.NotAsked.Should().NotContain(e => e.Role.Contains("redteam-sql"));
        SecurityRound.Clause([], work).Should().Contain("incomplete");
    }

    [Fact]
    public void A_fully_inspected_nonmatching_change_still_skips_the_preset()
    {
        var work = SqlWork([new("small.cs", "+return 42;")]);
        work.Excluded.Should().NotContain(e => e.Role == "redteam-sql");
        work.NotAsked.Should().Contain(e => e.Role.Contains("redteam-sql") && e.Reason.Contains("no matching trigger"));
    }

    [Fact]
    public void A_known_match_still_runs_when_another_file_is_uninspected()
    {
        var work = SqlWork([Files[0], new("large.cs", new string('a', SecuritySignals.MaxFileCharacters + 1))]);
        work.Reviewers.Should().Contain(w => w.IsSecurity && w.Invocation.Role == "redteam-sql");
        SecuritySignals.Classify([new("large.cs", new string('a', SecuritySignals.MaxFileCharacters + 1))])
            .Single().Diff.Text.Should().BeEmpty("oversized source must not enter a context pack");
    }

    /// <summary>
    /// D4 / S7 of <c>todo/PLAN_the_suite_is_deterministic_and_leaves_no_temp.md</c>: the folders a roster makes empty — the ordinary round's repair and workspace folders
    /// AND the lane's own workspace folder — are all registered with the scratch the roster was given, and gone when it ends. The lane's reaches the same scratch
    /// through <c>ordinary.Scratch</c>, not through a global.
    /// </summary>
    [Fact]
    public void TheEmptyFoldersOfTheOrdinaryRoundAndOfTheLane_AreAllRemovedWithTheScratchTheRosterWasGiven()
    {
        var lane = SecurityLaneSetting.Parse("""
            {"enabled":true,"runs":[{"vendor":"codex","prompt":"redteam-sql"}]}
            """, [new("codex")]);
        using var dir = TempDir.For("security-empty-folders-");
        var scratch = RoundScratch.At(dir.At("round"), _ => { });
        var work = Service(laneSetting: lane).Roster.BuildWork([RoleCatalog.ArchitectureRole], _repo.Path,
            "fixture", 1, Stage.CodeReview, true, securityFiles: Files, codexTiers: CodexTiers.None, scratch: scratch);
        string[] prefixes = ["coai-repair-", "coai-noworkspace-"];
        var made = work.Reviewers
            .SelectMany(w => new[] { w.Invocation, w.Repair }.OfType<ReviewerInvocation>())
            .Select(i => i.Request.WorkingDirectory)
            .Where(folder => prefixes.Any(prefix => Path.GetFileName(folder).StartsWith(prefix, StringComparison.Ordinal)))
            .Distinct()
            .ToList();

        made.Should().HaveCountGreaterThanOrEqualTo(3, "the ordinary round's repair and workspace folders, and the lane's workspace folder");
        made.Should().OnlyContain(folder => Directory.Exists(folder), "they are made when the roster is built");
        work.Reviewers.Should().Contain(w => w.IsSecurity);

        work.Scratch.Dispose();

        made.Should().OnlyContain(folder => !Directory.Exists(folder), "every one of them was registered with the round's scratch");
    }

    private Task<string> Run(PanelService service, bool laneOnly = false, FileDiff[]? files = null)
    {
        var stage = new StageRun(RoundMachine.BeginCodeRound, false, Stage.CodeReview, false,
            (session, path, _, _) => Task.FromResult(service.Roster.BuildWork(laneOnly ? [] : [RoleCatalog.ArchitectureRole], path,
                "A committed fixture change", laneOnly ? 2 : 1, Stage.CodeReview, false, securityFiles: files ?? Files, codexTiers: CodexTiers.None)))
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
        answer.RootElement.GetProperty("verdict").GetString().Should().Be("not_reviewed");
    }

    [Fact]
    public async Task A_clean_lane_only_second_round_passes_after_ordinary_budgets_are_spent()
    {
        using var answer = JsonDocument.Parse(await Run(Service(), laneOnly: true));
        answer.RootElement.TryGetProperty("error", out _).Should().BeFalse(answer.RootElement.ToString());
        answer.RootElement.GetProperty("verdict").GetString().Should().Be("proceed");
    }

    /// <summary>A lane answer with one blocking finding that carries a complete reproduction, so it is not capped and it gates.</summary>
    private const string LaneBlocks = """
        {"status":"FINDINGS","findings":[{"severity":"blocking","category":"security","file":"Query.cs","line":1,
        "title":"Fixture tenant boundary regression","why":"Fixture evidence","fix":"Restore tenant scope",
        "trigger":"Read endpoint with another tenant's id","mechanism":"Tenant filter removed",
        "consequence":"Another tenant's row is returned",
        "reproduction":{"preconditions":"Two tenants","steps":"Read tenant B's id as tenant A","expected":"Refused","actual":"Row returned"}}]}
        """;

    /// <summary>
    /// A code round under policy auto whose ordinary reviewer failed while the security lane answered with a blocking finding is
    /// <c>not_reviewed</c> — nobody ORDINARY answered — but the finding is pending, and the instruction must not say there is nothing to
    /// decide: <c>resolve</c> refuses an empty list while a finding awaits (research/PLAN_auto_never_asks_a_person_E1.md, § 8, the S2 fix).
    /// </summary>
    [Fact]
    public async Task Under_auto_a_round_only_the_lane_answered_says_what_awaits_a_decision_and_resolve_advances_it()
    {
        var service = Service(failOrdinary: true, laneAnswer: LaneBlocks, rounds: PanelConfig.Uniform(3, 2));
        using var answer = JsonDocument.Parse(await Run(service));
        var reply = answer.RootElement;
        reply.TryGetProperty("error", out _).Should().BeFalse(reply.ToString());
        reply.GetProperty("verdict").GetString().Should().Be("not_reviewed", reply.ToString());
        var instruction = reply.GetProperty("instruction").GetString()!;
        instruction.Should().NotContain("nothing to decide", "a finding of the security lane awaits a decision, and resolve refuses an empty list")
            .And.Contain("1 finding(s)").And.Contain("security lane").And.Contain("resolve with a decision for each");
        var note = new SessionStore(_data).Load(_repo.Path, "main")!.Rounds[^1].Note;
        note.Should().Contain("no reviewer answered").And.Contain("policy auto:").And.NotContain("nothing was reviewed");

        var refused = JsonDocument.Parse(await service.ResolveAsync(_repo.Path, "main", "[]")).RootElement;
        refused.TryGetProperty("error", out _).Should().BeTrue("an empty resolve is refused while the lane's finding awaits");

        var decided = JsonDocument.Parse(await service.ResolveAsync(_repo.Path, "main",
            """[{"finding":0,"action":"accept"}]""")).RootElement;
        decided.TryGetProperty("error", out _).Should().BeFalse(decided.ToString());
        new SessionStore(_data).Load(_repo.Path, "main")!.State.Stage.Should().NotBe(Stage.CodeReview, "resolve with a decision advances the stage");
    }

    /// <summary>The feature stage's twin: round 2 of a feature review is the cap, and a lane finding stands beside a failed ordinary reviewer.</summary>
    [Fact]
    public async Task Under_auto_a_feature_round_at_its_cap_only_the_lane_answered_says_the_lane_finding_stands()
    {
        var service = Service(failOrdinary: true, laneAnswer: LaneBlocks, rounds: PanelConfig.Uniform(3, 2));
        using var answer = JsonDocument.Parse(await RunFeature(service, roundsRun: 1, ground: SecondRoundGround.ReviewerFailure));
        var reply = answer.RootElement;
        reply.TryGetProperty("error", out _).Should().BeFalse(reply.ToString());
        reply.GetProperty("verdict").GetString().Should().Be("not_reviewed", reply.ToString());
        reply.GetProperty("instruction").GetString()!.Should().NotContain("nothing to decide").And.Contain("1 finding(s)");
        new SessionStore(_data).Load(_repo.Path, SessionKey.FeatureBranch, "", "plan.md")!.Rounds[^1].Note
            .Should().Contain("no reviewer answered").And.Contain("the security lane's 1 finding(s) stand and await a decision")
            .And.NotContain("blocking finding still stands").And.NotContain("nothing was reviewed");
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
        answer.RootElement.GetProperty("verdict").GetString().Should().Be("not_reviewed");
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
        var work = Service().Roster.BuildWork([], _repo.Path, "fixture", 2, Stage.CodeReview, false, securityFiles: Files, codexTiers: CodexTiers.None);
        work.Reviewers.Should().ContainSingle().Which.Invocation.Role.Should().Be("redteam-general");
        work.Reviewers[0].IsSecurity.Should().BeTrue();
        var spent = Service().Roster.BuildWork([], _repo.Path, "fixture", 3, Stage.CodeReview, false, securityFiles: Files, codexTiers: CodexTiers.None);
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

    private static SecurityLaneSetting Lane(string json) => SecurityLaneSetting.Parse(json, [new("codex")]);

    private async Task<string> RunFeature(PanelService service, int roundsRun = 0,
        SecondRoundGround ground = SecondRoundGround.None, FileDiff[]? files = null)
    {
        var head = await _repo.HeadAsync();
        var stage = new StageRun(RoundMachine.BeginFeatureRound, false, Stage.FeatureReview, false,
            (session, path, _, _) =>
            {
                var round = session.State.RoundsRunThisStage + 1;
                return Task.FromResult(service.Roster.BuildWork(service.Settings.Rounds.RolesForRound(Stage.FeatureReview, round),
                    path, "A committed fixture feature", round, Stage.FeatureReview, false, securityFiles: files ?? Files, codexTiers: CodexTiers.None));
            })
        {
            Feature = "plan.md",
            Head = head,
            WhenNobody = NobodyPolicy.RecordSkip,
            Session = new SessionRule.CreateIfAbsent(() => new PersistedSession(
                new SessionState("feature", _repo.Path, SessionKey.FeatureBranch, service.Settings.Rounds)
                { Stage = Stage.FeatureReview, Feature = "plan.md", RoundsRunThisStage = roundsRun, SecondRound = ground }, [])
            { PlanText = "Review the committed fixture feature." }),
        };
        return await service.Engine.RunStageAsync(_repo.Path, SessionKey.FeatureBranch, "Review the committed fixture feature.",
            stage, CancellationToken.None);
    }

    [Fact]
    public async Task A_lane_budget_does_not_buy_a_failed_ordinary_reviewer_a_second_feature_round()
    {
        var lane = Lane("""{"enabled":true,"maxRounds":2,"prompts":[{"id":"redteam-general"}],"runs":[{"vendor":"codex","prompt":"redteam-general"}]}""");
        using var answer = JsonDocument.Parse(await RunFeature(
            Service(failOrdinary: true, laneSetting: lane, rounds: PanelConfig.Uniform(1, 5))));
        answer.RootElement.TryGetProperty("error", out _).Should().BeFalse(answer.RootElement.ToString());
        answer.RootElement.GetProperty("verdict").GetString().Should().Be("not_reviewed",
            "the operator gave the ordinary feature roles one round, and the lane's own budget is not theirs to spend");
        answer.RootElement.GetProperty("instruction").GetString().Should().Contain("reviewer answered");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_round_admitted_only_for_the_lane_with_nothing_to_run_is_recorded_not_refused(bool laneStillEnabled)
    {
        // Round 2 was admitted for a lane finding; since then the fix removed the trigger, or the lane was switched off.
        var lane = Lane($$"""{"enabled":{{(laneStillEnabled ? "true" : "false")}},"runs":[{"vendor":"codex","prompt":"redteam-sql"}]}""");
        using var answer = JsonDocument.Parse(await Run(Service(laneSetting: lane), laneOnly: true,
            files: [new("Fixed.cs", "@@ -1 +1 @@\n-old\n+return 42;")]));
        answer.RootElement.TryGetProperty("error", out _).Should().BeFalse(answer.RootElement.ToString());
        answer.RootElement.GetProperty("verdict").GetString().Should().Be("not_reviewed",
            "nothing reviewed the lane's round, so it is said out loud rather than the gate passing over nobody");
        answer.RootElement.ToString().Should().Contain(SecurityCatalog.Gate + " was not asked");
        var session = new SessionStore(_data).Load(_repo.Path, "main")!;
        session.Rounds.Should().ContainSingle("the round is on the record, with its reason");
        session.State.AdvanceOnResolve.Should().BeTrue("nobody is held: the stage moves on resolve");
    }

    [Fact]
    public async Task A_feature_round_admitted_for_a_lane_finding_with_no_lane_work_is_recorded_not_refused()
    {
        var lane = Lane("""{"enabled":true,"runs":[{"vendor":"codex","prompt":"redteam-sql"}]}""");
        var service = Service(laneSetting: lane, rounds: PanelConfig.Uniform(1, 5));
        using var answer = JsonDocument.Parse(await RunFeature(service, roundsRun: 1, ground: SecondRoundGround.BlockingFinding,
            files: [new("Fixed.cs", "@@ -1 +1 @@\n-old\n+return 42;")]));
        answer.RootElement.TryGetProperty("error", out _).Should().BeFalse(answer.RootElement.ToString());
        answer.RootElement.GetProperty("verdict").GetString().Should().Be("not_reviewed",
            "a refusal would leave the admitted round standing, and every later call would meet it again");
        new SessionStore(_data).Load(_repo.Path, SessionKey.FeatureBranch, "", "plan.md")!.State.AdvanceOnResolve.Should().BeTrue();
    }

    [Fact]
    public async Task An_empty_override_keeps_the_shipped_security_prompt()
    {
        await File.WriteAllTextAsync(Path.Combine(_data, "prompts", "redteam-sql.md"), "  \n\t\n");
        var work = SqlWork(Files);
        work.Excluded.Should().NotContain(e => e.Role == "redteam-sql");
        work.Reviewers.Should().Contain(w => w.IsSecurity && w.Invocation.Role == "redteam-sql");
    }

    [Fact]
    public async Task A_custom_prompt_with_an_empty_override_is_still_refused()
    {
        await File.WriteAllTextAsync(Path.Combine(_data, "prompts", "redteam-custom.md"), "   ");
        var lane = Lane("""{"enabled":true,"prompts":[{"id":"redteam-custom","triggers":[]}],"runs":[{"vendor":"codex","prompt":"redteam-custom"}]}""");
        var work = Service(laneSetting: lane).Roster.BuildWork([RoleCatalog.ArchitectureRole], _repo.Path, "fixture", 1,
            Stage.CodeReview, false, securityFiles: Files, codexTiers: CodexTiers.None);
        work.Excluded.Should().Contain(e => e.Role == "redteam-custom" && e.Reason.Contains("prompt unavailable"));
    }

    [Theory]
    [InlineData("""{"enabled":true,"runs":[{"vendor":"codex","prompt":"redteam-sql","context":"whole-repo"}]}""", "context must be slice or diff")]
    [InlineData("""{"enabled":true,"prompts":[{"id":"redteam-sql","triggers":[]}],"runs":[{"vendor":"codex","prompt":"redteam-sql"}]}""", "requires at least one trigger")]
    public void A_misconfigured_pairing_is_reported_as_unable_to_run_rather_than_not_due(string json, string reason)
    {
        var work = Service(laneSetting: Lane(json)).Roster.BuildWork([RoleCatalog.ArchitectureRole], _repo.Path, "fixture", 1,
            Stage.CodeReview, false, securityFiles: Files, codexTiers: CodexTiers.None);
        work.Excluded.Should().Contain(e => e.Role == "redteam-sql" && e.Reason.Contains(reason));
        work.NotAsked.Should().NotContain(s => s.Role.Contains("redteam-sql"));
        SecurityRound.Clause([], work).Should().StartWith("Security lane incomplete: configured pairings could not run");
    }

    private sealed class CountingGit(ProcessLauncher real) : IProcessLauncher
    {
        private int _calls;
        public int Calls => _calls;

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _calls);
            return real.RunAsync(request, ct);
        }
    }

    [Theory]
    [InlineData(1, true, true)]
    [InlineData(3, true, false)]
    [InlineData(1, false, false)]
    public async Task Source_is_read_only_for_pairings_that_will_run_this_round(int round, bool rowEnabled, bool read)
    {
        var lane = Lane("""{"enabled":true,"runs":[{"vendor":"codex","prompt":"redteam-sql","context":"slice"}]}""");
        var service = Service(laneSetting: lane, providerEnabled: rowEnabled);
        var git = new CountingGit(_real);
        var resolver = new SourceResolver(new GitHistory(git), new TreeSitterOutliner(), _repo.Path, await _repo.HeadAsync());

        var sources = await SecuritySources.ReadAsync(service.Settings.SecurityLane,
            service.Roster.Security().Due(Stage.CodeReview, round), Files, resolver, CancellationToken.None);

        (git.Calls > 0).Should().Be(read, read ? "a due pairing reads its source" : "a pairing that cannot run this round must not spend git reads");
        sources.Should().HaveCount(read ? 1 : 0);
    }

    [Fact]
    public async Task A_disabled_lane_row_beside_a_working_one_reads_no_source()
    {
        var lane = SecurityLaneSetting.Parse("""{"enabled":true,"runs":[{"vendor":"gemini","prompt":"redteam-sql","context":"slice"}]}""",
            [new("codex"), new("gemini")]);
        var service = Service(laneSetting: lane, secondRow: new("gemini") { ExecutablePath = Exe, Enabled = false });
        var git = new CountingGit(_real);
        var resolver = new SourceResolver(new GitHistory(git), new TreeSitterOutliner(), _repo.Path, await _repo.HeadAsync());

        service.Roster.Security().Due(Stage.CodeReview, 1).Should().BeEmpty();
        await SecuritySources.ReadAsync(service.Settings.SecurityLane, service.Roster.Security().Due(Stage.CodeReview, 1),
            Files, resolver, CancellationToken.None);

        git.Calls.Should().Be(0);
        SqlWorkFor(service).Excluded.Should().Contain(e => e.Provider == "gemini" && e.Reason == "reviewer row disabled",
            "the roster reports the same row it kept the source reader from");
    }

    private RoundWork SqlWorkFor(PanelService service) => service.Roster.BuildWork([RoleCatalog.ArchitectureRole], _repo.Path,
        "fixture", 1, Stage.CodeReview, false, securityFiles: Files, codexTiers: CodexTiers.None);
}
