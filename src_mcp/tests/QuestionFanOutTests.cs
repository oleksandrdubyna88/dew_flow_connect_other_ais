using System.Diagnostics;
using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Normalizer;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog.Core;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The fan-out (PLAN_question_consultant.md, S2 acceptance 2): rows launched in parallel under their own
/// deadlines, each given what its capability allows and nothing more, each persisted as it settles, the
/// invariant once around the disk rows — over a scripted launcher, so ONE row of six can be slow and every
/// row's prompt can be read apart from its siblings'.
/// </summary>
public sealed class QuestionFanOutTests : IAsyncLifetime
{
    private const string Question = "Which retry shape fits a flaky vendor: a ladder or a circuit breaker?";

    private const string Context = "I tried a fixed 15 s wait; it burns the budget. The options are a backoff ladder or a breaker that opens after three failures.";

    private readonly ProcessLauncher _real = new();
    private readonly string _data = Directory.CreateTempSubdirectory("coai-qfanout-").FullName;
    private TempGitRepo _repo = null!;
    private TempGitRepo _projects = null!;
    private string _head = string.Empty;

    public async ValueTask InitializeAsync()
    {
        _repo = await TempGitRepo.InitAsync(_real, "coai-qfanout-repo-");
        Directory.CreateDirectory(Path.Combine(_repo.Path, "src"));
        await _repo.WriteAsync("src/Cart.cs", "public sealed class Cart\n{\n    public int Add(int n) => n + 1;\n}\n");
        await _repo.CommitAsync("the cart");
        _head = await _repo.HeadAsync();
        _projects = await TempGitRepo.InitAsync(_real, "coai-qfanout-projects-");
        await _projects.WriteAsync("Retry.cs", "public static class Retry { }\n");
        await _projects.CommitAsync("another project");
    }

    public async ValueTask DisposeAsync()
    {
        await _repo.DisposeAsync();
        await _projects.DisposeAsync();
        try
        {
            Directory.Delete(_data, recursive: true);
        }
        catch (IOException) { }
    }

    // ---------- the harness ----------

    private static QuestionRow Row(string id, string vendor, string runtime, string model, string prompt, bool enabled = true, string baseUrl = "") =>
        new(id, vendor, runtime, model, baseUrl, string.Empty, string.Empty, prompt, enabled, Acknowledged: true);

    private QuestionConsultSettings Settings(IReadOnlyList<QuestionRow> rows, TimeSpan? budget = null, IReadOnlyList<string>? roots = null) => new()
    {
        Rows = rows,
        Roots = roots ?? [_projects.Path],
        RowBudget = budget ?? TimeSpan.FromSeconds(30),
    };

    private QuestionConsultStore Store() => new(_data);

    private QuestionFanOut FanOut(ScriptedLauncher launcher, QuestionConsultSettings settings) => new(
        launcher,
        new ReviewerExecutor(launcher),
        Store(),
        new RolePrompts(_data),
        new UsageLedger(_data),
        new VaultKeys(new Dictionary<string, string> { ["grok-openrouter"] = "sk-test-0123456789abcdefghijklmnop" }, string.Empty),
        new PanelSettings { DataDir = _data, QuestionConsult = settings, Providers = [] },
        new TreeSitterOutliner(),
        ConsultSchemaFile.Ensure(Path.Combine(_data, "schemas"), QuestionAnswerSchema.Name, QuestionAnswerSchema.Json, "a test").Path,
        _ => null,
        Logger.None);

    private FanOutInput Input(QuestionConsultSettings settings, string question = Question, string context = Context) =>
        new(settings, [], Core.Api.ApiOverrides.None, question, context, _repo.Path, _head, FollowUps: 3, Nonce: "n0nce");

    private QuestionConsultRecord Fresh(string question = Question, string context = Context) =>
        new(QuestionConsultStore.NewId(), "caller-1", "claude", "no-session", _repo.Path, "main", _head, question, QuestionConsultStore.Stamp(DateTime.UtcNow))
        {
            Context = context,
        };

    private static bool IsModel(ScriptedLaunch launch, string model) => launch.Request.Arguments.Contains(model, StringComparer.Ordinal);

