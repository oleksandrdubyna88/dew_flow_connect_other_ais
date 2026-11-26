using System.Text.Json;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E2 s4 t1 — after each coder turn coai formats, commits and checks THAT commit, and writes what it saw into the envelope (<c>checks</c>, <c>green</c>). The real runner, worktree, hardened git, OS layer and check boundary; FakeCli stands
/// in for claude and its edits flip a REAL tiny build and test (<see cref="CoderRig.DefaultToolchain"/>: <c>/bin/sh -c</c> steps the fixture repository's own <c>.coai/toolchain.yaml</c> names). No model is called.
/// d1: green with the steps recorded; no toolchain file is <c>not_configured</c>, never green, a failed job. d2: the heartbeat says <c>checks</c> while a step runs, and the step runs inside the boundary. d3: format, commit, then checks of that exact
/// commit; a worktree or a HEAD that moved through the checks is never green.
/// </summary>
[Collection("fakecli-env")]
public sealed class ACoderChecksTests
{
    private static JsonElement Checks(JsonElement envelope) => envelope.GetProperty("checks");

    private static string[] StepsOf(JsonElement envelope) =>
        [.. Checks(envelope).EnumerateArray().Select(step => $"{step.GetProperty("step").GetString()}:{step.GetProperty("exitCode").GetInt32()}")];

    private static CoderRig.Launch Writes(string text, string file = CoderRig.StateFile) =>
        new(new Dictionary<string, string> { [file] = text + "\n" }, CoderRig.Report());

    // ---------- d1: the envelope's checks and green ----------

    [Fact]
    public async Task D1_ATurnWhoseWorkBuildsAndPasses_IsGreen_WithEveryStepRecorded()
    {
        await using var rig = await CoderRig.CreateAsync();
        rig.Says("sonnet", Writes(CoderRig.Green));
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        var envelope = rig.Envelope(job);
        envelope.GetProperty("green").GetBoolean().Should().BeTrue();
        envelope.GetProperty("checksState").GetString().Should().Be("green");
        StepsOf(envelope).Should().Equal("build:0", "test:0");
        foreach (var step in Checks(envelope).EnumerateArray())
        {
            step.GetProperty("argv").EnumerateArray().Select(arg => arg.GetString()).Should().StartWith(["/bin/sh", "-c"], "the argv as it was started, no shell added by coai");
            step.GetProperty("durationMs").GetInt64().Should().BeGreaterThanOrEqualTo(0);
            step.GetProperty("logRef").GetString().Should().NotBeNullOrEmpty();
        }

        var logs = Checks(envelope).EnumerateArray().Select(step => File.ReadAllText(Path.Combine(rig.World.Store.FolderOf(job.JobId)!.Location, step.GetProperty("logRef").GetString()!))).ToArray();
        logs[0].Should().Contain("built");
        logs[1].Should().Contain("tested");
        envelope.GetProperty("fixTurns").GetInt32().Should().Be(0);

        // The record of each check unit, beside its log, as the coder's own are kept.
        var round = Path.Combine(rig.World.Store.FolderOf(job.JobId)!.Location, "checks", "round-0");
        var records = new[] { "s1", "s2" }.Select(part => JsonDocument.Parse(File.ReadAllText(Path.Combine(round, $"unit-{part}.json"))).RootElement).ToArray();
        records.Select(record => record.GetProperty("unit").GetString()).Should().Equal($"coai-check-{job.JobId}-s1", $"coai-check-{job.JobId}-s2");
        records.Select(record => record.GetProperty("step").GetString()!.Split(' ')[^1]).Should().Equal("build", "test");
        records.Should().OnlyContain(record => record.GetProperty("started").GetBoolean() && record.GetProperty("boundary").GetString() == "systemd-run-user-unit");
        foreach (var record in records)
        {
            var properties = record.GetProperty("properties").EnumerateArray().Select(property => property.GetProperty("name").GetString() + "=" + property.GetProperty("value").GetString()).ToList();
            properties.Should().Contain(property => property.StartsWith("TemporaryFileSystem=", StringComparison.Ordinal), "the cover over the job folders is in the record");
            properties.Should().Contain(property => property.StartsWith("InaccessiblePaths=-", StringComparison.Ordinal) && property.Contains("keys", StringComparison.Ordinal), "the key folder is hidden from the check unit");
            record.GetProperty("preflight").EnumerateArray().Where(row => row.GetProperty("item").GetString() is "coder-folder-hidden" or "keys-folder-hidden" or "sign-in-not-in-unit").Select(row => row.GetProperty("held").GetBoolean()).Should().Equal(true, true, true);
        }

        records[1].GetProperty("network").GetString().Should().Be("none", "the test step has a private network");
    }

