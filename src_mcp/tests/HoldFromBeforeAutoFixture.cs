using System.Text.Json;
using System.Text.Json.Nodes;
using CoaiMcp.Core.Catalog;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Serilog;
using Serilog.Events;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A hold the REAL code raised under <c>human</c>, then made a hold from before policy auto by editing its file — never a session assembled by
/// hand (<c>todo/PLAN_auto_never_asks_a_person_E1_S1_spec.md</c>, § S1.10.6). The four stages' holds are raised here so that every class of the
/// hold-authority scenarios reaches them the same way; a subclass carries <c>[Collection("fakecli-env")]</c> itself.
/// </summary>
/// <remarks>
/// The auto service is the same settings with no policy named; its logger is the test's <see cref="Sink"/>, so "exactly one release line" is
/// asserted on what the server wrote. The human service logs nothing anyone reads.
/// </remarks>
public abstract class HoldFromBeforeAutoFixture : FakeCliRoundTests
{
    protected const string Branch = "epic-1";

    protected const string Plan = "# PLAN — held\n\n## Build order\n\n1. one\n2. two\n";

    protected const string OneBlockingFinding = """
        {"findings": [
          {"severity": "blocking", "category": "reliability", "file": "", "line": 0,
           "title": "the poll has no timeout", "why": "a hung peer hangs the loop", "fix": "add one"}
        ]}
        """;

    protected const string RejectIt = """[{"finding":0,"action":"reject","reason":"the timeout lives in the caller"}]""";

    protected const string DocumentPurpose = """
        # What this document is for

        It is the requirements for a payments integration, written for the two engineers who will
        build it and the manager who signs it off. After reading it they must be able to start
        without asking anybody a question. It deliberately does not cover the contract with the
        provider or the pricing, both of which are settled elsewhere.
        """;

    private const string DocumentText = "# Payments integration\n\nPayments must be fast. The team will migrate the existing records before launch.\n";

    private const string DocumentFinding = """
        {"findings": [
          {"severity": "major", "category": "completeness", "file": null, "line": null,
           "title": "'fast' is not a number", "why": "nobody can tell whether the result meets it",
           "fix": "state a latency and a percentile"}
        ],
         "notes": "The document settles almost nothing."}
        """;

    private const string FeaturePlanPath = "todo/PLAN_the_cart.md";

    private const string FeatureEpics = """
        [{"title": "Prices", "summary": "Prices are doubled at the till.", "branch": "feat/cart-e1"},
         {"title": "The cart", "summary": "A cart holds what the customer picked.", "branch": "feat/cart-e2"},
         {"title": "Checkout", "summary": "Checkout reads the cart and charges it.", "branch": "feat/cart-e3"}]
        """;

    private const string FeatureLessons = """
        {"pitfalls": ["The till doubled prices twice when Buy was called from the cart; fixed in epic 2 by reading the raw price."],
         "blockers": ["none — every epic merged without a blocker; the one risk was the shared flag and it held in all three."],
         "findings": ["The cart and the checkout both round the total; a reviewer of the whole should check they round the same way."]}
        """;

    private const string FeatureBlocking = """
        {"findings": [
          {"severity": "blocking", "category": "security", "file": "src/Shop.cs", "line": 3,
           "title": "The till charges before the cart is checked", "why": "a customer pays for an empty cart",
           "fix": "check the cart first"}
        ],
         "notes": "One contract broken between epics 1 and 3.", "sourceRequests": []}
        """;

    private protected readonly ListSink Sink = new();

    /// <summary>Which stage's hold a scenario raises.</summary>
    public enum Kind
    {
        Plan,
        Code,
        Document,
        Feature,
    }

    /// <summary>A hold, and the address of the session that carries it.</summary>
    protected sealed record Hold(Kind Kind, string Branch, string Document, string Feature);

    protected static readonly Kind[] AllKinds = [Kind.Plan, Kind.Code, Kind.Document, Kind.Feature];

