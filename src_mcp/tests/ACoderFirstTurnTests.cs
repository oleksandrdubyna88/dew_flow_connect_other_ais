using System.Text.Json;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E2 s3 t3 — the coder's first turn. The real runner, launcher, worktree, hardened git and report reader; FakeCli stands in for claude and edits files below its working folder, so no model is called and nothing is paid.
/// d1: the envelope (jobId, baseSha, headSha, branch, schemaValid, fixTurns, filesChanged from git alone). d2: ONE coai commit; ONE repair turn in the same session, then schemaValid false; <c>met</c> is a claim, not a green.
/// d3: the generation is compared before the commit and before the result; a cancel stops the child and leaves the worktree.
/// </summary>
[Collection("fakecli-env")]
public sealed class ACoderFirstTurnTests
{
    private const string Session = "00000000-0000-0000-0000-000000000000";

    private static string[] Files(JsonElement envelope) =>
        [.. envelope.GetProperty("filesChanged").EnumerateArray().Select(entry => $"{entry.GetProperty("status").GetString()} {entry.GetProperty("path").GetString()}")];

    private static string Resumed(string[] argv) => argv.SkipWhile(arg => arg != "--resume").Skip(1).FirstOrDefault() ?? string.Empty;

    [Fact]
    public void TheCoderRole_IsRegistered_RoutedAsCoder_WithTheTasksOwnCheck()
    {
        var coder = RoleDescriptors.Find(RoleDescriptors.Shipped, "coder");

        coder.Should().NotBeNull();
        coder!.RouteAs.Should().Be("coder");
        coder.Check.Should().NotBeNull();
        RoleDescriptors.Shipped.Select(descriptor => descriptor.Role).Should().Contain(["probe", "ba", "coder"]);
    }

    // ---------- d1: the envelope ----------

