using System.Text.Json;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// <c>--check-consultant</c>'s whole mode, in process: the real launcher, the real git, the fake CLI standing in for
/// the consultant's own — answered with the marker read, the canary in each of its three readings, a claude whose help
/// never answers, a turn that times out, and the refusals that launch nothing.
/// </summary>
/// <remarks>
/// <para>E4.2 of PLAN_the_consultant_works_on_every_vendor.md. In process for what needs a small reviewer timeout (the
/// setting is minutes on the wire) and for the fake CLI's steering, which travels in this process's environment —
/// hence the <c>fakecli-env</c> collection. The exit codes, the stdout shape, the HOME without a git identity, the two
/// concurrent checks and the killed holder are driven as real processes in
/// <see cref="ConsultantModesCliScenarioTests"/>.</para>
/// <para>The marker and the canary are random per check, so the stand-in "reads" them through its placeholders —
/// <c>{{cwd-file:CHECK.md}}</c> is the file inside the repository, <c>{{prompt-path:canary.txt}}</c> the one outside it
/// that the prompt names.</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class ConsultantCheckTests : IDisposable
{
    private static readonly string[] Steering =
        ["FAKECLI_MODE", "FAKECLI_STDOUT", "FAKECLI_OUTFILE_TEXT", "FAKECLI_EXIT", "FAKECLI_STDERR", "FAKECLI_SLEEP_MS",
         "FAKECLI_RECORD_DIR", "FAKECLI_HELP_STDOUT", "FAKECLI_HELP_EXIT", "FAKECLI_SIDE_EFFECT", "FAKECLI_REPAIR_MARKER", "FAKECLI_VERSION_STDOUT",
         "FAKECLI_TURN1_REPAIR_STDOUT", "FAKECLI_TURN1_REPAIR_STDERR"];

    private const string Session = "67289235-65f7-40b5-9532-e63515d90f30";

    private readonly string _data = Directory.CreateTempSubdirectory("coai-checktest-data-").FullName;
    private readonly string _temp = Directory.CreateTempSubdirectory("coai-checktest-temp-").FullName;
    private readonly string _record = Directory.CreateTempSubdirectory("coai-checktest-argv-").FullName;
    private readonly List<string> _warned = [];

    private static string FakeCliExe => Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli");

    public ConsultantCheckTests()
    {
        Environment.SetEnvironmentVariable("FAKECLI_MODE", "vendor");
        Environment.SetEnvironmentVariable("FAKECLI_RECORD_DIR", _record);
    }

    public void Dispose()
    {
        foreach (var name in Steering)
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        foreach (var dir in (string[])[_data, _temp, _record])
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A leftover temp directory is not a failing test.
            }
        }
    }

    private PanelSettings Settings(string runtime = "codex", TimeSpan? timeout = null, bool enabled = true) => new()
    {
        Providers = [],
        DataDir = _data,
        ReviewerTimeout = timeout ?? TimeSpan.FromSeconds(60),
        ConsultEnabled = enabled,
        Consultants = new Dictionary<string, ConsultantChoice>
        {
            [CallerIdentity.Claude] = new(runtime, Runtime: runtime, ExecutablePath: FakeCliExe),
        },
    };

    private Task<(int Code, JsonElement Said, string Err)> Check(PanelSettings settings, params string[] args) =>
        Check(settings, new ProcessLauncher(), TestContext.Current.CancellationToken, args);

    private async Task<(int Code, JsonElement Said, string Err)> Check(PanelSettings settings, IProcessLauncher launcher, CancellationToken ct, params string[] args)
    {
        var (code, stdout, stderr) = await ConsultantCheckMode.AnswerAsync(
            settings, ["--check-consultant", .. args], launcher, _temp, _warned.Add, Noticing.None, ct);

        return (code, stdout.Length > 0 ? JsonDocument.Parse(stdout).RootElement.Clone() : default, stderr);
    }

    private static string Text(JsonElement element, string name) => element.GetProperty(name).GetString() ?? string.Empty;

    /// <summary>The codex stand-in: a thread id on stdout, <paramref name="answer"/> in its <c>-o</c> file.</summary>
    private static void CodexAnswers(string answer)
    {
        Environment.SetEnvironmentVariable("FAKECLI_STDOUT", """{"type":"thread.started","thread_id":"0198f2c1-check"}""" + "\n");
        Environment.SetEnvironmentVariable("FAKECLI_OUTFILE_TEXT", answer);
    }

    private int TurnsLaunched => Directory.EnumerateFiles(_record, "*.argv").Count();

    private JsonElement StateFile() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(_data, "consultations", "health", "claude.check.json"))).RootElement.Clone();

    [Fact]
    public async Task ACheck_Answers_ReadsTheMarker_AndTouchesNoConsultation()
    {
        CodexAnswers("marker: {{cwd-file:CHECK.md}}\ncanary: CANNOT");

        var (code, said, _) = await Check(Settings(), "--caller", "claude");

        code.Should().Be(0);
        Text(said, "state").Should().Be("answered", said.ToString());
        said.GetProperty("answered").GetBoolean().Should().BeTrue();
        said.GetProperty("markerRead").GetBoolean().Should().BeTrue("the stand-in answered with the word CHECK.md holds");
        Text(said, "canary").Should().Be("not-attempted");
        Text(said, "vendor").Should().Be("codex");
        Text(said, "deadlineUtc").Should().NotBeEmpty("the panel's kill cap is derived from it");
        (DateTime.Parse(Text(said, "deadlineUtc"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind)
         - DateTime.Parse(Text(said, "startedUtc"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind))
            .Should().BeGreaterThan(ConsultantCheck.Deadline(TimeSpan.FromSeconds(60)) + ProcessRequest.DefaultDrainGrace,
                "the promised end includes the drain, the scratch's removal and the final write after the cancellation");
        TurnsLaunched.Should().Be(1, "exactly one paid turn");

        // What the check does NOT touch: no consultation record, no call counter.
        Directory.EnumerateFiles(Path.Combine(_data, "consultations"), "*.json").Should().BeEmpty("a check is not a consultation");
        Directory.Exists(Path.Combine(_data, "consultations", "callers")).Should().BeFalse("a check is not counted against the consult cap");
        // What it DOES: one ledger row of its own role, the final state written, the scratch gone.
        File.ReadAllLines(Path.Combine(_data, "usage.jsonl")).Should().ContainSingle(line => line.Contains("\"consult-check\""));
        Text(StateFile(), "state").Should().Be("answered");
        Text(StateFile(), "finishedUtc").Should().NotBeEmpty();
        Directory.EnumerateDirectories(_temp, "coai-check-*").Should().BeEmpty("the scratch repository is deleted at the end");
    }

    /// <summary>
    /// The check launches its consultant the way a consultation does, so it asks the installed codex its release first
    /// (research/PLAN_codex_tier_floor.md): an Off row on 0.120.0 — which refuses <c>service_tier=default</c> at config load,
    /// and so would fail every check — is sent no tier; one on 0.160.0 is sent exactly what it was before.
    /// </summary>
    [Theory]
    [InlineData("0.120.0", new string[0])]
    [InlineData("0.160.0", new[] { "service_tier=default" })]
    public async Task ACheckOfACodexConsultant_TellsItOnlyWhatItsReleaseTakes(string release, string[] sent)
    {
        Environment.SetEnvironmentVariable("FAKECLI_VERSION_STDOUT", $"codex-cli {release}\n");
        CodexAnswers("marker: {{cwd-file:CHECK.md}}\ncanary: CANNOT");

        var (code, said, _) = await Check(Settings(), "--caller", "claude");

        code.Should().Be(0, said.ToString());
        var argv = LaunchRecords.Read(Directory.EnumerateFiles(_record, "*.argv").Should().ContainSingle().Subject).Split('\0')[..^1];
        argv.Zip(argv.Skip(1)).Where(pair => pair.First == "-c" && pair.Second.StartsWith("service_tier=", StringComparison.Ordinal))
            .Select(pair => pair.Second).Should().Equal(sent, $"codex {release} with fast Off");
    }

    // ---------- --check-model <row on stdin> (PLAN_one_model_catalog.md D10, epic 2 story 4) ----------

    /// <summary>The same check, of ONE catalog row handed on stdin — a row that reviews nothing is on no wire the server reads.</summary>
    private async Task<(int Code, JsonElement Said, string Err)> CheckModel(string stdin)
    {
        var (code, stdout, stderr) = await ConsultantCheckMode.AnswerModelAsync(
            Settings(), stdin, new ProcessLauncher(), _temp, _warned.Add, Noticing.None, TestContext.Current.CancellationToken, VaultKeys.None("t"));

        return (code, stdout.Length > 0 ? JsonDocument.Parse(stdout).RootElement.Clone() : default, stderr);
    }

    private static string RowJson(string id, string runtime, string extra = "") =>
        "{\"row\":{\"id\":\"" + id + "\",\"runtime\":\"" + runtime + "\",\"executablePath\":" + JsonSerializer.Serialize(FakeCliExe) + extra + "}}";

    [Fact]
    public async Task ACheckOfAModel_Answers_UnderARecordOfItsOwn()
    {
        CodexAnswers("marker: {{cwd-file:CHECK.md}}\ncanary: CANNOT");

        var (code, said, _) = await CheckModel(RowJson("codex-2", "codex"));

        code.Should().Be(0);
        Text(said, "state").Should().Be("answered", said.ToString());
        Text(said, "vendor").Should().Be("codex-2", "the row checked is the row handed in");
        TurnsLaunched.Should().Be(1, "exactly one paid turn");
        File.Exists(Path.Combine(_data, "consultations", "health", "model-codex-2.check.json")).Should().BeTrue("one record per instance");
        File.Exists(Path.Combine(_data, "consultations", "health", "claude.check.json")).Should().BeFalse("a model's check is not the claude caller's");
    }

    [Fact]
    public async Task AModelWhoseRuntimeCannotConsult_IsUnavailable_AndNothingIsLaunched()
    {
        var (code, said, _) = await CheckModel("""{"row":{"id":"srv-codex","runtime":"remote","baseUrl":"https://coai.example"}}""");

        code.Should().Be(0, "unavailable is an answer, not a malformed request");
        Text(said, "state").Should().Be("unavailable");
        Text(said, "reason").Should().Contain("srv-codex");
        TurnsLaunched.Should().Be(0);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"row":{"runtime":"codex"}}""")]
    public async Task ACheckModelRequestWithoutARow_Is65_NeverTheOldBinarys64(string stdin)
    {
        var (code, _, err) = await CheckModel(stdin);

        code.Should().Be(65);
        err.Should().NotBeEmpty();
        TurnsLaunched.Should().Be(0);
    }

    [Fact]
    public void CheckModel_IsAKnownMode_ListedInTheHelpAndTheFeatures()
    {
        Program.Classify(["--check-model"]).Should().NotBe(Program.Startup.Usage);
        Program.HelpText.Should().Contain("`--check-model");
        FeaturesMode.Listed.Should().Contain("checkModel");
    }

    [Fact]
    public async Task ACanaryWordInTheAnswer_IsALeak()
    {
        CodexAnswers("marker: {{cwd-file:CHECK.md}}\ncanary: {{prompt-path:canary.txt}}");

        var (_, said, _) = await Check(Settings(), "--caller", "claude");

        Text(said, "canary").Should().Be("read", "the word of a file outside the repository came back");
    }

    [Fact]
    public async Task AReadTheClaudeCliRefused_IsDeniedByCli_AndTheConfinementSentIsRecorded()
    {
        Environment.SetEnvironmentVariable("FAKECLI_HELP_STDOUT", HelpFixture("help-2.1.258-windows.txt"));
        Environment.SetEnvironmentVariable("FAKECLI_STDOUT",
            $$$"""{"type":"result","subtype":"success","is_error":false,"result":"marker: {{cwd-file:CHECK.md}} canary: CANNOT","session_id":"{{{Session}}}","permission_denials":[{"tool_name":"Read","tool_use_id":"toolu_01","tool_input":{"file_path":"{{path-in-prompt:canary.txt}}"}}]}""");

        var (_, said, _) = await Check(Settings("claude"), "--caller", "claude");

        Text(said, "state").Should().Be("answered", said.ToString());
        said.GetProperty("markerRead").GetBoolean().Should().BeTrue();
        Text(said, "canary").Should().Be("denied-by-cli");
        said.GetProperty("deniedActions").EnumerateArray().Select(e => e.GetString()).Should().Equal("Read");
        Text(said, "confinement").Should().Be("restricted", "the help declared --restricted, so the turn was sent it");
    }

    [Fact]
    public async Task AClaudeWhoseHelpNeverAnswers_IsRefused_AndNothingIsLaunched()
    {
        Environment.SetEnvironmentVariable("FAKECLI_HELP_EXIT", "1");
        Environment.SetEnvironmentVariable("FAKECLI_STDOUT", """{"type":"result","result":"never sent","session_id":"x"}""");

        var (code, said, _) = await Check(Settings("claude"), "--caller", "claude");

        code.Should().Be(0, "a refusal is a classified outcome");
        Text(said, "state").Should().Be("failed");
        Text(said, "failureKind").Should().Be("vendor-refused");
        Text(said, "failureCure").Should().Contain("will not launch it unconfined");
        TurnsLaunched.Should().Be(0, "an unknown capability is never launched");
    }

    [Fact]
    public async Task ATurnPastItsBudget_IsATimeout_AndTheChildIsKilled()
    {
        CodexAnswers("too late");
        Environment.SetEnvironmentVariable("FAKECLI_SLEEP_MS", "60000");
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var (code, said, _) = await Check(Settings(timeout: TimeSpan.FromSeconds(2)), "--caller", "claude");

        code.Should().Be(0);
        Text(said, "state").Should().Be("failed");
        Text(said, "failureKind").Should().Be("timeout");
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(40), "the launcher killed the sleeping child at its budget rather than waiting a minute");
    }

    [Theory]
    [InlineData]
    [InlineData("--caller")]
    [InlineData("--caller", "copilot")]
    public async Task ARequestWithoutAKnownCaller_IsADataError_NeverTheCodeThatMeansTooOld(params string[] args)
    {
        var (code, _, err) = await Check(Settings(), args);

        code.Should().Be(65, "64 means 'this binary has never heard of --check-consultant'");
        err.Should().Contain("--caller");
        TurnsLaunched.Should().Be(0);
    }

    [Fact]
    public async Task AConsultantThatCannotBeHad_IsUnavailable_AndTakesNoLock()
    {
        var (code, said, _) = await Check(Settings(enabled: false), "--caller", "claude");

        code.Should().Be(0);
        Text(said, "state").Should().Be("unavailable");
        Text(said, "reason").Should().Contain("COAI_CONSULT_ENABLED", "the same sentence consult refuses with");
        File.Exists(Path.Combine(_data, "consultations", "health", "claude.check.json")).Should().BeFalse("nothing ran, so nothing is recorded");
        TurnsLaunched.Should().Be(0);
    }

    [Fact]
    public async Task ACheckWhileAnotherHoldsTheLock_IsAlreadyChecking_AndLaunchesNothing()
    {
        CodexAnswers("marker: x");
        var store = new ConsultCheckStore(_data);
        using var held = ConsultCheckLock.TryTake(store, "claude");
        store.Write(new ConsultCheckRecord { CallerKind = "claude", State = ConsultCheckStates.Checking, DeadlineUtc = "2026-10-03T10:06:00.0000000Z" });

        var (code, said, _) = await Check(Settings(), "--caller", "claude");

        code.Should().Be(0);
        Text(said, "state").Should().Be("already-checking");
        Text(said, "deadlineUtc").Should().Be("2026-10-03T10:06:00.0000000Z", "the running check's own times are shown");
        TurnsLaunched.Should().Be(0);
        Text(StateFile(), "state").Should().Be("checking", "only the holder writes the state");
    }

    [Fact]
    public async Task TheHolder_RefreshesTheHeartbeat_UntilItIsStopped()
    {
        var store = new ConsultCheckStore(_data);
        var checking = new ConsultCheckRecord { CallerKind = "codex", State = ConsultCheckStates.Checking, HeartbeatUtc = "2026-10-03T10:00:00.0000000Z" };
        using var stop = new CancellationTokenSource();

        var beating = ConsultCheckHeartbeat.RunAsync(store, checking, TimeSpan.FromMilliseconds(50), _warned.Add, stop.Token);
        await Task.Delay(400, TestContext.Current.CancellationToken);
        await stop.CancelAsync();
        await beating;

        var last = store.Read("codex")!.HeartbeatUtc;
        last.Should().NotBe("2026-10-03T10:00:00.0000000Z").And.NotBeEmpty();
        store.Read("codex")!.State.Should().Be(ConsultCheckStates.Checking, "a beat rewrites the stamp and nothing else");
        await Task.Delay(300, TestContext.Current.CancellationToken);
        store.Read("codex")!.HeartbeatUtc.Should().Be(last, "a stopped heartbeat beats no more");
    }

    /// <summary>
    /// The heartbeat has STOPPED before the final state is written: a beat landing after it would put <c>checking</c>
    /// back over the result. Deleting the wait for the heartbeat makes this red.
    /// </summary>
    [Fact]
    public async Task TheHeartbeatStops_BeforeTheFinalStateIsWritten()
    {
        var written = new System.Collections.Concurrent.ConcurrentQueue<string>();

        var ended = await ConsultantCheckMode.BeatingWhileAsync(
            async stop =>
            {
                // A beat already under way when the check ends — slow on purpose.
                try
                {
                    await Task.Delay(Timeout.Infinite, stop);
                }
                catch (OperationCanceledException)
                {
                    await Task.Delay(300, CancellationToken.None);
                    written.Enqueue("beat");
                }
            },
            () => Task.FromResult(new ConsultCheckRecord { State = ConsultCheckStates.Answered }));
        written.Enqueue("final");
        await Task.Delay(500, TestContext.Current.CancellationToken);

        ended.State.Should().Be(ConsultCheckStates.Answered);
        written.Should().Equal(["beat", "final"], "the final write comes after the last beat, never before it");
    }

    [Fact]
    public async Task AClaudeDenialOfSomethingElse_SaysNothingAboutTheCanary()
    {
        Environment.SetEnvironmentVariable("FAKECLI_HELP_STDOUT", HelpFixture("help-2.1.258-windows.txt"));
        Environment.SetEnvironmentVariable("FAKECLI_STDOUT",
            $$$"""{"type":"result","result":"marker: {{cwd-file:CHECK.md}} canary: CANNOT","session_id":"{{{Session}}}","permission_denials":[{"tool_name":"Glob","tool_use_id":"toolu_02","tool_input":{"pattern":"**/*.md"}}]}""");

        var (_, said, _) = await Check(Settings("claude"), "--caller", "claude");

        Text(said, "canary").Should().Be("not-attempted", "a refused glob inside the repository is not a refused read of the canary");
        said.GetProperty("deniedActions").EnumerateArray().Select(e => e.GetString()).Should().Equal("Glob");
    }

    /// <summary>An antigravity check through the real stream shapes: denied, continued once, answered — and its denial unattributed.</summary>
    [Fact]
    public async Task AnAgyCheck_IsContinuedAfterItsDenial_Answers_AndTheDenialIsUnattributed()
    {
        Environment.SetEnvironmentVariable("FAKECLI_STDOUT", AgyFixture("consult-denied.ndjson"));
        Environment.SetEnvironmentVariable("FAKECLI_STDERR", AgyFixture("consult-denied.stderr.txt").Trim());
        Environment.SetEnvironmentVariable("FAKECLI_REPAIR_MARKER", Runners.Consultation.AntigravityFollowUps.StaysDenied);
        Environment.SetEnvironmentVariable("FAKECLI_TURN1_REPAIR_STDERR", "agy: continuing the conversation");
        Environment.SetEnvironmentVariable("FAKECLI_TURN1_REPAIR_STDOUT",
            """
            {"event":"init","conversation_id":"5e1f2a3b-4c5d-4e6f-8a9b-0c1d2e3f4a5b"}
            {"event":"result","result":{"conversation_id":"5e1f2a3b-4c5d-4e6f-8a9b-0c1d2e3f4a5b","status":"SUCCESS","response":"marker: {{cwd-file:CHECK.md}} canary: CANNOT","usage":{"input_tokens":100,"output_tokens":10}}}

            """);

        var (code, said, _) = await Check(Settings("antigravity"), "--caller", "claude");

        code.Should().Be(0);
        Text(said, "state").Should().Be("answered", said.ToString());
        said.GetProperty("markerRead").GetBoolean().Should().BeTrue("the follow-up read CHECK.md");
        Text(said, "canary").Should().Be("denied-by-cli-unattributed", "agy's denied_actions carry no input to name the canary by");
        said.GetProperty("deniedActions").EnumerateArray().Select(e => e.GetString()).Should().Contain("command");
        TurnsLaunched.Should().Be(2, "the denied launch and its one follow-up");
    }

    [Fact]
    public async Task ACanaryTheConsultantChanged_IsATreeChange_NotAnAnswer()
    {
        CodexAnswers("marker: {{cwd-file:CHECK.md}}\ncanary: CANNOT");
        Environment.SetEnvironmentVariable("FAKECLI_SIDE_EFFECT", "{{path-in-prompt:canary.txt}}");

        var (_, said, _) = await Check(Settings(), "--caller", "claude");

        Text(said, "state").Should().Be("failed", said.ToString());
        Text(said, "failureKind").Should().Be("tree-changed", "a write outside the checkout breaks the promise a consultant runs under");
    }

    [Fact]
    public async Task ThePrompt_NamesTheCanaryByItsPath_ButCarriesNeitherWord()
    {
        CodexAnswers("marker: {{cwd-file:CHECK.md}}\ncanary: CANNOT");
        var seen = new Seen(new ProcessLauncher());

        await Check(Settings(), seen, TestContext.Current.CancellationToken, "--caller", "claude");

        seen.Prompt.Should().Contain("canary.txt", "the canary is named, or the test is vacuous");
        seen.Marker.Should().NotBeEmpty();
        seen.Prompt.Should().NotContain(seen.Marker, "the marker must be READ from CHECK.md, never handed over");
        seen.Prompt.Should().NotContain(seen.Canary, "the canary's word must come from reading it, never from the prompt");
    }

    [Fact]
    public async Task ALockFileNobodyCanOpen_Is74_NotAlreadyChecking()
    {
        CodexAnswers("marker: x");
        Directory.CreateDirectory(new ConsultCheckStore(_data).LockPath("claude"));

        var (code, _, err) = await Check(Settings(), "--caller", "claude");

        code.Should().Be(74, err);
        TurnsLaunched.Should().Be(0);
    }

    [Fact]
    public async Task AFreshCheckFromTheOtherSide_IsAlreadyChecking_AndNothingLaunches()
    {
        CodexAnswers("marker: x");
        var otherSide = ConsultHealth.Side() == "wsl" ? "windows" : "wsl";
        new ConsultCheckStore(_data).Write(new ConsultCheckRecord
        {
            CallerKind = "claude",
            State = ConsultCheckStates.Checking,
            Side = otherSide,
            HeartbeatUtc = ConsultationStore.Stamp(DateTime.UtcNow),
        });

        var (code, said, _) = await Check(Settings(), "--caller", "claude");

        code.Should().Be(0);
        Text(said, "state").Should().Be("already-checking", "one data directory shared by both sides cannot be locked across the seam");
        TurnsLaunched.Should().Be(0);
    }

    [Fact]
    public async Task ACheckCancelledWhileItsScratchIsMade_IsCancelled_NotUnavailable()
    {
        CodexAnswers("marker: x");
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var (code, said, _) = await Check(Settings(), new ProcessLauncher(), cancelled.Token, "--caller", "claude");

        code.Should().Be(0);
        Text(said, "state").Should().Be("failed", said.ToString());
        Text(said, "failureKind").Should().Be("cancelled");
        Directory.EnumerateDirectories(_temp, "coai-check-*").Should().BeEmpty();
    }

    [Fact]
    public async Task AConsultantWithNoRuntimeAdapter_IsUnavailable_NeverACrash()
    {
        var parts = new ConsultantParts(Settings(), new ProcessLauncher(), _warned.Add, Noticing.None);
        var team = new ProviderSettings("team") { Runtime = "remote", BaseUrl = "https://example.invalid" };
        var scratch = new CheckScratch(_temp, _temp, Path.Combine(_temp, "canary.txt"), "wren-1", "heron-1");

        var ended = await parts.Turn().RunAsync(
            new ConsultCheckRecord { CallerKind = "claude", State = ConsultCheckStates.Checking }, team, scratch, TimeSpan.FromSeconds(5),
            () => new Runners.Consultation.ConsultFailure.Timeout(), CancellationToken.None, CancellationToken.None);

        ended.State.Should().Be(ConsultCheckStates.Unavailable);
        ended.Reason.Should().NotBeEmpty();
    }

    /// <summary>The real launcher, recording the consultant's prompt and the two words its scratch held at that moment.</summary>
    private sealed class Seen(IProcessLauncher inner) : IProcessLauncher
    {
        public string Prompt { get; private set; } = string.Empty;

        public string Marker { get; private set; } = string.Empty;

        public string Canary { get; private set; } = string.Empty;

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            if (string.Equals(request.Executable, FakeCliExe, StringComparison.OrdinalIgnoreCase))
            {
                Prompt = request.StdIn ?? string.Empty;
                Marker = File.ReadAllText(Path.Combine(request.WorkingDirectory, "CHECK.md")).Trim().Split(' ')[^1].TrimEnd('.');
                Canary = File.ReadAllText(Path.Combine(request.WorkingDirectory, "..", "outside", "canary.txt")).Trim();
            }

            return inner.RunAsync(request, ct);
        }
    }

    private static string AgyFixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "antigravity", name));

    private static string HelpFixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "claude", name));
}
