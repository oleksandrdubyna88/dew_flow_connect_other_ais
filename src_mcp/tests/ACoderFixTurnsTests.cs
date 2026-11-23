using System.Text.Json;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E2 s4 t2 — fix turns. When the checks of a coder job are red, the failure digest goes back to the SAME claude session (<c>--resume</c> with the handle of turn 0, bound to the job and the worktree), up to <c>COAI_FIX_ROUNDS</c> times (default 3);
/// every fix turn is one coai commit and is checked again; the whole job stays inside <c>timeoutMinutes</c>. The real runner, worktree, hardened git, OS layer and check boundary; FakeCli stands in for claude and its edits flip a REAL tiny build and test
/// (<see cref="CoderRig.DefaultToolchain"/>). No model is called.
/// d1: red, then green after two fix turns, in one session. d2: always red stops at the cap with the last digest; the setting is read by one rule. d3: a turn or a check that passes the budget is stopped and the job ends <c>timeout</c> with its commits kept.
/// </summary>
[Collection("fakecli-env")]
public sealed class ACoderFixTurnsTests
{
    private const string SessionOfTurnZero = "11111111-1111-1111-1111-111111111111";

    private const string SessionOfAFixTurn = "22222222-2222-2222-2222-222222222222";

    private static string Resumed(string[] argv) => argv.SkipWhile(arg => arg != "--resume").Skip(1).FirstOrDefault() ?? string.Empty;

    private static CoderRig.Launch Writes(string text, string session = "") =>
        new(new Dictionary<string, string> { [CoderRig.StateFile] = text + "\n" }, CoderRig.Report(), Session: session);

    private static string[] States(JsonElement envelope) =>
        [.. envelope.GetProperty("rounds").EnumerateArray().Select(round => $"{round.GetProperty("turn").GetInt32()}:{round.GetProperty("state").GetString()}")];

