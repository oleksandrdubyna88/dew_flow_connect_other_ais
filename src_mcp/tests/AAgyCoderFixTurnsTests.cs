using System.Text.Json;
using CoaiMcp.Core.Roles;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Toolchain;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E3 s6 t5 (V13, V16, V18, V25; judgement 14.4 G15; ids AG-G5, AG-A2, AG-M2, AG-S1): the fix turns of an agy coder, in ONE conversation, and the check of every turn's own stream. d1: FakeCli as agy goes red, then green after 2 fix turns, with <c>--conversation</c> of turn 0 on both, the same home and the same guard
/// folder; an invalid report gets ONE repair turn in the same conversation. d2: a resumed turn in another conversation (also with exit 0), or with the not-found line, ends the attempt as a crash; the failover reads a lost sign-in; the login address is not copied. d3: the stream beside the guard's records after EVERY turn, and the signs of
/// a mode that did not hold: each end the job <c>coder-boundary</c>, never green, with nothing of that turn committed. d4: the usage line of each turn holds that turn only. d5: nothing here needs the codex story.
/// </summary>
/// <remarks>
/// The fake plays agy: it prints agy's stream and calls the REAL hook of the guard folder (the product's own handler, started as agy starts it) for every scripted tool, and takes its answer the way agy does — exit 0 with no output as allow. It runs inside a real unit; a real agy is never started.
/// </remarks>
[Collection("fakecli-env")]
public sealed class AAgyCoderFixTurnsTests
{
    private const string Model = AgyCoderBed.Model;

    private const string Conversation = AgyCoderBed.Conversation;

    private static readonly string[] Two = [$"agy/{Model}", "agy/gemini-4-argon-medium"];

    private static string After(string[] argv, string flag) => argv.SkipWhile(arg => arg != flag).Skip(1).FirstOrDefault() ?? string.Empty;

    private static IReadOnlyDictionary<string, object?> Turn(string state, string conversation = Conversation, string answer = "", string stderr = "") =>
        AgyCoderBed.Launch([AgyCoderBed.Writes(state)], answer.Length > 0 ? answer : null, conversation, stderr);

    private static async Task<CoderRig> RigAsync() => await AgyCoderBed.RigAsync();

    private static async Task<string> CommitsAsync(CoderRig rig, RoleJobRecord job) => await rig.GitAsync(rig.WorktreeOf(job), "rev-list", "--count", $"{rig.Base}..HEAD");

    // ---------- d1: red, then green after 2 fix turns ----------

    [Fact]
    public async Task D1_RedThenGreenAfterTwoFixTurns_InTheConversationOfTurnZero_WithTheSameHomeAndTheSameGuardFolder()
    {
        await using var rig = await RigAsync();
        rig.SaysEntries(Model, Turn(CoderRig.Red + " a"), Turn(CoderRig.Red + " b"), Turn(CoderRig.Green));
        var job = rig.NewJob(route: [$"agy/{Model}"]);

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        var launches = rig.Launches(job);
        launches.Should().HaveCount(3);
        After(launches[0], "--conversation").Should().BeEmpty("turn 0 starts the conversation");
        launches.Skip(1).Select(argv => After(argv, "--conversation")).Should().OnlyContain(id => id == Conversation, "both fix turns resume the conversation of turn 0");
        launches.Should().OnlyContain(argv => After(argv, "--mode") == "accept-edits" && After(argv, "--agent") == "coai-coder", "the mode and the agent are on EVERY argv");
        rig.Envelope(job).GetProperty("fixTurns").GetInt32().Should().Be(2);
        rig.Envelope(job).GetProperty("green").GetBoolean().Should().BeTrue();
        var seen = Directory.EnumerateFiles(Path.Combine(rig.JobTemp(job, "agy"), "launches", "sidecars"), "*.agy").Select(File.ReadAllLines).ToList();
        seen.Should().HaveCount(3);
        seen.Select(lines => lines.Single(line => line.StartsWith("cwd=", StringComparison.Ordinal))).Distinct().Should().ContainSingle().Which.Should().Be($"cwd={AgyCoderUnit.GuardFolder(JobOf(rig, job))}", "the same guard folder as the working directory, never the worktree");
        seen.Select(lines => lines.Single(line => line.StartsWith("home=", StringComparison.Ordinal))).Distinct().Should().ContainSingle().Which.Should().Be($"home={AgyCoderUnit.HomeFolder(JobOf(rig, job))}");
    }

