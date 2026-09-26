using System.Text.Json;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog.Core;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The skip-against-block truth table of the feature stage (§4.4, D1), pinned through the engine's
/// own seam before any tool reaches it.
/// </summary>
/// <remarks>
/// <para><b>Blocking iff it can run.</b> A stage nobody serves used to be — and for every other stage
/// still is — a REFUSAL: a round with no reviewer would pass having reviewed nothing. The feature gate
/// is different by decision: it is optional per vendor, so "nobody is ticked" is the ordinary state of
/// a machine that never asked for it, and refusing would block a release on a review nobody
/// configured. So a feature round with nobody to ask is RECORDED as <c>skipped</c> with its reason,
/// leaves the session exactly as it was, and does not block — while a round whose reviewers all
/// FAILED still calls a person, because otherwise any network failure silently bypasses the gate.</para>
/// <para>The engine is driven directly, with a <see cref="StageRun"/> shaped the way the feature
/// stage's entry point (S2.2) will shape it: the session created under the engine's own claim, the
/// head resolved from a ref the session's constant branch cannot name, and <c>RecordSkip</c> for a
/// roster that comes back empty.</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class AFeatureRoundIsSkippedNotRefusedTests : IAsyncLifetime
{
    private const string Plan = "todo/PLAN_feature_review.md";

    private const string PlanText = """
        # PLAN — the feature under review

        Three epics: the core, the engine, the store. Every one shipped; this is the whole.
        """;

    private const string Clean = """{"findings": []}""";

    private readonly ProcessLauncher _launcher = new();
    private string _repo = string.Empty;
    private string _data = string.Empty;
    private string _sha = string.Empty;

    private static string FakeCliExe => Path.Combine(
        AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli");

    public async ValueTask InitializeAsync()
    {
        _repo = Directory.CreateTempSubdirectory("coai-feature-repo-").FullName;
        _data = Directory.CreateTempSubdirectory("coai-feature-data-").FullName;
        await Git("init", "-b", "main");
        await File.WriteAllTextAsync(Path.Combine(_repo, "app.cs"), "v1\n");
        await Git("add", ".");
        await Git("commit", "-m", "base");
        await Git("checkout", "-b", "feature");
        await File.WriteAllTextAsync(Path.Combine(_repo, "app.cs"), "v2\n");
        await Git("add", ".");
        await Git("commit", "-m", "the feature");
        _sha = (await Run("rev-parse", "feature")).Trim();
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
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        return ValueTask.CompletedTask;
    }

    private static void Script(string answer, int exit = 0, string stderr = "")
    {
        Environment.SetEnvironmentVariable("FAKECLI_STDOUT", answer);
        Environment.SetEnvironmentVariable("FAKECLI_OUTFILE_TEXT", answer);
        Environment.SetEnvironmentVariable("FAKECLI_EXIT", exit.ToString());
        Environment.SetEnvironmentVariable("FAKECLI_STDERR", stderr);
    }

    private async Task Git(params string[] args) => await Run(args);

    private async Task<string> Run(params string[] args)
    {
        var result = await _launcher.RunAsync(new ProcessRequest(
            "git", ["-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", .. args], _repo));
        result.ExitCode.Should().Be(0, $"git {string.Join(' ', args)}: {result.StdErr}");

        return result.StdOut;
    }

    // ---------- the shapes of the table's rows ----------

    /// <summary>A vendor ticked for features that can run: the FakeCli on the codex adapter.</summary>
    private static ProviderSettings Ticked() =>
        new("codex") { ExecutablePath = FakeCliExe, Feature = true };

    /// <summary>A vendor nobody ticked for features — the ordinary settings file.</summary>
    private static ProviderSettings NotTicked() =>
        new("codex") { ExecutablePath = FakeCliExe };

    /// <summary>A vendor ticked for features that cannot run: an `api` row with no key in the vault.</summary>
    private static ProviderSettings TickedWithoutAKey() =>
        new("grok") { Runtime = "api", BaseUrl = "https://api.x.ai/v1", Model = "grok-4", Feature = true };

    private PanelService Service(bool featureRoleOn = true, params ProviderSettings[] providers) =>
        new(
            new PanelSettings
            {
                Providers = providers,
                Rounds = new PanelConfig(
                    PanelConfig.AllRoles.ToDictionary(r => r, r => new RoleGate(1, 5, Enabled: r != RoleCatalog.FeatureRole || featureRoleOn)),
                    StagePolicy.Human),
                DataDir = _data,
                ReviewerTimeout = TimeSpan.FromSeconds(30),
                RateLimitBackoff = TimeSpan.FromMilliseconds(5),
            },
            VaultKeys.None("no vault in tests"),
            default,
            _launcher,
            Logger.None, Noticing.None);

    /// <summary>What S2.2's entry point will hand the engine, shaped here by hand.</summary>
    private StageRun FeatureRun(PanelService service, Func<PersistedSession>? create = null, bool again = false) =>
        new(again ? RoundMachine.BeginFeatureRoundAgain : RoundMachine.BeginFeatureRound,
            NeedsWorktree: false, Stage: Stage.FeatureReview, ReadsCheckout: false,
            (running, workingDir, _) =>
            {
                var round = running.State.RoundsRunThisStage + 1;
                var roles = service.Settings.Rounds.RolesForRound(Stage.FeatureReview, round);

                return Task.FromResult(service.Roster.BuildWork(
                    roles, workingDir, "## The feature\n\nan outline", round, Stage.FeatureReview, readsCheckout: false));
            })
        {
            Feature = Plan,
            Head = "feature",
            WhenNobody = NobodyPolicy.RecordSkip,
            Session = new SessionRule.CreateIfAbsent(create ?? (() => NewFeatureSession(service))),
        };

    private PersistedSession NewFeatureSession(PanelService service) =>
        new(new SessionState(Guid.NewGuid().ToString("N")[..8], _repo, SessionKey.FeatureBranch, service.Settings.Rounds)
        {
            Feature = Plan,
            Stage = Stage.FeatureReview,
        },
        [])
        {
            OpenedUtc = DateTime.UtcNow,
            PlanText = PlanText,
        };

    private Task<string> RunAsync(PanelService service, bool again = false) =>
        service.Engine.RunStageAsync(_repo, SessionKey.FeatureBranch, PlanText, FeatureRun(service, again: again), CancellationToken.None);

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private PersistedSession? FeatureSession() =>
        new SessionStore(_data).Load(_repo, SessionKey.FeatureBranch, "", Plan);

    /// <summary>The feature stage's rows, straight from the table — the question is which ROWS exist.</summary>
    private IReadOnlyList<(long Number, string Verdict, string HeadSha, string Note)> RowsOf()
    {
        using var db = new Microsoft.Data.Sqlite.SqliteConnection(
            $"Data Source={Path.Combine(_data, Store.RoundsDb.FileName)}");
        db.Open();
        using var read = db.CreateCommand();
        read.CommandText = "SELECT number, verdict, head_sha, note FROM rounds WHERE stage = 'FeatureReview' ORDER BY number";
        using var rows = read.ExecuteReader();
        var found = new List<(long, string, string, string)>();
        while (rows.Read())
        {
            found.Add((rows.GetInt64(0), rows.GetString(1), rows.GetString(2), rows.GetString(3)));
        }

        return found;
    }

    // ---------- rows 1–3: skipped, with the reason ----------

    [Fact]
    public async Task WithTheFeatureRoleSwitchedOff_TheRoundIsSkipped_NotRefused()
    {
        var answer = Parse(await RunAsync(Service(featureRoleOn: false, Ticked())));

        answer.TryGetProperty("error", out var error).Should().BeFalse($"a skip is not a refusal: {error}");
        answer.GetProperty("verdict").GetString().Should().Be("skipped");
        answer.GetProperty("reviewers").GetString().Should().Contain("switched off");
        answer.GetProperty("instruction").GetString().Should().Contain("does NOT block");
    }

    [Fact]
    public async Task WithNoVendorTicked_TheRoundIsSkipped_SayingSo()
    {
        var answer = Parse(await RunAsync(Service(featureRoleOn: true, NotTicked())));

        answer.GetProperty("verdict").GetString().Should().Be("skipped");
        answer.GetProperty("reviewers").GetString().Should().Contain("no vendor is ticked");
    }

    [Fact]
    public async Task WithAVendorTickedThatCannotRun_TheRoundIsSkipped_NamingIt()
    {
        var answer = Parse(await RunAsync(Service(featureRoleOn: true, TickedWithoutAKey())));

        answer.GetProperty("verdict").GetString().Should().Be("skipped");
        answer.GetProperty("reviewers").GetString().Should().Contain("grok", "the vendor that could not run is named");
    }

    /// <summary>
    /// Rows 1–3 are decided from the SETTINGS, before the pack is built: with nobody to ask the work is never
    /// made, so a builder that would throw — or spend a minute of git — still yields the recorded skip.
    /// </summary>
    [Fact]
    public async Task WithNobodyToAsk_ThePackIsNeverBuilt()
    {
        foreach (var (service, row) in new[]
        {
            (Service(featureRoleOn: false, Ticked()), "the role is switched off"),
            (Service(featureRoleOn: true, NotTicked()), "no vendor is ticked"),
            (Service(featureRoleOn: true, TickedWithoutAKey()), "the ticked vendor cannot run"),
        })
        {
            var run = FeatureRun(service) with
            {
                MakeWork = (_, _, _) => throw new InvalidOperationException("the pack was built for a round nobody can review"),
            };

            var answer = Parse(await service.Engine.RunStageAsync(_repo, SessionKey.FeatureBranch, PlanText, run, CancellationToken.None));

            answer.TryGetProperty("error", out var error).Should().BeFalse($"{row}: the skip is decided before anything is built — {error}");
            answer.GetProperty("verdict").GetString().Should().Be("skipped", row);
        }
    }

    // ---------- what a skip writes, and what it leaves alone ----------

    [Fact]
    public async Task ASkip_IsRecorded_WithTheHead_AndLeavesTheSessionStateUntouched()
    {
        await RunAsync(Service(featureRoleOn: true, NotTicked()));

        var session = FeatureSession();
        session.Should().NotBeNull("the session was created under the engine's own claim");
        session!.Rounds.Should().ContainSingle();
        var record = session.Rounds[0];
        record.Verdict.Should().Be("skipped");
        record.Stage.Should().Be("FeatureReview");
        record.Number.Should().Be(1, "numbered from the journal");
        record.Sha.Should().Be(_sha, "the head the review would have read");
        record.Note.Should().Contain("no vendor is ticked");
        session.State.Stage.Should().Be(Stage.FeatureReview, "not Done");
        session.State.AwaitingResolve.Should().BeFalse("nothing to decide");
        session.State.RoundsRunThisStage.Should().Be(0, "no budget spent");
        session.State.HumanGate.Should().BeFalse();

        var rows = RowsOf();
        rows.Should().ContainSingle();
        rows[0].Verdict.Should().Be("skipped");
        rows[0].HeadSha.Should().Be(_sha, "rounds.head_sha is written from RoundContext.HeadSha, and the skip supplies it");
        rows[0].Note.Should().Contain("no vendor is ticked");
    }

    [Fact]
    public async Task TenIdenticalSkips_AreOneRow_SayingTimesTen()
    {
        var service = Service(featureRoleOn: true, NotTicked());
        for (var i = 0; i < 10; i++)
        {
            Parse(await RunAsync(service)).GetProperty("verdict").GetString().Should().Be("skipped");
        }

        var rows = RowsOf();
        rows.Should().ContainSingle("consecutive skips for one reason coalesce");
        rows[0].Number.Should().Be(1);
        rows[0].Note.Should().EndWith("×10");
        FeatureSession()!.Rounds.Should().ContainSingle().Which.Repeats.Should().Be(10);
    }

    [Fact]
    public async Task AChangedReason_OpensANewNumber()
    {
        await RunAsync(Service(featureRoleOn: true, NotTicked()));
        await RunAsync(Service(featureRoleOn: false, Ticked()));

        RowsOf().Select(r => r.Number).Should().Equal([1, 2], "a different reason is a different row");
        FeatureSession()!.Rounds.Select(r => r.Number).Should().Equal([1, 2]);
    }

    /// <summary>The skip only ever coalesces with the round immediately before it.</summary>
    [Fact]
    public async Task ASkipAfterADifferentSkip_IsANewRow_EvenIfAnOlderOneMatched()
    {
        await RunAsync(Service(featureRoleOn: true, NotTicked()));
        await RunAsync(Service(featureRoleOn: false, Ticked()));
        await RunAsync(Service(featureRoleOn: true, NotTicked()));

        RowsOf().Select(r => r.Number).Should().Equal([1, 2, 3]);
    }

    // ---------- the human-gate row: all failed → a person; un-ticking everyone is not a way past ----------

    [Fact]
    public async Task WhenEveryReviewerFails_APersonIsCalled_AndSwitchingTheReviewersOffDoesNotSkipPastThem()
    {
        Script(Clean, exit: 1, stderr: "429 Too Many Requests");
        var called = Parse(await RunAsync(Service(featureRoleOn: true, Ticked())));
        called.GetProperty("verdict").GetString().Should().Be("call_human", $"all failed blocks: {called}");
        FeatureSession()!.State.HumanGate.Should().BeTrue();

        // Now nobody serves the stage. `stage.Begin` runs BEFORE the skip branch, on purpose: a standing
        // call_human is a person's decision, and un-ticking every vendor must not dissolve it.
        var answer = Parse(await RunAsync(Service(featureRoleOn: true, NotTicked())));

        answer.TryGetProperty("verdict", out var verdict).Should().BeFalse($"not a skip: {verdict}");
        answer.GetProperty("error").GetString().Should().Contain("call_human").And.Contain("ask_human",
            "the human-gate sentence, not 'skipped'");
    }

    // ---------- the session is created under the claim ----------

    [Fact]
    public async Task TwoConcurrentFirstCalls_CreateOneSession()
    {
        var service = Service(featureRoleOn: true, NotTicked());
        var minted = new List<string>();
        PersistedSession Create()
        {
            var fresh = NewFeatureSession(service);
            lock (minted)
            {
                minted.Add(fresh.State.SessionId);
            }

            return fresh;
        }

        var first = service.Engine.RunStageAsync(_repo, SessionKey.FeatureBranch, PlanText, FeatureRun(service, Create), CancellationToken.None);
        var second = service.Engine.RunStageAsync(_repo, SessionKey.FeatureBranch, PlanText, FeatureRun(service, Create), CancellationToken.None);
        var answers = await Task.WhenAll(first, second);

        Directory.GetFiles(Path.Combine(_data, "sessions"), "session-*.json").Should().ContainSingle(
            "two first calls on one plan are one creator — the second either waited out the claim or was refused");
        minted.Should().ContainSingle("the factory ran once: the second call found the first's session or never got the claim");
        FeatureSession()!.State.SessionId.Should().Be(minted[0]);
        answers.Should().OnlyContain(a => a.Contains("\"skipped\"") || a.Contains("another call is already changing"));
    }

    [Fact]
    public async Task WithoutACreator_AMissingSessionIsStillRefused()
    {
        var service = Service(featureRoleOn: true, NotTicked());
        var run = FeatureRun(service) with { Session = new SessionRule.MustExist() };

        var answer = Parse(await service.Engine.RunStageAsync(_repo, SessionKey.FeatureBranch, PlanText, run, CancellationToken.None));

        answer.GetProperty("error").GetString().Should().Contain("no session");
        FeatureSession().Should().BeNull();
    }

    // ---------- the refusal is still the answer for every stage that keeps it ----------

    [Fact]
    public async Task WithRefuseAsThePolicy_NobodyToAsk_IsStillARefusal()
    {
        var service = Service(featureRoleOn: true, NotTicked());
        var run = FeatureRun(service) with { WhenNobody = NobodyPolicy.Refuse };

        var answer = Parse(await service.Engine.RunStageAsync(_repo, SessionKey.FeatureBranch, PlanText, run, CancellationToken.None));

        answer.GetProperty("error").GetString().Should().Contain("nothing could review");
        // Through the reader, which answers an empty log for a database nobody ever created — a
        // refusal opens nothing, so there is no file for a direct read to open.
        Store.RoundsQuery.Read(_data).Rounds.Should().BeEmpty("a refusal records no round");
    }
}