    [Fact]
    public async Task D1_AGreenThatTheCoderDidNotEarn_IsNotInTheReport_TheChecksDecide()
    {
        await using var rig = await CoderRig.CreateAsync();
        rig.Says("sonnet", new CoderRig.Launch(new Dictionary<string, string> { [CoderRig.StateFile] = CoderRig.Red + "\n" }, CoderRig.Report(met: true)));
        rig.Rig.Setting("COAI_FIX_ROUNDS", "0");
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, "not-green"), "every dod item said met: true, and the test step failed");
        var envelope = rig.Envelope(job);
        envelope.GetProperty("green").GetBoolean().Should().BeFalse();
        envelope.GetProperty("report").GetProperty("dod")[0].GetProperty("met").GetBoolean().Should().BeTrue("the claim stays what it is: a claim");
        StepsOf(envelope).Should().Equal("build:0", "test:1");
    }

    [Fact]
    public async Task D1_ARepositoryWithNoToolchainFile_IsNotConfigured_NeverGreen_AndTheJobFailsWithTheStop()
    {
        await using var rig = await CoderRig.CreateAsync(toolchain: string.Empty);
        rig.Says("sonnet", Writes(CoderRig.Green));
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, "not-configured"));
        ended.Reason.Should().Contain(".coai/toolchain.yaml");
        var envelope = rig.Envelope(job);
        envelope.GetProperty("green").GetBoolean().Should().BeFalse();
        envelope.GetProperty("checksState").GetString().Should().Be("not_configured");
        Checks(envelope).GetArrayLength().Should().Be(0);
        envelope.GetProperty("headSha").GetString().Should().NotBe(rig.Base, "the coder's commit is kept: the branch is the person's to read");
        (await rig.GitAsync(rig.Repo, "rev-parse", $"refs/heads/coder/{job.JobId}")).Should().Be(envelope.GetProperty("headSha").GetString());
        rig.Launches(job).Should().ContainSingle("not configured is not red: no fix turn is made for it");
    }

    [Fact]
    public async Task D1_AToolchainFileThatIsWrong_IsRefused_NeverGreen_AndNothingIsRun()
    {
        await using var rig = await CoderRig.CreateAsync(toolchain: "version: 1\nprojects: not-a-list\n");
        rig.Says("sonnet", Writes(CoderRig.Green));
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, "toolchain-refused"));
        var envelope = rig.Envelope(job);
        envelope.GetProperty("green").GetBoolean().Should().BeFalse();
        envelope.GetProperty("checksState").GetString().Should().Be("toolchain_refused");
        Checks(envelope).GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task D1_AChecksBoundaryThatDoesNotHold_RefusesTheChecks_NeverGreen()
    {
        await using var rig = await CoderRig.CreateAsync();
        var main = await rig.GitAsync(rig.Repo, "rev-parse", "--show-toplevel");
        rig.Degrade = arguments => arguments.Any(argument => argument.StartsWith("coai-check-", StringComparison.Ordinal))
            ? [.. arguments.Select(argument => argument == $"BindReadOnlyPaths={main}" ? $"BindPaths={main}" : argument)]
            : arguments;
        rig.Says("sonnet", Writes(CoderRig.Green));
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, RoleStopWords.CoderBoundary), ended.Reason);
        var envelope = rig.Envelope(job);
        envelope.GetProperty("checksState").GetString().Should().Be("boundary_refused");
        envelope.GetProperty("green").GetBoolean().Should().BeFalse();
        Checks(envelope).GetArrayLength().Should().Be(0, "nothing was run outside a boundary");
        var refusedRecord = JsonDocument.Parse(File.ReadAllText(Path.Combine(rig.World.Store.FolderOf(job.JobId)!.Location, "checks", "round-0", "unit-preflight.json"))).RootElement;
        refusedRecord.GetProperty("started").GetBoolean().Should().BeFalse();
        refusedRecord.GetProperty("preflight").EnumerateArray().Should().Contain(row => !row.GetProperty("held").GetBoolean(), "the row that refused the checks is in the record");
        rig.Launches(job).Should().ContainSingle("the coder was launched; a boundary that did not hold for the checks is not a fault the coder can fix");
    }

    // ---------- d2: the heartbeat, the boundary ----------

    [Fact]
    public async Task D2_TheHeartbeatSaysChecks_WhileABuildOrTestRuns()
    {
        await using var rig = await CoderRig.CreateAsync(toolchain: CoderRig.DefaultToolchain(testTail: "; sleep 6"));
        rig.Says("sonnet", Writes(CoderRig.Green));
        var job = rig.NewJob();

        var running = Task.Run(() => rig.RunAsync(job), TestContext.Current.CancellationToken);
        var seen = new HashSet<string>();
        RoleJobWorld.WaitFor(
            () =>
            {
                seen.Add(rig.World.Store.FolderOf(job.JobId)!.ReadHeartbeat()?.Phase ?? string.Empty);

                return seen.Contains("checks") || running.IsCompleted;
            },
            "the heartbeat to say checks");
        var (_, ended) = await running;

        seen.Should().Contain("checks", "a long build is never a silent stall: the heartbeat names what coai is doing");
        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
    }

    [Fact]
    public async Task D2_TheChecksRunInsideTheBoundary_TheMainCheckoutCannotBeWritten_AndTheHomeIsTheJobs()
    {
        // The main checkout is found from inside the unit: the .git file of a worktree names it.
        var probe = "main=$(sed -e 's/^gitdir: //' -e 's#/[.]git/worktrees/.*##' .git); if echo x > \"$main/written-by-a-check\" 2>/dev/null; then echo WROTE-MAIN; else echo MAIN-REFUSED; fi; echo HOME=$HOME; echo CWD=$(pwd)";
        await using var rig = await CoderRig.CreateAsync(toolchain: ToolchainFixture.Shell("echo built", probe));
        rig.Says("sonnet", Writes(CoderRig.Green));
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        var log = File.ReadAllText(Path.Combine(rig.World.Store.FolderOf(job.JobId)!.Location, Checks(rig.Envelope(job))[1].GetProperty("logRef").GetString()!));
        log.Should().Contain("MAIN-REFUSED").And.NotContain("WROTE-MAIN");
        log.Should().Contain($"CWD={rig.WorktreeOf(job)}", "the step runs in the worktree");
        log.Should().Contain(rig.JobFolder(job), "the unit's HOME is the job's own folder, never the person's");
        File.Exists(Path.Combine(rig.Repo, "written-by-a-check")).Should().BeFalse();
    }

    // ---------- d3: order, and the worktree and HEAD unchanged through the checks ----------

    [Fact]
    public async Task D3_TheChecksRunOnTheCommitCoaiMade_AndTheFormatSlotIsKeptAsNotConfigured()
    {
        await using var rig = await CoderRig.CreateAsync();
        rig.Says("sonnet", Writes(CoderRig.Green));
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        var envelope = rig.Envelope(job);
        var head = envelope.GetProperty("headSha").GetString()!;
        var round = envelope.GetProperty("rounds").EnumerateArray().Should().ContainSingle().Subject;
        round.GetProperty("headSha").GetString().Should().Be(head, "the checks ran on the exact commit the envelope names");
        round.GetProperty("turn").GetInt32().Should().Be(0);
        envelope.GetProperty("format").GetProperty("state").GetString().Should().Be("not_configured", "the toolchain file has no formatter step: the slot of the order is kept and says so");
        (await rig.GitAsync(rig.Repo, "rev-list", "--count", $"{rig.Base}..{head}")).Should().Be("1");
        (await rig.GitAsync(rig.WorktreeOf(job), "status", "--porcelain")).Should().BeEmpty();
    }

    [Fact]
    public async Task D3_ACheckThatChangesATrackedFile_IsNeverGreen_EvenWhenEveryStepExitsZero()
    {
        await using var rig = await CoderRig.CreateAsync(toolchain: CoderRig.DefaultToolchain(testTail: "; echo extra >> src/thing.cs"));
        rig.Says("sonnet", Writes(CoderRig.Green));
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, "checks-changed-tree"));
        ended.Reason.Should().Contain(CoderRig.StateFile, "the sentence names what the checks changed");
        var envelope = rig.Envelope(job);
        envelope.GetProperty("green").GetBoolean().Should().BeFalse();
        StepsOf(envelope).Should().Equal("build:0", "test:0");
        envelope.GetProperty("headSha").GetString().Should().Be(await rig.GitAsync(rig.Repo, "rev-parse", $"refs/heads/coder/{job.JobId}"), "the commit stays; the changed file is left as it is, uncommitted");
    }

    [Fact]
    public async Task D3_ACheckThatLeavesANewFileInTheWorktree_IsNeverGreen()
    {
        await using var rig = await CoderRig.CreateAsync(toolchain: CoderRig.DefaultToolchain(buildTail: "; echo junk > leftover-of-the-build.txt"));
        rig.Says("sonnet", Writes(CoderRig.Green));
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, "checks-changed-tree"));
        ended.Reason.Should().Contain("leftover-of-the-build.txt");
        rig.Envelope(job).GetProperty("green").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task D3_AToolchainTheCoderEdited_IsTheOneThatRuns_AndTheEnvelopeSaysTheTestCommandChanged()
    {
        await using var rig = await CoderRig.CreateAsync();
        var skipEverything = ToolchainFixture.Shell("exit 0", "exit 0");
        rig.Says("sonnet", new CoderRig.Launch(new Dictionary<string, string> { [CoderRig.StateFile] = CoderRig.Red + "\n", [".coai/toolchain.yaml"] = skipEverything }, CoderRig.Report()));
        rig.Rig.Setting("COAI_FIX_ROUNDS", "0");
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, "since the owner's decision of 2026-11-26 the head's own toolchain file is the file that runs: its test command passes, and that is the accepted risk — the envelope says so: " + ended.Reason);
        var envelope = rig.Envelope(job);
        envelope.GetProperty("green").GetBoolean().Should().BeTrue();
        envelope.GetProperty("definitionsChanged").GetBoolean().Should().BeTrue();
        envelope.GetProperty("definitionSummary").GetString().Should().Contain("the toolchain file was changed").And.Contain("the base's tests may have been changed");
        envelope.GetProperty("toolchain").GetProperty("changedTestCommands").EnumerateArray().Select(line => line.GetString()).Should().Contain("the worktree changed the test command of app");
        envelope.GetProperty("checksNote").GetString().Should().Contain("ran the head's file");
        StepsOf(envelope).Should().Equal("build:0", "test:0");
    }
}