    [Fact]
    public async Task D1_AnInvalidReport_GetsOneRepairTurn_InTheSameConversation()
    {
        await using var rig = await RigAsync();
        rig.SaysEntries(Model, Turn(CoderRig.Green, answer: "not a report at all"), Turn(CoderRig.Green));
        var job = rig.NewJob(route: [$"agy/{Model}"]);

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        var launches = rig.Launches(job);
        launches.Should().HaveCount(2);
        After(launches[1], "--conversation").Should().Be(Conversation);
        launches[1][^1].Should().Contain("Your report was checked and has errors");
        rig.Envelope(job).GetProperty("schemaValid").GetBoolean().Should().BeTrue();
    }

    private static CoaiMcp.Runners.Toolchain.BoundaryJob JobOf(CoderRig rig, RoleJobRecord job) =>
        CoaiMcp.Runners.Toolchain.BoundaryJob.ForCoder(job.JobId, new CoaiMcp.Runners.Worktrees.CoderTree(rig.WorktreeOf(job), rig.Repo, string.Empty), rig.JobFolder(job));

    // ---------- the release is a record: a self-update between two launches of one job (t1 d4, d2) ----------

    [Fact]
    public async Task T1D4_ASelfUpdateBetweenTwoLaunchesOfOneJob_RefusesNothing_AndTheEnvelopeNamesTheNewReleaseBesideTheMeasuredOne()
    {
        await using var rig = await RigAsync();
        rig.SaysEntries(Model, AgyCoderBed.Launch([AgyCoderBed.Writes(CoderRig.Red + " a")], conversation: Conversation, sleepMs: 1500), Turn(CoderRig.Green));
        var job = rig.NewJob(route: [$"agy/{Model}"]);
        var update = Task.Run(async () =>
        {
            // The first launch recorded itself: agy "updates" now, and the next ask of the installed CLI says another release.
            var waited = System.Diagnostics.Stopwatch.StartNew();
            while (rig.Launches(job).Count == 0 && waited.Elapsed < TimeSpan.FromSeconds(90))
            {
                await Task.Delay(20);
            }

            Environment.SetEnvironmentVariable("FAKECLI_VERSION_STDOUT", "1.5.19\n");
        });

        var (_, ended) = await rig.RunAsync(job);
        await update;

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        rig.Launches(job).Should().HaveCount(2, "the new release was launched like the measured one");
        var cli = rig.Envelope(job).GetProperty("cli");
        (cli.GetProperty("vendor").GetString(), cli.GetProperty("installed").GetString(), cli.GetProperty("lastMeasured").GetString()).Should().Be(("agy", "1.5.19", "1.5.18"));
    }

    [Fact]
    public async Task T1D2_AnOlderAndANewerRelease_AreEachLaunched_TheEnvelopeNamesBoth()
    {
        foreach (var printed in new[] { "1.5.17", "1.9.0", "no number\n" })
        {
            await using var rig = await RigAsync();
            Environment.SetEnvironmentVariable("FAKECLI_VERSION_STDOUT", printed.EndsWith('\n') ? printed : printed + "\n");
            rig.SaysEntries(Model, Turn(CoderRig.Green));
            var job = rig.NewJob(route: [$"agy/{Model}"]);

            var (_, ended) = await rig.RunAsync(job);

            ended.State.Should().Be(RoleJobStates.Succeeded, $"{printed}: {ended.Reason}");
            var cli = rig.Envelope(job).GetProperty("cli");
            cli.GetProperty("installed").GetString().Should().Be(printed.Contains("no number", StringComparison.Ordinal) ? "unknown" : printed);
            cli.GetProperty("lastMeasured").GetString().Should().Be("1.5.18");
        }
    }

