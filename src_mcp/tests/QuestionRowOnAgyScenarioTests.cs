using System.Text.Json;
using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog.Core;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// An antigravity question row whose shell command was auto-denied is continued ONCE in the same conversation, as a
/// consultation is — through <see cref="ConsultantTurn"/>, not a second copy of its rule.
/// </summary>
/// <remarks>
/// <para>The defect, as the operator met it on 2026-10-07 (research/PLAN_a_question_row_on_agy_is_continued_once.md): a
/// <c>question-disk</c> row on <c>antigravity · gemini-3.8-flash-low</c> ended <c>failed</c> in 18.6 s, "the consultant
/// exited cleanly but answered nothing". The model's first act is <c>run_command</c>, which headless agy auto-denies;
/// the stuck consultant was cured of exactly this by E1.3, and the question row never reached that cure.</para>
/// <para>The streams are the real ones <see cref="ConsultAntigravityDenialScenarioTests"/> uses: the 2026-10-02 denied
/// consultation and an answering follow-up re-keyed to its conversation id. The fake CLI runs as a REAL child with the
/// question row's MINIMAL environment, so it is steered by the temp-directory file (it learned agy's <c>--print=</c> argv
/// for this), and it tells the follow-up apart by <see cref="AntigravityFollowUps.StaysDenied"/> on stdin.</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class QuestionRowOnAgyScenarioTests : IAsyncLifetime
{
    private const string Conversation = "5e1f2a3b-4c5d-4e6f-8a9b-0c1d2e3f4a5b";

    /// <summary>The denied stream's usage and the answering follow-up's — agy reports both CUMULATIVELY.</summary>
    private const long DeniedIn = 36652, DeniedOut = 874, AdviceIn = 34218, AdviceOut = 3494;

    private readonly ProcessLauncher _launcher = new();
    private string _repo = string.Empty;
    private string _data = string.Empty;
    private string _record = string.Empty;

    private static string FakeCliExe => Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli");

    private static string SteeringFile => Path.Combine(Path.GetTempPath(), "fakecli-minimal.json");

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "antigravity", name));

    public async ValueTask InitializeAsync()
    {
        _repo = Directory.CreateTempSubdirectory("coai-qagy-repo-").FullName;
        _data = Directory.CreateTempSubdirectory("coai-qagy-data-").FullName;
        _record = Directory.CreateTempSubdirectory("coai-qagy-argv-").FullName;
        await Git("init", "-b", "main");
        await Git("config", "user.email", "t@example.com");
        await Git("config", "user.name", "t");
        await File.WriteAllTextAsync(Path.Combine(_repo, "Mailer.cs"), "void Send() { }\n");
        await Git("add", ".");
        await Git("commit", "-m", "base");
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            File.Delete(SteeringFile);
        }
        catch (IOException) { }

        foreach (var dir in (string[])[_repo, _data, _record])
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }

        return ValueTask.CompletedTask;
    }

    private async Task Git(params string[] args)
    {
        var result = await _launcher.RunAsync(new ProcessRequest("git", args, _repo), TestContext.Current.CancellationToken);
        result.ExitCode.Should().Be(0, string.Join(' ', args) + ": " + result.StdErr);
    }

    /// <summary>
    /// The first launch prints the real denied stream and its stderr sentence; a follow-up prints
    /// <paramref name="followUpStdout"/>. By FILE: a minimal-environment child never sees this process's variables.
    /// </summary>
    private void Steer(string followUpStdout, string followUpStderr = "agy: continuing the conversation", string sideEffect = "", int followUpSleepMs = 0)
    {
        var pairs = new Dictionary<string, string>
        {
            ["FAKECLI_STDOUT"] = Fixture("consult-denied.ndjson"),
            ["FAKECLI_STDERR"] = Fixture("consult-denied.stderr.txt").Trim(),
            ["FAKECLI_REPAIR_MARKER"] = AntigravityFollowUps.StaysDenied,
            ["FAKECLI_TURN1_REPAIR_STDOUT"] = followUpStdout,
            // Non-empty on purpose: the fake CLI falls back to the bare FAKECLI_STDERR otherwise.
            ["FAKECLI_TURN1_REPAIR_STDERR"] = followUpStderr,
            ["FAKECLI_RECORD_DIR"] = _record,
        };
        if (sideEffect.Length > 0)
        {
            pairs["FAKECLI_SIDE_EFFECT"] = sideEffect;
        }

        if (followUpSleepMs > 0)
        {
            pairs["FAKECLI_TURN1_REPAIR_SLEEP_MS"] = followUpSleepMs.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        File.WriteAllText(SteeringFile, JsonSerializer.Serialize(pairs));
    }

    private PanelService Service() => new(
        new PanelSettings
        {
            Providers = [],
            Rounds = PanelConfig.Uniform(3, 2, StagePolicy.Human),
            DataDir = _data,
            ReviewerTimeout = TimeSpan.FromSeconds(30),
            QuestionConsult = new QuestionConsultSettings
            {
                Rows = [new QuestionRow("agy-disk", "antigravity", "antigravity", "gemini-3.8-flash-low", string.Empty, FakeCliExe, string.Empty, "question-disk", Enabled: true)],
                Roots = [_repo],
                RowBudget = TimeSpan.FromSeconds(60),
            },
        },
        VaultKeys.None("no vault in tests"),
        default,
        _launcher,
        Logger.None,
        Noticing.None);

    private async Task<JsonElement> AskAsync() =>
        JsonDocument.Parse(await AskRawAsync(TestContext.Current.CancellationToken)).RootElement;

    private Task<string> AskRawAsync(CancellationToken ct) =>
        Service().AskConsultantsAsync(
            _repo, "Which helper retries an outbound SMTP send with backoff?", "I found none in this checkout; Send() has no retry.", ct: ct);

    /// <summary>Every launch's argv, its stdin as the last field (the fake CLI's recorder format).</summary>
    private IReadOnlyList<string[]> Launches() =>
        [.. Directory.EnumerateFiles(_record, "*.argv").Select(path => LaunchRecords.Read(path).Split('\0'))];

    private IReadOnlyList<JsonElement> LedgerRows() =>
        [.. File.ReadAllLines(Path.Combine(_data, "usage.jsonl")).Select(line => JsonDocument.Parse(line).RootElement)];

    [Fact]
    public async Task ADeniedCommand_IsContinuedInTheSameConversation_AndTheRowAnswers()
    {
        Steer(Fixture("consult-advice.ndjson"));

        var reply = await AskAsync();

        var answer = reply.GetProperty("answers").EnumerateArray().Should().ContainSingle().Subject;
        answer.GetProperty("status").GetString().Should().Be(RowOutcomes.Answered, answer.ToString());
        answer.GetProperty("advice").GetString().Should().Contain("git grep -n QUOKKA", "the follow-up's answer reaches the caller");
        reply.GetProperty("status").GetString().Should().Be("complete");

        var launches = Launches();
        launches.Should().HaveCount(2, "one launch, and ONE follow-up");
        var followUp = launches.Single(argv => argv[^1].Contains(AntigravityFollowUps.StaysDenied, StringComparison.Ordinal));
        followUp.Should().ContainInOrder("--conversation", Conversation).And.Contain("--mode").And.Contain("--add-dir");

        var billed = LedgerRows().Should().ContainSingle("one ledger line per turn, not one per launch").Subject;
        billed.GetProperty("kind").GetString().Should().Be("question");
        billed.GetProperty("tokensIn").GetInt64().Should().Be(Math.Max(DeniedIn, AdviceIn), "agy's reports are cumulative: the larger, never the sum");
        billed.GetProperty("tokensOut").GetInt64().Should().Be(Math.Max(DeniedOut, AdviceOut));
    }

    [Fact]
    public async Task AFollowUpThatIsDeniedAgain_EndsTheRow_OnTheDenialItself_AfterTwoLaunches()
    {
        Steer(Fixture("consult-denied.ndjson"), Fixture("consult-denied.stderr.txt").Trim());

        var reply = await AskAsync();

        var answer = reply.GetProperty("answers").EnumerateArray().Should().ContainSingle().Subject;
        answer.GetProperty("status").GetString().Should().Be(RowOutcomes.Failed);
        answer.GetProperty("reason").GetString().Should()
            .Contain("reached for a shell command").And.Contain("did not answer even when told the command would not come");
        Launches().Should().HaveCount(2, "never a third launch");
        LedgerRows().Should().ContainSingle();
    }

    [Fact]
    public async Task AFollowUpCutShortByTheCaller_StillBillsTheFirstLaunch_AndNeverAsOk()
    {
        // The follow-up sleeps far past the moment the caller gives up: whatever ends the turn, the first launch's
        // usage must stay on the ledger, and the line must not read as a turn that went well (code round, codex).
        Steer(Fixture("consult-advice.ndjson"), followUpSleepMs: 30_000);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var asking = AskRawAsync(caller.Token);
        await WaitUntilAsync(() => Launches().Count == 2, TimeSpan.FromSeconds(20));
        // The positive half of the check below: the first launch has exited and the follow-up is asleep, so exactly one
        // recorded launch is alive — which is what proves the check can see one at all.
        LaunchesStillRunning().Should().ContainSingle("the follow-up is still asleep when the caller gives up");
        await caller.CancelAsync();

        try
        {
            await asking;
        }
        catch (OperationCanceledException)
        {
            // The caller gave up; what this test reads is what was written down on the way out.
        }

        LaunchesStillRunning().Should().BeEmpty("a follow-up the caller cut short is killed and reaped before the call returns");
        var billed = LedgerRows().Should().ContainSingle("the turn is billed once, cut short or not").Subject;
        billed.GetProperty("tokensIn").GetInt64().Should().Be(DeniedIn, "the first launch's usage survives a follow-up that never reported");
        billed.GetProperty("outcome").GetString().Should().NotBe("ok", "a turn that did not answer must not read as one that went well");
    }

    /// <summary>The process ids of recorded launches still alive — the fake CLI names each record after its own pid.</summary>
    /// <remarks>Distinct, because Windows hands a freed id straight out again: the follow-up can carry the first launch's.</remarks>
    private IReadOnlyList<int> LaunchesStillRunning() =>
        [.. Directory.EnumerateFiles(_record, "*.argv")
            .Select(path => int.Parse(Path.GetFileName(path).Split('-')[0], System.Globalization.CultureInfo.InvariantCulture))
            .Distinct()
            .Where(IsFakeCliRunning)];

    private static bool IsFakeCliRunning(int pid)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);

            // The name first, because an id freed by our child may already belong to somebody else's process — one this
            // account may not open, which HasExited would need a handle for.
            return process.ProcessName == "FakeCli" && !process.HasExited;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // No process has that id any more, it exited while we looked, or it is not ours to open.
            return false;
        }
    }

    [Fact]
    public async Task ACallerWhoGivesUpBetweenTheLaunches_LeavesTheFirstBilled_AsInterruptedNotOk()
    {
        // The other way a turn ends early: the caller gives up WHILE the tree is checked, before any follow-up exists. Only
        // the first launch has landed, and it exited cleanly — so its own outcome reads "ok", which a turn that never
        // answered is not (code round, codex). Driven at the row's own level: the fan-out's tree check is the seam.
        Steer(Fixture("consult-advice.ndjson"));
        var admitted = (RowAdmission.Admitted)QuestionAdmission.Admit(
            new QuestionRow("agy-disk", "antigravity", "antigravity", "gemini-3.8-flash-low", string.Empty, FakeCliExe, string.Empty, "question-disk", Enabled: true),
            new QuestionConsultSettings { Roots = [_repo] }, [], Core.Api.ApiOverrides.None);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var input = new RowLaunchInput(
            admitted, "Which helper retries an outbound SMTP send?",
            new ReviewerSettings("antigravity") { ExecutablePath = FakeCliExe, Model = "gemini-3.8-flash-low", Timeout = TimeSpan.FromSeconds(60), DataDir = _data },
            _repo, Directory.CreateDirectory(Path.Combine(_data, "answers")).FullName, string.Empty, TimeSpan.FromSeconds(60))
        {
            ChangesSoFar = async t =>
            {
                await caller.CancelAsync();
                t.ThrowIfCancellationRequested();

                return [];
            },
        };
        var launch = new QuestionRowLaunch(new ReviewerExecutor(_launcher), new UsageLedger(_data), Logger.None, _launcher);

        var giving = () => launch.RunAsync(input, new QuestionRowRecord("agy-disk", "antigravity", "gemini-3.8-flash-low", "antigravity", "question-disk", "Projects on this disk", "disk", "default-deny"), caller.Token);

        await giving.Should().ThrowAsync<OperationCanceledException>();
        Launches().Should().ContainSingle("the follow-up never started");
        var billed = LedgerRows().Should().ContainSingle().Subject;
        billed.GetProperty("tokensIn").GetInt64().Should().Be(DeniedIn, "the first launch's usage is kept");
        billed.GetProperty("outcome").GetString().Should().NotBe("ok", "the turn was cut short before it answered");
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan limit)
    {
        var deadline = DateTime.UtcNow + limit;
        while (!condition())
        {
            DateTime.UtcNow.Should().BeBefore(deadline, "the follow-up was expected to have started by now");
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task ATreeChangedByTheFirstLaunch_StopsTheFollowUp_AndTheReasonSaysSo()
    {
        Steer(Fixture("consult-advice.ndjson"), sideEffect: Path.Combine(_repo, "written-by-the-row.txt"));

        var reply = await AskAsync();

        Launches().Should().ContainSingle("a consultant that already wrote to a watched root is not given a second chance");
        var answer = reply.GetProperty("answers").EnumerateArray().Should().ContainSingle().Subject;
        answer.GetProperty("status").GetString().Should().Be(RowOutcomes.Failed);
        answer.GetProperty("reason").GetString().Should().Contain("written-by-the-row.txt")
            .And.NotContain("did not answer even when told", "no follow-up ran, so the reason must not claim one");
        LedgerRows().Should().ContainSingle();
    }
}
