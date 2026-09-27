using System.Text.Json;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog.Core;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A whole feature is reviewed, end to end, through the public service entry the eleventh tool calls —
/// on a REAL repository, with the fake CLI standing in for the one collaborator a test cannot have, the
/// vendor's model.
/// </summary>
/// <remarks>
/// <para>Every unit under this — the inputs, the outline builder, the composer, the context, the engine's
/// skip branch — has its own suite, and every one of them could be green while <c>review_feature</c>
/// dispatches to nothing. This drives the wiring between them: the pack the reviewer is actually handed
/// (recorded from the fake CLI's stdin), the verdict it produces, <c>resolve</c> and <c>status</c> and
/// <c>ask_human</c> finding the feature session by its plan, <c>again</c> over a moved head, and each row of
/// the skip-against-block table that a caller can reach (§4.4).</para>
/// <para>The repository: a base commit with the plan, a class and a credential-shaped TypeScript file; a
/// head commit changing one method body, adding a file and changing the credential file.</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class AFeatureIsReviewedEndToEndTests : IAsyncLifetime
{
    private const string PlanPath = "todo/PLAN_the_cart.md";

    private const string PlanText = """
        # PLAN — the cart

        Three epics: prices are doubled at the till (epic 1), the cart holds what a customer picked
        (epic 2), and the checkout reads the cart (epic 3). Every one ships behind the same flag.
        """;

    private const string Secret = "SECRET-staging-hunter2";

    /// <summary>A line only an UNCHANGED member's body holds — the no-body property is asserted on it.</summary>
    private const string UnchangedBody = "var unchangedBodyMarker = n - 1;";

    private const string Epics = """
        [{"title": "Prices", "summary": "Prices are doubled at the till.", "branch": "feat/cart-e1"},
         {"title": "The cart", "summary": "A cart holds what the customer picked.", "branch": "feat/cart-e2"},
         {"title": "Checkout", "summary": "Checkout reads the cart and charges it.", "branch": "feat/cart-e3"}]
        """;

    private const string TwoEpics = """
        [{"title": "Prices", "summary": "Prices are doubled at the till."},
         {"title": "The cart", "summary": "A cart holds what the customer picked."}]
        """;

    private const string Lessons = """
        {"pitfalls": ["The till doubled prices twice when Buy was called from the cart; fixed in epic 2 by reading the raw price."],
         "blockers": ["none — every epic merged without a blocker; the one risk was the shared flag and it held in all three."],
         "findings": ["The cart and the checkout both round the total; a reviewer of the whole should check they round the same way."]}
        """;

    private const string Clean = """
        {"findings": [], "notes": "The epics add up to the plan.", "sourceRequests": []}
        """;

    /// <summary>A clean answer that asks for one member's source — the shape S3.2's turn loop answers.</summary>
    private const string AsksForSell = """
        {"findings": [], "notes": "The epics add up to the plan.",
         "sourceRequests": [{"file": "src/Shop.cs", "symbol": "Shop.Sell", "startLine": null, "endLine": null, "why": "does Sell still round like Buy?"}]}
        """;

    /// <summary>A gating finding made while WAITING for source — the one only the last turn may keep.</summary>
    private const string FindingAndAsk = """
        {"findings": [
          {"severity": "major", "category": "reliability", "file": "src/Shop.cs", "line": 17,
           "title": "Sell may round the other way", "why": "I cannot see its body", "fix": "check"}
        ],
         "notes": "One suspicion, unconfirmed.",
         "sourceRequests": [{"file": "src/Shop.cs", "symbol": "Shop.Sell", "startLine": null, "endLine": null, "why": "does Sell still round like Buy?"}]}
        """;

    private const string AsksForTheCredentialFile = """
        {"findings": [], "notes": "waiting",
         "sourceRequests": [{"file": "config/.env.local.ts", "symbol": null, "startLine": null, "endLine": null, "why": "what does connect default to?"},
                            {"file": "src/Shop.cs", "symbol": "Shop.Sell", "startLine": null, "endLine": null, "why": "rounding"}]}
        """;

    private const string OneThousandIn = """{"input_tokens": 1000, "output_tokens": 50}""";

    /// <summary>One gating finding — over a threshold of zero it is a <c>revise</c>, under five a <c>proceed</c>.</summary>
    private const string OneFinding = """
        {"findings": [
          {"severity": "major", "category": "reliability", "file": "src/Shop.cs", "line": 3,
           "title": "The till and the cart round the total differently", "why": "the cart rounds down and the checkout rounds half up",
           "fix": "round in one place"}
        ],
         "notes": "One seam between the epics.", "sourceRequests": []}
        """;

    private const string RejectTheFinding = """[{"finding":0,"action":"reject","reason":"both round through Money.Round; checked in epic 3"}]""";

    private const string AcceptTheFinding = """[{"finding":0,"action":"accept"}]""";

    /// <summary>A <c>blocking</c> finding — the one severity that buys a second round (D23).</summary>
    private const string BlockingFinding = """
        {"findings": [
          {"severity": "blocking", "category": "security", "file": "src/Shop.cs", "line": 3,
           "title": "The till charges before the cart is checked", "why": "a customer pays for an empty cart",
           "fix": "check the cart first"}
        ],
         "notes": "One contract broken between epics 1 and 3.", "sourceRequests": []}
        """;

    /// <summary>The model a second, failing vendor is configured with — what its launch is told apart by.</summary>
    private const string FailingModel = "m-that-falls-over";

    private readonly ProcessLauncher _launcher = new();
    private string _repo = string.Empty;
    private string _data = string.Empty;
    private string _record = string.Empty;
    private string _base = string.Empty;

    private static string FakeCliExe => Path.Combine(
        AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli");

    public async ValueTask InitializeAsync()
    {
        _repo = Directory.CreateTempSubdirectory("coai-feature-e2e-repo-").FullName;
        _data = Directory.CreateTempSubdirectory("coai-feature-e2e-data-").FullName;
        _record = Directory.CreateTempSubdirectory("coai-feature-e2e-record-").FullName;

        await Git("init", "-b", "main");
        Write(PlanPath, PlanText);
        Write("src/Shop.cs", Shop("var total = n;"));
        Write("config/.env.local.ts", $"export function connect(password: string = \"{Secret}\"): number {{\n  return 1;\n}}\n");
        await Commit("base");
        _base = await Rev("HEAD");

        Write("src/Shop.cs", Shop("var total = n * 2;"));
        Write("src/Cart.cs", "public sealed class Cart\n{\n    public int Count() => 0;\n}\n");
        Write("config/.env.local.ts", $"export function connect(password: string = \"{Secret}\"): number {{\n  return 2;\n}}\n");
        await Commit("the feature");

        Environment.SetEnvironmentVariable("FAKECLI_MODE", "vendor");
        Environment.SetEnvironmentVariable("FAKECLI_RECORD_DIR", _record);
        // The turn family (S3.2): the fake reads which turn it is off the prompt's tail heading.
        Environment.SetEnvironmentVariable("FAKECLI_TURN_MARKER", Core.Feature.TurnTail.HeadingPrefix + "{n} of");
        Script(Clean);
    }

    public ValueTask DisposeAsync()
    {
        foreach (var name in Environment.GetEnvironmentVariables().Keys.Cast<string>().Where(n => n.StartsWith("FAKECLI_", StringComparison.Ordinal)))
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var dir in (string[])[_repo, _data, _record])
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

    private static string Shop(string buyLine) =>
        "public sealed class Shop\n{\n"
        + $"    public int Buy(int n)\n    {{\n        {buyLine}\n        return total;\n    }}\n\n"
        + string.Concat(Enumerable.Range(0, 4).Select(i => $"    public int Filler{i}() => {i};\n\n"))
        + $"    public int Sell(int n)\n    {{\n        {UnchangedBody}\n        return unchangedBodyMarker;\n    }}\n}}\n";

    private static void Script(string answer, int exit = 0, string stderr = "")
    {
        Environment.SetEnvironmentVariable("FAKECLI_STDOUT", answer);
        Environment.SetEnvironmentVariable("FAKECLI_OUTFILE_TEXT", answer);
        Environment.SetEnvironmentVariable("FAKECLI_EXIT", exit.ToString());
        Environment.SetEnvironmentVariable("FAKECLI_STDERR", stderr);
    }

    /// <summary>One turn's answer, on the codex path (the `-o` file); its stdout carries the usage the adapter reads.</summary>
    private static void Turn(int n, string answer, string stdout = "", int exit = 0, string stderr = "")
    {
        var prefix = $"FAKECLI_TURN{n}_";
        Environment.SetEnvironmentVariable(prefix + "OUTFILE_TEXT", answer);
        Environment.SetEnvironmentVariable(prefix + "STDOUT", stdout.Length > 0 ? stdout : answer);
        Environment.SetEnvironmentVariable(prefix + "EXIT", exit == 0 ? null : exit.ToString());
        Environment.SetEnvironmentVariable(prefix + "STDERR", stderr.Length > 0 ? stderr : null);
    }

    private void Write(string path, string text)
    {
        var full = Path.Combine(_repo, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    private async Task Commit(string message)
    {
        await Git("add", "-A");
        await Git("commit", "-m", message);
    }

    private async Task Git(params string[] args) => await Run(args);

    private async Task<string> Rev(string rev) => (await Run("rev-parse", rev)).Trim();

    private async Task<string> Run(params string[] args)
    {
        var result = await _launcher.RunAsync(new ProcessRequest(
            "git", ["-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", "-c", "core.autocrlf=false", .. args], _repo));
        result.ExitCode.Should().Be(0, $"git {string.Join(' ', args)}: {result.StdErr}");

        return result.StdOut;
    }

    /// <summary>The shipped catalog with its FeatureReview role on, and one vendor — the fake CLI on codex.</summary>
    /// <param name="gate">Every role's gate; the shipped feature budget (two rounds, a threshold of five) unless a test needs another.</param>
    /// <param name="secondVendor">A second vendor on the same fake CLI, told apart by its model — for a round that partly fails.</param>
    private PanelService Service(
        bool ticked = true, int minEpics = 3, TimeSpan? escalationBudget = null, IProcessLauncher? launcher = null, RoleGate? gate = null,
        int followUps = 3, TimeSpan? reviewerTimeout = null, bool secondVendor = false) =>
        new(
            new PanelSettings
            {
                Providers = secondVendor
                    ? [new("codex") { ExecutablePath = FakeCliExe, Feature = ticked },
                       new("grok") { Runtime = "codex", ExecutablePath = FakeCliExe, Model = FailingModel, Feature = ticked }]
                    : [new("codex") { ExecutablePath = FakeCliExe, Feature = ticked }],
                Rounds = new PanelConfig(PanelConfig.AllRoles.ToDictionary(r => r, _ => gate ?? new RoleGate(2, 5)), StagePolicy.Human),
                DataDir = _data,
                ReviewerTimeout = reviewerTimeout ?? TimeSpan.FromSeconds(30),
                RateLimitBackoff = TimeSpan.FromMilliseconds(5),
                FeatureMinEpics = minEpics,
                FeatureSourceFollowUps = followUps,
                EscalationBudget = escalationBudget ?? TimeSpan.FromMinutes(30),
            },
            VaultKeys.None("no vault in tests"),
            default,
            launcher ?? _launcher,
            Logger.None, Noticing.None);

    private PersistedSession Session() =>
        new SessionStore(_data).Load(_repo, SessionKey.FeatureBranch, string.Empty, PlanPath)!;

    /// <summary>One more commit on the feature — what a "second train" or a fix pull request leaves behind.</summary>
    private async Task CommitMore(int count)
    {
        Write("src/Cart.cs", $"public sealed class Cart\n{{\n    public int Count() => {count};\n}}\n");
        await Commit($"the cart counts {count}");
    }

    private Task<string> ReviewAsync(PanelService service, string epics = Epics, bool again = false, string baseRef = "") =>
        service.ReviewFeatureAsync(_repo, PlanPath, baseRef.Length > 0 ? baseRef : _base, epics, Lessons, again, callerModel: "claude-opus-5");

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    /// <summary>What the reviewer was handed: the prompt, the last field of each recorded launch.</summary>
    private IReadOnlyList<string> Prompts() =>
        [.. Directory.GetFiles(_record, "*.argv").OrderBy(File.GetCreationTimeUtc).Select(f => File.ReadAllText(f).Split('\0')[^1])];

    /// <summary>Every recorded launch, whole — which vendor ran is read off its model argument.</summary>
    private IReadOnlyList<string> Launches() =>
        [.. Directory.GetFiles(_record, "*.argv").OrderBy(File.GetCreationTimeUtc).Select(File.ReadAllText)];

    /// <summary>A launcher that answers the second vendor's launch with a failure, and runs everything else.</summary>
    private WatchedLauncher SecondVendorFalls() =>
        new(_launcher, r => r.Arguments.Contains(FailingModel) ? new ProcessResult(1, string.Empty, "the model fell over", TimedOut: false) : null);

    /// <summary>The feature session's rounds, as the trail has them.</summary>
    private IReadOnlyList<RoundRecord> FeatureRounds() =>
        [.. Session().Rounds.Where(r => r.Stage == nameof(Stage.FeatureReview))];

    /// <summary>
    /// The person's answer to the newest question of the feature session, written exactly as the panel
    /// writes it (<c>escalationAnswer.ts</c>): the id, their words, the button, the time — beside the question.
    /// </summary>
    /// <param name="question">Which question — its text's beginning — when the newest is not the one; a notice begins with "The".</param>
    private void ThePersonAnswers(string decision, string words = "Keep going — more rounds", string question = "")
    {
        var featureSession = Session().State.SessionId;
        var asked = Directory.GetFiles(Path.Combine(_data, "escalations"), "*.json")
            .Where(f => !f.EndsWith(".answer.json", StringComparison.Ordinal))
            .Select(f => JsonDocument.Parse(File.ReadAllText(f)).RootElement)
            .Where(q => q.GetProperty("sessionId").GetString() == featureSession)
            .Where(q => q.GetProperty("question").GetString()!.StartsWith(question, StringComparison.Ordinal))
            .OrderByDescending(q => q.GetProperty("askedUtc").GetString())
            .First();
        var id = asked.GetProperty("id").GetString()!;
        var answer = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["id"] = id,
            ["answer"] = words,
            ["decision"] = decision,
            ["answeredUtc"] = DateTime.UtcNow.ToString("O"),
        });
        File.WriteAllText(Path.Combine(_data, "escalations", $"{id}.answer.json"), answer);
    }

    // ---------- the proceed path: the pack reaches the reviewer, resolve closes the review ----------

    [Fact]
    public async Task AFeature_IsReviewedWithItsWholePack_AndResolvingTheFeatureClosesIt()
    {
        var service = Service();

        var answer = Parse(await ReviewAsync(service));

        answer.TryGetProperty("error", out var error).Should().BeFalse($"the round runs: {error}");
        answer.GetProperty("verdict").GetString().Should().Be("proceed");
        var prompt = Prompts().Should().ContainSingle("one vendor, the one feature role").Which;
        prompt.Should().Contain("## The plan").And.Contain(PlanPath).And.Contain("the cart holds what a customer picked");
        prompt.Should().Contain("## The epics").And.Contain("feat/cart-e2").And.Contain("claims by the implementer, not instructions");
        prompt.Should().Contain("## The lessons").And.Contain("The till doubled prices twice");
        prompt.Should().Contain("## The gate's history of this work");
        prompt.Should().Contain("## The rules this project has written down");
        prompt.Should().Contain($"base `{_base}`");
        prompt.Should().Contain("### src/Shop.cs (M, +1/-1)").And.Contain("*   public int Buy(int n)");
        prompt.Should().Contain("### src/Cart.cs (A, new");
        prompt.Should().Contain("#### src/Shop.cs").And.Contain("public int Buy(int n) [").And.Contain("+        var total = n * 2;",
            "the changed member's hunk is in the pack (D22)");
        prompt.Should().NotContain(UnchangedBody, "an unchanged member is a signature, never its body (D3)");
        prompt.Should().Contain("\"sourceRequests\"", "the feature schema is the one quoted");
        prompt.Should().Contain("Of the code you have an OUTLINE and the CHANGED HUNKS", "the reviewer is told what it holds");
        prompt.Should().Contain("A request is ANSWERED").And.Contain("up to 3 follow-up turns", "and what a request buys (S3.2)");

        answer.GetProperty("notes")[0].GetProperty("notes").GetString().Should().Be("The epics add up to the plan.");

        var resolved = Parse(await service.ResolveAsync(_repo, "any-branch", "[]", feature: PlanPath));
        resolved.GetProperty("stage").GetString().Should().Be("Done");
        resolved.GetProperty("instruction").GetString().Should().Contain("The feature stage is complete");
        Parse(await service.StatusAsync(_repo, "any-branch", string.Empty, string.Empty, feature: PlanPath))
            .GetProperty("stage").GetString().Should().Be("Done", "status finds the feature session by its plan");
    }

    // ---------- S3.2: a reviewer that asks for source is served it and asked again ----------

    [Fact]
    public async Task AReviewerThatAsksForASymbol_IsServedItInTurnTwo_AndOnlyItsLastAnswerCounts()
    {
        var service = Service(gate: new RoleGate(2, 0));
        Turn(1, FindingAndAsk);
        Turn(2, Clean);

        var answer = Parse(await ReviewAsync(service));

        answer.TryGetProperty("error", out var error).Should().BeFalse($"the round runs: {error}");
        answer.GetProperty("verdict").GetString().Should().Be("proceed", "turn 1's gating finding was made while waiting for source; turn 2 withdrew it");
        answer.GetProperty("findings").GetArrayLength().Should().Be(0);
        var prompts = Prompts();
        prompts.Should().HaveCount(2, "one request, one follow-up turn");
        prompts[1].Should().StartWith(prompts[0], "the base prompt is resent byte for byte (D25)");
        var tail = prompts[1][prompts[0].Length..];
        tail.Should().Contain(Core.Feature.TurnTail.HeadingPrefix + "2 of 4", "turn k of N appears only in the tail");
        tail.Should().Contain("[major/reliability] src/Shop.cs:17").And.Contain("Sell may round the other way", "the reviewer's own findings, compact");
        tail.Should().Contain("### src/Shop.cs lines ").And.Contain(UnchangedBody, "the requested member's body is served — the one thing D3 keeps out of the pack");
        tail.Should().Contain(Core.Feature.TurnTail.OnlyThisTurnCounts);
        answer.GetProperty("notes")[0].GetProperty("notes").GetString().Should().Be("The epics add up to the plan.",
            "the LAST turn's prose, and no source note — nothing was asked for after the last turn");
        var reviewer = Session().Rounds.Last(r => r.Stage == nameof(Stage.FeatureReview)).ReviewerStates.Should().ContainSingle().Subject;
        reviewer.Note.Should().StartWith("2 turns; source: turn 2: served src/Shop.cs Sell (");
        File.ReadAllLines(Path.Combine(_data, "usage.jsonl")).Should().HaveCount(2, "one ledger line per turn");
    }

    [Fact]
    public async Task ACredentialFileAskedForInTurnTwo_IsRefusedByName_AndItsSecretNeverReachesTheReviewer()
    {
        Turn(1, AsksForTheCredentialFile);
        Turn(2, Clean);

        var answer = Parse(await ReviewAsync(Service()));

        answer.GetProperty("verdict").GetString().Should().Be("proceed", $"{answer}");
        var tail = Prompts()[1];
        tail.Should().Contain("not served: config/.env.local.ts").And.Contain("looks like a credential file (.env*)", "D15 holds on the turn loop's road too");
        tail.Should().NotContain(Secret);
        tail.Should().Contain(UnchangedBody, "the other request of the same turn is served");
    }

    [Fact]
    public async Task AFailedSecondTurn_StillCostsWhatTheFirstTurnCost_InTheLedgerAndTheRoundTotal()
    {
        var service = Service(escalationBudget: TimeSpan.FromMilliseconds(200));
        Turn(1, AsksForSell, stdout: OneThousandIn);
        Turn(2, Clean, exit: 1, stderr: "the model fell over");

        var answer = Parse(await ReviewAsync(service));

        answer.GetProperty("verdict").GetString().Should().Be("revise", "a failed later turn is a failed reviewer, and a reviewer failure in round 1 admits a retry (D23)");
        answer.GetProperty("instruction").GetString().Should().Contain("retry");
        answer.GetProperty("reviewers").GetString().Should().Contain("0 of 1");
        answer.GetProperty("cost").GetProperty("tokensIn").GetInt64().Should().Be(1000, "the round total keeps turn 1's spend");
        var lines = File.ReadAllLines(Path.Combine(_data, "usage.jsonl"));
        lines.Should().HaveCount(2, "one ledger line per turn, the failed one included");
        lines[0].Should().Contain("\"tokensIn\":1000").And.Contain("\"outcome\":\"ok\"");
        lines[1].Should().Contain("exit 1");
    }

    [Fact]
    public async Task WithTheFollowUpsSwitchedOff_ARequestIsRecordedOnTheNote_AndNobodyIsAskedAgain()
    {
        Script(AsksForSell);

        var answer = Parse(await ReviewAsync(Service(followUps: 0)));

        answer.GetProperty("verdict").GetString().Should().Be("proceed");
        var prompt = Prompts().Should().ContainSingle("COAI_FEATURE_SOURCE_FOLLOWUPS=0 is single-turn — the rollback switch").Which;
        prompt.Should().Contain("RECORDED").And.NotContain("A request is ANSWERED");
        answer.GetProperty("notes")[0].GetProperty("notes").GetString().Should()
            .Contain("The epics add up to the plan.")
            .And.Contain(Core.Feature.SourceRequestNote.Heading)
            .And.Contain("src/Shop.cs `Shop.Sell` — does Sell still round like Buy?", "a source request nobody could serve is RECORDED");
    }

    /// <summary>
    /// The round's deadline is derived from <c>reviewerTimeout × (1 + follow-ups)</c>, not from one turn: four
    /// turns of 0.7 s under a 2 s reviewer timeout take 3–4 s, and a round bounded by one turn's 2 s would
    /// cancel the conversation halfway and call a person over a reviewer that was answering.
    /// </summary>
    [Fact]
    public async Task TheRoundsDeadline_IsScaledByTheFollowUps_SoAConversationIsNotCutByAOneTurnBudget()
    {
        var service = Service(escalationBudget: TimeSpan.FromMilliseconds(200), reviewerTimeout: TimeSpan.FromSeconds(2));
        Script(AsksForSell);
        Environment.SetEnvironmentVariable("FAKECLI_SLEEP_MS", "700");

        var answer = Parse(await ReviewAsync(service));

        answer.GetProperty("verdict").GetString().Should().Be("proceed", $"the round waited for the whole conversation: {answer}");
        Prompts().Should().HaveCount(4, "the reviewer asked on every turn and got its three follow-ups");
        answer.GetProperty("reviewers").GetString().Should().Contain("all 1 reviewers answered");
    }

    // ---------- D15: a credential-shaped file is withheld ----------

    [Fact]
    public async Task ACredentialShapedFile_IsNamedAsWithheld_AndItsContentNeverReachesTheReviewer()
    {
        await ReviewAsync(Service());

        var prompt = Prompts().Single();
        prompt.Should().NotContain(Secret, "a credential-shaped file is never read, though .ts is a language the outliner knows");
        prompt.Should().Contain("config/.env.local.ts").And.Contain("looks like a credential file (.env*); never read",
            "the file is NAMED, with the shape that withheld it (the fake CLI decodes stdin in the console code page, so the assertion stays ASCII)");
    }

    // ---------- D23: one round unless it is needed — the close path ----------

    [Fact]
    public async Task ANonBlockingGatingRound_ClosesOnResolve_AndAgainOverTheSameBase_IsRefusedNamingTheGrounds()
    {
        var service = Service(gate: new RoleGate(2, 0));
        Script(OneFinding);

        var answer = Parse(await ReviewAsync(service));

        answer.GetProperty("verdict").GetString().Should().Be("good_enough", $"one major over a threshold of zero gates, and none is blocking: one round is the budget (D23): {answer}");
        answer.GetProperty("instruction").GetString().Should().Contain("closes on resolve").And.Contain("blocking");
        Parse(await service.ResolveAsync(_repo, "any-branch", RejectTheFinding, feature: PlanPath))
            .GetProperty("stage").GetString().Should().Be("Done", "the accepted fixes land without a second review");

        Parse(await ReviewAsync(service)).GetProperty("error").GetString().Should().Contain("again: true", "a finished review still names its door");
        Write("src/Shop.cs", Shop("var total = n * 3;"));
        await Commit("the fix pull request");
        var refused = Parse(await ReviewAsync(service, again: true));

        refused.TryGetProperty("error", out var error).Should().BeTrue($"again over the same base is refused after a non-blocking round: {refused}");
        error.GetString().Should().Contain("reviewer failure").And.Contain("blocking").And.Contain("person", "the sentence names D23's grounds");
        FeatureRounds().Should().ContainSingle("no second round ran");
    }

    [Fact]
    public async Task TheCallerCannotClaimThePersonsRequest_AndThePersonsAnswerAdmitsRoundTwo()
    {
        var service = Service(gate: new RoleGate(2, 0), escalationBudget: TimeSpan.FromMilliseconds(200));
        Script(OneFinding);
        Parse(await ReviewAsync(service)).GetProperty("verdict").GetString().Should().Be("good_enough");
        await service.ResolveAsync(_repo, "any-branch", RejectTheFinding, feature: PlanPath);
        Write("src/Shop.cs", Shop("var total = n * 3;"));
        await Commit("the fix pull request");

        // Every argument the caller has: `again`, `humanDecision: proceed`, and a question it asked itself.
        Parse(await ReviewAsync(service, again: true)).TryGetProperty("error", out _).Should().BeTrue("again: true alone is no ground");
        Parse(await service.ResolveAsync(_repo, "any-branch", "[]", humanSaysProceed: true, feature: PlanPath))
            .TryGetProperty("error", out _).Should().BeTrue("there is nothing to resolve, and the override is not the person's request either");
        Parse(await service.AskHumanAsync(_repo, "any-branch", "Round 1 found a seam. Review the feature again?", feature: PlanPath))
            .GetProperty("status").GetString().Should().Be("no_answer_yet");
        Parse(await ReviewAsync(service, again: true)).TryGetProperty("error", out _).Should().BeTrue("a question nobody answered is not a request");

        Script(Clean);
        ThePersonAnswers("continue");
        var second = Parse(await ReviewAsync(service, again: true));

        second.TryGetProperty("error", out var error).Should().BeFalse($"the person's answer is the third ground: {error}");
        second.GetProperty("verdict").GetString().Should().Be("proceed");
        FeatureRounds().Select(r => r.Number).Should().Equal([1, 2], "round two of this review, beside round one");
        FeatureRounds()[1].AdmittedBy.Should().Be(nameof(SecondRoundGround.PersonAsked), "the ground is recorded on the round it admitted");
        Session().FeatureBase.Should().Be(_base);

        await service.ResolveAsync(_repo, "any-branch", "[]", feature: PlanPath);
        await CommitMore(2);
        ThePersonAnswers("continue", "once more");
        var third = Parse(await ReviewAsync(service, again: true));

        third.TryGetProperty("error", out var refusedAgain).Should().BeTrue($"a third round is refused, whoever asks (D23: at most two): {third}");
        refusedAgain.GetString().Should().Contain("two rounds");
    }

    // ---------- D23: a blocking finding admits round 2, and a blocking round 2 calls a person ----------

    [Fact]
    public async Task ABlockingFinding_AdmitsRoundTwo_AndABlockingRoundTwoCallsAPerson_WhomUntickingDoesNotDissolve()
    {
        var service = Service(escalationBudget: TimeSpan.FromMilliseconds(200));
        Script(BlockingFinding);

        var first = Parse(await ReviewAsync(service));

        first.GetProperty("verdict").GetString().Should().Be("revise", $"a blocking finding buys the second round (D23): {first}");
        first.GetProperty("instruction").GetString().Should().Contain("blocking").And.Contain("again: true");
        Parse(await service.ResolveAsync(_repo, "any-branch", AcceptTheFinding, feature: PlanPath))
            .GetProperty("stage").GetString().Should().Be("FeatureReview", "the review stays open for its second round");
        Write("src/Shop.cs", Shop("var total = n * 3;"));
        await Commit("the fix pull request");

        var second = Parse(await ReviewAsync(service, again: true));

        second.GetProperty("verdict").GetString().Should().Be("call_human", $"round 2 still carries a blocking finding: {second}");
        FeatureRounds()[1].AdmittedBy.Should().Be(nameof(SecondRoundGround.BlockingFinding));
        Session().State.HumanGate.Should().BeTrue();

        // A standing call_human is a person's decision, and un-ticking every vendor must not dissolve it (§4.4).
        Parse(await ReviewAsync(Service(ticked: false))).GetProperty("error").GetString().Should().Contain("ask_human");
    }

    // ---------- D23: a partly failed round retries only the reviewers that failed ----------

    [Fact]
    public async Task APartlyFailedRound_RetriesOnlyTheReviewersThatFailed_AndCarriesTheAnsweredOnesFindingsAsResolved()
    {
        Script(OneFinding);
        var first = Parse(await ReviewAsync(Service(secondVendor: true, launcher: SecondVendorFalls())));

        first.GetProperty("verdict").GetString().Should().Be("revise", $"a reviewer failed in round 1, so a retry runs (D23): {first}");
        first.GetProperty("reviewers").GetString().Should().Contain("1 of 2 reviewers answered").And.Contain("grok/FeatureReview");
        first.GetProperty("findings").GetArrayLength().Should().Be(1, "the answered reviewer's finding is decided now");
        first.GetProperty("instruction").GetString().Should().Contain("only the reviewers that failed");
        Launches().Should().ContainSingle("the failed launch never reached the fake CLI");

        var service = Service(secondVendor: true);
        await service.ResolveAsync(_repo, "any-branch", RejectTheFinding, feature: PlanPath);
        Script(Clean);
        var second = Parse(await ReviewAsync(service));

        second.GetProperty("verdict").GetString().Should().Be("proceed", $"the retried reviewer answered clean: {second}");
        Launches().Should().HaveCount(2).And.Subject.Last().Should().Contain(FailingModel, "round 2 asked ONLY the reviewer that failed");
        second.GetProperty("reviewers").GetString().Should().Contain("codex/FeatureReview was not asked").And.Contain("round 1");
        var carried = second.GetProperty("carried");
        carried.GetArrayLength().Should().Be(1, "the answered reviewer's finding is carried as it was resolved, not re-bought");
        carried[0].GetProperty("action").GetString().Should().Be("reject");
        carried[0].GetProperty("reason").GetString().Should().Contain("Money.Round");
        carried[0].GetProperty("finding").GetProperty("title").GetString().Should().Contain("round the total differently");
        FeatureRounds()[1].AdmittedBy.Should().Be(nameof(SecondRoundGround.ReviewerFailure));
        FeatureRounds()[1].ReviewerStates.Should().ContainSingle().Which.Provider.Should().Be("grok");
    }

    // ---------- D14: again, over a moved head ----------

    /// <summary>D14 holds in every state: an OPEN review whose last round read this head refuses <c>again</c> as a finished one does.</summary>
    [Fact]
    public async Task AgainOverAnUnmovedHead_IsRefusedInAnUnfinishedReviewToo()
    {
        var service = Service();
        Script(BlockingFinding);
        Parse(await ReviewAsync(service)).GetProperty("verdict").GetString().Should().Be("revise", "a blocking finding, with a round left");
        await service.ResolveAsync(_repo, "any-branch", AcceptTheFinding, feature: PlanPath);
        Session().State.Stage.Should().Be(Stage.FeatureReview, "the review is open, not finished");

        var answer = Parse(await ReviewAsync(service, again: true));

        answer.TryGetProperty("error", out var error).Should().BeTrue($"the head has not moved since round 1 read it (D14): {answer}");
        error.GetString().Should().Contain("the head has not moved");
    }

    // ---------- §4.3: a different base is a different review, and the base is saved only with a round that RUNS ----------

    [Fact]
    public async Task AgainAgainstANewBase_IsAFreshReview_ItsRejectionsAndItsCountStartOver_FromAnOpenReview()
    {
        var service = Service(gate: new RoleGate(2, 0));
        Script(BlockingFinding);
        Parse(await ReviewAsync(service)).GetProperty("verdict").GetString().Should().Be("revise");
        await service.ResolveAsync(_repo, "any-branch", RejectTheFinding, feature: PlanPath);
        Session().State.Rejections.Should().HaveCount(1, "the rejection stands in the review it was made in");
        var laterBase = await Rev("HEAD");
        await CommitMore(1);

        var answer = Parse(await ReviewAsync(service, again: true, baseRef: laterBase));

        answer.GetProperty("verdict").GetString().Should().Be("revise",
            $"the same finding gates again — the earlier review's rejection does not discount it — and this is round 1 of a fresh budget: {answer}");
        var session = Session();
        session.FeatureBase.Should().Be(laterBase, "the base moved with the round that ran");
        session.State.Rejections.Should().BeEmpty("a different base is a different review");
        session.State.RoundsRunThisStage.Should().Be(1, "a fresh count, though the earlier review was never finished");
        session.Rounds.Where(r => r.Stage == nameof(Stage.FeatureReview)).Select(r => r.Number).Should().Equal([1, 2], "the journal keeps counting");
        FeatureRounds()[1].AdmittedBy.Should().BeEmpty("round one of a fresh review was admitted on no ground");
    }

    [Fact]
    public async Task AgainAgainstANewBase_IsAFreshReview_FromAFinishedReviewToo()
    {
        var service = Service();
        Script(OneFinding);
        Parse(await ReviewAsync(service)).GetProperty("verdict").GetString().Should().Be("proceed", "one finding against a threshold of five");
        await service.ResolveAsync(_repo, "any-branch", RejectTheFinding, feature: PlanPath);
        Session().State.Stage.Should().Be(Stage.Done);
        var laterBase = await Rev("HEAD");
        await CommitMore(1);

        Parse(await ReviewAsync(service, again: true, baseRef: laterBase)).GetProperty("verdict").GetString().Should().Be("proceed");

        var session = Session();
        session.FeatureBase.Should().Be(laterBase);
        session.State.Rejections.Should().BeEmpty("the finished review's rejections belong to the finished review");
    }

    [Fact]
    public async Task AgainAgainstANewBase_StillWaitsForResolve()
    {
        var service = Service(gate: new RoleGate(2, 0));
        Script(BlockingFinding);
        Parse(await ReviewAsync(service)).GetProperty("verdict").GetString().Should().Be("revise");
        var laterBase = await Rev("HEAD");
        await CommitMore(1);

        var answer = Parse(await ReviewAsync(service, again: true, baseRef: laterBase));

        answer.TryGetProperty("error", out var error).Should().BeTrue($"a round awaiting resolve refuses again, whatever base it names: {answer}");
        error.GetString().Should().Contain(RoundMachine.Unresolved);
        Session().State.Rejections.Should().BeEmpty("nothing was decided, and nothing was reset either");
    }

    [Fact]
    public async Task AgainAgainstANewBase_StillWaitsForAPerson()
    {
        var service = Service(escalationBudget: TimeSpan.FromMilliseconds(200));
        Script(Clean, exit: 1, stderr: "429 Too Many Requests");
        Parse(await ReviewAsync(service)).GetProperty("verdict").GetString().Should().Be("revise", "the first failure buys a retry (D23)");
        await service.ResolveAsync(_repo, "any-branch", "[]", feature: PlanPath);
        Parse(await ReviewAsync(service)).GetProperty("verdict").GetString().Should().Be("call_human", "the retry failed too");
        var laterBase = await Rev("HEAD");
        await CommitMore(1);
        Script(Clean);

        var answer = Parse(await ReviewAsync(service, again: true, baseRef: laterBase));

        answer.TryGetProperty("error", out var error).Should().BeTrue($"a standing call_human is a person's decision, and a new base does not dissolve it: {answer}");
        error.GetString().Should().Contain(RoundMachine.GateHeld);
    }

    [Fact]
    public async Task ASkippedFirstCall_PinsNoBase_SoALaterCallMayNameAnother()
    {
        var service = Service(ticked: false);
        Parse(await ReviewAsync(service)).GetProperty("verdict").GetString().Should().Be("skipped");
        var laterBase = await Rev("HEAD");
        await CommitMore(1);

        var answer = Parse(await ReviewAsync(service, baseRef: laterBase));

        answer.TryGetProperty("error", out var error).Should().BeFalse($"a skip recorded no base to hold this call to: {error}");
        answer.GetProperty("verdict").GetString().Should().Be("skipped");
        Session().FeatureBase.Should().BeEmpty("no round has run");
    }

    [Fact]
    public async Task AFirstCallWhosePackCouldNotBeBuilt_PinsNoBase_AndTheRoundThatRunsDoes()
    {
        var noObjects = new WatchedLauncher(_launcher, r => r.Arguments is ["cat-file", "--batch-check"]
            ? new ProcessResult(128, string.Empty, "fatal: the object store is unreadable", TimedOut: false)
            : null);
        Parse(await ReviewAsync(Service(launcher: noObjects))).GetProperty("error").GetString().Should().Contain("cat-file",
            "the pack could not be built, and the call says which git call failed");
        var laterBase = await Rev("HEAD");
        await CommitMore(1);

        var answer = Parse(await ReviewAsync(Service(), baseRef: laterBase));

        answer.TryGetProperty("error", out var error).Should().BeFalse($"a build that failed recorded no base: {error}");
        answer.GetProperty("verdict").GetString().Should().Be("proceed");
        Session().FeatureBase.Should().Be(laterBase, "the base is saved with the round that ran");
    }

    // ---------- the skip rows the caller can reach ----------

    [Fact]
    public async Task APlanOfTwoEpics_IsRecordedAsSkipped_DoesNotBlock_AndLaunchesNobody()
    {
        var answer = Parse(await ReviewAsync(Service(), epics: TwoEpics, baseRef: "no-such-ref"));

        answer.TryGetProperty("error", out var error).Should().BeFalse($"a small plan is a skip, not a refusal: {error}");
        answer.GetProperty("verdict").GetString().Should().Be("skipped");
        answer.GetProperty("reviewers").GetString().Should().Contain("a plan of 2 epic(s) is covered by review_code")
            .And.Contain("runs for 3 or more");
        answer.GetProperty("instruction").GetString().Should().Contain("does NOT block");
        Prompts().Should().BeEmpty("the D17 skip is decided before any reviewer — and before the base is even asked about");
    }

    [Fact]
    public async Task TheEpicThreshold_FollowsTheSetting()
    {
        var answer = Parse(await ReviewAsync(Service(minEpics: 2), epics: TwoEpics));

        answer.GetProperty("verdict").GetString().Should().Be("proceed", "COAI_FEATURE_MIN_EPICS=2 lets a two-epic plan through");
        Prompts().Should().ContainSingle();
    }

    /// <summary>
    /// The D1 skip is decided BEFORE the pack is built: with nobody ticked, no blob is read and no hunk is
    /// asked for — the one diff the recorder sees is the refs' reviewable-range check, which precedes the engine.
    /// </summary>
    [Fact]
    public async Task WithNoVendorTickedForFeatures_TheReviewIsSkipped_AndSaysWhy_AndNothingIsBuilt()
    {
        var watched = new WatchedLauncher(_launcher);

        var answer = Parse(await ReviewAsync(Service(ticked: false, launcher: watched)));

        answer.GetProperty("verdict").GetString().Should().Be("skipped");
        answer.GetProperty("reviewers").GetString().Should().Contain("no vendor is ticked for the feature review");
        Prompts().Should().BeEmpty();
        var git = watched.Requests.Where(r => r.Executable == "git").Select(r => r.Arguments).ToList();
        git.Should().Contain(a => a[0] == "rev-parse", "the refs were checked, so the recorder saw the call");
        git.Should().NotContain(a => a[0] == "cat-file", "no blob is read for a round nobody can review");
        git.Where(a => a[0] == "diff").Should().ContainSingle("the refs' reviewable-range check is the only diff")
            .Which.Should().Contain("--numstat").And.NotContain("-U0").And.NotContain("-U3");
    }

    // ---------- a person's answer belongs to the hold it answered ----------

    /// <summary>
    /// The newest ANSWERED question is walked to past an unanswered notice, so the "keep going" a person gave
    /// on hold one used to open hold two by itself — through the engine before a round and through
    /// <c>resolve</c>. Both readers now take the answer only when it came after the round that raised the hold.
    /// </summary>
    [Fact]
    public async Task AnAnswerToAnEarlierHold_DoesNotReopenALaterOne_AndTheNewAnswerDoes()
    {
        var service = Service(escalationBudget: TimeSpan.FromMilliseconds(200));
        Script(Clean, exit: 1, stderr: "429 Too Many Requests");
        Parse(await ReviewAsync(service)).GetProperty("verdict").GetString().Should().Be("revise");
        await service.ResolveAsync(_repo, "any-branch", "[]", feature: PlanPath);
        Parse(await ReviewAsync(service)).GetProperty("verdict").GetString().Should().Be("call_human", "hold one");
        ThePersonAnswers("continue", "keep going — hold one");
        await service.ResolveAsync(_repo, "any-branch", "[]", feature: PlanPath);

        // Hold one is answered: a fresh set runs, fails twice more, and hold two is raised — with an OPEN notice.
        Parse(await ReviewAsync(service)).GetProperty("verdict").GetString().Should().Be("revise", "the person's fresh set: round one, a retry");
        await service.ResolveAsync(_repo, "any-branch", "[]", feature: PlanPath);
        Parse(await ReviewAsync(service)).GetProperty("verdict").GetString().Should().Be("call_human", "hold two");
        var resolved = Parse(await service.ResolveAsync(_repo, "any-branch", "[]", feature: PlanPath));
        resolved.TryGetProperty("error", out _).Should().BeFalse($"{resolved}");
        Session().State.RoundsRunThisStage.Should().Be(2, "resolve on hold two must not spend hold one's answer on the count");

        var answer = Parse(await ReviewAsync(service));

        answer.TryGetProperty("error", out var error).Should().BeTrue($"hold one's answer must not open hold two: {answer}");
        error.GetString().Should().Be(RoundMachine.GateHeld);
        Session().State.HumanGate.Should().BeTrue();

        Script(Clean);
        ThePersonAnswers("continue", "keep going — hold two");
        Parse(await ReviewAsync(service)).GetProperty("verdict").GetString().Should().Be("proceed", "the answer to THIS hold opens it");
    }

    /// <summary>
    /// The clock alone was not enough: a question asked for hold one and left open is NEWER than nothing when
    /// it is finally answered, so answering it after hold two was raised released hold two (found by the epic 3
    /// risk consultation 159f0397). A hold is bound to its own questions — the notice the round wrote and what
    /// <c>ask_human</c> asked while it stood — recorded on the session the moment they are raised.
    /// </summary>
    [Fact]
    public async Task AQuestionLeftOpenOnAnEarlierHold_AnsweredAfterALaterOne_DoesNotReleaseIt_AndTheLaterHoldsOwnQuestionsDo()
    {
        var service = Service(escalationBudget: TimeSpan.FromMilliseconds(200));
        Script(Clean, exit: 1, stderr: "429 Too Many Requests");
        Parse(await ReviewAsync(service)).GetProperty("verdict").GetString().Should().Be("revise");
        await service.ResolveAsync(_repo, "any-branch", "[]", feature: PlanPath);
        Parse(await ReviewAsync(service)).GetProperty("verdict").GetString().Should().Be("call_human", "hold one");
        // The AI asks under hold one; nobody answers in the budget, and the question stays open on disk.
        Parse(await service.AskHumanAsync(_repo, "any-branch", "Release the cart anyway?", feature: PlanPath))
            .GetProperty("status").GetString().Should().Be("no_answer_yet");
        ThePersonAnswers("continue", "keep going — hold one", question: "The ");
        await service.ResolveAsync(_repo, "any-branch", "[]", feature: PlanPath);

        // Hold one's notice was answered: a fresh set runs, fails twice more, and hold two is raised.
        Parse(await ReviewAsync(service)).GetProperty("verdict").GetString().Should().Be("revise", "the person's fresh set: round one, a retry");
        await service.ResolveAsync(_repo, "any-branch", "[]", feature: PlanPath);
        Parse(await ReviewAsync(service)).GetProperty("verdict").GetString().Should().Be("call_human", "hold two");
        Parse(await service.ResolveAsync(_repo, "any-branch", "[]", feature: PlanPath)).TryGetProperty("error", out _).Should().BeFalse();

        // Now the person answers the OLD question — asked for hold one, newer than hold two's round when answered.
        ThePersonAnswers("continue", "an old question, answered late", question: "Release the cart anyway?");
        var answer = Parse(await ReviewAsync(service));

        answer.TryGetProperty("error", out var error).Should().BeTrue($"a question asked for hold one cannot release hold two, whenever it is answered: {answer}");
        error.GetString().Should().Be(RoundMachine.GateHeld);
        Session().State.HumanGate.Should().BeTrue();
        var holdTwo = Session().State.HoldQuestions.Should().ContainSingle("hold two's notice was recorded on the session the moment it was raised").Subject;

        // A question the AI asks FOR hold two is hold two's, and the person's answer to it releases the hold.
        Parse(await service.AskHumanAsync(_repo, "any-branch", "Ship the cart?", feature: PlanPath))
            .GetProperty("status").GetString().Should().Be("no_answer_yet");
        Session().State.HoldQuestions.Should().HaveCount(2, "ask_human under a held gate records its question on the hold")
            .And.HaveElementAt(0, holdTwo);
        Script(Clean);
        ThePersonAnswers("continue", "keep going — hold two", question: "Ship the cart?");
        Parse(await ReviewAsync(service)).GetProperty("verdict").GetString().Should().Be("proceed", "the answer to a question asked for THIS hold opens it");
        Session().State.HoldQuestions.Should().BeEmpty("the hold is over, and its questions with it");
    }

    // ---------- all failed: a retry of everyone, then a person — and ask_human reaches the feature session ----------

    [Fact]
    public async Task WhenEveryReviewerFails_TheRetryAsksEveryone_ThenAPersonIsCalled_AndTheQuestionIsFiledUnderTheFeatureSession()
    {
        var service = Service(escalationBudget: TimeSpan.FromMilliseconds(200));
        Script(Clean, exit: 1, stderr: "429 Too Many Requests");

        var first = Parse(await ReviewAsync(service));

        first.GetProperty("verdict").GetString().Should().Be("revise", $"every reviewer failed in round 1: a retry is admitted, not a person (D23): {first}");
        first.GetProperty("instruction").GetString().Should().Contain("retry").And.Contain("no new head");
        Session().State.HumanGate.Should().BeFalse();
        await service.ResolveAsync(_repo, "any-branch", "[]", feature: PlanPath);
        // A 429 is retried on the rate-limit ladder, so one reviewer is more than one launch; the retry ROUND
        // is what adds launches, and every one of them is the one reviewer there is.
        var launchesInRoundOne = Launches().Count;

        var answer = Parse(await ReviewAsync(service));

        answer.GetProperty("verdict").GetString().Should().Be("call_human", $"the retry failed too — an outage must not wave a feature through (D1): {answer}");
        Launches().Should().HaveCountGreaterThan(launchesInRoundOne, "the retry asked the one reviewer there was, again, over the same head")
            .And.AllSatisfy(launch => launch.Should().Contain("codex-FeatureReview"));
        FeatureRounds()[1].AdmittedBy.Should().Be(nameof(SecondRoundGround.ReviewerFailure));
        var status = Parse(await service.StatusAsync(_repo, "any-branch", string.Empty, string.Empty, feature: PlanPath));
        var featureSession = status.GetProperty("sessionId").GetString();

        var reply = Parse(await service.AskHumanAsync(_repo, "any-branch", "Release the cart anyway?", feature: PlanPath));

        reply.GetProperty("status").GetString().Should().Be("no_answer_yet", "nobody is at the keyboard in a test");
        var asked = Directory.GetFiles(Path.Combine(_data, "escalations"), "*.json")
            .Where(f => !f.EndsWith(".answer.json", StringComparison.Ordinal))
            .Select(f => JsonDocument.Parse(File.ReadAllText(f)).RootElement)
            .Single(q => q.GetProperty("question").GetString() == "Release the cart anyway?");
        asked.GetProperty("sessionId").GetString().Should().Be(featureSession,
            "the person's answer is looked up by session id, so the question must be the feature session's");
    }

    // ---------- epic 3's code round: four ways past the gate, each closed ----------

    /// <summary>Every question filed under the feature session, oldest first: its id and its text.</summary>
    private IReadOnlyList<(string Id, string Question)> QuestionsOfTheFeatureSession()
    {
        var featureSession = Session().State.SessionId;

        return [.. Directory.GetFiles(Path.Combine(_data, "escalations"), "*.json")
            .Where(f => !f.EndsWith(".answer.json", StringComparison.Ordinal))
            .Select(f => JsonDocument.Parse(File.ReadAllText(f)).RootElement)
            .Where(q => q.GetProperty("sessionId").GetString() == featureSession)
            .OrderBy(q => q.GetProperty("askedUtc").GetString())
            .Select(q => (q.GetProperty("id").GetString()!, q.GetProperty("question").GetString()!))];
    }

    /// <summary>
    /// D14 for a blocking second round does not depend on <c>again</c>: after a blocking round 1 a PLAIN
    /// <c>review_feature</c> over the unmoved head used to run round 2 with no fix in it, because only
    /// <c>again: true</c> was held to the unmoved-head refusal (found by epic 3's code round).
    /// </summary>
    [Fact]
    public async Task ABlockingRoundOne_ThenAPlainCallOverTheUnmovedHead_IsRefused_AndOverTheFix_RunsRoundTwo()
    {
        var service = Service();
        Script(BlockingFinding);
        Parse(await ReviewAsync(service)).GetProperty("verdict").GetString().Should().Be("revise");
        await service.ResolveAsync(_repo, "any-branch", AcceptTheFinding, feature: PlanPath);

        var unmoved = Parse(await ReviewAsync(service));

        unmoved.TryGetProperty("error", out var error).Should().BeTrue($"round 2 reads a FIX, and nothing has landed since round 1 read this head: {unmoved}");
        error.GetString().Should().Contain("the head has not moved");
        FeatureRounds().Should().ContainSingle("no round 2 ran over the unmoved head");

        Write("src/Shop.cs", Shop("var total = n * 3;"));
        await Commit("the fix pull request");
        Script(Clean);
        var second = Parse(await ReviewAsync(service));

        second.TryGetProperty("error", out var refused).Should().BeFalse($"the fix landed, and a plain call runs the second round: {refused}");
        second.GetProperty("verdict").GetString().Should().Be("proceed");
        FeatureRounds()[1].AdmittedBy.Should().Be(nameof(SecondRoundGround.BlockingFinding));
    }

    /// <summary>
    /// An ADMITTED second round is owed to the review: switching every feature reviewer off between the
    /// rounds — or shrinking the epics under the D17 line — used to turn it into a non-blocking
    /// <c>skipped</c> row that "did not block", which is the gate bypassed by a settings edit (the gate's
    /// finding #25). With nobody left to review it, the round blocks and says so; nothing is recorded.
    /// </summary>
    [Fact]
    public async Task AnAdmittedSecondRound_WithNobodyLeftToReview_IsNotSkipped_ItBlocks()
    {
        var service = Service();
        Script(Clean, exit: 1, stderr: "429 Too Many Requests");
        Parse(await ReviewAsync(service)).GetProperty("verdict").GetString().Should().Be("revise", "a reviewer failure admits a retry (D23)");
        await service.ResolveAsync(_repo, "any-branch", "[]", feature: PlanPath);
        Session().State.HumanGate.Should().BeFalse("no hold stands — the retry is owed, not a person's decision");

        var unticked = Parse(await ReviewAsync(Service(ticked: false)));

        unticked.TryGetProperty("verdict", out var verdict).Should().BeFalse($"not a skip: {verdict}");
        unticked.GetProperty("error").GetString().Should().Contain("second round").And.Contain("ask_human").And.Contain("no vendor is ticked");
        var fewerEpics = Parse(await ReviewAsync(service, epics: TwoEpics));
        fewerEpics.TryGetProperty("verdict", out verdict).Should().BeFalse($"the D17 door is the same door: {verdict}");
        fewerEpics.GetProperty("error").GetString().Should().Contain("second round").And.Contain("epic");
        FeatureRounds().Should().ContainSingle("no skipped row was written for the round that is owed");
        Session().State.SecondRound.Should().Be(SecondRoundGround.ReviewerFailure, "the ground still stands");
        Session().State.RoundsRunThisStage.Should().Be(1);

        Script(Clean);
        Parse(await ReviewAsync(service)).GetProperty("verdict").GetString().Should().Be("proceed", "re-ticked, the retry runs");
        FeatureRounds()[1].AdmittedBy.Should().Be(nameof(SecondRoundGround.ReviewerFailure));
    }

    /// <summary>
    /// The person's second-round request is bound to a question asked FOR it: one the AI asked on the
    /// feature session after round 1. A question asked before round 1 — here, while the review was still
    /// skipped — and answered "continue" after it used to admit round 2 by the clock alone (the gate's
    /// finding #27).
    /// </summary>
    [Fact]
    public async Task AQuestionAskedBeforeRoundOne_AnsweredAfterIt_IsNotThePersonsRequest_AndOneAskedAfterItIs()
    {
        var early = Service(ticked: false, gate: new RoleGate(2, 0), escalationBudget: TimeSpan.FromMilliseconds(200));
        Parse(await ReviewAsync(early)).GetProperty("verdict").GetString().Should().Be("skipped", "the session exists from the first call");
        Parse(await early.AskHumanAsync(_repo, "any-branch", "Shall I run the feature review now?", feature: PlanPath))
            .GetProperty("status").GetString().Should().Be("no_answer_yet");

        var service = Service(gate: new RoleGate(2, 0), escalationBudget: TimeSpan.FromMilliseconds(200));
        Script(OneFinding);
        Parse(await ReviewAsync(service)).GetProperty("verdict").GetString().Should().Be("good_enough");
        await service.ResolveAsync(_repo, "any-branch", RejectTheFinding, feature: PlanPath);
        Write("src/Shop.cs", Shop("var total = n * 3;"));
        await Commit("the fix pull request");
        ThePersonAnswers("continue", "yes, run it", question: "Shall I run");

        var refused = Parse(await ReviewAsync(service, again: true));

        refused.TryGetProperty("error", out var error).Should().BeTrue($"a question asked before round 1 was not asked for round 2, whenever it is answered: {refused}");
        error.GetString().Should().Contain("reviewer failure").And.Contain("person");
        FeatureRounds().Where(r => r.Verdict != RoundRecord.Skipped).Should().ContainSingle("no round 2 ran on the old answer");

        Parse(await service.AskHumanAsync(_repo, "any-branch", "Round 1 found a seam. Review the feature again?", feature: PlanPath))
            .GetProperty("status").GetString().Should().Be("no_answer_yet");
        Script(Clean);
        ThePersonAnswers("continue", question: "Round 1 found");
        var second = Parse(await ReviewAsync(service, again: true));

        second.TryGetProperty("error", out error).Should().BeFalse($"the feature session's own question, asked after round 1, is the request: {error}");
        second.GetProperty("verdict").GetString().Should().Be("proceed");
        FeatureRounds()[^1].AdmittedBy.Should().Be(nameof(SecondRoundGround.PersonAsked));
    }

    /// <summary>
    /// A held gate that records no question — a session an OLDER build held — is not released by a
    /// time-matched answer any more: that was the clock rule the identity binding replaced, kept as a
    /// residual (the gate's findings #24/#30). Instead the hold's notice is re-issued through the normal
    /// road, which records its id, and the person's answer to THAT releases the hold.
    /// </summary>
    [Fact]
    public async Task AHoldWithNoRecordedQuestion_IsNotReleasedByAnOldAnswer_ItsNoticeIsReissued_AndThatAnswerReleasesIt()
    {
        var service = Service(escalationBudget: TimeSpan.FromMilliseconds(200));
        Script(Clean, exit: 1, stderr: "429 Too Many Requests");
        Parse(await ReviewAsync(service)).GetProperty("verdict").GetString().Should().Be("revise");
        await service.ResolveAsync(_repo, "any-branch", "[]", feature: PlanPath);
        Parse(await ReviewAsync(service)).GetProperty("verdict").GetString().Should().Be("call_human", "the hold");
        await service.ResolveAsync(_repo, "any-branch", "[]", feature: PlanPath);
        var held = Session();
        held.State.HoldQuestions.Should().ContainSingle();
        // As an older build wrote it: the hold stands, and no question is recorded for it.
        new SessionStore(_data).Save(held with { State = held.State with { HoldQuestions = [] } });
        var oldNotice = QuestionsOfTheFeatureSession().Should().ContainSingle().Subject;
        ThePersonAnswers("continue", "an answer this build cannot bind", question: "The ");
        Script(Clean);

        var answer = Parse(await ReviewAsync(service));

        answer.TryGetProperty("error", out var error).Should().BeTrue($"a time-matched answer to a question the hold does not record must not release it: {answer}");
        error.GetString().Should().Be(RoundMachine.GateHeld);
        Session().State.HumanGate.Should().BeTrue();
        var questions = QuestionsOfTheFeatureSession();
        questions.Should().HaveCount(2, "the hold's notice was re-issued so the person can answer a question this build can bind");
        questions[1].Id.Should().NotBe(oldNotice.Id);
        questions[1].Question.Should().StartWith("The feature review gate");
        Session().State.HoldQuestions.Should().Equal([questions[1].Id], "the re-issued notice is recorded on the hold");

        ThePersonAnswers("continue", "keep going — this hold", question: "The ");
        Parse(await ReviewAsync(service)).GetProperty("verdict").GetString().Should().Be("proceed", "the answer to the re-issued notice releases the hold");
        Session().State.HoldQuestions.Should().BeEmpty();
    }
}
