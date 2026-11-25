using System.Text.Json;
using CoaiMcp.Core.Notices;
using CoaiMcp.Runners.Collecting;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E2 s4 t3 — failover of a coder. A limit, a refusal or a crash of a model — in turn 0 OR in a fix turn — puts the worktree back to the last coai commit (what the model had written and coai had not committed is dropped, and coai proves it with
/// <c>git status</c>, through the hardened door), and the next claude model of the route starts from turn 0 with the task prompt, in a NEW session (a new resume handle, bound to the job and the worktree), with one switch line and one
/// <c>role-model-switched</c> notice. The fix-turn count is the JOB's: a switch gives no new turns. <c>output-invalid</c>, <c>cancelled</c>, <c>not-green</c>, <c>checks-changed-tree</c> and <c>coder-boundary</c> never fail over.
/// The real runner, worktree, hardened git, OS layer and checks; FakeCli stands in for claude. No model is called.
/// d1: the crash, limit and refusal of turn 0, and of a fix turn, and the restore. d2: the count per job; the stops that end the job; the read-only roles are the tests that already exist.
/// </summary>
[Collection("fakecli-env")]
public sealed class ACoderFailoverTests
{
    private static readonly string[] SonnetThenOpus = ["claude/sonnet", "claude/opus"];

    private const string SessionOfTheFirstModel = "11111111-1111-1111-1111-111111111111";

    private const string SessionOfTheSecondModel = "33333333-3333-3333-3333-333333333333";

    /// <summary>A file the first model leaves behind, half written, when it falls over: nobody committed it.</summary>
    private const string Stray = "src/half-written.cs";

    /// <summary>A throttle that clears — the shape the classifier reads as a limit that does NOT end the vendor (the recorded 429 of a spent monthly quota does, and is tested apart).</summary>
    private const string Throttle = "503 UNAVAILABLE: this model is currently experiencing high demand";

    private static string Resumed(string[] argv) => argv.SkipWhile(arg => arg != "--resume").Skip(1).FirstOrDefault() ?? string.Empty;

    private static string ModelOf(string[] argv) => argv[Array.FindIndex(argv, arg => arg is "--model" or "-m") + 1];

    private static CoderRig.Launch Writes(string text, string session = "") =>
        new(new Dictionary<string, string> { [CoderRig.StateFile] = text + "\n" }, CoderRig.Report(), Session: session);

    /// <summary>The model falls over in the middle of its turn: a file of the state broken, a file nobody asked for, and exit 1 with a line on stderr.</summary>
    private static CoderRig.Launch CrashesWithFilesHalfWritten(string session = "") =>
        new(new Dictionary<string, string> { [CoderRig.StateFile] = CoderRig.Broken + " half\n", [Stray] = "half\n" }, string.Empty, Session: session, Exit: 1, Stderr: "the CLI fell over: boom");

    private static string[] Files(JsonElement envelope) =>
        [.. envelope.GetProperty("filesChanged").EnumerateArray().Select(entry => $"{entry.GetProperty("status").GetString()} {entry.GetProperty("path").GetString()}")];

    private static string[] States(JsonElement envelope) =>
        [.. envelope.GetProperty("rounds").EnumerateArray().Select(round => $"{round.GetProperty("turn").GetInt32()}:{round.GetProperty("state").GetString()}")];

    // ---------- d1: the restore, alone ----------

    /// <summary>The identity coai's own commit needs: the hardened door cannot carry a <c>-c user.name</c>, so it comes from the repository's configuration.</summary>
    internal static async Task IdentifyAsync(BoundaryBed bed)
    {
        foreach (var (key, value) in new[] { ("user.email", "t@t"), ("user.name", "t"), ("commit.gpgsign", "false") })
        {
            await bed.Launcher.RunAsync(new ProcessRequest("git", ["config", key, value], bed.Main), TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task D1_TheRestore_DropsEditsOfTrackedFiles_StagedFiles_UntrackedFiles_AndIgnoredOnes_AndStatusProvesIt()
    {
        await using var bed = await BoundaryBed.CreateAsync();
        await IdentifyAsync(bed);
        var git = new GitHistory(bed.Launcher);
        var tracked = Path.Combine(bed.Worktree, "tracked.txt");
        await File.WriteAllTextAsync(tracked, "one\n", TestContext.Current.CancellationToken);
        (await CoderCommits.CommitAsync(git, bed.Tree, "the last coai commit", TestContext.Current.CancellationToken)).Failure.Should().BeNull();
        var last = (await CoderCommits.HeadAsync(git, bed.Tree, TestContext.Current.CancellationToken)).Head;
        await File.WriteAllTextAsync(Path.Combine(bed.Worktree, ".gitignore"), "*.cache\n", TestContext.Current.CancellationToken);
        (await CoderCommits.CommitAsync(git, bed.Tree, "ignore caches", TestContext.Current.CancellationToken)).Failure.Should().BeNull();
        last = (await CoderCommits.HeadAsync(git, bed.Tree, TestContext.Current.CancellationToken)).Head;

        await File.WriteAllTextAsync(tracked, "half written\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(bed.Worktree, "staged.txt"), "staged\n", TestContext.Current.CancellationToken);
        await git.InCoderTreeAsync(bed.Tree, ["add", "staged.txt"], TestContext.Current.CancellationToken);
        Directory.CreateDirectory(Path.Combine(bed.Worktree, "deep", "er"));
        await File.WriteAllTextAsync(Path.Combine(bed.Worktree, "deep", "er", "untracked.txt"), "nobody added me\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(bed.Worktree, "built.cache"), "ignored\n", TestContext.Current.CancellationToken);
        var dirty = await git.InCoderTreeAsync(bed.Tree, ["status", "--porcelain=v1", "-z", "--untracked-files=all", "--ignored"], TestContext.Current.CancellationToken);
        dirty.Out.Should().Contain("tracked.txt").And.Contain("staged.txt").And.Contain("untracked.txt").And.Contain("built.cache", "CONTROL: the tree really is dirty in all four ways");

        var said = await CoderRestore.RestoreAsync(git, bed.Tree, last, TestContext.Current.CancellationToken);

        said.Should().BeEmpty();
        var clean = await git.InCoderTreeAsync(bed.Tree, ["status", "--porcelain=v1", "-z", "--untracked-files=all", "--ignored"], TestContext.Current.CancellationToken);
        clean.Out.Should().BeEmpty("git status, ignored files included, shows nothing: every uncommitted change is gone");
        (await File.ReadAllTextAsync(tracked, TestContext.Current.CancellationToken)).Should().Be("one\n", "the last coai commit is what the tree holds");
        (await CoderCommits.HeadAsync(git, bed.Tree, TestContext.Current.CancellationToken)).Head.Should().Be(last);
        File.Exists(Path.Combine(bed.Worktree, "built.cache")).Should().BeFalse();
    }

    [Fact]
    public async Task D1_TheRestore_GoesBackToAnEarlierCommit_WhenAskedFor_AndLeavesTheBranchAtIt()
    {
        await using var bed = await BoundaryBed.CreateAsync();
        await IdentifyAsync(bed);
        var git = new GitHistory(bed.Launcher);
        await File.WriteAllTextAsync(Path.Combine(bed.Worktree, "a.txt"), "a\n", TestContext.Current.CancellationToken);
        await CoderCommits.CommitAsync(git, bed.Tree, "first", TestContext.Current.CancellationToken);
        var first = (await CoderCommits.HeadAsync(git, bed.Tree, TestContext.Current.CancellationToken)).Head;
        await File.WriteAllTextAsync(Path.Combine(bed.Worktree, "second.txt"), "b\n", TestContext.Current.CancellationToken);
        await CoderCommits.CommitAsync(git, bed.Tree, "second", TestContext.Current.CancellationToken);

        (await CoderRestore.RestoreAsync(git, bed.Tree, first, TestContext.Current.CancellationToken)).Should().BeEmpty();

        (await CoderCommits.HeadAsync(git, bed.Tree, TestContext.Current.CancellationToken)).Head.Should().Be(first);
        File.Exists(Path.Combine(bed.Worktree, "second.txt")).Should().BeFalse();
    }

    [Fact]
    public async Task D1_TheRestore_IsRefused_WhenThePointerOfTheTreeChanged_OrTheCommitIsNotACommit_AndSaysWhy()
    {
        await using var bed = await BoundaryBed.CreateAsync();
        await IdentifyAsync(bed);
        var git = new GitHistory(bed.Launcher);
        var head = (await CoderCommits.HeadAsync(git, bed.Tree, TestContext.Current.CancellationToken)).Head;

        (await CoderRestore.RestoreAsync(git, bed.Tree, "--hard", TestContext.Current.CancellationToken)).Should().Contain("not a commit", "a value that could be an option never reaches git");

        await File.WriteAllTextAsync(Path.Combine(bed.Worktree, ".git"), "gitdir: /somewhere/else\n", TestContext.Current.CancellationToken);
        var said = await CoderRestore.RestoreAsync(git, bed.Tree, head, TestContext.Current.CancellationToken);

        said.Should().Contain(".git pointer", "the hardened door compares the pointer first; nothing was run in a tree git would read through another folder");
    }

    // ---------- d1: the failover of turn 0 ----------

    public static TheoryData<string> TurnZeroStops => new() { "crash", "limit", "refusal" };

    private static CoderRig.Launch FirstModelStops(string how) => how switch
    {
        "crash" => CrashesWithFilesHalfWritten(SessionOfTheFirstModel),
        "limit" => CrashesWithFilesHalfWritten(SessionOfTheFirstModel) with { Stderr = Throttle },
        _ => new CoderRig.Launch(new Dictionary<string, string> { [CoderRig.StateFile] = CoderRig.Broken + " half\n", [Stray] = "half\n" }, """{"status":"refused","reason":"I will not write this: it asks for something I decline"}""", Session: SessionOfTheFirstModel),
    };

    [Theory]
    [MemberData(nameof(TurnZeroStops))]
    public async Task D1_AModelThatStopsInTurnZero_WithFilesHalfWritten_GoesToTheNextModel_FromTheLastCoaiCommit_InANewSession(string how)
    {
        await using var rig = await CoderRig.CreateAsync();
        rig.Says("sonnet", FirstModelStops(how)).Says("opus", new CoderRig.Launch(new Dictionary<string, string> { ["src/other.cs"] = "fine\n" }, CoderRig.Report(), Session: SessionOfTheSecondModel));
        var job = rig.NewJob(route: SonnetThenOpus);

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason + " — the half-written build break of the first model must be gone, or the checks of the second would be red");
        ended.Attempts.Select(attempt => (attempt.Model, attempt.Stop)).Should().Equal(("sonnet", how), ("opus", string.Empty));
        var launches = rig.Launches(job);
        launches.Select(ModelOf).Should().Equal("sonnet", "opus");
        launches.Select(Resumed).Should().OnlyContain(handle => handle.Length == 0, "the second model starts a NEW session: nothing of the first one is resumed");
        launches[1][^1].Should().Be(launches[0][^1], "the second model gets the task prompt of turn 0, the same one");
        var envelope = rig.Envelope(job);
        Files(envelope).Should().Equal(["A src/other.cs"], "what the first model wrote and coai had not committed is not in the commit of the second");
        File.Exists(Path.Combine(rig.WorktreeOf(job), Stray)).Should().BeFalse();
        (await rig.GitAsync(rig.WorktreeOf(job), "status", "--porcelain", "--ignored")).Should().BeEmpty();
        (await rig.GitAsync(rig.Repo, "rev-list", "--count", $"{rig.Base}..refs/heads/coder/{job.JobId}")).Should().Be("1");
        rig.Rig.SwitchLines().Should().ContainSingle().Which.Should().StartWith($"role job {job.JobId} (coder) switched claude/sonnet -> claude/opus after {how}: ");
        rig.Rig.NoticesOf(ServerNoticeCodes.RoleModelSwitched).Should().ContainSingle();
        rig.Rig.LedgerLines().Should().HaveCount(2, "one ledger line for every launch of every model");
    }

    // ---------- d1: the failover of a fix turn ----------

    [Theory]
    [MemberData(nameof(TurnZeroStops))]
    public async Task D1_AModelThatStopsInAFixTurn_GoesToTheNextModel_FromTheLastCoaiCommit_WithTheTaskPrompt_InANewSession(string how)
    {
        await using var rig = await CoderRig.CreateAsync();
        var fixTurn = how switch
        {
            "crash" => CrashesWithFilesHalfWritten(),
            "limit" => CrashesWithFilesHalfWritten() with { Stderr = Throttle },
            _ => new CoderRig.Launch(new Dictionary<string, string> { [CoderRig.StateFile] = CoderRig.Broken + " half\n", [Stray] = "half\n" }, """{"status":"refused","reason":"I decline to fix this"}"""),
        };
        rig.Says("sonnet", Writes(CoderRig.Red + " a", SessionOfTheFirstModel), fixTurn).Says("opus", Writes(CoderRig.Green, SessionOfTheSecondModel));
        var job = rig.NewJob(route: SonnetThenOpus);

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        ended.Attempts.Select(attempt => (attempt.Model, attempt.Stop)).Should().Equal(("sonnet", how), ("opus", string.Empty));
        ended.Attempts[0].Reason.Should().Contain("fix turn 1", "the attempt names the turn it stopped in");
        var launches = rig.Launches(job);
        launches.Select(ModelOf).Should().Equal("sonnet", "sonnet", "opus");
        Resumed(launches[1]).Should().Be(SessionOfTheFirstModel, "CONTROL: the fix turn of the first model resumed its session");
        Resumed(launches[2]).Should().BeEmpty("the second model starts from turn 0 in a new session");
        launches[2][^1].Should().Be(launches[0][^1], "with the task prompt, not the failure digest");
        var envelope = rig.Envelope(job);
        States(envelope).Should().Equal("0:failed", "1:green");
        Files(envelope).Should().Equal("A src/thing.cs");
        File.Exists(Path.Combine(rig.WorktreeOf(job), Stray)).Should().BeFalse("the file the fix turn left half written was dropped with the rest of what coai had not committed");
        (await rig.GitAsync(rig.Repo, "rev-list", "--count", $"{rig.Base}..refs/heads/coder/{job.JobId}")).Should().Be("2", "the first model's turn 0 commit stays; the crashed fix turn made none; the second model's turn 0 made one");
        rig.Rig.SwitchLines().Should().ContainSingle();
        rig.Rig.NoticesOf(ServerNoticeCodes.RoleModelSwitched).Should().ContainSingle();
    }

    // ---------- d2: the count is the job's ----------

    [Fact]
    public async Task D2_TheFixTurnCountIsPerJob_ASwitchGivesNoNewTurns_AndTheSecondModelResumesItsOwnSession()
    {
        await using var rig = await CoderRig.CreateAsync();
        rig.Says("sonnet", Writes(CoderRig.Red + " 0", SessionOfTheFirstModel), Writes(CoderRig.Red + " 1"), CrashesWithFilesHalfWritten())
            .Says("opus", Writes(CoderRig.Red + " 3", SessionOfTheSecondModel), Writes(CoderRig.Red + " 4"), Writes(CoderRig.Red + " 5"), Writes(CoderRig.Red + " 6"));
        var job = rig.NewJob(route: SonnetThenOpus);

        var (_, ended) = await rig.RunAsync(job);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, "not-green"));
        var launches = rig.Launches(job);
        launches.Select(ModelOf).Should().Equal("sonnet", "sonnet", "sonnet", "opus", "opus");
        Resumed(launches[4]).Should().Be(SessionOfTheSecondModel, "the second model's fix turn resumes the session the SECOND model started");
        var envelope = rig.Envelope(job);
        envelope.GetProperty("fixTurns").GetInt32().Should().Be(3, "the cap is the job's: two turns of the first model, one of the second; a per-model count would have made it six");
        envelope.GetProperty("fixRounds").GetProperty("cap").GetInt32().Should().Be(3);
        States(envelope).Should().Equal("0:failed", "1:failed", "2:failed", "3:failed");
    }

    // ---------- d2: what never fails over ----------

    [Fact]
    public async Task D2_NotGreen_NeverFailsOver_TheFirstModelsFixTurnsAreAllTheJobGets()
    {
        await using var rig = await CoderRig.CreateAsync();
        rig.Says("sonnet", Writes(CoderRig.Red, SessionOfTheFirstModel)).Says("opus", Writes(CoderRig.Green, SessionOfTheSecondModel));
        var job = rig.NewJob(route: SonnetThenOpus);

        var (_, ended) = await rig.RunAsync(job);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, "not-green"));
        rig.Launches(job).Select(ModelOf).Should().OnlyContain(model => model == "sonnet", "a red job is not a model that stopped");
        rig.Rig.NoticesOf(ServerNoticeCodes.RoleModelSwitched).Should().BeEmpty();
    }

    [Fact]
    public async Task D2_ACheckThatChangedTheTree_NeverFailsOver()
    {
        await using var rig = await CoderRig.CreateAsync(toolchain: CoderRig.DefaultToolchain(testTail: "; echo extra >> src/thing.cs"));
        rig.Says("sonnet", Writes(CoderRig.Green, SessionOfTheFirstModel)).Says("opus", Writes(CoderRig.Green, SessionOfTheSecondModel));
        var job = rig.NewJob(route: SonnetThenOpus);

        var (_, ended) = await rig.RunAsync(job);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, "checks-changed-tree"));
        rig.Launches(job).Should().ContainSingle();
    }

    [Fact]
    public async Task D2_AWriteTheLayerStopped_NeverFailsOver_NotInTurnZero()
    {
        await using var rig = await CoderRig.CreateAsync();
        var inMain = Path.Combine(rig.Repo, "written-by-the-first-model.txt");
        rig.Says("sonnet", new CoderRig.Launch(CoderRig.Edits("src/thing.cs"), CoderRig.Report(), TryWrites: [inMain])).Says("opus", Writes(CoderRig.Green));
        var job = rig.NewJob(route: SonnetThenOpus);

        var (_, ended) = await rig.RunAsync(job);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, RoleStopWords.CoderBoundary));
        rig.Launches(job).Should().ContainSingle();
    }

    [Fact]
    public async Task D2_AWriteTheLayerStopped_NeverFailsOver_NotInAFixTurn()
    {
        await using var rig = await CoderRig.CreateAsync();
        var inMain = Path.Combine(rig.Repo, "written-by-a-fix-turn.txt");
        rig.Says("sonnet", Writes(CoderRig.Red, SessionOfTheFirstModel), new CoderRig.Launch(CoderRig.Edits("src/thing.cs"), CoderRig.Report(), TryWrites: [inMain])).Says("opus", Writes(CoderRig.Green));
        var job = rig.NewJob(route: SonnetThenOpus);

        var (_, ended) = await rig.RunAsync(job);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, RoleStopWords.CoderBoundary));
        rig.Launches(job).Select(ModelOf).Should().Equal("sonnet", "sonnet");
    }

    [Fact]
    public async Task D2_AnAnswerThatCannotBeRepaired_IsOutputInvalid_AndNeverFailsOver()
    {
        await using var rig = await CoderRig.CreateAsync();
        // A session the vendor did not name properly: the report is wrong, the repair turn cannot be made in the same conversation, and the job ends output-invalid.
        rig.Says("sonnet", new CoderRig.Launch(CoderRig.Edits("src/thing.cs"), "this is not a report", Session: "bad handle!")).Says("opus", Writes(CoderRig.Green));
        var job = rig.NewJob(route: SonnetThenOpus);

        var (_, ended) = await rig.RunAsync(job);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, RoleStopWords.OutputInvalid));
        rig.Launches(job).Should().ContainSingle();
    }

    [Fact]
    public async Task D2_ACancel_NeverFailsOver_TheNextModelIsNotStarted()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the child is a process started detached on a POSIX host");
        await using var rig = await CoderRig.CreateAsync();
        rig.Says("sonnet", CoderRig.Edits("src/thing.cs"), CoderRig.Report(), sleepMs: 60_000).Says("opus", Writes(CoderRig.Green));
        var job = rig.NewJob(route: SonnetThenOpus);
        var running = Task.Run(() => rig.RunAsync(job), TestContext.Current.CancellationToken);
        RoleJobWorld.WaitFor(() => rig.World.Store.Read(job.JobId)!.Child is not null, "the runner to start its CLI child");
        RoleJobWorld.WaitFor(() => File.Exists(Path.Combine(rig.WorktreeOf(job), "src", "thing.cs")), "the fake coder to write its edit");

        rig.World.Store.FolderOf(job.JobId)!.RequestCancel();
        var (_, ended) = await running;

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Cancelled, "cancelled"));
        rig.Launches(job).Should().ContainSingle();
        rig.Rig.NoticesOf(ServerNoticeCodes.RoleModelSwitched).Should().BeEmpty();
    }

    [Fact]
    public async Task D2_ASpentQuota_EndsTheVendorForEveryModelOfIt_SoTheSecondClaudeModelIsSkipped_AndTheJobFailsModelsExhausted()
    {
        await using var rig = await CoderRig.CreateAsync();
        rig.Says("sonnet", CrashesWithFilesHalfWritten(SessionOfTheFirstModel) with { Stdout = RoleStopFixtures.Load("claude-spend-limit-429-envelope").StdOut }).Says("opus", Writes(CoderRig.Green, SessionOfTheSecondModel));
        var job = rig.NewJob(route: SonnetThenOpus);

        var (_, ended) = await rig.RunAsync(job);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, "models-exhausted"));
        ended.Reason.Should().Contain("monthly spend limit").And.Contain("skipped");
        rig.Launches(job).Select(ModelOf).Should().Equal("sonnet");
    }

    [Fact]
    public async Task D2_WhenEveryModelStops_TheJobFailsModelsExhausted_NamingEachAttempt_AndTheBranchIsLeftAtTheLastCoaiCommit()
    {
        await using var rig = await CoderRig.CreateAsync();
        rig.Says("sonnet", Writes(CoderRig.Red + " a", SessionOfTheFirstModel), CrashesWithFilesHalfWritten()).Says("opus", CrashesWithFilesHalfWritten());
        var job = rig.NewJob(route: SonnetThenOpus);

        var (_, ended) = await rig.RunAsync(job);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, "models-exhausted"));
        ended.Reason.Should().Contain("claude/sonnet").And.Contain("claude/opus");
        ended.Attempts.Select(attempt => attempt.Stop).Should().Equal("crash", "crash");
        (await rig.GitAsync(rig.Repo, "rev-list", "--count", $"{rig.Base}..refs/heads/coder/{job.JobId}")).Should().Be("1", "the commit of turn 0 is kept");
    }
}
