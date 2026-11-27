using System.Text;
using static CoaiMcp.Tests.CodexScript;
using System.Text.Json;
using System.Text.RegularExpressions;
using CoaiMcp.Core.Work;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Toolchain;
using CoaiMcp.Runners.Worktrees;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E3 s5 t4 (V13, V17, V26): the fix turns of a codex coder with FakeCli as codex — the REAL <see cref="CodexCoderVendor"/> in a table that makes it launchable (the product's own table keeps the mark off until s5 t5), the real units with FakeCli's own <c>sandbox</c>, <c>debug prompt-input</c> and <c>doctor</c>
/// answering the preflight, a sign-in COPY made from a made-up file in a stand-in home, and the model map that scripts each turn's stream, report file and rollout file.
/// </summary>
/// <remarks>
/// d1: red, then green after two fix turns, <c>exec resume</c> of turn 0's thread from the worktree with the same <c>CODEX_HOME</c> and the same flags; an invalid report gets one repair turn in the same thread. d2: the recorded no-session text is a crash; the three signs of a lost sign-in and the text that is not one. d3: the turn check over the
/// stream and the rollout of every turn, before anything is committed. d4: the prompt of the codex coder. Real <c>systemd-run --user</c> units; skipped, with the reason, where there is no user manager.
/// </remarks>
[Collection("fakecli-env")]
public sealed class ACodexCoderFixTurnsTests
{
    private static readonly string[] Two = ["codex/terra", "codex/astra"];

    private static string[] Launched(string[] argv) => argv[..^1];

    // ---------- d1: the fix turns ----------

    [Fact]
    public async Task D1_FakeCliAsCodex_GoesRedThenGreenAfterTwoFixTurns_InTheThreadOfTurnZero_FromTheWorktree_WithTheSameHomeAndTheSameFlags()
    {
        await using var world = await CodexRigWorld.CreateAsync();
        var rig = world.Rig;
        rig.SaysRaw("terra", Turn(Writes(CoderRig.Red + " a"), Report()), Turn(Writes(CoderRig.Red + " b"), Report()), Turn(Writes(CoderRig.Green), Report()));
        var job = rig.NewJob(route: ["codex/terra"]);

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        var launches = rig.Launches(job);
        launches.Should().HaveCount(3);
        launches[0].Take(2).Should().Equal(["exec", "--json"], "turn 0 is a fresh exec");
        foreach (var fix in launches.Skip(1))
        {
            fix.Take(2).Should().Equal("exec", "resume");
            fix.Should().Contain(ThreadId, "a fix turn resumes the thread of turn 0");
            Launched(fix).Where(argument => argument is not ("exec" or "resume") && argument != ThreadId).Order().Should().Equal(Launched(launches[0]).Where(argument => argument != "exec").Order(), "the same flags, none dropped and none added");
        }

        var where = Directory.EnumerateFiles(Path.Combine(rig.LaunchFolder(job), "sidecars"), "*.where").Select(File.ReadAllLines).ToList();
        where.Should().HaveCount(3);
        where.Select(lines => lines[0]).Should().OnlyContain(folder => folder == rig.WorktreeOf(job), "every turn runs from the worktree");
        where.Select(lines => lines[1]).Distinct().Should().ContainSingle("every turn has the same CODEX_HOME").Which.Should().EndWith(Path.Combine("coder", "codex", "home", ".codex"));
        rig.Envelope(job).GetProperty("fixTurns").GetInt32().Should().Be(2);
        rig.Envelope(job).GetProperty("green").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task D1_AnInvalidReport_GetsOneRepairTurn_InTheSameThread_AndTheReportIsReadFromTheOutputFile()
    {
        await using var world = await CodexRigWorld.CreateAsync();
        var rig = world.Rig;
        rig.SaysRaw("terra", Turn(Writes(CoderRig.Green), "this is not a report at all"), Turn(null, Report()));
        var job = rig.NewJob(route: ["codex/terra"]);

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        var launches = rig.Launches(job);
        launches.Should().HaveCount(2);
        launches[1].Take(2).Should().Equal("exec", "resume");
        launches[1].Should().Contain(ThreadId);
        launches[1][^1].Should().Contain("Your report was checked and has errors");
        rig.Envelope(job).GetProperty("schemaValid").GetBoolean().Should().BeTrue();
    }

    // ---------- d2: a lost session, and a lost sign-in ----------

    [Fact]
    public async Task D2_AResumeThatAnswersTheRecordedNoSessionText_IsACrash_AndTheNextModelStartsFromTheLastCoaiCommit_InANewThread()
    {
        await using var world = await CodexRigWorld.CreateAsync();
        var rig = world.Rig;
        var lost = RoleStopFixtures.Load("codex-resume-no-session");
        lost.ExitCode.Should().NotBe(0);
        rig.SaysRaw("terra", Turn(Writes(CoderRig.Red + " a"), Report()), new Dictionary<string, object?> { ["stdout"] = lost.StdOut, ["stderr"] = lost.StdErr, ["exit"] = lost.ExitCode, ["writeFiles"] = new Dictionary<string, string> { ["stray.txt"] = "half\n" } })
            .SaysRaw("astra", Turn(Writes(CoderRig.Green), Report(), OtherThread, rollout: Rollout(OtherThread)));
        var job = rig.NewJob(route: Two);

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        var launches = rig.Launches(job);
        launches.Should().HaveCount(3);
        launches[1].Take(2).Should().Equal("exec", "resume");
        launches[2].Take(2).Should().Equal(["exec", "--json"], "no fix turn starts a new session by itself: the next model starts from turn 0, in a session of its own");
        File.Exists(Path.Combine(rig.WorktreeOf(job), "stray.txt")).Should().BeFalse("the worktree was put back to the last coai commit");
    }

    private static ProcessResult Fixture(string name, Func<string, string>? stderr = null, Func<string, string>? stdout = null)
    {
        var fixture = RoleStopFixtures.Load(name);

        return new ProcessResult(fixture.ExitCode, stdout?.Invoke(fixture.StdOut) ?? fixture.StdOut, stderr?.Invoke(fixture.StdErr) ?? fixture.StdErr, TimedOut: false);
    }

    [Theory]
    [InlineData("codex-refresh-failed")]
    [InlineData("codex-refresh-failed-host-network")]
    [InlineData("codex-no-sign-in")]
    public void D2_ARecordedLostSignIn_IsReadAsOne_ByEachOfItsFixtures(string fixture)
    {
        RoleStops.SignInLost("codex", Fixture(fixture)).Should().BeTrue();
    }

    [Fact]
    public void D2_TheThreeSigns_EachAloneIsALostSignIn_AndTheTextThatIsNoneOfThemIsNot()
    {
        var calm = new ProcessResult(1, string.Empty, string.Empty, TimedOut: false);
        string Failed(string message) => "{\"type\":\"turn.failed\",\"error\":{\"message\":\"" + message + "\"}}\n";

        RoleStops.SignInLost("codex", calm with { StdErr = "Failed to refresh token: error sending request for url (http://127.0.0.1:9/oauth/token)\n" }).Should().BeTrue("sign 1: the CLI's own line on stderr");
        RoleStops.SignInLost("codex", calm with { StdOut = Failed("workspace routing discovery unauthorized (401)") }).Should().BeTrue("sign 2: the vendor refused the token");
        RoleStops.SignInLost("codex", calm with { StdOut = Failed("Your access token could not be refreshed because the server answered 400") }).Should().BeTrue("sign 3: the sentence of the override that answered");
        RoleStops.SignInLost("codex", calm with { StdOut = Failed("workspace routing discovery failed") }).Should().BeFalse("with the host network this says the network or the vendor could not be reached: a crash, not a lost sign-in");
        RoleStops.SignInLost("codex", calm with { StdOut = Failed("unauthorized (401)"), ExitCode = 0 }).Should().BeFalse("an exit of zero is an answer");
    }

    [Fact]
    public void D2_TheNoNetworkFixtureOnItsStdoutAlone_IsTheTextThatIsNotALostSignIn_AndTheHostNetworkFixtureIsOneByItsOwnStdout()
    {
        RoleStops.SignInLost("codex", Fixture("codex-refresh-failed", stderr: _ => string.Empty)).Should().BeFalse("the no-network stdout is the text that is not one by itself: only its stderr line is the sign");
        RoleStops.SignInLost("codex", Fixture("codex-refresh-failed-host-network", stderr: _ => string.Empty)).Should().BeTrue("the shape a job meets: its stdout holds the 401 of the vendor");
        RoleStops.SignInLost("codex", Fixture("codex-refresh-failed-host-network", stdout: _ => string.Empty)).Should().BeTrue("and its stderr holds the CLI's own line");
    }

    [Fact]
    public async Task D2_ALostSignInOnTurnZero_SkipsTheOtherModelsOfCodex_AndTheCureTellsThePersonToRunCodexOnce()
    {
        await using var world = await CodexRigWorld.CreateAsync();
        var rig = world.Rig;
        var lost = RoleStopFixtures.Load("codex-refresh-failed-host-network");
        rig.SaysRaw("terra", new Dictionary<string, object?> { ["stdout"] = lost.StdOut, ["stderr"] = lost.StdErr, ["exit"] = lost.ExitCode })
            .SaysRaw("astra", Turn(Writes(CoderRig.Green), Report(), OtherThread, rollout: Rollout(OtherThread)));
        var job = rig.NewJob(route: Two);

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Failed);
        ended.Reason.Should().Contain("sign-in is lost").And.Contain("run codex once yourself");
        rig.Launches(job).Should().ContainSingle("the other model of codex was skipped: it would meet the same sign-in");
    }

    // ---------- d3: the turn check, in the loop ----------

    public static TheoryData<string, string[]> Bad => new()
    {
        { "a web search", [OfType("web_search", ",\"query\":\"\",\"results\":[]")] },
        { "an agent call", [OfType("collab_tool_call", ",\"tool\":\"wait\",\"receiver_thread_ids\":[]")] },
        { "an item type nobody measured", [OfType("reasoning", ",\"text\":\"thinking\"")] },
        { "an MCP call of another server", [OfType("mcp_tool_call", ",\"server\":\"other\",\"tool\":\"list_mcp_resources\",\"result\":null,\"error\":null")] },
        { "a read_mcp_resource that succeeded", [OfType("mcp_tool_call", ",\"server\":\"codex\",\"tool\":\"read_mcp_resource\",\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"x\"}]},\"error\":null")] },
        { "a list that is not empty", [OfType("mcp_tool_call", ",\"server\":\"codex\",\"tool\":\"list_mcp_resources\",\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"{\\\"resources\\\":[{\\\"uri\\\":\\\"x\\\"}]}\"}]},\"error\":null")] },
    };

    [Theory]
    [MemberData(nameof(Bad))]
    public async Task D3_ABadItemOnTurnZero_EndsTheJobCoderBoundary_NothingOfThatTurnIsCommitted_AndTheNextModelIsNotAsked(string name, string[] items)
    {
        await using var world = await CodexRigWorld.CreateAsync();
        var rig = world.Rig;
        rig.SaysRaw("terra", Turn(Writes(CoderRig.Green), Report(), items: [Message(), .. items])).SaysRaw("astra", Turn(Writes(CoderRig.Green), Report(), OtherThread, rollout: Rollout(OtherThread)));
        var job = rig.NewJob(route: Two);

        var (_, ended) = await rig.RunAsync(job);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, RoleStopWords.CoderBoundary), $"{name}: {ended.Reason}");
        (await rig.GitAsync(rig.Repo, "rev-list", "--count", $"{rig.Base}..coder/{job.JobId}")).Should().Be("0", "the worktree holds no commit of the turn the check refused");
        rig.Launches(job).Should().ContainSingle("a stop of the boundary never fails over");
    }

    [Fact]
    public async Task D3_TheItemsOfTheMeasuredTypes_AndTheTwoListTools_AreNotRefused_AControlForTheCasesAbove()
    {
        await using var world = await CodexRigWorld.CreateAsync();
        var rig = world.Rig;
        string[] fine =
        [
            Message(),
            OfType("command_execution", ",\"command\":\"ls\",\"status\":\"completed\",\"exit_code\":0"),
            OfType("file_change", ",\"changes\":[],\"status\":\"completed\""),
            OfType("error", ",\"message\":\"Falling back from WebSockets to HTTPS transport.\""),
            OfType("mcp_tool_call", ",\"server\":\"codex\",\"tool\":\"list_mcp_resources\",\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"{\\\"resources\\\":[]}\"}]},\"error\":null"),
        ];
        rig.SaysRaw("terra", Turn(Writes(CoderRig.Green), Report(), items: fine));
        var job = rig.NewJob(route: ["codex/terra"]);

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
    }

    [Fact]
    public async Task D3_ABadItemOnAFixTurn_EndsTheJobCoderBoundary_BeforeThatTurnIsCommitted()
    {
        await using var world = await CodexRigWorld.CreateAsync();
        var rig = world.Rig;
        rig.SaysRaw("terra", Turn(Writes(CoderRig.Red + " a"), Report()), Turn(Writes(CoderRig.Green), Report(), items: [Message(), OfType("web_search", ",\"query\":\"\"")]));
        var job = rig.NewJob(route: ["codex/terra"]);

        var (_, ended) = await rig.RunAsync(job);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, RoleStopWords.CoderBoundary), ended.Reason);
        (await rig.GitAsync(rig.Repo, "rev-list", "--count", $"{rig.Base}..coder/{job.JobId}")).Should().Be("1", "turn 0's commit only: the fix turn's work was not committed");
    }

    [Fact]
    public async Task D3_AThreadThatIsNotTheHandles_EndsTheAttemptAsACrash_AndTheNextModelRuns()
    {
        await using var world = await CodexRigWorld.CreateAsync();
        var rig = world.Rig;
        rig.SaysRaw("terra", Turn(Writes(CoderRig.Red + " a"), Report()), Turn(Writes(CoderRig.Green), Report(), OtherThread, rollout: Rollout(OtherThread)))
            .SaysRaw("astra", Turn(Writes(CoderRig.Green), Report(), OtherThread, rollout: Rollout(OtherThread)));
        var job = rig.NewJob(route: Two);

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        rig.Launches(job).Should().HaveCount(3, "the second model ran after the first one's fix turn answered in another thread");
    }

    public static TheoryData<string> Rollouts => ["none", "another thread", "an agent call", "unreadable"];

    [Theory]
    [MemberData(nameof(Rollouts))]
    public async Task D3_ARolloutThatIsMissing_OfAnotherThread_HoldsAnAgentCall_OrCannotBeRead_EndsTheJobCoderBoundary(string kind)
    {
        await using var world = await CodexRigWorld.CreateAsync();
        var rig = world.Rig;
        var home = kind switch
        {
            "none" => new Dictionary<string, string> { [Path.Combine(".codex", "note.txt")] = "no sessions\n" },
            "another thread" => new Dictionary<string, string>([.. Rollout(ThreadId), .. Rollout(OtherThread)]),
            "an agent call" => Rollout(ThreadId, "{\"type\":\"response_item\",\"payload\":{\"type\":\"function_call\",\"name\":\"collaboration.spawn_agent\",\"arguments\":\"{}\"}}"),
            _ => new Dictionary<string, string> { [Path.Combine(".codex", "sessions", "2026", "11", "26", $"rollout-2026-11-26T10-00-00-{ThreadId}.jsonl")] = "this is not json\n" },
        };
        rig.SaysRaw("terra", Turn(Writes(CoderRig.Green), Report(), rollout: home));
        var job = rig.NewJob(route: ["codex/terra"]);

        var (_, ended) = await rig.RunAsync(job);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, RoleStopWords.CoderBoundary), $"{kind}: {ended.Reason}");
        ended.Reason.Should().Contain("CX-T5");
        (await rig.GitAsync(rig.Repo, "rev-list", "--count", $"{rig.Base}..coder/{job.JobId}")).Should().Be("0");
    }

    [Fact]
    public void D3_AFreshLaunchClearsTheRolloutsOfAnEarlierThread_AResumedLaunchKeepsThem_SoTheCheckCanHoldNoFileOfAnotherThread()
    {
        using var dir = TempDir.For("cxs-");
        var job = new BoundaryJob("job-x", new CoderTree(Path.Combine(dir.Path, "wt", "coai-coder-job-x"), Path.Combine(dir.Path, "main"), string.Empty), Path.Combine(dir.Path, "jobs", "job-x"), NetworkChoice.None("t"), NetworkChoice.None("t"), 100);
        var home = CodexCoderVendor.CodexHome(job);
        Directory.CreateDirectory(home);
        Directory.CreateDirectory(job.Tree.Root);
        var host = BoundaryHost.Current() with { RealHome = Path.Combine(dir.Path, "no-real-home"), Path = "/usr/bin:/bin" };
        var rollout = Path.Combine(home, "sessions", "2026", "11", "26", $"rollout-2026-11-26T10-00-00-{OtherThread}.jsonl");
        void Plant() { Directory.CreateDirectory(Path.GetDirectoryName(rollout)!); File.WriteAllText(rollout, "{}\n"); }
        var vendor = CodexCoderVendor.Instance;
        var fresh = new ProcessRequest("/bin/true", vendor.FreshArgv("terra", job.Tree.Root, new CoderLaunchFiles(Path.Combine(dir.Path, "out.txt"))), job.Tree.Root);
        var resumed = new ProcessRequest("/bin/true", vendor.ResumedArgv("terra", job.Tree.Root, ThreadId, new CoderLaunchFiles(Path.Combine(dir.Path, "out.txt"))), job.Tree.Root);

        Plant();
        vendor.PrepareLaunch(host, job, resumed).Problem.Should().BeEmpty();
        File.Exists(rollout).Should().BeTrue("a resumed turn continues a thread that is in the sessions");
        vendor.PrepareLaunch(host, job, fresh).Problem.Should().BeEmpty();
        File.Exists(rollout).Should().BeFalse("a fresh attempt starts with no rollout of an earlier thread");
        Directory.Exists(Path.Combine(home, "sessions")).Should().BeFalse();
        File.Delete(Path.Combine(home, "sessions"));
        File.WriteAllText(Path.Combine(home, "sessions"), "a file where the folder belongs");
        vendor.PrepareLaunch(host, job, fresh).Problem.Should().Contain("sessions").And.Contain("was not launched");
    }

    // ---------- M16 of pass 6: a turn that ended badly is checked too ----------

    [Fact]
    public async Task M16_ATurnThatExitsOneAfterAWebSearch_EndsTheJobCoderBoundary_AndTheNextModelIsNotAsked()
    {
        await using var world = await CodexRigWorld.CreateAsync();
        var rig = world.Rig;
        rig.SaysRaw("terra", Turn(Writes(CoderRig.Green), Report(), items: [Message(), OfType("web_search", ",\"query\":\"\"")], exit: 1, stderr: "the CLI fell over"))
            .SaysRaw("astra", Turn(Writes(CoderRig.Green), Report(), OtherThread, rollout: Rollout(OtherThread)));
        var job = rig.NewJob(route: Two);

        var (_, ended) = await rig.RunAsync(job);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, RoleStopWords.CoderBoundary), ended.Reason);
        rig.Launches(job).Should().ContainSingle("a violation the stream proves is not hidden behind the crash that would have failed over");
    }

    [Fact]
    public async Task M16_ABadItemInAStreamThatNamesNoThread_OnABadEnd_IsABoundary_NotTheCrashThatFailsOver()
    {
        await using var world = await CodexRigWorld.CreateAsync();
        var rig = world.Rig;
        rig.SaysRaw("terra", new Dictionary<string, object?> { ["stdout"] = "{\"type\":\"turn.started\"}\n" + OfType("web_search", ",\"query\":\"\"") + "\n", ["exit"] = 1, ["stderr"] = "the CLI fell over" })
            .SaysRaw("astra", Turn(Writes(CoderRig.Green), Report(), OtherThread, rollout: Rollout(OtherThread)));
        var job = rig.NewJob(route: Two);

        var (_, ended) = await rig.RunAsync(job);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, RoleStopWords.CoderBoundary), ended.Reason);
        rig.Launches(job).Should().ContainSingle();
    }

    [Fact]
    public async Task M16_ATurnThatExitsOneWithASecondRolloutFile_EndsTheJobCoderBoundary()
    {
        await using var world = await CodexRigWorld.CreateAsync();
        var rig = world.Rig;
        rig.SaysRaw("terra", Turn(Writes(CoderRig.Green), Report(), exit: 1, rollout: new Dictionary<string, string>([.. Rollout(ThreadId), .. Rollout(OtherThread)])))
            .SaysRaw("astra", Turn(Writes(CoderRig.Green), Report(), OtherThread, rollout: Rollout(OtherThread)));
        var job = rig.NewJob(route: Two);

        var (_, ended) = await rig.RunAsync(job);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, RoleStopWords.CoderBoundary), ended.Reason);
        rig.Launches(job).Should().ContainSingle();
    }

    [Fact]
    public async Task M16_ATurnThatCrashedWithAnAgentCallInItsRollout_IsSeenBeforeTheNextFreshLaunchDeletesTheFolder()
    {
        await using var world = await CodexRigWorld.CreateAsync();
        var rig = world.Rig;
        rig.SaysRaw("terra", Turn(Writes(CoderRig.Green), Report(), exit: 1, rollout: Rollout(ThreadId, "{\"type\":\"response_item\",\"payload\":{\"type\":\"function_call\",\"name\":\"collaboration.spawn_agent\"}}")))
            .SaysRaw("astra", Turn(Writes(CoderRig.Green), Report(), OtherThread, rollout: Rollout(OtherThread)));
        var job = rig.NewJob(route: Two);

        var (_, ended) = await rig.RunAsync(job);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, RoleStopWords.CoderBoundary), ended.Reason);
        rig.Launches(job).Should().ContainSingle("the second model never started: nothing was deleted unread and nothing was passed");
    }

    [Fact]
    public void S47_AReportFileOfMoreThanAMebibyte_IsNotCopiedToTheHost()
    {
        using var dir = TempDir.For("cxs-");
        var job = new BoundaryJob("job-x", new CoderTree(Path.Combine(dir.Path, "wt", "coai-coder-job-x"), Path.Combine(dir.Path, "main"), string.Empty), Path.Combine(dir.Path, "jobs", "job-x"), NetworkChoice.None("t"), NetworkChoice.None("t"), 100);
        Directory.CreateDirectory(CodexCoderVendor.CodexHome(job));
        Directory.CreateDirectory(CodexCoderVendor.Instance.Home(job).Home);
        Directory.CreateDirectory(job.CoderVendorFolder("codex"));
        var host = Path.Combine(dir.Path, "answer.txt");
        var request = new ProcessRequest("/bin/true", ["exec", "-o", host, "-"], dir.Path);

        File.WriteAllText(CodexCoderVendor.ReportFile(job), "a small report\n");
        CodexCoderVendor.Instance.CollectLaunch(job, request, request);
        File.Exists(host).Should().BeTrue("a control: a small report is copied");

        File.Delete(host);
        File.WriteAllText(CodexCoderVendor.ReportFile(job), new string('x', 1024 * 1024 + 1));
        CodexCoderVendor.Instance.CollectLaunch(job, request, request);
        File.Exists(host).Should().BeFalse("the report is the CLI's own file in a folder its commands could write: its size is bounded before it is read");
    }

    // ---------- M11 leg (a) of the re-check of pass 5: a turn that the job's own budget cuts is checked too ----------

    [Fact]
    public async Task M11a_ATurnThatTheJobsBudgetCuts_AfterAViolation_EndsTheJobCoderBoundary_NotTimeout()
    {
        await using var world = await CodexRigWorld.CreateAsync();
        var rig = world.Rig;
        rig.AttemptTimeout = TimeSpan.FromMinutes(5);
        rig.SaysRaw("terra", Turn(Writes(CoderRig.Green), Report(), items: [Message(), OfType("web_search", ",\"query\":\"\"")], holdAfterMs: 150_000));
        var job = rig.NewJob(route: ["codex/terra"], timeoutMinutes: 1);

        var (_, ended) = await rig.RunAsync(job);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, RoleStopWords.CoderBoundary), ended.Reason);
        rig.Launches(job).Should().ContainSingle();
    }

    [Fact]
    public async Task M11a_AFixTurnThatTheJobsBudgetCuts_AfterAViolation_EndsTheJobCoderBoundary_NotTimeout()
    {
        await using var world = await CodexRigWorld.CreateAsync();
        var rig = world.Rig;
        rig.AttemptTimeout = TimeSpan.FromMinutes(5);
        rig.SaysRaw("terra", Turn(Writes(CoderRig.Red + " a"), Report()), Turn(Writes(CoderRig.Green), Report(), items: [Message(), OfType("web_search", ",\"query\":\"\"")], holdAfterMs: 150_000));
        var job = rig.NewJob(route: ["codex/terra"], timeoutMinutes: 1);

        var (_, ended) = await rig.RunAsync(job);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, RoleStopWords.CoderBoundary), ended.Reason);
        rig.Launches(job).Should().HaveCount(2);
    }

    [Fact]
    public async Task M11a_ACalmTurnThatTheBudgetCuts_StillEndsTheJobTimeout_TheCheckAddsNothingOfItsOwn()
    {
        await using var world = await CodexRigWorld.CreateAsync();
        var rig = world.Rig;
        rig.AttemptTimeout = TimeSpan.FromMinutes(5);
        rig.SaysRaw("terra", Turn(Writes(CoderRig.Green), Report(), holdAfterMs: 150_000));
        var job = rig.NewJob(route: ["codex/terra"], timeoutMinutes: 1);

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Failed);
        ended.Stop.Should().NotBe(RoleStopWords.CoderBoundary, "a stream that proves nothing is not a boundary: the budget's own word stands");
    }

    // ---------- d4: the prompt ----------

    private static string[] LinesOf(string resource) => PromptTemplate.Embedded(resource).Split('\n');

    private static (string[] Before, string[] Middle, string[] After) Around(string[] lines, string start, string next) =>
        (lines.TakeWhile(line => !line.StartsWith(start, StringComparison.Ordinal)).ToArray(), lines.SkipWhile(line => !line.StartsWith(start, StringComparison.Ordinal)).TakeWhile(line => !line.StartsWith(next, StringComparison.Ordinal)).ToArray(), lines.SkipWhile(line => !line.StartsWith(next, StringComparison.Ordinal)).ToArray());

    [Fact]
    public void D4_TheCodexPrompt_DiffersFromClaudesInTheToolsBulletAndOneAddedBullet_EveryOtherLineByteEqual()
    {
        var claude = Around(LinesOf("CoaiMcp.coder-prompt.md"), "- You may use the tools", "- Change only files");
        var codex = Around(LinesOf(CodexCoderVendor.Instance.PromptResource), "- You read files", "- Change only files");

        codex.Before.Should().Equal(claude.Before);
        codex.After.Should().Equal(claude.After);
        var bullets = string.Join('\n', codex.Middle).Split("\n- ", StringSplitOptions.RemoveEmptyEntries).ToList();
        bullets.Should().HaveCount(2, "the tools bullet and ONE added bullet");
        bullets[0].Should().Contain("apply_patch").And.Contain("read-only");
        bullets[1].Should().Contain("Run by coai before your turn").And.Contain("notes");
        claude.Middle.Should().NotBeEmpty("a control: claude's bullet was found");
    }

    [Fact]
    public void D4_TheAddedBulletOfTheCodexPrompt_IsWordForWordTheAddedBulletOfAgys()
    {
        static string Added(string[] lines, string start) => string.Join('\n', lines.SkipWhile(line => !line.StartsWith("- An instruction file", StringComparison.Ordinal)).TakeWhile(line => !line.StartsWith(start, StringComparison.Ordinal)));
        var agy = Added(LinesOf("CoaiMcp.coder-agy-prompt.md"), "- Change only files");
        var codex = Added(LinesOf(CodexCoderVendor.Instance.PromptResource), "- Change only files");

        codex.Should().NotBeEmpty().And.Be(agy, "SPEC C7 item 4: the added bullet is the same for both vendors");
    }

    [Fact]
    public void D4_ClaudesPromptIsStillItsOwnBytes_AndTheCodexPromptNamesOnlyToolsOfTheMeasuredList()
    {
        var claude = string.Join('\n', LinesOf("CoaiMcp.coder-prompt.md"));
        System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(claude)).Should().Equal(Convert.FromHexString("ee31db20b05e779b817131fdb615be1225271dfad715e3b647cfa38d7d92d7bf"), "the claude text stays exactly as it was");
        var middle = Around(LinesOf(CodexCoderVendor.Instance.PromptResource), "- You read files", "- Change only files").Middle;
        var named = Regex.Matches(string.Join(' ', middle), "`([a-z_]+)`").Select(match => match.Groups[1].Value).Where(name => name.Contains('_')).Distinct().ToList();

        named.Should().NotBeEmpty();
        named.Should().OnlyContain(name => CodexToolList.Measured.Contains(name) || name == "notes", "every tool the prompt names is in the measured list of CX-T1");
        string.Join(' ', middle).Should().NotContain("no shell").And.NotContain("coai runs a command").And.NotContain("because coai runs the checks");
    }

    [Fact]
    public void D4_TheCodexFixPromptDoesNotSayThatTheModelHasNoShell_AndClaudesFixPromptIsWhatItWas()
    {
        var codex = CoderPrompt.Fix(1, 3, [], "the build failed", CodexCoderVendor.Instance);
        var claude = CoderPrompt.Fix(1, 3, [], "the build failed", CoderVendors.Claude);

        codex.Should().Contain("you do not run them").And.NotContain("no shell");
        claude.Should().Contain("you have no shell, and you do not run them");
    }

    [Fact]
    public async Task D4_FakeCliAsCodex_RecordsThisPromptOnTurnZero_AndTheCodexFixPromptOnAFixTurn()
    {
        await using var world = await CodexRigWorld.CreateAsync();
        var rig = world.Rig;
        rig.SaysRaw("terra", Turn(Writes(CoderRig.Red + " a"), Report()), Turn(Writes(CoderRig.Green), Report()));
        var job = rig.NewJob(route: ["codex/terra"]);

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        var launches = rig.Launches(job);
        launches[0][^1].Should().Contain("You read files with read-only shell commands").And.Contain("apply_patch").And.NotContain("You have no shell");
        launches[0][^1].Should().Contain("## Run by coai before your turn").And.Contain("Nothing was run", "this repository declares no instruction step, and the prompt points at the block");
        launches[1][^1].Should().Contain("you do not run them").And.NotContain("no shell");
    }
}