    // ---------- d2: a lost session, a lost sign-in ----------

    [Fact]
    public async Task D2_AResumedTurnInAnotherConversation_AlsoWithExitZero_IsACrash_AndTheNextModelStartsFromTheLastCoaiCommit_InANewConversation()
    {
        await using var rig = await RigAsync();
        rig.SaysEntries(Model, Turn(CoderRig.Red + " a"), AgyCoderBed.Launch([AgyCoderBed.Tool("write_file", "stray.txt", "half\n")], conversation: AgyCoderBed.Other))
            .SaysEntries("gemini-4-argon-medium", Turn(CoderRig.Green, AgyCoderBed.Other));
        var job = rig.NewJob(route: Two);

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        var launches = rig.Launches(job);
        launches.Should().HaveCount(3);
        After(launches[1], "--conversation").Should().Be(Conversation);
        After(launches[2], "--conversation").Should().BeEmpty("no fix turn goes on in a new conversation: the next model starts from turn 0");
        File.Exists(Path.Combine(rig.WorktreeOf(job), "stray.txt")).Should().BeFalse("the worktree went back to the last coai commit");
        ended.Attempts.Select(attempt => attempt.Stop).Should().Contain(RoleStopWords.Crash);
    }

    [Fact]
    public async Task D2_TheNotFoundLineOnStandardError_IsACrash_EvenWhenTheStreamNamesAnId()
    {
        await using var rig = await RigAsync();
        rig.SaysEntries(Model, Turn(CoderRig.Red + " a"), Turn(CoderRig.Green, Conversation, stderr: $"warning: conversation \"{Conversation}\" not found"))
            .SaysEntries("gemini-4-argon-medium", Turn(CoderRig.Green, AgyCoderBed.Other));
        var job = rig.NewJob(route: Two);

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        ended.Attempts.Select(attempt => attempt.Stop).Should().Contain(RoleStopWords.Crash);
        After(rig.Launches(job)[^1], "--conversation").Should().BeEmpty();
    }

    public static TheoryData<string, int, string, string> LostSignIns => new()
    {
        { "Authentication required. Please visit the URL to log in:\n  https://accounts.google.com/o/oauth2/auth?x=1\n", 1, string.Empty, "Authentication required" },
        { "Error: authentication timed out.\n", 1, string.Empty, "authentication timed out" },
        { "Error: Please sign in to view available models. Launch the CLI without arguments to sign in.\n", 1, string.Empty, "Please sign in" },
        { string.Empty, 1, "{\"event\":\"result\",\"result\":{\"conversation_id\":\"\",\"status\":\"ERROR\",\"response\":\"\",\"error\":\"authentication failed or timed out\"}}\n", "authentication failed or timed out" },
        { "Error: authentication required. Run 'agy' to log in, then retry.\n", 1, string.Empty, "authentication required" },
    };

    [Theory]
    [MemberData(nameof(LostSignIns))]
    public void D2_TheFailoverReadsTheTextsOfALostAgySignIn(string stderr, int exit, string stdout, string label)
    {
        RoleStops.SignInLost("agy", new ProcessResult(exit, stdout, stderr, false)).Should().BeTrue(label);
        RoleStops.SignInLost("agy", new ProcessResult(0, stdout, stderr, false)).Should().BeFalse("a launch that exited 0 is the model speaking");
    }

    [Theory]
    [InlineData("the model wrote: authentication of the user is handled by the login flow")]
    [InlineData("warning: conversation not found")]
    public void D2_TextThatIsNotALostSignIn_IsNotOne(string stderr) =>
        RoleStops.SignInLost("agy", new ProcessResult(1, string.Empty, stderr, false)).Should().BeFalse();