    protected SessionStore Store => new(_data, Defaults().Rounds.Catalog);

    protected ILogger AuditLog => new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(Sink).CreateLogger();

    protected PersistedSession Session() => Store.Load(_repo, Branch)!;

    protected PersistedSession SessionOf(Hold hold) => Store.Load(_repo, hold.Branch, hold.Document, hold.Feature)!;

    private static Hold AddressOf(Kind kind) => kind switch
    {
        Kind.Document => new Hold(kind, Branch, "docs/spec.md", string.Empty),
        Kind.Feature => new Hold(kind, SessionKey.FeatureBranch, string.Empty, FeaturePlanPath),
        _ => new Hold(kind, Branch, string.Empty, string.Empty),
    };

    protected static string ErrorOf(string reply) => Parse(reply).TryGetProperty("error", out var e) ? e.GetString()! : string.Empty;

    protected static string StageWords(PersistedSession session) => RoundSubject.StageName(session.State.Stage.ToString());

    // ---------- the services ----------

    /// <summary>The settings of a stage's hold. <paramref name="human"/> names the policy; otherwise none is named, which is policy auto.</summary>
    protected PanelSettings SettingsOf(Kind kind, bool human)
    {
        var settings = Defaults() with
        {
            CadenceMode = Core.Cadence.CadenceMode.Off,
            SplitPlan = false,
            // A question asked on the held gate waits this long for a person, then returns with its card still on disk.
            EscalationBudget = TimeSpan.FromMilliseconds(300),
        };
        switch (kind)
        {
            case Kind.Document:
                var catalog = RoleComposition.Compose([
                    .. new[] { RoleCatalog.PlanRole, RoleCatalog.ConventionsRole, RoleCatalog.ArchitectureRole, RoleCatalog.SecurityRole, RoleCatalog.UxDxRole,
                        RoleCatalog.DocumentRole, RoleCatalog.DocumentSummaryRole }.Select(id => new RoleEntry(id, Active: false)),
                    new RoleEntry("Spec", Name: "The specification", Stage: RoleStages.Result, ProgrammingTask: false,
                        Prompts: [new PromptEntry("spec-general", "General", "Whether a reader could act on it.")]),
                ]);
                var gates = catalog.Roles.ToDictionary(r => r.Id, _ => new RoleGate(1, 0));

                return settings with { Rounds = (human ? new PanelConfig(gates, StagePolicy.Human) : new PanelConfig(gates)) with { Catalog = catalog } };
            case Kind.Feature:
                var feature = PanelConfig.AllRoles.ToDictionary(r => r, _ => new RoleGate(2, 5));

                return settings with
                {
                    Providers = [new("codex") { ExecutablePath = FakeCliExe, Feature = true }],
                    Rounds = human ? new PanelConfig(feature, StagePolicy.Human) : new PanelConfig(feature),
                    FeatureMinEpics = 3,
                    FeatureSourceFollowUps = 3,
                };
            default:
                return settings with { Rounds = human ? PanelConfig.Uniform(1, 0, StagePolicy.Human) : PanelConfig.Uniform(1, 0) };
        }
    }

    protected PanelService HumanService(Kind kind = Kind.Plan) => ServiceFor(SettingsOf(kind, human: true));

    /// <summary>The same settings with no policy named — policy auto — logging to <see cref="Sink"/>.</summary>
    protected PanelService AutoService(Kind kind = Kind.Plan) =>
        new(SettingsOf(kind, human: false), VaultKeys.None("no vault in tests"), default, _launcher, AuditLog, Noticing.None);

    // ---------- the holds ----------

    /// <summary>The plan gate, held: <c>call_human</c>, the notice on disk, the finding pending.</summary>
    protected async Task<PanelService> HeldAsync()
    {
        var service = HumanService();
        await service.OpenAsync(_repo, Branch);
        Script(OneBlockingFinding);
        var verdict = Parse(await service.ReviewPlanAsync(_repo, Branch, Plan, TestContext.Current.CancellationToken));
        verdict.GetProperty("verdict").GetString().Should().Be("call_human", verdict.ToString());
        Session().State.HumanGate.Should().BeTrue();

        return service;
    }