    [Fact]
    public async Task D1_AFakeCoder_EndsSucceeded_WithTheEnvelope_TakenFromGitAlone()
    {
        await using var rig = await CoderRig.CreateAsync();
        rig.Says("sonnet", CoderRig.Edits("src/thing.cs", "src/new/other.cs"), CoderRig.Report(notes: "I also changed ghost.cs and plan/feature.yaml"));
        var job = rig.NewJob();

        var (exit, ended) = await rig.RunAsync(job);

        exit.Should().Be(RoleRunner.Done);
        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        var envelope = rig.Envelope(job);
        envelope.GetProperty("jobId").GetString().Should().Be(job.JobId);
        envelope.GetProperty("baseSha").GetString().Should().Be(rig.Base, "no baseSha in the request: the HEAD of the repository when the work began");
        envelope.GetProperty("branch").GetString().Should().Be($"coder/{job.JobId}");
        envelope.GetProperty("schemaValid").GetBoolean().Should().BeTrue();
        envelope.GetProperty("fixTurns").GetInt32().Should().Be(0);
        var head = envelope.GetProperty("headSha").GetString()!;
        head.Should().NotBe(rig.Base).And.MatchRegex("^[0-9a-f]{40}$");
        (await rig.GitAsync(rig.Repo, "rev-parse", $"refs/heads/coder/{job.JobId}")).Should().Be(head, "the branch of the job is at the commit the envelope names");
        Files(envelope).Should().BeEquivalentTo("A src/new/other.cs", "A src/thing.cs");
        (await rig.GitAsync(rig.Repo, "diff", "--name-status", rig.Base, head)).Split('\n').Select(line => line.Replace('\t', ' ')).Should().BeEquivalentTo(Files(envelope), "the list IS git's list");
        envelope.GetProperty("report").GetProperty("notes").GetString().Should().Contain("ghost.cs", "the report is passed on as the coder wrote it; it decides nothing");
        envelope.GetProperty("report").GetProperty("dod").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task AFileTheReportClaims_ButNobodyChanged_IsNotInFilesChanged()
    {
        await using var rig = await CoderRig.CreateAsync();
        rig.Says("sonnet", CoderRig.Edits("src/thing.cs"), CoderRig.Report(notes: "changed src/ghost.cs, src/tests/phantom.cs"));
        var job = rig.NewJob();

        await rig.RunAsync(job);

        Files(rig.Envelope(job)).Should().Equal("A src/thing.cs");
    }

    [Fact]
    public async Task ABaseShaInTheRequest_IsTheStartOfTheWorktree()
    {
        await using var rig = await CoderRig.CreateAsync();
        var first = rig.Base;
        await rig.Rig.Git.WriteAsync("later.txt", "a later commit\n");
        await rig.Rig.Git.CommitAsync("a later commit");
        rig.Says("sonnet", CoderRig.Edits("src/thing.cs"), CoderRig.Report());
        var job = rig.NewJob(baseSha: first);

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        var envelope = rig.Envelope(job);
        envelope.GetProperty("baseSha").GetString().Should().Be(first).And.NotBe(await rig.Rig.Git.HeadAsync());
        (await rig.GitAsync(rig.Repo, "rev-list", "--count", $"{first}..{envelope.GetProperty("headSha").GetString()}")).Should().Be("1", "only coai's commit lies between the named base and the head");
        File.Exists(Path.Combine(rig.WorktreeOf(job), "later.txt")).Should().BeFalse("the worktree started at the base the request named, not at HEAD");
    }

    [Fact]
    public async Task TheCoder_RunsInItsWorktree_AndTheMainCheckoutIsNotTouched()
    {
        await using var rig = await CoderRig.CreateAsync();
        rig.Says("sonnet", CoderRig.Edits("src/thing.cs"), CoderRig.Report());
        var job = rig.NewJob();
        var before = await CoderTestBed.SnapshotOfAsync(rig.Repo);

        await rig.RunAsync(job);

        File.Exists(Path.Combine(rig.WorktreeOf(job), "src", "thing.cs")).Should().BeTrue("the edit landed in the worktree the coder stood in");
        File.Exists(Path.Combine(rig.Repo, "src", "thing.cs")).Should().BeFalse("and not in the main checkout");
        (await CoderTestBed.SnapshotOfAsync(rig.Repo)).Should().BeEquivalentTo(before);
        var argv = rig.Rig.Launches().Should().ContainSingle().Subject;
        argv.Should().Contain(["--restricted", "acceptEdits"]);
        argv.SkipWhile(arg => arg != "--add-dir").Skip(1).First().Should().EndWith($"coai-coder-{job.JobId}", "the worktree is the only added directory");
    }

    // ---------- d2: one commit, one repair turn, no green ----------

    [Fact]
    public async Task D2_TheChanges_AreOneCoaiCommit_OnTheJobsBranch()
    {
        await using var rig = await CoderRig.CreateAsync();
        rig.Says("sonnet", CoderRig.Edits("src/thing.cs", "src/other.cs"), CoderRig.Report());
        var job = rig.NewJob();

        await rig.RunAsync(job);

        var head = rig.Envelope(job).GetProperty("headSha").GetString()!;
        (await rig.GitAsync(rig.Repo, "rev-list", "--count", $"{rig.Base}..{head}")).Should().Be("1", "ONE commit, however many files");
        (await rig.GitAsync(rig.Repo, "log", "-1", "--format=%s", head)).Should().Contain(job.JobId).And.Contain(CoderRig.TaskId);
        (await rig.GitAsync(rig.WorktreeOf(job), "status", "--porcelain")).Should().BeEmpty("everything the coder wrote is in the commit");
        (await rig.GitAsync(rig.Repo, "rev-parse", "HEAD")).Should().Be(rig.Base, "the main checkout's HEAD never moved");
    }

    [Fact]
    public async Task D2_AnInvalidReport_GetsOneRepairTurn_InTheSameSession_AndThenIsValid()
    {
        await using var rig = await CoderRig.CreateAsync();
        var wrong = """{"status":"ok","dod":[{"id":"d1","met":true,"evidence":"x"},{"id":"d9","met":true,"evidence":"y"}],"notes":""}""";
        rig.Says("sonnet", new CoderRig.Launch(CoderRig.Edits("src/thing.cs"), wrong), new CoderRig.Launch(null, CoderRig.Report()));
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        ended.RepairTurns.Should().Be(1);
        var launches = rig.Rig.Launches();
        launches.Should().HaveCount(2);
        Resumed(launches[0]).Should().BeEmpty("turn 0 is a fresh session");
        Resumed(launches[1]).Should().Be(Session, "the repair turn resumes the session the first turn reported");
        launches[1][^1].Should().Contain("d9").And.Contain("WHOLE JSON report");
        var envelope = rig.Envelope(job);
        envelope.GetProperty("schemaValid").GetBoolean().Should().BeTrue();
        envelope.GetProperty("fixTurns").GetInt32().Should().Be(0, "a repair of the REPORT is not a fix turn of the checks");
        Files(envelope).Should().Equal("A src/thing.cs");
    }

    [Fact]
    public async Task D2_AReportStillInvalidAfterTheOneRepairTurn_IsSchemaValidFalse_AndTheWorkIsStillCommitted()
    {
        await using var rig = await CoderRig.CreateAsync();
        rig.Says("sonnet", new CoderRig.Launch(CoderRig.Edits("src/thing.cs"), "this is prose, not a report"), new CoderRig.Launch(null, """{"status":"ok","dod":[],"notes":""}"""), new CoderRig.Launch(null, CoderRig.Report()));
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        rig.Rig.Launches().Should().HaveCount(2, "ONE repair turn, never a second");
        var envelope = rig.Envelope(job);
        envelope.GetProperty("schemaValid").GetBoolean().Should().BeFalse();
        envelope.GetProperty("report").ValueKind.Should().Be(JsonValueKind.Null);
        Files(envelope).Should().Equal("A src/thing.cs");
        envelope.GetProperty("headSha").GetString().Should().NotBe(rig.Base);
    }

    [Fact]
    public async Task D2_AReportThatClaimsEveryDodMet_IsAClaim_NotAGreen()
    {
        await using var rig = await CoderRig.CreateAsync();
        rig.Says("sonnet", edits: null, CoderRig.Report(met: true));
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, "the job ended; it is not green");
        var envelope = rig.Envelope(job);
        envelope.TryGetProperty("green", out _).Should().BeFalse("green comes from checks (story s4), never from a coder's own claim");
        envelope.GetProperty("report").GetProperty("dod")[0].GetProperty("met").GetBoolean().Should().BeTrue("the claim is shown as a claim");
        envelope.GetProperty("headSha").GetString().Should().Be(rig.Base, "a coder that changed nothing makes no commit");
        Files(envelope).Should().BeEmpty();
    }

