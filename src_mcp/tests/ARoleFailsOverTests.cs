using CoaiMcp.Core.Notices;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E1 s3 t3 — scenarios A7 to A11 and A13 of <c>v2/epoch-3/e1-role-jobs-ba/SPEC.md</c>: a role job moves down its route when a model stops, and ends where the spec says it ends. The real runner, the real launcher, the real
/// classifier, notices, ledger and tree check; FakeCli answers each CLI by model from ONE file (<c>FAKECLI_MODEL_MAP</c>), and every answer is a recorded shape (<see cref="FakeRoleAnswers"/>).
/// </summary>
/// <remarks>In the serial <c>fakecli-env</c> collection: the fake is steered by environment variables of this process.</remarks>
[Collection("fakecli-env")]
public sealed class ARoleFailsOverTests
{
    private static readonly string[] ClaudeTwoThenCodex = ["claude/fable", "claude/opus", "codex/astra"];

    // ---- A7: a declared refusal ----

    [Fact]
    public async Task A7_ADeclaredRefusal_SendsTheJobToTheSecondModel_FromTheStart_InANewSession()
    {
        await using var rig = await RoleFailoverRig.CreateAsync();
        rig.Says("fable", FakeRoleAnswers.ClaudeSays(FakeRoleAnswers.Refusal)).Says("opus", FakeRoleAnswers.ClaudeSays(FakeRoleAnswers.Ok));
        var job = rig.NewJob(["claude/fable", "claude/opus"]);

        var (exit, after) = await rig.RunAsync(job);

        exit.Should().Be(RoleRunner.Done);
        after.State.Should().Be(RoleJobStates.Succeeded);
        after.Model.Should().Be("claude/opus");
        after.Attempts.Select(attempt => (attempt.N, attempt.Model, attempt.Vendor, attempt.Stop)).Should().Equal((1, "fable", "claude", "refusal"), (2, "opus", "claude", string.Empty));
        after.Attempts[0].Reason.Should().Contain("I will not write this plan");
        rig.Models().Should().Equal("fable", "opus");
        rig.Launches().Should().OnlyContain(argv => !argv.Contains("--resume"), "the second model starts from turn 0 in a new session, never in the first one's");
        rig.World.Store.FolderOf(job.JobId)!.ReadResult().Should().Contain("\"status\":\"ok\"");
    }

    // ---- A8: the recorded Claude 429 envelope ----

