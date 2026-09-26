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
        {"findings": [], "notes": "The epics add up to the plan.",
         "sourceRequests": [{"file": "src/Shop.cs", "symbol": "Shop.Sell", "startLine": null, "endLine": null, "why": "does Sell still round like Buy?"}]}
        """;

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
        Script(Clean);
    }

    public ValueTask DisposeAsync()
    {
        foreach (var name in (string[])["FAKECLI_MODE", "FAKECLI_STDOUT", "FAKECLI_OUTFILE_TEXT", "FAKECLI_EXIT", "FAKECLI_STDERR", "FAKECLI_RECORD_DIR"])
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
    /// <param name="gate">Every role's gate; one round and a threshold of five unless a test needs a <c>revise</c>.</param>
    private PanelService Service(
        bool ticked = true, int minEpics = 3, TimeSpan? escalationBudget = null, IProcessLauncher? launcher = null, RoleGate? gate = null) =>
        new(
            new PanelSettings
            {
                Providers = [new("codex") { ExecutablePath = FakeCliExe, Feature = ticked }],
                Rounds = new PanelConfig(PanelConfig.AllRoles.ToDictionary(r => r, _ => gate ?? new RoleGate(1, 5)), StagePolicy.Human),
                DataDir = _data,
                ReviewerTimeout = TimeSpan.FromSeconds(30),
                RateLimitBackoff = TimeSpan.FromMilliseconds(5),
                FeatureMinEpics = minEpics,
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
        [.. Directory.GetFiles(_record, "*.argv").Select(f => File.ReadAllText(f).Split('\0')[^1])];

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

        answer.GetProperty("notes")[0].GetProperty("notes").GetString().Should()
            .Contain("The epics add up to the plan.")
            .And.Contain("src/Shop.cs `Shop.Sell` — does Sell still round like Buy?", "a source request is RECORDED, as the prompt promises");

        var resolved = Parse(await service.ResolveAsync(_repo, "any-branch", "[]", feature: PlanPath));
        resolved.GetProperty("stage").GetString().Should().Be("Done");
        resolved.GetProperty("instruction").GetString().Should().Contain("The feature stage is complete");
        Parse(await service.StatusAsync(_repo, "any-branch", string.Empty, string.Empty, feature: PlanPath))
            .GetProperty("stage").GetString().Should().Be("Done", "status finds the feature session by its plan");
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

    // ---------- D14: again, over a moved head ----------

    [Fact]
    public async Task AFinishedReview_OpensAgainOnlyOverAMovedHead_AndBothRoundsStayOnTheRecord()
    {
        var service = Service();
        Parse(await ReviewAsync(service)).GetProperty("verdict").GetString().Should().Be("proceed");
        await service.ResolveAsync(_repo, "any-branch", "[]", feature: PlanPath);

        Parse(await ReviewAsync(service)).GetProperty("error").GetString().Should().Contain("again: true",
            "a finished feature review names its door");
        Parse(await ReviewAsync(service, again: true)).GetProperty("error").GetString().Should().Contain("the head has not moved",
            "again over the head the last round read reviews nothing new");

        Write("src/Shop.cs", Shop("var total = n * 3;"));
        await Commit("the fix pull request");
        var again = Parse(await ReviewAsync(service, again: true));

        again.GetProperty("verdict").GetString().Should().Be("proceed", $"the moved head is reviewed: {again}");
        var session = Session();
        session.Rounds.Where(r => r.Stage == nameof(Stage.FeatureReview)).Select(r => r.Number).Should().Equal([1, 2],
            "the second round is written beside the first, never over it");
        session.FeatureBase.Should().Be(_base);
    }

    /// <summary>D14 holds in every state: an OPEN review whose last round read this head refuses <c>again</c> as a finished one does.</summary>
    [Fact]
    public async Task AgainOverAnUnmovedHead_IsRefusedInAnUnfinishedReviewToo()
    {
        var service = Service(gate: new RoleGate(2, 0));
        Script(OneFinding);
        Parse(await ReviewAsync(service)).GetProperty("verdict").GetString().Should().Be("revise", "one gating finding over a threshold of zero, with a round left");
        await service.ResolveAsync(_repo, "any-branch", RejectTheFinding, feature: PlanPath);
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
        Script(OneFinding);
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
        Script(OneFinding);
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
        Parse(await ReviewAsync(service)).GetProperty("verdict").GetString().Should().Be("call_human");
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

    // ---------- all failed: a person decides, and ask_human reaches the feature session ----------

    [Fact]
    public async Task WhenEveryReviewerFails_APersonIsCalled_AndTheQuestionIsFiledUnderTheFeatureSession()
    {
        var service = Service(escalationBudget: TimeSpan.FromMilliseconds(200));
        Script(Clean, exit: 1, stderr: "429 Too Many Requests");

        var answer = Parse(await ReviewAsync(service));

        answer.GetProperty("verdict").GetString().Should().Be("call_human", $"all failed blocks (D1): {answer}");
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
}