    [Fact]
    public async Task ADeclaredRefusal_EndsTheJobFailed_WithNoCommit()
    {
        await using var rig = await CoderRig.CreateAsync();
        rig.Says("sonnet", CoderRig.Edits("src/thing.cs"), """{"status":"refused","reason":"the task asks for something I decline"}""");
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Failed);
        File.Exists(rig.World.Store.FolderOf(job.JobId)!.ResultFile).Should().BeFalse();
        (await rig.GitAsync(rig.Repo, "rev-parse", $"refs/heads/coder/{job.JobId}")).Should().Be(rig.Base, "nothing was committed");
    }

    [Fact]
    public async Task ATaskThatCannotBeRead_IsAFailedJob_BeforeAnyWorktree()
    {
        await using var rig = await CoderRig.CreateAsync();
        rig.Says("sonnet", edits: null, CoderRig.Report());
        var job = rig.World.Store.Create(
            new RoleJobRequest { JobId = RoleJobIds.New(rig.World.Clock), Role = RoleDescriptors.Coder, RepoPath = rig.Repo, TaskFile = CoderRig.TaskFile.Replace("t1-a", "t9-gone"), TimeoutMinutes = 5 },
            new RoleJobPlan(["claude/sonnet"], 4, false));

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Failed);
        ended.Reason.Should().Contain("task.yaml");
        Directory.GetDirectories(rig.Root).Should().BeEmpty("no worktree was made");
        rig.Rig.Launches().Should().BeEmpty();
    }

    // ---------- d3: the generation, the cancel ----------

    [Fact]
    public async Task D3_AStaleRunner_WritesNoCommit_AndNoResult_AndExits75()
    {
        await using var rig = await CoderRig.CreateAsync();
        rig.Says("sonnet", CoderRig.Edits("src/thing.cs"), CoderRig.Report());
        var job = rig.NewJob();

        var (exit, ended) = await rig.RunAsync(job, boundary: point =>
        {
            if (point == "before-commit")
            {
                rig.World.Store.BumpGeneration(job.JobId);
            }
        });

        exit.Should().Be(RoleRunner.NotTheWriter);
        ended.State.Should().Be(RoleJobStates.Running, "the stale runner wrote no terminal state");
        File.Exists(rig.World.Store.FolderOf(job.JobId)!.ResultFile).Should().BeFalse();
        (await rig.GitAsync(rig.Repo, "rev-parse", $"refs/heads/coder/{job.JobId}")).Should().Be(rig.Base, "no commit was made by the stale runner");
        Directory.Exists(rig.WorktreeOf(job)).Should().BeTrue("the worktree stays for the retention sweep");
    }

    [Fact]
    public async Task D3_ARunnerThatTurnsStaleAfterTheCommit_PublishesNoResult()
    {
        await using var rig = await CoderRig.CreateAsync();
        rig.Says("sonnet", CoderRig.Edits("src/thing.cs"), CoderRig.Report());
        var job = rig.NewJob();

        var (exit, _) = await rig.RunAsync(job, boundary: point =>
        {
            if (point == "before-result")
            {
                rig.World.Store.BumpGeneration(job.JobId);
            }
        });

        exit.Should().Be(RoleRunner.NotTheWriter);
        File.Exists(rig.World.Store.FolderOf(job.JobId)!.ResultFile).Should().BeFalse("the generation is compared again before the result");
    }

    [Fact]
    public async Task D3_ACancel_StopsTheCliChild_AndLeavesTheWorktree()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the child is a process started detached on a POSIX host");
        await using var rig = await CoderRig.CreateAsync();
        rig.Says("sonnet", CoderRig.Edits("src/thing.cs"), CoderRig.Report(), sleepMs: 60_000);
        var job = rig.NewJob();
        var running = Task.Run(() => rig.RunAsync(job), TestContext.Current.CancellationToken);
        RoleJobWorld.WaitFor(() => rig.World.Store.Read(job.JobId)!.Child is not null, "the runner to start its CLI child");
        var child = rig.World.Store.Read(job.JobId)!.Child!;
        // The child is recorded the moment it starts; the fake writes its edit a moment later and then sleeps. A cancel before that kills it before it wrote anything.
        RoleJobWorld.WaitFor(() => File.Exists(Path.Combine(rig.WorktreeOf(job), "src", "thing.cs")), "the fake coder to write its edit");

        rig.World.Store.FolderOf(job.JobId)!.RequestCancel();
        var (exit, ended) = await running;

        exit.Should().Be(RoleRunner.Done);
        (ended.State, ended.Stop).Should().Be((RoleJobStates.Cancelled, "cancelled"));
        RoleJobWorld.WaitFor(() => !RoleJobWorld.IsAlive(child), "the CLI child to be gone");
        File.Exists(Path.Combine(rig.WorktreeOf(job), "src", "thing.cs")).Should().BeTrue("the worktree and what the coder wrote stay for the retention sweep");
        (await rig.GitAsync(rig.Repo, "rev-parse", $"refs/heads/coder/{job.JobId}")).Should().Be(rig.Base, "nothing was committed");
        File.Exists(rig.World.Store.FolderOf(job.JobId)!.ResultFile).Should().BeFalse();
    }
}