    [Fact]
    public async Task A8_TheRecordedClaude429Envelope_GoesToTheNextVendorAtOnce_AndTheSameVendorIsSkipped()
    {
        await using var rig = await RoleFailoverRig.CreateAsync();
        rig.Says("fable", FakeRoleAnswers.ClaudeSpendLimit()).Says("opus", FakeRoleAnswers.ClaudeSays(FakeRoleAnswers.Ok)).Says("astra", FakeRoleAnswers.CodexSays(FakeRoleAnswers.Ok));
        var job = rig.NewJob(ClaudeTwoThenCodex);
        var clock = System.Diagnostics.Stopwatch.StartNew();

        // A ladder of five minutes: a wait of any kind would be seen as time.
        var (_, after) = await rig.RunAsync(job, ladder: [TimeSpan.FromMinutes(5)]);

        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(60), "no wait: the next model starts at once");
        after.State.Should().Be(RoleJobStates.Succeeded);
        after.Attempts.Select(attempt => (attempt.Model, attempt.Vendor, attempt.Stop)).Should().Equal(("fable", "claude", "limit"), ("astra", "codex", string.Empty));
        after.Attempts[0].Reason.Should().Contain("monthly spend limit");
        rig.Models().Should().Equal("fable", "astra");
    }

    [Fact]
    public async Task A8_ALostSignIn_AlsoSkipsTheVendorsOtherModels()
    {
        await using var rig = await RoleFailoverRig.CreateAsync();
        rig.Says("fable", FakeRoleAnswers.ClaudeNoSignIn()).Says("opus", FakeRoleAnswers.ClaudeSays(FakeRoleAnswers.Ok)).Says("astra", FakeRoleAnswers.CodexSays(FakeRoleAnswers.Ok));
        var job = rig.NewJob(ClaudeTwoThenCodex);

        var (_, after) = await rig.RunAsync(job);

        after.State.Should().Be(RoleJobStates.Succeeded);
        after.Attempts.Select(attempt => (attempt.Model, attempt.Stop)).Should().Equal(("fable", "crash"), ("astra", string.Empty));
        after.Attempts[0].Reason.Should().Contain("the sign-in is lost");
        rig.Models().Should().Equal("fable", "astra");
    }

    [Fact]
    public async Task A8_AMissingCli_AlsoSkipsTheVendorsOtherModels_AndLeavesNoLedgerLine()
    {
        await using var rig = await RoleFailoverRig.CreateAsync();
        rig.Setting("COAI_ROLE_CLAUDE_CLI", Path.Combine(rig.Repo, "no-such-claude-cli"));
        rig.Says("astra", FakeRoleAnswers.CodexSays(FakeRoleAnswers.Ok));
        var job = rig.NewJob(ClaudeTwoThenCodex);

        var (_, after) = await rig.RunAsync(job);

        after.State.Should().Be(RoleJobStates.Succeeded);
        after.Attempts.Select(attempt => (attempt.Model, attempt.Vendor, attempt.Stop)).Should().Equal(("fable", "claude", "crash"), ("astra", "codex", string.Empty));
        rig.Models().Should().Equal("astra");
        rig.LedgerLines().Should().ContainSingle("a CLI that was never started is no launch").Which.GetProperty("model").GetString().Should().Be("astra");
    }

    // ---- A9: a crash, a timeout, an empty answer ----

    public static TheoryData<string, string> Crashes => new()
    {
        { "exit", "the CLI fell over" },
        { "timeout", "did not answer within its time" },
        { "empty", "answered nothing" },
    };

    [Theory]
    [MemberData(nameof(Crashes))]
    public async Task A9_AnExit_ATimeout_OrAnEmptyAnswer_GoesToTheNextModel(string how, string said)
    {
        await using var rig = await RoleFailoverRig.CreateAsync();
        rig.Says("fable", how switch { "exit" => FakeRoleAnswers.Crash(), "timeout" => FakeRoleAnswers.Slow(30_000), _ => FakeRoleAnswers.Silent() })
            .Says("opus", FakeRoleAnswers.ClaudeSays(FakeRoleAnswers.Ok));
        var job = rig.NewJob(["claude/fable", "claude/opus"]);

        var (_, after) = await rig.RunAsync(job, attemptTimeout: TimeSpan.FromSeconds(2));

        after.State.Should().Be(RoleJobStates.Succeeded);
        after.Attempts[0].Stop.Should().Be("crash");
        after.Attempts[0].Reason.Should().Contain(said);
        after.Attempts[1].Stop.Should().BeEmpty();
        rig.Models().Should().Equal("fable", "opus");
    }

    // ---- A10: the list is exhausted ----

    [Fact]
    public async Task A10_WhenTheListIsExhausted_TheJobFails_NamingEveryAttempt_WithOneNotice()
    {
        await using var rig = await RoleFailoverRig.CreateAsync();
        rig.Says("fable", FakeRoleAnswers.Crash("fable fell over")).Says("opus", FakeRoleAnswers.Crash("opus fell over")).Says("astra", FakeRoleAnswers.Crash("astra fell over"));
        var job = rig.NewJob(ClaudeTwoThenCodex);

        var (exit, after) = await rig.RunAsync(job);

        exit.Should().Be(RoleRunner.Done);
        (after.State, after.Stop).Should().Be((RoleJobStates.Failed, "models-exhausted"));
        after.Reason.Should().Contain("claude/fable").And.Contain("claude/opus").And.Contain("codex/astra").And.Contain("fable fell over").And.Contain("astra fell over");
        after.Attempts.Should().HaveCount(3).And.OnlyContain(attempt => attempt.Stop == "crash");
        File.Exists(rig.World.Store.FolderOf(job.JobId)!.ResultFile).Should().BeFalse();
        var failed = rig.NoticesOf(ServerNoticeCodes.RoleJobFailed).Should().ContainSingle().Subject;
        failed.Subject.Should().Be(job.JobId);
    }

    [Fact]
    public async Task A10_OnlyTheLastModelWaits_ForAThrottle_WithTheLadder_AndGivesUpWhenTheLadderIsSpent()
    {
        await using var rig = await RoleFailoverRig.CreateAsync();
        rig.Says("fable", FakeRoleAnswers.Throttled()).Says("opus", FakeRoleAnswers.Throttled());
        var job = rig.NewJob(["claude/fable", "claude/opus"]);

        var (_, after) = await rig.RunAsync(job, ladder: [TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50)]);

        // fable (not last): one launch and on. opus (last): its first launch and one launch after each wait of the ladder.
        rig.Models().Should().Equal("fable", "opus", "opus", "opus");
        after.State.Should().Be(RoleJobStates.Failed);
        after.Attempts.Select(attempt => attempt.Stop).Should().OnlyContain(stop => stop == "limit");
    }

    [Fact]
    public async Task A10_TheLastModelThatClearsAfterAWait_AnswersTheJob()
    {
        await using var rig = await RoleFailoverRig.CreateAsync();
        rig.Says("fable", FakeRoleAnswers.Throttled(), FakeRoleAnswers.ClaudeSays(FakeRoleAnswers.Ok));
        var job = rig.NewJob(["claude/fable"]);

        var (_, after) = await rig.RunAsync(job, ladder: [TimeSpan.FromMilliseconds(50)]);

        after.State.Should().Be(RoleJobStates.Succeeded);
        after.Attempts.Select(attempt => attempt.Stop).Should().Equal("limit", string.Empty);
        rig.SwitchLines().Should().BeEmpty("a wait on the last model is no switch");
    }

    [Fact]
    public async Task A10_AWaitThatTheJobsBudgetCannotHold_IsNotTaken()
    {
        await using var rig = await RoleFailoverRig.CreateAsync();
        rig.Says("fable", FakeRoleAnswers.Throttled());
        var job = rig.NewJob(["claude/fable"], timeoutMinutes: 1);

        var (_, after) = await rig.RunAsync(job, ladder: [TimeSpan.FromMinutes(2)]);

        rig.Models().Should().Equal("fable");
        (after.State, after.Stop).Should().Be((RoleJobStates.Failed, "models-exhausted"));
    }

    // ---- A11: two switches ----

    [Fact]
    public async Task A11_TwoSwitches_AreTwoLinesAndTwoNotices_EachNamingTheVendorTheWorkWentTo()
    {
        await using var rig = await RoleFailoverRig.CreateAsync();
        rig.Says("fable", FakeRoleAnswers.Crash("fable fell over")).Says("opus", FakeRoleAnswers.Crash("opus fell over")).Says("astra", FakeRoleAnswers.CodexSays(FakeRoleAnswers.Ok));
        var job = rig.NewJob(ClaudeTwoThenCodex);

        var (_, after) = await rig.RunAsync(job);

        after.State.Should().Be(RoleJobStates.Succeeded);
        rig.SwitchLines().Should().HaveCount(2);
        rig.SwitchLines()[0].Should().StartWith($"role job {job.JobId} (failover-probe) switched claude/fable -> claude/opus after crash: ").And.Contain("fable fell over");
        rig.SwitchLines()[1].Should().StartWith($"role job {job.JobId} (failover-probe) switched claude/opus -> codex/astra after crash: ").And.Contain("opus fell over");
        var notices = rig.NoticesOf(ServerNoticeCodes.RoleModelSwitched);
        notices.Select(notice => notice.Subject).Should().Equal($"{job.JobId}/1", $"{job.JobId}/2");
        notices.Select(notice => notice.Provider).Should().Equal("claude", "codex");
        notices[1].Detail.Should().Contain("codex").And.Contain("another vendor than claude");
        notices[0].Title.Should().Contain("claude/fable").And.Contain("claude/opus");
        rig.NoticesOf(ServerNoticeCodes.RoleJobFailed).Should().BeEmpty();
    }

    // ---- A13: the three stops that do NOT fail over ----

    [Fact]
    public async Task A13_ACancel_EndsTheJob_AndNoSecondModelIsLaunched()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the cancel goes through the process tree of a POSIX child");
        await using var rig = await RoleFailoverRig.CreateAsync();
        rig.Says("fable", FakeRoleAnswers.Slow(60_000)).Says("opus", FakeRoleAnswers.ClaudeSays(FakeRoleAnswers.Ok));
        var job = rig.NewJob(["claude/fable", "claude/opus"]);
        var running = Task.Run(() => rig.RunAsync(job), TestContext.Current.CancellationToken);
        RoleJobWorld.WaitFor(() => rig.World.Store.Read(job.JobId) is { Child: not null }, "the first model's CLI to start and be recorded");

        rig.World.Store.FolderOf(job.JobId)!.RequestCancel();
        var (exit, after) = await running;

        exit.Should().Be(RoleRunner.Done);
        (after.State, after.Stop).Should().Be((RoleJobStates.Cancelled, "cancelled"));
        rig.Models().Should().Equal("fable");
        rig.SwitchLines().Should().BeEmpty();
        rig.NoticesOf(ServerNoticeCodes.RoleModelSwitched).Should().BeEmpty();
    }

    [Fact]
    public async Task A13_ATreeThatChanged_EndsTheJob_AsTreeChanged_AndNoSecondModelIsLaunched()
    {
        await using var rig = await RoleFailoverRig.CreateAsync();
        Environment.SetEnvironmentVariable("FAKECLI_SIDE_EFFECT", Path.Combine(rig.Repo, "written-by-the-model.txt"));
        rig.Says("fable", FakeRoleAnswers.ClaudeSays(FakeRoleAnswers.Ok)).Says("opus", FakeRoleAnswers.ClaudeSays(FakeRoleAnswers.Ok));
        var job = rig.NewJob(["claude/fable", "claude/opus"]);

        var (_, after) = await rig.RunAsync(job);

        (after.State, after.Stop).Should().Be((RoleJobStates.Failed, "tree-changed"));
        after.Reason.Should().Contain("written-by-the-model.txt");
        after.Attempts.Should().ContainSingle().Which.Stop.Should().Be("tree-changed");
        rig.Models().Should().Equal("fable");
        File.Exists(rig.World.Store.FolderOf(job.JobId)!.ResultFile).Should().BeFalse("an answer formed on a tree that changed is never published");
        rig.NoticesOf(ServerNoticeCodes.RoleJobFailed).Should().ContainSingle();
    }

    [Theory]
    [InlineData("not json at all", "not JSON")]
    [InlineData("[1,2]", "not one object")]
    [InlineData("""{"status":"fine"}""", "status is not ok")]
    public async Task A13_AnAnswerThatIsNotTheRolesOutput_EndsTheJob_AsOutputInvalid_AndNoSecondModelIsLaunched(string answer, string said)
    {
        await using var rig = await RoleFailoverRig.CreateAsync();
        rig.Says("fable", FakeRoleAnswers.ClaudeSays(answer)).Says("opus", FakeRoleAnswers.ClaudeSays(FakeRoleAnswers.Ok));
        var job = rig.NewJob(["claude/fable", "claude/opus"]);

        var (_, after) = await rig.RunAsync(job);

        (after.State, after.Stop).Should().Be((RoleJobStates.Failed, "output-invalid"));
        after.Reason.Should().Contain(said);
        rig.Models().Should().Equal("fable");
        rig.NoticesOf(ServerNoticeCodes.RoleJobFailed).Should().ContainSingle();
    }

    // ---- the record: ledger, projection, evidence, status ----

    [Fact]
    public async Task EveryLaunch_IsOneLedgerLine_OfKindRole_NamingItsVendorModelAndOutcome()
    {
        await using var rig = await RoleFailoverRig.CreateAsync();
        rig.Says("fable", FakeRoleAnswers.ClaudeSpendLimit()).Says("astra", FakeRoleAnswers.CodexSays(FakeRoleAnswers.Ok));
        var job = rig.NewJob(["claude/fable", "claude/opus", "codex/astra"]);

        await rig.RunAsync(job);

        var lines = rig.LedgerLines();
        lines.Should().HaveCount(rig.Launches().Count).And.HaveCount(2, "fable and astra were launched; opus was skipped and has no line");
        lines.Select(line => (line.GetProperty("provider").GetString(), line.GetProperty("model").GetString(), line.GetProperty("role").GetString(), line.GetProperty("kind").GetString(), line.GetProperty("stage").GetString()))
            .Should().Equal(("claude", "fable", "failover-probe", "role", "RoleJob"), ("codex", "astra", "failover-probe", "role", "RoleJob"));
        lines.Select(line => line.GetProperty("outcome").GetString()).Should().Equal("exit", "ok");
    }

    [Fact]
    public async Task TheAttempts_AreInJobJson_InRoleAttempts_AndRoleStatusShowsThem_WithEvidenceUnderTheirFolder()
    {
        await using var rig = await RoleFailoverRig.CreateAsync();
        rig.Says("fable", FakeRoleAnswers.Crash("fable fell over")).Says("opus", FakeRoleAnswers.ClaudeSays(FakeRoleAnswers.Ok));
        var job = rig.NewJob(["claude/fable", "claude/opus"]);

        var (_, after) = await rig.RunAsync(job);

        after.Attempts[0].EvidenceRef.Should().Be("attempts/1/evidence.txt");
        File.ReadAllText(Path.Combine(rig.World.Store.FolderOf(job.JobId)!.Location, "attempts", "1", "evidence.txt")).Should().Contain("fable fell over");
        after.Attempts[1].EvidenceRef.Should().BeEmpty("a model that answered leaves no failure to keep");
        using var db = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(rig.World.DataDir, CoaiMcp.Store.RoundsDb.FileName)};Pooling=False;Mode=ReadOnly");
        db.Open();
        using var ask = db.CreateCommand();
        ask.CommandText = "SELECT ordinal, model, vendor, stop FROM role_attempts WHERE job_id = $id ORDER BY ordinal";
        ask.Parameters.AddWithValue("$id", job.JobId);
        using var rows = ask.ExecuteReader();
        var read = new List<(long, string, string, string)>();
        while (rows.Read())
        {
            read.Add((rows.GetInt64(0), rows.GetString(1), rows.GetString(2), rows.GetString(3)));
        }

        read.Should().Equal((1, "fable", "claude", "crash"), (2, "opus", "claude", string.Empty));
        var status = System.Text.Json.JsonDocument.Parse(RoleJobAnswers.Status(new RoleJobObservation(after, rig.World.Store.FolderOf(job.JobId)!.ReadHeartbeat(), false, false, false), string.Empty)).RootElement;
        status.GetProperty("attempts").EnumerateArray().Select(attempt => attempt.GetProperty("stop").GetString()).Should().Equal("crash", string.Empty);
        status.GetProperty("attempts")[0].GetProperty("evidenceRef").GetString().Should().Be("attempts/1/evidence.txt");
    }
}
