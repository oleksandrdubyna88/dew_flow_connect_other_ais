using System.Diagnostics;
using System.Text.Json;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// An antigravity consultation whose shell command was auto-denied is continued ONCE in the same
/// conversation, told the command will not come — and the advice that answer carries reaches the caller.
/// </summary>
/// <remarks>
/// <para>The defect, as the operator met it on 2026-10-02: two real consultations on
/// <c>antigravity · gemini-3.1-pro-high</c> came back empty, and the caller was told to "try once more
/// with a sharper problem statement". Headless agy cannot ask a person whether <c>run_command</c> may run,
/// so it auto-denies the command and ends the turn itself; the reviewer path had been cured of exactly this
/// by issue #504, and the consultation path never reached that cure. Observed RED before the cure, on the
/// first test here: <c>the consultant (antigravity) exited cleanly but answered nothing; its transcript is
/// kept at … — try once more with a sharper problem statement</c>.</para>
/// <para>The DENIED stream is real: <c>fixtures/antigravity/consult-denied.ndjson</c> (+ its stderr) is the
/// 2026-10-02 consultation that came back empty, its paths redacted and its conversation id replaced by a
/// synthetic one. <c>consult-advice.ndjson</c> is a real follow-up that answered, from the WSL side of the
/// E1.1 measurement (research/RESULTS_agy_consult_follow_up.md) — a different conversation, RE-KEYED to the
/// same synthetic id so the two read as one thread. The fake CLI tells the follow-up apart by
/// <see cref="AntigravityFollowUps.StaysDenied"/> on its stdin — the phrase the shipped follow-up carries
/// and the turn's own prompt must not (its own test,
/// <c>AntigravityStreamContinueTests.TheToolbox_NamesOnlyTheObservedTool_AndNeverTheFollowUpMarker</c>).</para>
/// <para>What these do NOT prove: the wording of a failed turn. Classifying a failure (<c>command-denied</c>
/// and the rest) is epic 2 of PLAN_the_consultant_works_on_every_vendor.md; here a twice-denied turn still
/// ends in today's sentence, and only the NUMBER of launches is asserted about it.</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class ConsultAntigravityDenialScenarioTests : ConsultScenarioBase
{
    private const string Conversation = "5e1f2a3b-4c5d-4e6f-8a9b-0c1d2e3f4a5b";

    /// <summary>What every follow-up says and the first prompt never does — the fake CLI's way to know which launch it is.</summary>
    private const string FollowUpMarker = AntigravityFollowUps.StaysDenied;

    /// <summary>The denied stream's own usage (its `result` event) and the answered follow-up's.</summary>
    private const long DeniedIn = 36652, DeniedOut = 874, AdviceIn = 34218, AdviceOut = 3494;

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "antigravity", name));

    private static readonly IReadOnlyDictionary<string, ConsultantChoice> OnAntigravity = new Dictionary<string, ConsultantChoice>
    {
        [CallerIdentity.Claude] = new("antigravity", "gemini-3.1-pro-high", Runtime: "antigravity", ExecutablePath: FakeCliExe),
    };

    private PanelService Agy(TimeSpan? reviewerTimeout = null, int callsPerSession = 10) =>
        Service(providers: [], consultants: OnAntigravity, reviewerTimeout: reviewerTimeout, callsPerSession: callsPerSession);

    /// <summary>The consultation record exactly as it sits on disk — read raw, so a field the type lacks is still visible.</summary>
    private JsonElement RecordFile() =>
        JsonDocument.Parse(File.ReadAllText(Directory.EnumerateFiles(Path.Combine(_data, "consultations"), "*.json").Single())).RootElement;

    private static string Field(JsonElement record, string name) =>
        record.TryGetProperty(name, out var value) ? value.GetString() ?? string.Empty : string.Empty;

    /// <summary>One agy stream: the conversation named, a result with no answer and the usage given.</summary>
    private static string Said(long tokensIn, long tokensOut, string response = "") =>
        """
        {"event":"init","conversation_id":"CONV"}
        {"event":"result","result":{"conversation_id":"CONV","status":"SUCCESS","response":"ANSWER","usage":{"input_tokens":IN,"output_tokens":OUT}}}

        """.Replace("CONV", Conversation, StringComparison.Ordinal)
            .Replace("ANSWER", response, StringComparison.Ordinal)
            .Replace("IN", tokensIn.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("OUT", tokensOut.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);

    private async Task<long> BilledIn() => (await LedgerRows()).Sum(row => row.GetProperty("tokensIn").GetInt64());

    /// <summary>
    /// The first launch prints <paramref name="firstStdout"/> (the real denied stream unless told otherwise);
    /// a follow-up, when one comes, prints <paramref name="followUpStdout"/>. Undone on dispose.
    /// </summary>
    private Steering Steer(string followUpStdout, string? firstStdout = null, string? firstStderr = null, string followUpStderr = "agy: continuing the conversation")
    {
        var recorded = Directory.CreateTempSubdirectory("coai-consult-agy-argv-").FullName;
        Environment.SetEnvironmentVariable("FAKECLI_STDOUT", firstStdout ?? Fixture("consult-denied.ndjson"));
        Environment.SetEnvironmentVariable("FAKECLI_STDERR", firstStderr ?? Fixture("consult-denied.stderr.txt").Trim());
        Environment.SetEnvironmentVariable("FAKECLI_OUTFILE_TEXT", null);
        Environment.SetEnvironmentVariable("FAKECLI_REPAIR_MARKER", FollowUpMarker);
        Environment.SetEnvironmentVariable("FAKECLI_TURN1_REPAIR_STDOUT", followUpStdout);
        // Non-empty on purpose: the fake CLI falls back to the bare FAKECLI_STDERR otherwise, and an
        // answering follow-up must not ALSO print the denial sentence.
        Environment.SetEnvironmentVariable("FAKECLI_TURN1_REPAIR_STDERR", followUpStderr);
        Environment.SetEnvironmentVariable("FAKECLI_RECORD_DIR", recorded);

        return new Steering(recorded);
    }

    private sealed record Steering(string Recorded) : IDisposable
    {
        private static readonly string[] Names =
        [
            "FAKECLI_REPAIR_MARKER", "FAKECLI_TURN1_REPAIR_STDOUT", "FAKECLI_TURN1_REPAIR_STDERR",
            "FAKECLI_TURN1_REPAIR_SLEEP_MS", "FAKECLI_RECORD_DIR", "FAKECLI_SIDE_EFFECT",
        ];

        /// <summary>Every launch's argv, its stdin as the last field (the fake CLI's recorder format).</summary>
        public IReadOnlyList<string[]> Launches() =>
            [.. Directory.EnumerateFiles(Recorded, "*.argv").Select(path => LaunchRecords.Read(path).Split('\0'))];

        /// <summary>
        /// The follow-up — told apart by what rode stdin, never by file times: it is the launch that was
        /// handed the follow-up's text.
        /// </summary>
        public string[] FollowUp() => Launches().Single(argv => argv[^1].Contains(FollowUpMarker, StringComparison.Ordinal));

        /// <summary>What the follow-up told the model — its stdin line DECODED, as the CLI reads it.</summary>
        public string FollowUpText() =>
            JsonDocument.Parse(FollowUp()[^1]).RootElement.GetProperty("message").GetProperty("content").GetString()!;

        public void Dispose()
        {
            foreach (var name in Names)
            {
                Environment.SetEnvironmentVariable(name, null);
            }

            try
            {
                Directory.Delete(Recorded, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    private async Task<IReadOnlyList<JsonElement>> LedgerRows() =>
        [.. (await File.ReadAllLinesAsync(Path.Combine(_data, "usage.jsonl"), TestContext.Current.CancellationToken))
            .Select(line => JsonDocument.Parse(line).RootElement)];

    [Fact]
    public async Task ADeniedCommand_IsContinuedInTheSameConversation_AndTheAdviceArrives()
    {
        using var calling = CallingAs("CLAUDE_CODE_SESSION_ID");
        using var steering = Steer(Fixture("consult-advice.ndjson"));

        var reply = await Consult(Agy(), "where is the sweep invoked?");

        reply.TryGetProperty("error", out var error).Should().BeFalse(error.ToString());
        Advise(reply).Should().Contain("git grep -n QUOKKA", "the follow-up's answer is the advice the caller gets");
        steering.Launches().Should().HaveCount(2, "one denied launch, one follow-up — never a third");
        var second = steering.FollowUp();
        second.Count(arg => arg == "--conversation").Should().Be(1);
        second.Should().ContainInConsecutiveOrder("--conversation", Conversation);
        steering.FollowUpText().Should().Contain("run_command was denied", "a denied COMMAND gets the shell's text");
        var firstPrompt = steering.Launches().Single(argv => !argv[^1].Contains(FollowUpMarker, StringComparison.Ordinal))[^1];
        firstPrompt.Should().Contain("Your only tool for reading this checkout is view_file", "the toolbox reaches the real prompt");
    }

    [Fact]
    public async Task ATwoLaunchTurn_IsRecordedAsFollowedUp_AndBilledOnceByTheLargerReport()
    {
        using var calling = CallingAs("CLAUDE_CODE_SESSION_ID");
        using var steering = Steer(Fixture("consult-advice.ndjson"));

        var reply = await Consult(Agy(), "where is the sweep invoked?");

        var record = new ConsultationStore(_data).Read(reply.GetProperty("consultationId").GetString()!)!;
        var turn = record.Turns.Should().ContainSingle().Subject;
        turn.FollowedUp.Should().BeTrue();
        record.Handle.Should().Be(Conversation);
        // agy reports the conversation's running total, so the turn is the field-wise MAXIMUM of the two
        // reports — never their sum, which would bill the denied launch twice.
        (turn.TokensIn, turn.TokensOut).Should().Be((Math.Max(DeniedIn, AdviceIn), Math.Max(DeniedOut, AdviceOut)));
        var row = (await LedgerRows()).Should().ContainSingle("one turn is one ledger row, however many launches it took").Subject;
        row.GetProperty("tokensIn").GetInt64().Should().Be(Math.Max(DeniedIn, AdviceIn));
        row.GetProperty("outcome").GetString().Should().Be("ok");
    }

    [Fact]
    public async Task AFailedLaterTurn_IsBilledItsOwnShare_NotTheConversationsRunningTotal()
    {
        // ThisTurnsShare used to be applied on success only, so a FAILED turn of a cumulative vendor
        // wrote the whole conversation's total into the ledger — turn 1 billed again, on the turn that
        // produced nothing. Every path that records usage now takes the share.
        using var calling = CallingAs("CLAUDE_CODE_SESSION_ID");
        using var steering = Steer(Fixture("consult-advice.ndjson"), firstStdout: Fixture("consult-advice.ndjson"), firstStderr: "agy: ok");
        var service = Agy();
        var id = (await Consult(service, "where is the sweep invoked?")).GetProperty("consultationId").GetString()!;
        Environment.SetEnvironmentVariable("FAKECLI_STDOUT",
            """
            {"event":"init","conversation_id":"CONV"}
            {"event":"result","result":{"conversation_id":"CONV","status":"SUCCESS","response":"","usage":{"input_tokens":50000,"output_tokens":4000}}}

            """.Replace("CONV", Conversation, StringComparison.Ordinal));

        Refusal(await Consult(service, "I looked; it is not in BackupRunner", id)).Should().Contain("answered nothing");

        var rows = await LedgerRows();
        rows.Should().HaveCount(2);
        rows[1].GetProperty("outcome").GetString().Should().Be("empty");
        rows[1].GetProperty("tokensIn").GetInt64().Should().Be(50000 - AdviceIn, "turn 1's tokens were already recorded");
        rows[1].GetProperty("tokensOut").GetInt64().Should().Be(4000 - AdviceOut);
    }

    [Fact]
    public async Task AFollowUpThatIsDeniedAgain_EndsTheTurn_AfterExactlyTwoLaunches()
    {
        using var calling = CallingAs("CLAUDE_CODE_SESSION_ID");
        using var steering = Steer(Fixture("consult-denied.ndjson"), followUpStderr: Fixture("consult-denied.stderr.txt").Trim());

        var service = Agy(callsPerSession: 1);

        var reply = await Consult(service, "where is the sweep invoked?");

        var refused = Refusal(reply);
        refused.Should().Contain("command-denied", "the failure is CLASSIFIED, by the permission agy denied");
        refused.Should().Contain("a NEW consult whose problem names the files that hold the answer").And.NotContain("do not retry",
            "the cure invites a reframed ask, so the reply must not also forbid one (the whole-branch review, M)");
        refused.Should().Contain("was not counted", "a vendor's denial gives the caller's call back");
        refused.Should().NotContain("sharper problem statement", "the problem statement was never the problem");
        steering.Launches().Should().HaveCount(2, "never a third launch");
        var record = new ConsultationStore(_data).All().Single();
        record.Status.Should().Be(ConsultationStatuses.Failed);
        record.Handle.Should().Be(Conversation, "the conversation is kept even when the turn failed");
        Field(RecordFile(), "failureKind").Should().Be("command-denied");
        Field(RecordFile(), "failureCure").Should().NotBeEmpty();
        (await LedgerRows()).Should().ContainSingle().Which.GetProperty("outcome").GetString().Should().Be("command-denied");
        (await LedgerRows()).Single().GetProperty("tokensIn").GetInt64()
            .Should().Be(DeniedIn, "billed once: both reports are the same running total");

        // The cap was one call and it was given back: a second consultation may run.
        Environment.SetEnvironmentVariable("FAKECLI_STDOUT", Fixture("consult-advice.ndjson"));
        Environment.SetEnvironmentVariable("FAKECLI_STDERR", "agy: ok");
        var again = await Consult(service, "where is the sweep invoked? (asking once more)");
        again.TryGetProperty("error", out var error).Should().BeFalse(error.ToString());
    }

    [Fact]
    public async Task TheAgyQuotaStream_IsQuota_WithItsCure_AndGivesTheCallBack()
    {
        // The P0 stream of 2026-10-02 (research/RESULTS_agy_allow_rule.md): `AGY_ERROR … RESOURCE_EXHAUSTED
        // (code 429): Individual quota reached … Resets in 33m52s`, exit 3. The fixture is that line with
        // its elisions filled in — the words the classification reads are the recorded ones.
        using var calling = CallingAs("CLAUDE_CODE_SESSION_ID");
        using var steering = Steer(Fixture("consult-advice.ndjson"), firstStdout: string.Empty, firstStderr: Fixture("consult-quota.stderr.txt").Trim());
        Environment.SetEnvironmentVariable("FAKECLI_EXIT", "3");
        var service = Agy(callsPerSession: 1);

        var refused = Refusal(await Consult(service, "where is the sweep invoked?"));

        refused.Should().Contain("quota").And.Contain("(failure: quota)");
        refused.Should().Contain("do not retry this consultation", "a spent quota is not cured by asking again");
        refused.Should().Contain("was not counted");
        Field(RecordFile(), "failureKind").Should().Be("quota");
    }

    /// <summary>The service over the fake CLI as agy, with git refused after the consultant ran while <paramref name="launcher"/> says so.</summary>
    private PanelService Agy(TurnLauncher launcher) =>
        Service(providers: [], consultants: OnAntigravity, launcher: launcher);

    [Fact]
    public async Task AnInterruptedTurn_ThenResumed_IsBilledOnceInTotal()
    {
        // agy reports the conversation's RUNNING total. A turn the vendor answered and billed, whose answer
        // was then lost on our side (git refused the snapshot after it), keeps the conversation (interrupted)
        // and adds no turn — so subtracting only the answered turns billed it again inside the next one. The
        // record now keeps what the conversation was billed.
        using var calling = CallingAs("CLAUDE_CODE_SESSION_ID");
        using var steering = Steer(Fixture("consult-advice.ndjson"), firstStdout: Said(20000, 1000, "an answer that will be lost"), firstStderr: "agy: ok");
        var launcher = new TurnLauncher(_launcher) { GitRefused = true };
        var service = Agy(launcher);
        var interrupted = await Consult(service, "where is the sweep invoked?");
        Refusal(interrupted).Should().Contain("NOT counted");
        var id = new ConsultationStore(_data).All().Single().Id;

        launcher.GitRefused = false;
        Environment.SetEnvironmentVariable("FAKECLI_STDOUT", Fixture("consult-advice.ndjson"));
        var answered = await Consult(service, "I looked again", id);

        answered.TryGetProperty("error", out var error).Should().BeFalse(error.ToString());
        (await BilledIn()).Should().Be(AdviceIn, "the conversation's last running total, billed once across both turns");
        Field(RecordFile(), "failureKind").Should().BeEmpty("an answered turn clears the failure it followed");
    }

    [Fact]
    public async Task AnAnsweredTurn_AFailedResumableTurn_AndAnAnswer_AreBilledOnceInTotal()
    {
        using var calling = CallingAs("CLAUDE_CODE_SESSION_ID");
        using var steering = Steer(Fixture("consult-advice.ndjson"), firstStdout: Said(10000, 500, "turn one's advice"), firstStderr: "agy: ok");
        var launcher = new TurnLauncher(_launcher);
        var service = Agy(launcher);
        var id = (await Consult(service, "where is the sweep invoked?")).GetProperty("consultationId").GetString()!;

        Environment.SetEnvironmentVariable("FAKECLI_STDOUT", Said(20000, 1000, "an answer that will be lost"));
        // Refused from the moment the consultant is launched — the turn's own snapshot BEFORE it must still run.
        launcher.BeforeConsultant = () => launcher.GitRefused = true;
        Refusal(await Consult(service, "and the lock?", id)).Should().Contain("NOT counted");

        launcher.BeforeConsultant = null;
        launcher.GitRefused = false;
        Environment.SetEnvironmentVariable("FAKECLI_STDOUT", Fixture("consult-advice.ndjson"));
        var answered = await Consult(service, "and the lock, once more", id);

        answered.TryGetProperty("error", out var error).Should().BeFalse(error.ToString());
        (await BilledIn()).Should().Be(AdviceIn, "three reports of one running total, billed once between them");
    }

    [Fact]
    public async Task ADeniedRead_IsContinuedWithTheReadWording_NamingThatPermission()
    {
        using var calling = CallingAs("CLAUDE_CODE_SESSION_ID");
        var deniedRead = Fixture("consult-denied.ndjson")
            .Replace("\"action\":\"command\",\"display_name\":\"RunCommand\"", "\"action\":\"read_file\",\"display_name\":\"ReadFile\"", StringComparison.Ordinal);
        deniedRead.Should().Contain("read_file", "the fixture edit must have landed, or this tests the command path");
        using var steering = Steer(Fixture("consult-advice.ndjson"), firstStdout: deniedRead, firstStderr: "agy: one tool was not allowed");

        var reply = await Consult(Agy(), "what does the file outside the checkout say?");

        reply.TryGetProperty("error", out var error).Should().BeFalse(error.ToString());
        var said = steering.FollowUpText();
        said.Should().Contain("'read_file' permission");
        said.Should().NotContain("Shell commands", "a refused read is not a refused shell");
    }

    [Fact]
    public async Task AFirstLaunchThatChangedTheTree_IsABreach_AndNoFollowUpRuns()
    {
        using var calling = CallingAs("CLAUDE_CODE_SESSION_ID");
        using var steering = Steer(Fixture("consult-advice.ndjson"));
        var written = Path.Combine(_repo, "written-by-the-denied-launch.txt");
        Environment.SetEnvironmentVariable("FAKECLI_SIDE_EFFECT", written);

        var reply = await Consult(Agy(), "where is the sweep invoked?");

        Refusal(reply).Should().Contain("written-by-the-denied-launch.txt");
        steering.Launches().Should().ContainSingle("a consultant that broke the read-only promise is not given a second launch");
        var record = new ConsultationStore(_data).All().Single();
        record.Status.Should().Be(ConsultationStatuses.Failed);
        record.Alert.Should().Contain("written-by-the-denied-launch.txt");
        File.Exists(written).Should().BeTrue("the invariant never deletes — the file may be the person's");
    }

    [Fact]
    public async Task AHungFollowUp_IsKilledByTheLauncher_InsideTheRemainingBudget_AndTheConversationIsKept()
    {
        using var calling = CallingAs("CLAUDE_CODE_SESSION_ID");
        using var steering = Steer(Fixture("consult-advice.ndjson"));
        // The first launch spends most of the budget, so what the follow-up may have is SMALL — and only the
        // cap can make the turn end near the budget. Without it the follow-up would get the whole 8 s again,
        // run past the turn's own deadline (8 s + its 5 s grace) and be stopped by that instead.
        Environment.SetEnvironmentVariable("FAKECLI_SLEEP_MS", "6000");
        Environment.SetEnvironmentVariable("FAKECLI_TURN1_REPAIR_SLEEP_MS", "120000");
        var budget = TimeSpan.FromSeconds(8);
        var clock = Stopwatch.StartNew();

        var reply = await Consult(Agy(budget), "where is the sweep invoked?");

        // The budget plus a margin for what the turn does around its launches (two snapshots, the prompt's
        // diff, two process starts, the kill) — well inside both the budget plus the launcher's 5 s drain
        // grace and the turn's deadline, which with this budget are the same 13 s and so cannot tell a capped
        // follow-up from an uncapped one.
        clock.Elapsed.Should().BeLessThan(budget + TimeSpan.FromSeconds(3), "the follow-up was capped to what the first launch left");
        clock.Elapsed.Should().BeLessThan(ConsultationDeadline.For(budget), "the launcher's own kill ends it, not the turn's backstop");
        var refused = Refusal(reply);
        refused.Should().Contain("did not arrive").And.Contain("NOT counted");
        refused.Should().NotContain("deadline", "a hung follow-up is a timed-out LAUNCH, resumable — not the turn's deadline");
        steering.Launches().Should().HaveCount(2);
        var record = new ConsultationStore(_data).All().Single();
        record.Status.Should().Be(ConsultationStatuses.Interrupted);
        record.Handle.Should().Be(Conversation, "the killed follow-up named no id; the first launch's survives");
        (await LedgerRows()).Should().ContainSingle().Which.GetProperty("tokensIn").GetInt64()
            .Should().Be(DeniedIn, "a killed second launch reports nothing, and that must not erase the first's tokens");
    }
}