    private async Task Commit(string message)
    {
        await Git("add", "-A");
        await Git("commit", "-m", message);
    }

    private void Write(string path, string text)
    {
        var full = Path.Combine(_repo, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    /// <summary>
    /// A hold of the stage's own, raised by the real code under <c>human</c>. <paramref name="resolved"/>: the caller then decided the held round
    /// (a reasoned reject), so the hold stays and no round awaits a resolve; otherwise its finding is still pending.
    /// </summary>
    protected async Task<Hold> RaiseAsync(Kind kind, bool resolved)
    {
        var hold = AddressOf(kind);
        var service = HumanService(kind);
        var ct = TestContext.Current.CancellationToken;
        switch (kind)
        {
            case Kind.Plan:
                await service.OpenAsync(_repo, Branch);
                Script(OneBlockingFinding);
                Verdict(await service.ReviewPlanAsync(_repo, Branch, Plan, ct)).Should().Be("call_human");
                break;
            case Kind.Code:
                await service.OpenAsync(_repo, Branch);
                Script(Clean);
                Verdict(await service.ReviewPlanAsync(_repo, Branch, Plan, ct)).Should().Be("proceed");
                ErrorOf(await service.ResolveAsync(_repo, Branch, "[]")).Should().BeEmpty();
                Write("app.cs", "v2\n");
                await Commit("the change");
                Script(OneBlockingFinding);
                Verdict(await service.ReviewCodeAsync(_repo, Branch, "main", CodeRoundHarness.Scope, ct: ct)).Should().Be("call_human");
                break;
            case Kind.Document:
                Directory.CreateDirectory(Path.Combine(_data, "prompts"));
                await File.WriteAllTextAsync(Path.Combine(_data, "prompts", "spec-general.md"), "You read one thing: whether a reader could act on this document.", ct);
                Write("docs/spec.md", DocumentText);
                await service.OpenAsync(_repo, Branch);
                Script(DocumentFinding);
                Verdict(await service.ReviewDocumentAsync(_repo, Branch, DocumentPurpose, documentPath: Path.Combine(_repo, "docs/spec.md"))).Should().Be("call_human");
                break;
            default:
                await FeatureWorkAsync();
                Script(FeatureBlocking);
                Verdict(await Feature(service, again: false)).Should().Be("revise");
                ErrorOf(await service.ResolveAsync(_repo, hold.Branch, """[{"finding":0,"action":"accept"}]""", feature: FeaturePlanPath)).Should().BeEmpty();
                Write("src/Cart.cs", "public sealed class Cart\n{\n    public int Count() => 1;\n}\n");
                await Commit("the fix pull request");
                Verdict(await Feature(service, again: true)).Should().Be("call_human");
                break;
        }

        SessionOf(hold).State.HumanGate.Should().BeTrue($"the {kind} fixture must raise a hold");
        if (resolved)
        {
            ErrorOf(await ResolveAsync(service, hold, RejectIt)).Should().BeEmpty("the caller's decision of the held round is recorded");
            SessionOf(hold).State.AwaitingResolve.Should().BeFalse("the hold stays and the round is resolved");
        }

        return hold;
    }

    private string _featureBase = string.Empty;

    private async Task FeatureWorkAsync()
    {
        Write(FeaturePlanPath, "# PLAN — the cart\n\nThree epics: prices at the till, the cart, the checkout.\n");
        Write("src/Shop.cs", "public sealed class Shop\n{\n    public int Buy(int n)\n    {\n        var total = n;\n        return total;\n    }\n}\n");
        await Commit("base");
        _featureBase = (await Rev("HEAD")).Trim();
        Write("src/Shop.cs", "public sealed class Shop\n{\n    public int Buy(int n)\n    {\n        var total = n * 2;\n        return total;\n    }\n}\n");
        Write("src/Cart.cs", "public sealed class Cart\n{\n    public int Count() => 0;\n}\n");
        await Commit("the feature");
    }

    private async Task<string> Rev(string rev)
    {
        var result = await _launcher.RunAsync(new Runners.Processes.ProcessRequest("git", ["rev-parse", rev], _repo));
        result.ExitCode.Should().Be(0, result.StdErr);

        return result.StdOut;
    }

    private Task<string> Feature(PanelService service, bool again) =>
        service.ReviewFeatureAsync(_repo, FeaturePlanPath, _featureBase, FeatureEpics, FeatureLessons, again, callerModel: "claude-opus-5");

    protected static string StageOf(string reply)
    {
        Parse(reply).TryGetProperty("stage", out var stage).Should().BeTrue(reply);

        return stage.GetString()!;
    }

    protected static string Verdict(string reply) =>
        Parse(reply).TryGetProperty("verdict", out var verdict) ? verdict.GetString()! : throw new InvalidOperationException("no verdict: " + reply);

    // ---------- the entries ----------

    protected Task<string> ResolveAsync(PanelService service, Hold hold, string decisions, bool callerClaimsProceed = false) =>
        service.ResolveAsync(_repo, hold.Branch, decisions, callerClaimsProceed, hold.Document, hold.Feature);

    protected Task<string> StatusAsync(PanelService service, Hold hold) =>
        service.StatusAsync(_repo, hold.Branch, hold.Document, string.Empty, hold.Feature);

    /// <summary>The stage's own review entry — <c>review_plan</c>, <c>review_code</c>, <c>review_document</c> or <c>review_feature</c>.</summary>
    protected async Task<string> ReviewEntryAsync(PanelService service, Hold hold)
    {
        var ct = TestContext.Current.CancellationToken;
        switch (hold.Kind)
        {
            case Kind.Plan:
                Script(Clean);
                return await service.ReviewPlanAsync(_repo, hold.Branch, Plan, ct);
            case Kind.Code:
                Script(Clean);
                return await service.ReviewCodeAsync(_repo, hold.Branch, "main", CodeRoundHarness.Scope, again: true, ct: ct);
            case Kind.Document:
                Script(DocumentFinding);
                return await service.ReviewDocumentAsync(_repo, hold.Branch, DocumentPurpose, documentPath: Path.Combine(_repo, "docs/spec.md"));
            default:
                Script(FeatureBlocking);
                return await Feature(service, again: true);
        }
    }

    // ---------- the fixture as the real loader reads it ----------

    protected string SessionFileOf(Hold hold) => Store.FileFor(_repo, hold.Branch, hold.Document, hold.Feature);

    protected string SessionBytes(Hold hold) => File.ReadAllText(SessionFileOf(hold));

    /// <summary>
    /// The session file as a build before policy auto left it: no <c>state.config.autonomous</c>; and, when <paramref name="recorded"/> is false, no
    /// recorded question either (<c>state.holdQuestions: []</c>), as an older build left it.
    /// </summary>
    protected void StripTheMarker(Hold hold, bool recorded)
    {
        var file = SessionFileOf(hold);
        var json = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        json["state"]!["config"]!.AsObject().Remove("autonomous").Should().BeTrue("the fixture strips the marker the real code wrote");
        if (!recorded)
        {
            json["state"]!.AsObject()["holdQuestions"] = new JsonArray();
        }

        File.WriteAllText(file, json.ToJsonString());
        var state = SessionOf(hold).State;
        state.Config.PredatesAuto.Should().BeTrue("as the real loader reads the fixture");
        state.HumanGate.Should().BeTrue();
        if (recorded)
        {
            state.HoldQuestions.Should().NotBeEmpty();
        }
    }

    protected void StripTheMarker(bool recorded) => StripTheMarker(AddressOf(Kind.Plan), recorded);

    protected string NoticeId() => Session().State.HoldQuestions.Should().ContainSingle("the round raised one notice").Subject;

    protected string NoticeIdOf(Hold hold) => SessionOf(hold).State.HoldQuestions.Should().ContainSingle("the round raised one notice").Subject;

    protected string NoticeFile(string id) => Path.Combine(_data, "escalations", $"{id}.json");

    protected JsonElement CardOf(string id) => JsonDocument.Parse(File.ReadAllText(NoticeFile(id))).RootElement;

    protected string CardStatus(string id) => CardOf(id).TryGetProperty("status", out var s) ? s.GetString() ?? string.Empty : string.Empty;

    /// <summary>How many question files the escalations folder holds — what "nothing was re-issued" is measured on.</summary>
    protected int Cards() =>
        Directory.Exists(Path.Combine(_data, "escalations"))
            ? Directory.GetFiles(Path.Combine(_data, "escalations"), "*.json").Count(f => !f.EndsWith(".answer.json", StringComparison.Ordinal))
            : 0;

    /// <summary>The person's answer to a question, written exactly as the panel writes it, beside the question.</summary>
    protected void ThePersonAnswers(string id, string decision, string words = "Proceed despite the findings")
    {
        var answer = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["id"] = id,
            ["answer"] = words,
            ["decision"] = decision,
            ["answeredUtc"] = DateTime.UtcNow.ToString("O"),
        });
        File.WriteAllText(Path.Combine(_data, "escalations", $"{id}.answer.json"), answer);
    }