    [Fact]
    public async Task D2_ALostSignIn_SkipsTheOtherModelsOfAgy_AndTheLoginAddressIsInNoEnvelopeNoEvidenceAndNoNotice()
    {
        await using var rig = await RigAsync();
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "role-stops", "agy-copy-expired.json")));
        rig.Says(Model, new CoderRig.Launch(null, string.Empty, Exit: 1, Stdout: fixture.RootElement.GetProperty("stdout").GetString()!, Stderr: fixture.RootElement.GetProperty("stderr").GetString()!));
        var job = rig.NewJob(route: Two);

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Failed);
        rig.Launches(job).Select(argv => After(argv, "--model")).Should().Equal(Model);
        ended.Attempts.Should().Contain(attempt => attempt.Stop == RoleStopWords.Crash && attempt.Reason.StartsWith("the sign-in is lost", StringComparison.Ordinal));
        var folder = rig.World.Store.FolderOf(job.JobId)!;
        var everything = File.ReadAllText(folder.JobFile) + string.Join('\n', Directory.EnumerateFiles(folder.Location, "*", SearchOption.AllDirectories).Select(File.ReadAllText)) + string.Join('\n', rig.World.Notices.Select(notice => notice.ToString()));
        everything.Should().NotContain("accounts.google.com").And.NotContain("code_challenge").And.NotContain("oauth2/auth", "the login address is in no envelope, evidence or notice");
    }

    // ---------- d3: G15 and the signs of AG-M2, through a job ----------

    public static TheoryData<string> BrokenTies => new() { "no-record", "no-event", "two-records", "another-tool", "manage-task" };

    private static IReadOnlyDictionary<string, object?> Broken(string which) => which switch
    {
        "no-record" => AgyCoderBed.Launch([AgyCoderBed.Tool("write_file", CoderRig.StateFile, CoderRig.Green + "\n", skipHook: true)]),
        "no-event" => AgyCoderBed.Launch([AgyCoderBed.Tool("write_file", CoderRig.StateFile, CoderRig.Green + "\n", noEvent: true)]),
        "two-records" => AgyCoderBed.Launch([AgyCoderBed.Tool("write_file", CoderRig.StateFile, CoderRig.Green + "\n", hookTwice: true)]),
        "another-tool" => AgyCoderBed.Launch([AgyCoderBed.Writes(CoderRig.Green), AgyCoderBed.Tool("run_command", CoderRig.StateFile)]),
        _ => AgyCoderBed.Launch([AgyCoderBed.Writes(CoderRig.Green), AgyCoderBed.Tool("manage_task", CoderRig.StateFile)]),
    };

    [Theory]
    [MemberData(nameof(BrokenTies))]
    public async Task D3_ABrokenTie_OnTurnZero_EndsTheJobCoderBoundary_NeverGreen_AndNothingOfTheTurnIsCommitted(string which)
    {
        await using var rig = await RigAsync();
        rig.SaysEntries(Model, Broken(which));
        var job = rig.NewJob(route: [$"agy/{Model}"]);

        var (_, ended) = await rig.RunAsync(job);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, RoleStopWords.CoderBoundary), $"{which}: {ended.Reason}");
        (await CommitsAsync(rig, job)).Should().Be("0", "nothing of that turn was committed");
    }

    [Theory]
    [MemberData(nameof(BrokenTies))]
    public async Task D3_ABrokenTie_OnAFixTurn_EndsTheJobToo_AndTheFixIsNotCommitted(string which)
    {
        await using var rig = await RigAsync();
        rig.SaysEntries(Model, Turn(CoderRig.Red + " a"), Broken(which));
        var job = rig.NewJob(route: [$"agy/{Model}"]);

        var (_, ended) = await rig.RunAsync(job);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, RoleStopWords.CoderBoundary), $"{which}: {ended.Reason}");
        (await CommitsAsync(rig, job)).Should().Be("1", "the commit of turn 0 only");
    }

    [Fact]
    public async Task D3_ADeniedWrite_IsAnErrorEventWithADenyRecord_AndThatTurnPasses_TheGuardStoppedIt()
    {
        await using var rig = await RigAsync();
        var outside = Path.Combine(rig.Root, "outside-of-the-worktree.txt");
        rig.SaysEntries(Model, AgyCoderBed.Launch([AgyCoderBed.Tool("write_file", outside, "x"), AgyCoderBed.Writes(CoderRig.Green)]));
        var job = rig.NewJob(route: [$"agy/{Model}"]);

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        File.Exists(outside).Should().BeFalse("the guard denied it and the fake did not write it");
        var records = Directory.EnumerateFiles(AgyCoderUnit.VendorFolder(JobOf(rig, job)), "guard-records.jsonl*").SelectMany(File.ReadAllLines).ToList();
        records.Should().Contain(line => line.Contains("\"decision\":\"deny\"", StringComparison.Ordinal)).And.Contain(line => line.Contains("\"decision\":\"allow\"", StringComparison.Ordinal));
    }

    public static TheoryData<string, string, string> ModeSigns => new()
    {
        { "fallback", "warning: --mode auto is not available for this account; continuing in the default mode.", string.Empty },
        { "unrecognized", "warning: unrecognized --mode value \"bogus-mode\" (valid: accept-edits, auto, plan)", string.Empty },
        { "permission", "jetski: no output produced - a tool required the \"command\" permission that headless mode cannot prompt for, so it was auto-denied.", string.Empty },
        { "denied", string.Empty, "[{\"action\":\"command\",\"display_name\":\"Bash\"}]" },
    };

    [Theory]
    [MemberData(nameof(ModeSigns))]
    public async Task D3_ASignOfAModeThatDidNotHold_EndsTheJob_AlthoughTheArgvIsRight_TheFlagAloneNeverPasses(string which, string stderr, string denied)
    {
        await using var rig = await RigAsync();
        rig.SaysEntries(Model, AgyCoderBed.Launch([AgyCoderBed.Writes(CoderRig.Green)], answer: which == "denied" ? string.Empty : null, stderr: stderr, denied: denied));
        var job = rig.NewJob(route: [$"agy/{Model}"]);

        var (_, ended) = await rig.RunAsync(job);

        rig.Launches(job).Single().Should().Contain(["--mode", "accept-edits"], "the argv is right, and that proves no mode");
        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, RoleStopWords.CoderBoundary), $"{which}: {ended.Reason}");
        (await CommitsAsync(rig, job)).Should().Be("0");
    }

    [Fact]
    public async Task D3_TheInitLineIsNotCompared_AM3IsNotBuilt()
    {
        // The fake's init line says request-review, as agy's does for every mode: a job passes, so nothing compares it.
        await using var rig = await RigAsync();
        rig.SaysEntries(Model, Turn(CoderRig.Green));
        var job = rig.NewJob(route: [$"agy/{Model}"]);

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
    }

    // ---------- M11: a turn that ended badly is checked too, and a boundary wins over a crash ----------

    public static TheoryData<string> CutTurns => new() { "exit-after-a-done-with-no-record", "timeout-after-a-done-with-no-record", "exit-with-a-mode-warning", "exit-after-a-denied-action-result", "timeout-while-another-tool-is-active", "exit-while-another-tool-is-active" };

    [Theory]
    [MemberData(nameof(CutTurns))]
    public async Task M11_ATurnThatEndedBadly_ButShowsABrokenBoundary_EndsTheJobCoderBoundary_NoFailover(string which)
    {
        await using var rig = await RigAsync();
        rig.AttemptTimeout = TimeSpan.FromSeconds(4);
        var broken = AgyCoderBed.Tool("write_file", CoderRig.StateFile, CoderRig.Green + "\n", skipHook: true);
        rig.SaysEntries(Model, which switch
        {
            "exit-after-a-done-with-no-record" => AgyCoderBed.Launch([broken], exit: 1),
            "timeout-after-a-done-with-no-record" => AgyCoderBed.Launch([broken], holdMs: 60_000),
            "timeout-while-another-tool-is-active" => AgyCoderBed.Launch([AgyCoderBed.Tool("run_command", string.Empty, skipHook: true, stayActive: true)], holdMs: 60_000),
            "exit-while-another-tool-is-active" => AgyCoderBed.Launch([AgyCoderBed.Tool("run_command", string.Empty, skipHook: true, stayActive: true)], exit: 1),
            "exit-with-a-mode-warning" => AgyCoderBed.Launch([AgyCoderBed.Writes(CoderRig.Green)], exit: 1, stderr: "warning: --mode auto is not available for this account; continuing in the default mode."),
            _ => AgyCoderBed.Launch([AgyCoderBed.Writes(CoderRig.Green)], exit: 1, answer: string.Empty, denied: "[{\"action\":\"command\",\"display_name\":\"Bash\"}]"),
        }).SaysEntries("gemini-4-argon-medium", Turn(CoderRig.Green, AgyCoderBed.Other));
        var job = rig.NewJob(route: Two);

        var (_, ended) = await rig.RunAsync(job);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, RoleStopWords.CoderBoundary), $"{which}: {ended.Reason}");
        rig.Launches(job).Should().ContainSingle("a boundary wins over a crash: the next model is never launched");
        (await CommitsAsync(rig, job)).Should().Be("0");
    }

    [Fact]
    public async Task M11_ATurnThatEndedBadly_WithNothingBroken_StillFailsOver_AsACrash()
    {
        await using var rig = await RigAsync();
        rig.SaysEntries(Model, AgyCoderBed.Launch([AgyCoderBed.Tool("write_file", "stray.txt", "half\n")], exit: 1, stderr: "the CLI fell over: boom"))
            .SaysEntries("gemini-4-argon-medium", Turn(CoderRig.Green, AgyCoderBed.Other));
        var job = rig.NewJob(route: Two);

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        ended.Attempts.Select(attempt => attempt.Stop).Should().Contain(RoleStopWords.Crash);
        File.Exists(Path.Combine(rig.WorktreeOf(job), "stray.txt")).Should().BeFalse("the worktree went back to the last coai commit");
    }

    [Fact]
    public void M11_OnACutStream_TheCheckAnswersBoundaryForWhatOneSideAloneProves_AndNothingForWhatACutExplains()
    {
        var done = Stream(Init("c"), Step("c", 2, "ACTIVE", "write_file"), Step("c", 2, "DONE", "write_file"));
        var cut = (string stream, string? records, string stderr) => Check(stream, records, stderr, exit: 1);

        cut(done, string.Empty, string.Empty).Should().BeOfType<CoderTurnVerdict.Boundary>("a DONE event with no record");
        cut(done, Record("c", 2, "write_file", "deny") + "\n", string.Empty).Should().BeOfType<CoderTurnVerdict.Boundary>("a DONE event whose record says deny");
        cut(done, null, string.Empty).Should().BeOfType<CoderTurnVerdict.Boundary>("a DONE event and records that cannot be read: nothing shows the hook decided");
        cut(Stream(Init("c"), Step("c", 2, "DONE", "run_command")), Record("c", 2, "run_command", "allow") + "\n", string.Empty).Should().BeOfType<CoderTurnVerdict.Boundary>("a DONE event of another tool");
        cut(done, Record("c", 2, "write_file", "allow") + "\n", string.Empty).Should().BeOfType<CoderTurnVerdict.Fine>("a matched event: the cut explains the rest");
        cut(Stream(Init("c"), Step("c", 2, "ACTIVE", "write_file")), string.Empty, string.Empty).Should().BeOfType<CoderTurnVerdict.Fine>("an event that did not end is what a cut explains");
        cut(Stream(Init("c"), Step("c", 2, "ACTIVE", "run_command")), string.Empty, string.Empty).Should().BeOfType<CoderTurnVerdict.Boundary>("an event of another tool is a break in every state: a cut does not explain that it was started");
        cut(Stream(Init("c")), Record("c", 2, "write_file", "allow") + "\n", string.Empty).Should().BeOfType<CoderTurnVerdict.Fine>("a record with no event: the stream was cut before it");
        cut(Stream(Init("c")), string.Empty, "warning: --mode auto is not available").Should().BeOfType<CoderTurnVerdict.Boundary>("a mode sign");
    }

    [Fact]
    public void M11b_AnActiveEventOfAnotherTool_IsBoundary_OnACleanStreamAndOnACutOne_Alike()
    {
        var active = Stream(Init("c"), Step("c", 2, "ACTIVE", "run_command"));

        Check(active, string.Empty, exit: 0).Should().BeOfType<CoderTurnVerdict.Boundary>("a clean stream (G15 item 4)");
        Check(active, string.Empty, exit: 1).Should().BeOfType<CoderTurnVerdict.Boundary>("a stream that ended with an exit that is not 0");
        AgyTurnCheck.Check(new ProcessResult(-1, active, string.Empty, true), string.Empty, string.Empty).Should().BeOfType<CoderTurnVerdict.Boundary>("a stream the deadline cut");
        foreach (var state in new[] { "ERROR", "DONE" })
        {
            Check(Stream(Init("c"), Step("c", 2, state, "run_command")), string.Empty, exit: 1).Should().BeOfType<CoderTurnVerdict.Boundary>(state);
        }
    }

    // ---------- the check itself, over crafted streams ----------

    private static string Stream(params string[] events) => string.Join('\n', events) + "\n";

    private static string Init(string conversation) => $"{{\"event\":\"init\",\"conversation_id\":\"{conversation}\",\"init\":{{\"permission_mode\":\"request-review\"}}}}";

    private static string Step(string conversation, int step, string state, string tool) =>
        $"{{\"event\":\"step_update\",\"step_update\":{{\"conversation_id\":\"{conversation}\",\"step_index\":{step},\"state\":\"{state}\",\"step_type\":\"tool\",\"tool_name\":\"{tool}\"}}}}";

    private static string Record(string conversation, int step, string tool, string decision) =>
        $"{{\"conversationId\":\"{conversation}\",\"stepIdx\":{step},\"tool\":\"{tool}\",\"decision\":\"{decision}\"}}";

    private static CoderTurnVerdict Check(string stdout, string? records, string stderr = "", string expected = "", int exit = 0) =>
        AgyTurnCheck.Check(new ProcessResult(exit, stdout, stderr, false), expected, records);

    [Fact]
    public void TheCheck_PassesAMatchedTurn_AllowWithDone_DenyWithError_AndATurnWithNoTool()
    {
        Check(Stream(Init("c"), Step("c", 2, "ACTIVE", "write_file"), Step("c", 2, "DONE", "write_file"), Step("c", 4, "ACTIVE", "read_file"), Step("c", 4, "ERROR", "read_file")), Record("c", 2, "write_file", "allow") + "\n" + Record("c", 4, "read_file", "deny") + "\n")
            .Should().BeOfType<CoderTurnVerdict.Fine>();
        Check(Stream(Init("c")), string.Empty).Should().BeOfType<CoderTurnVerdict.Fine>();
        Check(Stream(Init("c")), null).Should().BeOfType<CoderTurnVerdict.Fine>("no tool and no record: nothing to tie");
    }

    public static TheoryData<string, string, string> Broken_ => new()
    {
        { "done-with-deny", "DONE", "deny" },
        { "active-never-ended", "ACTIVE", "allow" },
    };

    [Theory]
    [MemberData(nameof(Broken_))]
    public void TheCheck_RefusesADecisionThatDoesNotMatchTheEvent(string why, string state, string decision)
    {
        Check(Stream(Init("c"), Step("c", 2, state, "write_file")), Record("c", 2, "write_file", decision) + "\n").Should().BeOfType<CoderTurnVerdict.Boundary>(why);
    }

    [Fact]
    public void TheCheck_RefusesADenyThatEndedDone_AndARecordOfAnotherConversationOrStepOrTool_AndUnreadableRecords()
    {
        var stream = Stream(Init("c"), Step("c", 2, "DONE", "write_file"));

        Check(Stream(Init("c"), Step("c", 2, "ACTIVE", "write_file"), Step("c", 2, "DONE", "write_file")), Record("c", 2, "write_file", "deny") + "\n").Should().BeOfType<CoderTurnVerdict.Boundary>("a deny whose tool ended DONE");
        Check(stream, Record("d", 2, "write_file", "allow") + "\n").Should().BeOfType<CoderTurnVerdict.Boundary>("another conversation");
        Check(stream, Record("c", 3, "write_file", "allow") + "\n").Should().BeOfType<CoderTurnVerdict.Boundary>("another step");
        Check(stream, Record("c", 2, "edit_file", "allow") + "\n").Should().BeOfType<CoderTurnVerdict.Boundary>("another tool");
        Check(stream, "this is not a record\n").Should().BeOfType<CoderTurnVerdict.Boundary>();
        Check(stream, null).Should().BeOfType<CoderTurnVerdict.Boundary>("the records could not be read");
        Check(stream, string.Empty).Should().BeOfType<CoderTurnVerdict.Boundary>("an event and no record");
        Check(Stream(Init("c")), Record("c", 2, "write_file", "allow") + "\n").Should().BeOfType<CoderTurnVerdict.Boundary>("a record and no event");
    }

    [Fact]
    public void TheCheck_AFailedLaunchIsNotTiedToTheGuard_ButItsModeSignsStillEndTheJob()
    {
        Check(Stream(Init("c"), Step("c", 2, "ACTIVE", "write_file")), string.Empty, exit: 1).Should().BeOfType<CoderTurnVerdict.Fine>("an exit that is not 0 is a crash by its own road, and the stream is cut");
        Check(Stream(Init("c")), string.Empty, "warning: --mode auto is not available for this account; continuing in the default mode.", exit: 1).Should().BeOfType<CoderTurnVerdict.Boundary>();
    }

    [Fact]
    public void TheCheck_NoSentenceHoldsAnArgumentAPathOrALineOfStandardError()
    {
        var verdict = Check(Stream(Init("c"), Step("c", 2, "DONE", "write_file")), string.Empty, "warning: --mode auto https://accounts.google.com/o?code_challenge=SECRET");

        verdict.Should().BeOfType<CoderTurnVerdict.Boundary>().Which.Sentence.Should().NotContain("SECRET").And.NotContain("google");
    }

    // ---------- d4: the usage of each turn ----------

    [Fact]
    public async Task D4_TheUsageLineOfEachTurn_HoldsThatTurnOnly_TheSumAgyReportsIsSubtracted()
    {
        await using var rig = await RigAsync();
        rig.SaysEntries(Model, Turn(CoderRig.Red + " a"), Turn(CoderRig.Red + " b"), Turn(CoderRig.Green));
        var job = rig.NewJob(route: [$"agy/{Model}"]);

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        var lines = rig.Rig.LedgerLines();
        lines.Select(line => line.GetProperty("tokensIn").GetInt64()).Should().Equal([100L, 100L, 100L], "the fake reports 100, 200 and 300: the running sum of the conversation");
        lines.Select(line => line.GetProperty("tokensOut").GetInt64()).Should().Equal([10L, 10L, 10L]);
    }

    // ---------- d5 ----------

    [Fact]
    public void D5_NothingHereNeedsTheCodexStory_ATableInWhichCodexIsRefusedLaunchesAgy()
    {
        var set = AgyCoderBed.Set();

        set.Resolve("codex").Should().BeOfType<CoderVendorResolution.Refused>();
        set.Resolve("agy").Should().BeOfType<CoderVendorResolution.Launchable>();
        set.Launchable.Should().Equal("claude", "agy");
    }
}
