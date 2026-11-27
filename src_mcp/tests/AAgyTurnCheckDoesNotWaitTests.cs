using System.Diagnostics;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Roles;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Runners.Toolchain;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E3, pass 7, M18: a FIFO (or any entry that is not a regular file) at the name of the guard's records — a name inside the folder the agy unit can write — must not stop the check of the turn. The stream is judged first; an entry that is not a regular file is never opened to wait on;
/// and EVERY vendor's check has a time limit, with <c>coder-boundary</c> as the answer when it does not finish. Made-up folders only.
/// </summary>
[Collection("fakecli-env")]
public sealed class AAgyTurnCheckDoesNotWaitTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(3);

    private static string Event(string state, string tool) =>
        $"{{\"event\":\"step_update\",\"step_update\":{{\"conversation_id\":\"c\",\"step_index\":2,\"state\":\"{state}\",\"step_type\":\"tool\",\"tool_name\":\"{tool}\"}}}}\n";

    private static ProcessResult Turn(string stdout, int exit, bool timedOut = false) => new(exit, stdout, string.Empty, timedOut);

    private static BoundaryJob Job(string root) =>
        BoundaryJob.ForCoder("r20261127101010-0000000b", new CoaiMcp.Runners.Worktrees.CoderTree(Path.Combine(root, "roots", "coai-coder-r20261127101010-0000000b"), Path.Combine(root, "main"), "gitdir: x"), Path.Combine(root, "jobs", "r20261127101010-0000000b"));

    private static void MakeFifo(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var made = Process.Start(new ProcessStartInfo("mkfifo", [path]) { RedirectStandardError = true, UseShellExecute = false })!;
        made.WaitForExit();
        made.ExitCode.Should().Be(0, "mkfifo makes the stand-in");
    }

    private static async Task<CoderTurnVerdict?> CheckAsync(AgyCoderVendor vendor, ProcessResult process, BoundaryJob job)
    {
        var task = Task.Run(() => vendor.CheckTurn(process, string.Empty, job));

        return await Task.WhenAny(task, Task.Delay(Wait)) == task ? await task : null;
    }

    public static TheoryData<string> Ends => new() { "exit-1", "timed-out", "clean" };

    [Theory]
    [MemberData(nameof(Ends))]
    public async Task P1_AFifoAtTheRecordsName_DoesNotStopTheCheck_AToolOutsideTheThreeIsBoundaryFromTheStreamAlone(string end)
    {
        using var temp = TempDir.For("coai-m18-");
        var job = Job(temp.Path);
        MakeFifo(AgyCoderUnit.RecordsFile(job));
        var process = end switch { "exit-1" => Turn(Event("ACTIVE", "run_command"), 1), "timed-out" => Turn(Event("ACTIVE", "run_command"), -1, timedOut: true), _ => Turn(Event("ACTIVE", "run_command"), 0) };

        var verdict = await CheckAsync(AgyCoderBed.Vendor(), process, job);

        verdict.Should().BeOfType<CoderTurnVerdict.Boundary>($"{end}: the check answers in {Wait.TotalSeconds} seconds and says boundary");
    }

    [Theory]
    [MemberData(nameof(Ends))]
    public async Task AnEntryThatIsNotARegularFile_AtTheRecordsName_IsABoundary_EvenWhenTheStreamAloneProvesNothing(string end)
    {
        using var temp = TempDir.For("coai-m18-");
        var job = Job(temp.Path);
        MakeFifo(AgyCoderUnit.RecordsFile(job));
        var stream = Event("ACTIVE", "write_file") + Event("DONE", "write_file");
        var process = end switch { "exit-1" => Turn(stream, 1), "timed-out" => Turn(stream, -1, timedOut: true), _ => Turn(stream, 0) };

        var verdict = await CheckAsync(AgyCoderBed.Vendor(), process, job);

        verdict.Should().BeOfType<CoderTurnVerdict.Boundary>($"{end}: a name that is not a regular file is never read, and nothing ties the event to the guard");
    }

    [Fact]
    public async Task AFifoAtTheRecordsName_IsABoundary_EvenForATurnWithNoToolEventAtAll()
    {
        using var temp = TempDir.For("coai-m18-");
        var job = Job(temp.Path);
        MakeFifo(AgyCoderUnit.RecordsFile(job));

        (await CheckAsync(AgyCoderBed.Vendor(), Turn(string.Empty, 0), job)).Should().BeOfType<CoderTurnVerdict.Boundary>("a FIFO at that name means a command ran in the unit");
    }

    [Fact]
    public async Task AFolderOrALinkAtTheRecordsName_IsABoundary_TooAndTheRecordsOfARegularFileStillTieTheTurn()
    {
        using var temp = TempDir.For("coai-m18-");
        var job = Job(temp.Path);
        var file = AgyCoderUnit.RecordsFile(job);
        Directory.CreateDirectory(file);
        var stream = Event("ACTIVE", "write_file") + Event("DONE", "write_file");

        (await CheckAsync(AgyCoderBed.Vendor(), Turn(stream, 0), job)).Should().BeOfType<CoderTurnVerdict.Boundary>("a folder is not a records file");
        Directory.Delete(file);
        File.CreateSymbolicLink(file, Path.Combine(temp.Path, "elsewhere.jsonl"));
        (await CheckAsync(AgyCoderBed.Vendor(), Turn(stream, 0), job)).Should().BeOfType<CoderTurnVerdict.Boundary>("a link is not a records file");
        File.Delete(file);
        File.WriteAllText(file, "{\"conversationId\":\"c\",\"stepIdx\":2,\"tool\":\"write_file\",\"decision\":\"allow\"}\n");
        (await CheckAsync(AgyCoderBed.Vendor(), Turn(stream, 0), job)).Should().BeOfType<CoderTurnVerdict.Fine>("a regular file with a matching record is read as before");
    }

    /// <summary>A vendor whose check never returns (a read of a file the model made to wait): stands in for any vendor.</summary>
    private sealed class WaitingVendor(ICoderVendor inner) : ICoderVendor
    {
        public string Id => AgyCoderVendor.VendorId;

        public string PromptId => inner.PromptId;

        public string PromptResource => inner.PromptResource;

        public CoderSignInRecipe SignIn => inner.SignIn;

        public IConsultantRuntime Runtime => inner.Runtime;

        public string FixClause => inner.FixClause;

        public bool PointsAtInstructionBlock => inner.PointsAtInstructionBlock;

        public CoderTurnVerdict CheckTurn(ProcessResult process, string expectedSession)
        {
            Thread.Sleep(TimeSpan.FromSeconds(30));

            return new CoderTurnVerdict.Fine();
        }

        public Task<CoderCapability> ProbeAsync(IProcessLauncher launcher, string executable, string workingDirectory, CancellationToken ct) => inner.ProbeAsync(launcher, executable, workingDirectory, ct);

        public IReadOnlyList<string> FreshArgv(string model, string worktree) => inner.FreshArgv(model, worktree);

        public IReadOnlyList<string> ResumedArgv(string model, string worktree, string sessionId) => inner.ResumedArgv(model, worktree, sessionId);

        public IReadOnlyList<string> UnitProperties(BoundaryHost host, BoundaryJob job, CoderProgram program, TimeSpan runtimeMax) => inner.UnitProperties(host, job, program, runtimeMax);

        public string WorkingDirectory(BoundaryJob job) => inner.WorkingDirectory(job);

        public IReadOnlyList<KeyValuePair<string, string>> Environment(BoundaryHost host, BoundaryJob job) => inner.Environment(host, job);

        public CoderHomeLayout Home(BoundaryJob job) => inner.Home(job);

        public CoderProgram Program(string executable, string path, BoundaryJob job) => inner.Program(executable, path, job);
    }

    private static RoleLaunched Launched(ProcessResult process) =>
        new(new ReviewerLaunch(null, string.Empty, Usage.None, string.Empty, process), null, RoleModelLauncher.RuntimeOf("claude").Runtime, null, string.Empty);

    private static (CoderVendorSet Vendors, ModelRef Model) Waiting() => (CoderVendorSet.Of(AgyCoderBed.Table(), new WaitingVendor(CoderVendors.Claude)), new ModelRef("m", AgyCoderVendor.VendorId));

    [Fact]
    public void P2_ACheckThatDoesNotFinish_IsCoderBoundary_OnTheCleanRoad_NotAnAnswerOfNothing()
    {
        var (vendors, model) = Waiting();
        var watch = Stopwatch.StartNew();

        var stop = CoderTurnCheck.StopFirst(vendors, model, Launched(Turn(string.Empty, 0)), string.Empty, job: null, limit: TimeSpan.FromMilliseconds(400));

        stop.Should().NotBeNull();
        stop!.Word.Should().Be(RoleStopWords.CoderBoundary);
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10), "the limit ends the wait; the job never stays running");
    }

    [Fact]
    public async Task P2_ACheckThatDoesNotFinish_IsCoderBoundary_OnTheCutRoad_Too()
    {
        var (vendors, model) = Waiting();

        var stop = await CoderTurnCheck.StopOfACutTurnAsync(vendors, model, Launched(Turn(string.Empty, -1, timedOut: true)), string.Empty, job: null, limit: TimeSpan.FromMilliseconds(400));

        stop.Should().NotBeNull("a check of a cut turn that does not finish is a boundary, not nothing");
        stop!.Word.Should().Be(RoleStopWords.CoderBoundary);
    }

    /// <summary>P6: the product's own road, a real unit, with the program as a command of a model: a FIFO is made at the records name and the turn goes on; the job must end <c>coder-boundary</c> and not hang or end <c>timeout</c>.</summary>
    [Theory]
    [InlineData("exit")]
    [InlineData("hold")]
    public async Task P6_AFifoMadeAtTheRecordsName_ThroughTheRig_EndsTheJobCoderBoundary(string how)
    {
        await using var rig = await AgyCoderBed.RigAsync();
        rig.AttemptTimeout = TimeSpan.FromSeconds(4);
        var command = AgyCoderBed.Tool("run_command", string.Empty, skipHook: true, stayActive: true);
        rig.SaysEntries(AgyCoderBed.Model, how == "exit" ? AgyCoderBed.Launch([command], exit: 1, fifoRecords: true) : AgyCoderBed.Launch([command], holdMs: 60_000, fifoRecords: true))
            .SaysEntries("gemini-4-argon-medium", AgyCoderBed.Launch([AgyCoderBed.Writes(CoderRig.Green)]));
        var job = rig.NewJob(route: [$"agy/{AgyCoderBed.Model}", "agy/gemini-4-argon-medium"]);

        var (_, ended) = await rig.RunAsync(job).WaitAsync(TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, RoleStopWords.CoderBoundary), $"{how}: {ended.Reason}");
        rig.Launches(job).Should().ContainSingle("a boundary ends the job: the next model is never launched");
    }
}