    // ---------- what the server said ----------

    protected IReadOnlyList<string> Said(string text) => [.. Sink.Lines.Where(l => l.Contains(text, StringComparison.Ordinal))];

    protected IReadOnlyList<string> Warnings() => [.. Sink.Events.Zip(Sink.Lines).Where(e => e.First.Level == LogEventLevel.Warning).Select(e => e.Second)];

    protected List<string> Column(string sql)
    {
        using var db = new SqliteConnection($"Data Source={Path.Combine(_data, RoundsDb.FileName)}");
        db.Open();
        using var read = db.CreateCommand();
        read.CommandText = sql;
        using var reader = read.ExecuteReader();
        var found = new List<string>();
        while (reader.Read())
        {
            found.Add(reader.IsDBNull(0) ? "<null>" : reader.GetValue(0).ToString() ?? string.Empty);
        }

        return found;
    }

    /// <summary>
    /// The assertions every release shares: nothing held, the stage waiting for its resolve, the policy the settings'; the notice withdrawn when it was
    /// recorded and nothing re-issued when it was not; one release line; the held round keeping its verdict and carrying the note, in the file and in
    /// <c>coai.db</c>. <paramref name="advanced"/>: the resolve that moves the stage has run.
    /// </summary>
    protected void AssertReleased(Hold hold, bool recorded, string? noticeId, int cardsBefore, bool advanced = false)
    {
        var session = SessionOf(hold);
        session.State.HumanGate.Should().BeFalse();
        session.State.HoldQuestions.Should().BeEmpty();
        session.State.AdvanceOnResolve.Should().Be(!advanced, "the next resolve consumes it");
        session.State.AwaitingResolve.Should().Be(!advanced);
        session.State.Config.OnExhausted.Should().Be(StagePolicy.GoodEnough);
        session.State.Config.Autonomous.Should().BeTrue();
        if (recorded)
        {
            CardStatus(noticeId!).Should().Be(EscalationStatuses.Expired, "the notice of the released hold is withdrawn");
        }
        else
        {
            Cards().Should().Be(cardsBefore, "nothing is re-issued for a hold from before policy auto");
        }

        Said("auto released the").Should().ContainSingle().Which.Should().Contain(session.State.SessionId);
        var held = session.Rounds[^1];
        held.Verdict.Should().Be("call_human");
        held.Note.Should().Be(LegacyHold.Note);
        Column($"SELECT note FROM rounds WHERE session_id = '{session.State.SessionId}' AND stage = '{held.Stage}' AND number = {held.Number}")
            .Should().Equal([LegacyHold.Note], "the note is in coai.db as well");
    }
}
