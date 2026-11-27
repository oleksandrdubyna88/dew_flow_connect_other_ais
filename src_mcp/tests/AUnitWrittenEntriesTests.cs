using System.Diagnostics;
using CoaiMcp.Core.Roles;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Toolchain;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;
using static CoaiMcp.Tests.CodexScript;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E3, pass 8, M19 and S55 (the probes P8_1 to P8_R1 of the security verify): the host reads of an entry in a folder that a unit can write — the sign-in copy, the agy records, a codex rollout, <c>report.txt</c>, the folder walk of <c>CodexSessions.Clear</c> — never wait on a FIFO, never follow a link, read a regular file only and
/// within a limit; and an entry of that kind that is not a regular file ends the job <c>coder-boundary</c>. Made-up folders only; the rig cases go through the product's road with a real unit.
/// </summary>
[Collection("fakecli-env")]
public sealed class AUnitWrittenEntriesTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(3);

    private static void MakeFifo(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var made = Process.Start(new ProcessStartInfo("mkfifo", [path]) { UseShellExecute = false })!;
        made.WaitForExit();
        made.ExitCode.Should().Be(0, "mkfifo makes the stand-in");
    }

    /// <summary>The answer of <paramref name="work"/>, or null when it gave none in the time — the shape of a read that waits.</summary>
    private static async Task<T?> WithinAsync<T>(Func<T> work, TimeSpan? time = null) where T : class
    {
        var task = Task.Run(work);

        return await Task.WhenAny(task, Task.Delay(time ?? Wait)) == task ? await task : null;
    }

    // ---------- the helper itself ----------

    [Fact]
    public async Task TheSignInCopy_ReadAsAFifo_GivesNoTokens_InsteadOfWaiting()
    {
        using var temp = TempDir.For("coai-m19-");
        var copy = Path.Combine(temp.Path, "antigravity-oauth-token");
        MakeFifo(copy);
        var recipe = (CoderSignInRecipe.CopyForJob)AgyCoderBed.Vendor().SignIn;

        var tokens = await WithinAsync(() => SignInCopy.TokensOf(recipe, copy));

        tokens.Should().NotBeNull("a read of the sign-in copy never waits on a FIFO").And.BeEmpty();
    }

    [Fact]
    public void TheCheckBudget_IsOneFixedThirtySeconds()
    {
        CoderTurnCheck.CheckBudget.Should().Be(TimeSpan.FromSeconds(30), "a stop for a wait that never ends, not a budget for work, and not scaled with a size");
    }

    [Fact]
    public void TheRecordsAreNotRead_WhenTheStreamAloneProvesABoundary_AndAreRead_WhenItDoesNot()
    {
        var called = false;
        string? Read()
        {
            called = true;

            return string.Empty;
        }

        var foreign = "{\"event\":\"step_update\",\"step_update\":{\"conversation_id\":\"c\",\"step_index\":2,\"state\":\"ACTIVE\",\"step_type\":\"tool\",\"tool_name\":\"run_command\"}}\n";
        AgyTurnCheck.Check(new ProcessResult(0, foreign, string.Empty, false), string.Empty, Read).Should().BeOfType<CoderTurnVerdict.Boundary>();
        called.Should().BeFalse("the stream is judged BEFORE the records are read (pass 7, M18, part 1)");

        AgyTurnCheck.Check(new ProcessResult(0, "{\"event\":\"init\",\"conversation_id\":\"c\",\"init\":{\"permission_mode\":\"request-review\"}}\n", string.Empty, false), string.Empty, Read).Should().BeOfType<CoderTurnVerdict.Fine>();
        called.Should().BeTrue("a stream that proves nothing alone is tied to the records");
    }

    public static TheoryData<string> Entries => new() { "fifo", "link", "folder", "too-big", "missing", "regular" };

    [Theory]
    [MemberData(nameof(Entries))]
    public async Task TheOneRoad_NeverWaits_NeverFollowsALink_ReadsARegularFileWithinItsLimit(string kind)
    {
        using var temp = TempDir.For("coai-m19-");
        var path = Path.Combine(temp.Path, "entry");
        var outside = Path.Combine(temp.Path, "outside.txt");
        File.WriteAllText(outside, "outside\n");
        switch (kind)
        {
            case "fifo": MakeFifo(path); break;
            case "link": File.CreateSymbolicLink(path, outside); break;
            case "folder": Directory.CreateDirectory(path); break;
            case "too-big": File.WriteAllText(path, new string('x', 100)); break;
            case "regular": File.WriteAllText(path, "{}\n"); break;
            default: break;
        }

        var read = await WithinAsync(() => Runners.Files.UnitWrittenFile.Read(path, kind == "too-big" ? 10 : 1024));
        var problem = await WithinAsync(() => Runners.Files.UnitWrittenFile.Problem(path, kind == "too-big" ? 10 : 1024));

        read.Should().NotBeNull($"{kind}: the read never waits");
        problem.Should().NotBeNull($"{kind}: the look never waits");
        (kind switch { "regular" => read is Runners.Files.FileReadResult.Content, "missing" => read is Runners.Files.FileReadResult.Missing, _ => read is Runners.Files.FileReadResult.Unreadable }).Should().BeTrue($"{kind}: {read}");
        (problem!.Length == 0).Should().Be(kind is "regular" or "missing", $"{kind}: {problem}");
    }

    [Fact]
    public async Task TheWalk_NeverFollowsALink_NeverOpensAFifo_AndLimitsItsDepth()
    {
        using var temp = TempDir.For("coai-m19-");
        var root = Path.Combine(temp.Path, "sessions");
        MakeFifo(Path.Combine(root, "a", "rollout-fifo.jsonl"));
        File.WriteAllText(Path.Combine(root, "a", "rollout-file.jsonl"), "{}\n");

        var plain = await WithinAsync(() => Runners.Files.UnitWrittenFile.Walk(root).Files);
        plain.Should().NotBeNull("a FIFO is listed, never opened");
        plain!.Select(Path.GetFileName).Should().BeEquivalentTo(["rollout-fifo.jsonl", "rollout-file.jsonl"]);

        File.CreateSymbolicLink(Path.Combine(root, "b"), temp.Path);
        var linked = Runners.Files.UnitWrittenFile.Walk(root);
        linked.Problem.Should().Contain("link");
        linked.Odd.Should().BeTrue("a link is not what a CLI writes");

        var deep = Path.Combine(temp.Path, "deep");
        Directory.CreateDirectory(Path.Combine([deep, .. Enumerable.Repeat("d", Runners.Files.UnitWrittenFile.MostDepth + 2)]));
        Runners.Files.UnitWrittenFile.Walk(deep).Problem.Should().Contain("deeper");
    }

    [Fact]
    public void ARunThatDoesNotFinish_InTheBound_IsTheAnswerForTimeOut()
    {
        var answer = Runners.Files.UnitWrittenFile.Bounded(() =>
        {
            Thread.Sleep(TimeSpan.FromSeconds(5));

            return "late";
        }, "timed out", TimeSpan.FromMilliseconds(300));

        answer.Should().Be("timed out");
    }

    // ---------- the product road, with a real unit ----------

    [Theory]
    [InlineData("exit")]
    [InlineData("hold")]
    public async Task P8_R1_AFifoMadeAtTheAgySignInCopy_ThroughTheRig_EndsTheJobCoderBoundary(string how)
    {
        await using var rig = await AgyCoderBed.RigAsync();
        rig.AttemptTimeout = TimeSpan.FromSeconds(4);
        rig.SaysEntries(AgyCoderBed.Model, how == "exit" ? AgyCoderBed.Launch([AgyCoderBed.Writes(CoderRig.Green)], exit: 1, fifoSignIn: true) : AgyCoderBed.Launch([AgyCoderBed.Writes(CoderRig.Green)], holdMs: 60_000, fifoSignIn: true))
            .SaysEntries("gemini-4-argon-medium", AgyCoderBed.Launch([AgyCoderBed.Writes(CoderRig.Green)]));
        var job = rig.NewJob(route: [$"agy/{AgyCoderBed.Model}", "agy/gemini-4-argon-medium"]);

        var (_, ended) = await rig.RunAsync(job).WaitAsync(TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, RoleStopWords.CoderBoundary), $"{how}: {ended.Reason}");
        rig.Launches(job).Should().ContainSingle("a boundary ends the job: the next model is never launched");
    }

    public static TheoryData<string> CodexEntries => new() { "rollout-clean", "rollout-cut", "report" };

    [Theory]
    [MemberData(nameof(CodexEntries))]
    public async Task AFifoMadeAtACodexRolloutOrAtTheReportFile_ThroughTheRig_EndsTheJobCoderBoundary(string which)
    {
        await using var world = await CodexRigWorld.CreateAsync();
        var rig = world.Rig;
        var rollout = Path.Combine(".codex", "sessions", "2026", "11", "26", $"rollout-2026-11-26T10-00-00-{ThreadId}.jsonl");
        var turn = (Dictionary<string, object?>)Turn(Writes(CoderRig.Green), Report(), rollout: which == "report" ? null : new Dictionary<string, string>(), exit: which == "rollout-cut" ? 1 : 0);
        if (which == "report")
        {
            turn["outfileFifo"] = "1";
        }
        else
        {
            turn["homeFifos"] = new[] { rollout };
        }

        rig.SaysRaw("terra", turn).SaysRaw("astra", Turn(Writes(CoderRig.Green), Report(), OtherThread, rollout: Rollout(OtherThread)));
        var job = rig.NewJob(route: ["codex/terra", "codex/astra"]);

        var (_, ended) = await rig.RunAsync(job).WaitAsync(TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, RoleStopWords.CoderBoundary), $"{which}: {ended.Reason}");
        ended.Reason.Should().Contain("regular file", $"{which}: the sentence says what was wrong with the entry — not that a check ran out of time (S69, part 1)");
        (await rig.GitAsync(rig.Repo, "rev-list", "--count", $"{rig.Base}..coder/{job.JobId}")).Should().Be("0", "nothing of the turn was committed");
    }

    [Fact]
    public async Task AFreshCodexLaunch_WithAFifoInTheSessions_IsRefused_NeverWaitedOn()
    {
        using var dir = TempDir.For("cxs-");
        var job = new BoundaryJob("job-x", new CoaiMcp.Runners.Worktrees.CoderTree(Path.Combine(dir.Path, "wt", "coai-coder-job-x"), Path.Combine(dir.Path, "main"), string.Empty), Path.Combine(dir.Path, "jobs", "job-x"), NetworkChoice.None("t"), NetworkChoice.None("t"), 100);
        var home = CodexCoderVendor.CodexHome(job);
        Directory.CreateDirectory(job.Tree.Root);
        MakeFifo(Path.Combine(home, "sessions", "2026", "11", "26", $"rollout-2026-11-26T10-00-00-{OtherThread}.jsonl"));
        var host = BoundaryHost.Current() with { RealHome = Path.Combine(dir.Path, "no-real-home"), Path = "/usr/bin:/bin" };
        var vendor = CodexCoderVendor.Instance;
        var fresh = new ProcessRequest("/bin/true", vendor.FreshArgv("terra", job.Tree.Root, new CoderLaunchFiles(Path.Combine(dir.Path, "out.txt"))), job.Tree.Root);

        var prepared = await WithinAsync(() => vendor.PrepareLaunch(host, job, fresh));

        prepared.Should().NotBeNull("the walk of the sessions folder never waits on a FIFO");
        prepared!.Problem.Should().NotBeEmpty("a rollout that is not a regular file refuses the launch");
    }

    [Fact]
    public async Task AFreshCodexLaunch_WithALinkInTheSessions_DoesNotFollowIt_AndIsRefused()
    {
        using var dir = TempDir.For("cxs-");
        var job = new BoundaryJob("job-y", new CoaiMcp.Runners.Worktrees.CoderTree(Path.Combine(dir.Path, "wt", "coai-coder-job-y"), Path.Combine(dir.Path, "main"), string.Empty), Path.Combine(dir.Path, "jobs", "job-y"), NetworkChoice.None("t"), NetworkChoice.None("t"), 100);
        var home = CodexCoderVendor.CodexHome(job);
        Directory.CreateDirectory(job.Tree.Root);
        var outside = Path.Combine(dir.Path, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, $"rollout-2026-11-26T10-00-00-{OtherThread}.jsonl"), "{}\n");
        Directory.CreateDirectory(Path.Combine(home, "sessions"));
        File.CreateSymbolicLink(Path.Combine(home, "sessions", "2026"), outside);
        var host = BoundaryHost.Current() with { RealHome = Path.Combine(dir.Path, "no-real-home"), Path = "/usr/bin:/bin" };
        var vendor = CodexCoderVendor.Instance;
        var fresh = new ProcessRequest("/bin/true", vendor.FreshArgv("terra", job.Tree.Root, new CoderLaunchFiles(Path.Combine(dir.Path, "out.txt"))), job.Tree.Root);

        var prepared = await WithinAsync(() => vendor.PrepareLaunch(host, job, fresh));

        prepared.Should().NotBeNull();
        prepared!.Problem.Should().NotBeEmpty("a link in the sessions is not followed and is not a rollout");
        File.Exists(Path.Combine(outside, $"rollout-2026-11-26T10-00-00-{OtherThread}.jsonl")).Should().BeTrue("what the link points at was never touched");
    }
}