    /// <summary>Waits, bounded, until a unit of the job that <paramref name="match"/> accepts is loaded.</summary>
    private static async Task WaitForAUnitAsync(CoderRig rig, RoleJobRecord job, Func<string, bool> match, string what)
    {
        var until = DateTime.UtcNow.AddSeconds(30);
        while (!(await rig.UnitsOfAsync(job)).Any(match))
        {
            if (DateTime.UtcNow > until)
            {
                throw new TimeoutException($"timed out waiting for {what}");
            }

            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
    }

    // ---------- d1: red, then green after two fix turns, in the one session ----------

    [Fact]
    public async Task D1_RedThenGreenAfterTwoFixTurns_BothFixTurnsResumeTheSessionOfTurnZero_InTheWorktree()
    {
        await using var rig = await CoderRig.CreateAsync();
        rig.Says("sonnet", Writes(CoderRig.Red + " a", SessionOfTurnZero), Writes(CoderRig.Red + " b", SessionOfAFixTurn), Writes(CoderRig.Green, SessionOfAFixTurn));
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        var launches = rig.Launches(job);
        launches.Should().HaveCount(3);
        Resumed(launches[0]).Should().BeEmpty("turn 0 starts the session");
        Resumed(launches[1]).Should().Be(SessionOfTurnZero);
        Resumed(launches[2]).Should().Be(SessionOfTurnZero, "the handle of turn 0 on EVERY fix turn: the session the turn before answered with (another id here) is not the one that is resumed");
        launches.Skip(1).Should().OnlyContain(argv => argv.SkipWhile(arg => arg != "--add-dir").Skip(1).First().EndsWith($"coai-coder-{job.JobId}", StringComparison.Ordinal), "the worktree is the one added directory of every fix turn, as of turn 0");
        launches.Should().OnlyContain(argv => argv.Contains("--restricted") && argv.Contains("acceptEdits"), "a fix turn is the same write launch as turn 0, no new session and no other flags");
        var envelope = rig.Envelope(job);
        envelope.GetProperty("fixTurns").GetInt32().Should().Be(2);
        envelope.GetProperty("green").GetBoolean().Should().BeTrue();
        States(envelope).Should().Equal("0:failed", "1:failed", "2:green");
        File.ReadAllText(Path.Combine(rig.WorktreeOf(job), CoderRig.StateFile)).Should().Contain(CoderRig.Green, "the fix turns wrote into the worktree they stood in");
    }

    [Fact]
    public async Task D1_TheFailureDigestIsThePrompt_AndEveryTurnIsOneCoaiCommit_CheckedOnItsOwnHead()
    {
        await using var rig = await CoderRig.CreateAsync();
        rig.Says("sonnet", Writes(CoderRig.Red + " a"), Writes(CoderRig.Red + " b"), Writes(CoderRig.Green));
        var job = rig.NewJob();

        await rig.RunAsync(job);

        var launches = rig.Launches(job);
        var prompt = launches[1][^1];
        prompt.Should().Contain("\"step\":\"test\"").And.Contain("the state is red", "the digest is what the failed step printed, as {step,file,line,code,message}");
        prompt.Should().Contain("WHOLE JSON report", "a fix turn answers with the report again, as the repair turn does");
        prompt.Should().NotContain(CoderRig.Green);
        var envelope = rig.Envelope(job);
        var head = envelope.GetProperty("headSha").GetString()!;
        (await rig.GitAsync(rig.Repo, "rev-list", "--count", $"{rig.Base}..{head}")).Should().Be("3", "one coai commit per turn: turn 0 and each fix turn");
        (await rig.GitAsync(rig.Repo, "log", "--format=%s", $"{rig.Base}..{head}")).Split('\n').Should().Equal($"coder {job.JobId}: {CoderRig.TaskId} fix 2", $"coder {job.JobId}: {CoderRig.TaskId} fix 1", $"coder {job.JobId}: {CoderRig.TaskId}");
        var checkedHeads = envelope.GetProperty("rounds").EnumerateArray().Select(round => round.GetProperty("headSha").GetString()).ToArray();
        checkedHeads.Should().OnlyHaveUniqueItems("each round checked the commit of its own turn").And.EndWith(head);
        (await rig.GitAsync(rig.WorktreeOf(job), "status", "--porcelain")).Should().BeEmpty();
        rig.Rig.LedgerLines().Should().HaveCount(3, "every launch of the model is a line of the usage ledger, the fix turns included");
    }

    [Fact]
    public async Task D1_AGreenFirstTurn_MakesNoFixTurn()
    {
        await using var rig = await CoderRig.CreateAsync();
        rig.Says("sonnet", Writes(CoderRig.Green));
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        rig.Launches(job).Should().ContainSingle();
        rig.Envelope(job).GetProperty("fixTurns").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task D1_AFixTurnWhoseAnswerIsNotAReport_KeepsTheReportOfTurnZero_TheChecksAreTheVerdict()
    {
        await using var rig = await CoderRig.CreateAsync();
        rig.Says("sonnet", Writes(CoderRig.Red), new CoderRig.Launch(new Dictionary<string, string> { [CoderRig.StateFile] = CoderRig.Green + "\n" }, "I fixed it, trust me"));
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        rig.Launches(job).Should().HaveCount(2, "no repair turn is spent on a fix turn's report");
        var envelope = rig.Envelope(job);
        envelope.GetProperty("green").GetBoolean().Should().BeTrue();
        envelope.GetProperty("schemaValid").GetBoolean().Should().BeTrue();
        envelope.GetProperty("report").ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Fact]
    public async Task D1_AFixTurn_GoesThroughTheCoderOsLayer_LikeTurnZero()
    {
        await using var rig = await CoderRig.CreateAsync();
        var inMain = Path.Combine(rig.Repo, "written-by-a-fix-turn.txt");
        rig.Says("sonnet", Writes(CoderRig.Red), new CoderRig.Launch(new Dictionary<string, string> { [CoderRig.StateFile] = CoderRig.Green + "\n" }, CoderRig.Report(), TryWrites: [inMain]));
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, RoleStopWords.CoderBoundary), "a fix turn the layer had to stop did not do its work");
        ended.Reason.Should().Contain(inMain);
        File.Exists(inMain).Should().BeFalse("the main checkout is read-only to a fix turn, as to turn 0");
        File.ReadAllText(Path.Combine(rig.WorktreeOf(job), CoderRig.StateFile)).Should().Contain(CoderRig.Green, "CONTROL: the same fix turn's write into its worktree landed");
        rig.Launches(job).Should().HaveCount(2);
    }

    // ---------- d2: the cap, the setting ----------