    /// <summary>Every vendor launch answers at once with its advice; git runs for real.</summary>
    private static Task<ScriptedAnswer?> AnswersAtOnce(ScriptedLaunch launch) => Task.FromResult<ScriptedAnswer?>(
        launch.Is("--ask-api") ? ScriptedAnswer.Api("""{"answer":"A breaker. Three failures, then open for a minute.","sourceRequests":null}""")
        : launch.Request.Executable.Contains("claude", StringComparison.OrdinalIgnoreCase) ? ScriptedAnswer.Claude("A ladder: 5, 30, 60, 120 seconds.")
        : launch.Request.Executable.Contains("codex", StringComparison.OrdinalIgnoreCase) ? ScriptedAnswer.Codex("A ladder, and give up after four.")
        : null);

    // ---------- A1: one row past its deadline, the others return ----------

    [Fact]
    public async Task OneRowPastItsDeadline_TheOthersAnswersReturn_StatusPartial_TheRowTimedOut()
    {
        var settings = Settings([Row("fast", "claude", "claude", "sonnet", "question-opinion"), Row("slow", "claude", "claude", "opus", "question-opinion")], budget: TimeSpan.FromSeconds(2));
        var launcher = new ScriptedLauncher(_real, launch => Task.FromResult<ScriptedAnswer?>(
            IsModel(launch, "opus") ? ScriptedAnswer.Hangs() : launch.Request.Executable == "claude" ? ScriptedAnswer.Claude("A ladder.") : null));
        var started = Stopwatch.StartNew();

        var settled = await FanOut(launcher, settings).RunAsync(Fresh(), Input(settings), TestContext.Current.CancellationToken);

        settled.Status.Should().Be(QuestionConsultStatuses.Partial);
        settled.Rows.Single(r => r.RowId == "fast").Status.Should().Be(RowOutcomes.Answered);
        settled.Rows.Single(r => r.RowId == "fast").Advice.Should().Be("A ladder.");
        var slow = settled.Rows.Single(r => r.RowId == "slow");
        slow.Status.Should().Be(RowOutcomes.TimedOut, "a row past its budget is a RowOutcome, not a failed question");
        slow.Reason.Should().Contain("budget");
        started.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(25), "the slow row was ended at its two-second budget, not waited out");
        settled.Outcome.Should().Be(QuestionOutcomes.AnsweredByConsultants);
        settled.EndedUtc.Should().NotBeEmpty();
    }

    // ---------- A3 / D13: a blocked row never launches ----------

    [Fact]
    public async Task ABlockedRow_IsNeverLaunched_AndSaysWhy()
    {
        var settings = Settings([
            Row("grok-disk", "grok-openrouter", "api", "x-ai/grok-4.7", "question-disk", baseUrl: "https://openrouter.ai/api/v1"),
            Row("sonnet", "claude", "claude", "sonnet", "question-opinion"),
        ]);
        var launcher = new ScriptedLauncher(_real, AnswersAtOnce);

        var settled = await FanOut(launcher, settings).RunAsync(Fresh(), Input(settings), TestContext.Current.CancellationToken);

        var blocked = settled.Rows.Single(r => r.RowId == "grok-disk");
        blocked.Status.Should().Be(RowOutcomes.Blocked);
        blocked.Reason.Should().Contain("'api'").And.Contain("'disk'");
        launcher.Vendors.Should().NotContain(l => l.Request.Arguments.Contains("--ask-api"), "a blocked pair reaches no runtime");
        settled.Rows.Single(r => r.RowId == "sonnet").Status.Should().Be(RowOutcomes.Answered);
        settled.Status.Should().Be(QuestionConsultStatuses.Answered, "the blocked row was never launched, so every launched row answered");
    }

    [Fact]
    public async Task ADisabledRow_IsRecordedDisabled_AndNeverLaunched()
    {
        var settings = Settings([Row("off", "codex", "codex", "gpt-6-astra", "question-web", enabled: false), Row("on", "claude", "claude", "sonnet", "question-opinion")]);
        var launcher = new ScriptedLauncher(_real, AnswersAtOnce);

        var settled = await FanOut(launcher, settings).RunAsync(Fresh(), Input(settings), TestContext.Current.CancellationToken);

        settled.Rows.Single(r => r.RowId == "off").Status.Should().Be(RowOutcomes.Disabled);
        launcher.Vendors.Should().OnlyContain(l => l.Request.Executable == "claude");
    }

    // ---------- A2 / D10: what each row is given ----------

    [Fact]
    public async Task AWebRow_ReceivesOnlyTheSanitisedQuestion_NoContextNoOutline()
    {
        var settings = Settings([Row("astra-web", "codex", "codex", "gpt-6-astra", "question-web")]);
        var launcher = new ScriptedLauncher(_real, AnswersAtOnce);

        var settled = await FanOut(launcher, settings).RunAsync(Fresh(), Input(settings), TestContext.Current.CancellationToken);

        var web = launcher.Vendors.Should().ContainSingle().Subject;
        web.Request.Arguments.Should().StartWith(["--search", "exec"], "codex's --search is a top-level flag (F1)");
        web.Prompt.Should().Contain(Question);
        web.Prompt.Should().NotContain("fixed 15 s wait", "the context never reaches a web row").And.NotContain(QuestionPrompt.ContextHeading).And.NotContain(QuestionPrompt.OutlineHeading);
        web.Prompt.Should().NotContain(_repo.Path).And.NotContain("Cart");
        settled.Rows.Single().Status.Should().Be(RowOutcomes.Answered);
        settled.Rows.Single().Flag.Should().Be("unconfined", "D13: codex cannot be confined, and the record says so beside the answer");
    }

    [Fact]
    public async Task ARefusedWebQuestion_NamesItsClassAndCure_AndLaunchesNothing()
    {
        var settings = Settings([Row("astra-web", "codex", "codex", "gpt-6-astra", "question-web")]);
        var launcher = new ScriptedLauncher(_real, AnswersAtOnce);
        const string withPath = "Why does D:\\work\\app\\src\\Retry.cs throw on the second attempt?";

        var settled = await FanOut(launcher, settings).RunAsync(Fresh(withPath), Input(settings, question: withPath), TestContext.Current.CancellationToken);

        var row = settled.Rows.Single();
        row.Status.Should().Be(RowOutcomes.Refused);
        row.Reason.Should().Contain("(path)").And.Contain("describe the file by what it does", "the refusal names its class and its cure");
        launcher.Vendors.Should().BeEmpty("a refused question launches nothing");
        settled.Status.Should().Be(QuestionConsultStatuses.Failed, "nothing was launched, so nothing answered");
    }

    [Fact]
    public async Task ANoneApiRow_ReceivesContextAndOutline_ADiskRow_ContextAndItsRoots()
    {
        var settings = Settings([
            Row("grok", "grok-openrouter", "api", "x-ai/grok-4.7", "question-opinion", baseUrl: "https://openrouter.ai/api/v1"),
            Row("astra-disk", "codex", "codex", "gpt-6-astra", "question-disk"),
        ]);
        var launcher = new ScriptedLauncher(_real, AnswersAtOnce);

        var settled = await FanOut(launcher, settings).RunAsync(Fresh(), Input(settings), TestContext.Current.CancellationToken);

        var api = launcher.Vendors.Single(l => l.Request.Arguments.Contains("--ask-api"));
        api.Prompt.Should().Contain("fixed 15 s wait", "the context, after the secret check");
        api.Prompt.Should().Contain("src/Cart.cs").And.Contain("Add", "the outline of what exists at HEAD");
        api.Prompt.Should().Contain("sourceRequests").And.Contain("3 follow-up");
        api.Request.Environment.Should().ContainKey(ApiRuntime.KeyVariable, "the vault key reaches the api row's child, by the row's key name");

        var disk = launcher.Vendors.Single(l => l.Request.Executable == "codex");
        disk.Prompt.Should().Contain("fixed 15 s wait").And.Contain(QuestionPrompt.RootsHeading).And.Contain(_projects.Path);
        disk.Prompt.Should().NotContain(QuestionPrompt.OutlineHeading, "a disk row has its folders, not the repository's outline");
        disk.Request.Arguments.Should().ContainInOrder("-C", _projects.Path);
        disk.Request.WorkingDirectory.Should().Be(_projects.Path, "a disk row stands in its first root");

        settled.Rows.Should().OnlyContain(r => r.Status == RowOutcomes.Answered);
        settled.Rows.Single(r => r.RowId == "grok").Advice.Should().Be("A breaker. Three failures, then open for a minute.", "the api row's envelope is unwrapped by the one answer reader");
    }

    [Fact]
    public async Task AContextCarryingASecret_RefusesTheNoneAndDiskRows_ByClass_AndTheWebRowStillRuns()
    {
        var settings = Settings([Row("sonnet", "claude", "claude", "sonnet", "question-opinion"), Row("astra-web", "codex", "codex", "gpt-6-astra", "question-web")]);
        var launcher = new ScriptedLauncher(_real, AnswersAtOnce);
        const string leaking = "the key is sk-live-0123456789abcdefghijklmnop and it still fails";

        var settled = await FanOut(launcher, settings).RunAsync(Fresh(context: leaking), Input(settings, context: leaking), TestContext.Current.CancellationToken);

        var none = settled.Rows.Single(r => r.RowId == "sonnet");
        none.Status.Should().Be(RowOutcomes.Refused);
        none.Reason.Should().Contain("vendor-key").And.NotContain("sk-live", "the refusal never quotes the secret");
        settled.Rows.Single(r => r.RowId == "astra-web").Status.Should().Be(RowOutcomes.Answered, "a web row never had the context");
        launcher.Vendors.Should().ContainSingle().Which.Request.Executable.Should().Be("codex");
    }

    // ---------- D6: six rows at once ----------

    [Fact]
    public async Task SixRowsStartWithinOneTick_NoSchedulerWave_NoRepositoryLock()
    {
        var rows = Enumerable.Range(1, 6).Select(n => Row($"r{n}", "claude", "claude", $"model-{n}", "question-opinion")).ToList();
        var settings = Settings(rows);
        var wait = TimeSpan.FromMilliseconds(600);
        var launcher = new ScriptedLauncher(_real, launch => Task.FromResult<ScriptedAnswer?>(
            launch.Request.Executable == "claude" ? ScriptedAnswer.Claude("A ladder.") with { Wait = wait } : null));
        var started = Stopwatch.StartNew();

        var settled = await FanOut(launcher, settings).RunAsync(Fresh(), Input(settings), TestContext.Current.CancellationToken);

        settled.Rows.Should().HaveCount(6).And.OnlyContain(r => r.Status == RowOutcomes.Answered);
        var starts = launcher.Vendors.Select(l => l.StartedUtc).OrderBy(t => t).ToList();
        starts.Should().HaveCount(6);
        (starts[^1] - starts[0]).Should().BeLessThan(TimeSpan.FromMilliseconds(400), "every admitted row is launched in one go — no wave");
        started.Elapsed.Should().BeLessThan(wait * 3, "six rows waiting 600 ms each in sequence would be 3.6 s");
        Directory.Exists(Path.Combine(_data, "consultations", "locks")).Should().BeFalse("no RepositoryLock is taken for a question");
    }

    // ---------- the record: before the first launch, and as each row settles ----------

    [Fact]
    public async Task TheRecordSaysConsultingWithThePid_BeforeAnyLaunch_AndEachRowIsPersistedAsItSettles()
    {
        var settings = Settings([Row("fast", "claude", "claude", "sonnet", "question-opinion"), Row("slow", "claude", "claude", "opus", "question-opinion")]);
        var fresh = Fresh();
        var store = Store();
        QuestionConsultRecord? atFirstLaunch = null;
        QuestionConsultRecord? whileSlowRuns = null;
        var launcher = new ScriptedLauncher(_real, async launch =>
        {
            if (launch.Request.Executable != "claude")
            {
                return null;
            }

            atFirstLaunch ??= store.Read(fresh.Id);
            if (!IsModel(launch, "opus"))
            {
                return ScriptedAnswer.Claude("A ladder.");
            }

            await Task.Delay(800, TestContext.Current.CancellationToken);
            whileSlowRuns = store.Read(fresh.Id);

            return ScriptedAnswer.Claude("A breaker.");
        });

        var settled = await FanOut(launcher, settings).RunAsync(fresh, Input(settings), TestContext.Current.CancellationToken);

        atFirstLaunch.Should().NotBeNull("the record is written BEFORE anything is launched");
        atFirstLaunch!.Status.Should().Be(QuestionConsultStatuses.Consulting);
        atFirstLaunch.RunnerPid.Should().Be(Environment.ProcessId);
        atFirstLaunch.HeartbeatUtc.Should().NotBeEmpty();
        atFirstLaunch.Rows.Should().OnlyContain(r => r.Status == QuestionRowStatuses.Consulting);

        whileSlowRuns.Should().NotBeNull();
        whileSlowRuns!.Rows.Single(r => r.RowId == "fast").Status.Should().Be(RowOutcomes.Answered, "the fast row was persisted the moment it settled");
        whileSlowRuns.Rows.Single(r => r.RowId == "slow").Status.Should().Be(QuestionRowStatuses.Consulting);

        settled.Rows.Should().OnlyContain(r => r.Status == RowOutcomes.Answered);
        store.Read(fresh.Id)!.Status.Should().Be(QuestionConsultStatuses.Answered);
    }

    // ---------- the invariant, around the disk rows ----------

    [Fact]
    public async Task TheInvariantIsTakenOncePerFanOut_AndABreach_WithholdsOnlyTheDiskRowsAdvice()
    {
        var settings = Settings([
            Row("astra-disk", "codex", "codex", "gpt-6-astra", "question-disk"),
            Row("terra-disk", "codex", "codex", "gpt-5.6-terra", "question-disk"),
            Row("sonnet", "claude", "claude", "sonnet", "question-opinion"),
        ]);
        var launcher = new ScriptedLauncher(_real, launch =>
        {
            if (launch.Request.Executable == "codex" && IsModel(launch, "gpt-6-astra"))
            {
                // The vendor breaking its promise: a write into the root it was given to READ.
                File.WriteAllText(Path.Combine(_projects.Path, "written-by-the-consultant.txt"), "oops\n");
            }

            return AnswersAtOnce(launch);
        });

        var settled = await FanOut(launcher, settings).RunAsync(Fresh(), Input(settings), TestContext.Current.CancellationToken);

        var statusCalls = launcher.Launches.Where(l => l.Request.Executable == "git" && l.Request.Arguments.Contains("status") && SamePath(l.Request.WorkingDirectory, _projects.Path)).ToList();
        statusCalls.Should().HaveCount(2, "one snapshot before the launches and one after — for two disk rows on one root, not four");
        settled.Alert.Should().Contain("written-by-the-consultant.txt").And.Contain("withheld");
        foreach (var disk in settled.Rows.Where(r => r.RowId.EndsWith("-disk", StringComparison.Ordinal)))
        {
            disk.Status.Should().Be(RowOutcomes.Failed, "a breach withholds the disk rows' advice");
            disk.Advice.Should().BeEmpty();
            disk.Reason.Should().Contain("written-by-the-consultant.txt");
        }

        var none = settled.Rows.Single(r => r.RowId == "sonnet");
        none.Status.Should().Be(RowOutcomes.Answered, "only the DISK rows' advice is withheld");
        none.Advice.Should().NotBeEmpty();
        File.Exists(Path.Combine(_projects.Path, "written-by-the-consultant.txt")).Should().BeTrue("nothing is deleted and nothing is reverted");
        settled.Status.Should().Be(QuestionConsultStatuses.Partial);
    }

    [Fact]
    public async Task WithoutADiskRow_NoSnapshotIsTaken()
    {
        var settings = Settings([Row("sonnet", "claude", "claude", "sonnet", "question-opinion")]);
        var launcher = new ScriptedLauncher(_real, AnswersAtOnce);

        await FanOut(launcher, settings).RunAsync(Fresh(), Input(settings), TestContext.Current.CancellationToken);

        launcher.Launches.Should().NotContain(l => l.Request.Executable == "git" && l.Request.Arguments.Contains("status"), "the invariant guards disk rows alone");
    }

    private static bool SamePath(string one, string other) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(one)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(other)), StringComparison.OrdinalIgnoreCase);
}