    [Fact]
    public async Task D2_AlwaysRed_IsExactlyThreeFixTurns_ThenFailedNotGreen_WithTheLastDigestInTheResult()
    {
        await using var rig = await CoderRig.CreateAsync();
        rig.Says("sonnet", Writes(CoderRig.Red + " 0"), Writes(CoderRig.Red + " 1"), Writes(CoderRig.Red + " 2"), Writes(CoderRig.Red + " 3"), Writes(CoderRig.Red + " 4"));
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, "not-green"));
        rig.Launches(job).Should().HaveCount(4, "turn 0 and exactly three fix turns, never a fourth");
        ended.Reason.Should().Contain("3 fix turn").And.Contain("the state is red");
        var envelope = rig.Envelope(job);
        envelope.GetProperty("fixTurns").GetInt32().Should().Be(3);
        envelope.GetProperty("green").GetBoolean().Should().BeFalse();
        States(envelope).Should().Equal("0:failed", "1:failed", "2:failed", "3:failed");
        envelope.GetProperty("lastDigest").EnumerateArray().Select(item => item.GetProperty("message").GetString()).Should().Contain(message => message!.Contains("the state is red"));
        envelope.GetProperty("fixRounds").GetProperty("cap").GetInt32().Should().Be(3);
        (await rig.GitAsync(rig.Repo, "rev-list", "--count", $"{rig.Base}..refs/heads/coder/{job.JobId}")).Should().Be("4", "the branch and every commit of every turn are kept");
        envelope.GetProperty("headSha").GetString().Should().Be(await rig.GitAsync(rig.Repo, "rev-parse", $"refs/heads/coder/{job.JobId}"));
    }

    [Theory]
    [InlineData("1", 2)]
    [InlineData("2", 3)]
    [InlineData("0", 1)]
    public async Task D2_TheCapIsTheSetting_FixTurnsAreNeverMoreThanIt(string setting, int launchesExpected)
    {
        await using var rig = await CoderRig.CreateAsync();
        rig.Says("sonnet", Writes(CoderRig.Red + " 0"), Writes(CoderRig.Red + " 1"), Writes(CoderRig.Red + " 2"), Writes(CoderRig.Red + " 3"), Writes(CoderRig.Red + " 4"));
        rig.Rig.Setting("COAI_FIX_ROUNDS", setting);
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, "not-green"));
        rig.Launches(job).Should().HaveCount(launchesExpected);
        var envelope = rig.Envelope(job);
        envelope.GetProperty("fixTurns").GetInt32().Should().Be(launchesExpected - 1);
        envelope.GetProperty("fixRounds").GetProperty("cap").GetInt32().Should().Be(int.Parse(setting, System.Globalization.CultureInfo.InvariantCulture));
        envelope.GetProperty("fixRounds").GetProperty("note").GetString().Should().BeEmpty();
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-1")]
    [InlineData("11")]
    [InlineData("2.5")]
    [InlineData("1e1")]
    [InlineData("99999999999")]
    public async Task D2_ASettingThatIsNotAWholeNumberFromZeroToTen_FallsBackToThree_WithANote(string setting)
    {
        await using var rig = await CoderRig.CreateAsync();
        rig.Says("sonnet", Writes(CoderRig.Red + " 0"), Writes(CoderRig.Red + " 1"), Writes(CoderRig.Red + " 2"), Writes(CoderRig.Red + " 3"), Writes(CoderRig.Red + " 4"));
        rig.Rig.Setting("COAI_FIX_ROUNDS", setting);
        var job = rig.NewJob();

        await rig.RunAsync(job);

        rig.Launches(job).Should().HaveCount(4, "the fallback is three fix turns");
        var cap = rig.Envelope(job).GetProperty("fixRounds");
        cap.GetProperty("cap").GetInt32().Should().Be(3);
        cap.GetProperty("note").GetString().Should().Contain("COAI_FIX_ROUNDS").And.Contain($"'{setting}'").And.Contain("0 to 10").And.Contain("3");
    }

    [Theory]
    [InlineData(null, 3, "")]
    [InlineData("", 3, "")]
    [InlineData("   ", 3, "")]
    [InlineData("0", 0, "")]
    [InlineData("10", 10, "")]
    [InlineData(" 4 ", 4, "")]
    [InlineData("007", 7, "")]
    [InlineData("11", 3, "COAI_FIX_ROUNDS is '11'; it must be a whole number from 0 to 10, so the default of 3 fix turns is used")]
    [InlineData("-1", 3, "COAI_FIX_ROUNDS is '-1'; it must be a whole number from 0 to 10, so the default of 3 fix turns is used")]
    [InlineData("+2", 3, "COAI_FIX_ROUNDS is '+2'; it must be a whole number from 0 to 10, so the default of 3 fix turns is used")]
    [InlineData("1_0", 3, "COAI_FIX_ROUNDS is '1_0'; it must be a whole number from 0 to 10, so the default of 3 fix turns is used")]
    [InlineData("٣", 3, "COAI_FIX_ROUNDS is '٣'; it must be a whole number from 0 to 10, so the default of 3 fix turns is used")]
    public void D2_TheOneRuleThatReadsTheSetting(string? text, int cap, string note)
    {
        var read = FixRounds.Read(name => name == "COAI_FIX_ROUNDS" ? text : null);

        (read.Cap, read.Note).Should().Be((cap, note));
    }

    [Fact]
    public void D2_TheSettingReachesTheDetachedRunner_ByItsName()
    {
        var source = new System.Collections.Hashtable { ["COAI_FIX_ROUNDS"] = "2", ["COAI_SOMETHING_ELSE"] = "x" };

        RoleRunEnvironment.Capture(source).Variables.Keys.Should().BeEquivalentTo(["COAI_FIX_ROUNDS"], "the runner is started with its own environment and takes only the names the allow-list holds");
    }

    // ---------- d3: the budget ----------

    [Fact]
    public async Task D3_ACheckThatWouldPassTheBudget_IsStopped_TheJobEndsTimeout_TheCommitIsKept()
    {
        await using var rig = await CoderRig.CreateAsync(toolchain: CoderRig.DefaultToolchain(testTail: "; sleep 60"));
        var clock = new ManualClock(new DateTimeOffset(2026, 11, 23, 9, 0, 0, TimeSpan.Zero));
        rig.Says("sonnet", Writes(CoderRig.Green));
        var job = rig.NewJob(timeoutMinutes: 1);

        var running = Task.Run(() => rig.RunAsync(job, clock: clock), TestContext.Current.CancellationToken);
        await WaitForAUnitAsync(rig, job, unit => unit.StartsWith("coai-check-", StringComparison.Ordinal) && unit.EndsWith("-s2.service", StringComparison.Ordinal), "the test step to be running in its unit");
        await clock.WhenTimersAsync(2);

        clock.Advance(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(1));
        var (_, ended) = await running.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, "timeout"), "the job ends when the budget does: a step or a turn that is left to run its sleep ends it a minute late");
        ended.Reason.Should().Contain("1 minute");
        (await rig.GitAsync(rig.Repo, "rev-parse", $"refs/heads/coder/{job.JobId}")).Should().NotBe(rig.Base, "the branch and its commit are kept");
        Directory.Exists(rig.WorktreeOf(job)).Should().BeTrue();
        (await rig.UnitsOfAsync(job)).Should().BeEmpty("the step's unit was stopped by name when the budget ended, not left to run its sleep");
    }

    [Fact]
    public async Task D3_AFixTurnThatWouldPassTheBudget_IsStopped_TheJobEndsTimeout_TheCommitsAreKept()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the child is a process started detached on a POSIX host");
        await using var rig = await CoderRig.CreateAsync();
        var clock = new ManualClock(new DateTimeOffset(2026, 11, 23, 9, 0, 0, TimeSpan.Zero));
        rig.Says("sonnet", Writes(CoderRig.Red), new CoderRig.Launch(new Dictionary<string, string> { [CoderRig.StateFile] = CoderRig.Green + "\n" }, CoderRig.Report(), SleepMs: 60_000));
        var job = rig.NewJob(timeoutMinutes: 1);

        var running = Task.Run(() => rig.RunAsync(job, clock: clock), TestContext.Current.CancellationToken);
        RoleJobWorld.WaitFor(() => rig.Launches(job).Count == 2 && rig.World.Store.Read(job.JobId)!.Child is not null, "the fix turn to be running");
        var child = rig.World.Store.Read(job.JobId)!.Child!;
        await clock.WhenTimersAsync(2);

        clock.Advance(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(1));
        var (_, ended) = await running.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, "timeout"), "the job ends when the budget does: a step or a turn that is left to run its sleep ends it a minute late");
        RoleJobWorld.WaitFor(() => !RoleJobWorld.IsAlive(child), "the fix turn's CLI child to be gone");
        (await rig.GitAsync(rig.Repo, "rev-list", "--count", $"{rig.Base}..refs/heads/coder/{job.JobId}")).Should().Be("1", "turn 0's commit is kept; the fix turn was stopped before it could be committed");
        (await rig.UnitsOfAsync(job)).Should().BeEmpty();
    }
}
