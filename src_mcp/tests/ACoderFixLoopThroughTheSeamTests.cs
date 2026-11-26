using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CoaiMcp.Core.Toolchain;
using CoaiMcp.Core.Work;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E3 s7 t2 — the fix loop, the repair turn and the failover go through the vendor seam, proved with a TEST vendor in an injected table (<see cref="SeamVendor"/>): a vendor that has its own prompt, its own check of the stream and its own words in the fix prompt, and
/// whose CLI is FakeCli. d1: red, then green after two fix turns in the session of turn 0, and one repair turn. d2: a lost session is a crash and the next model starts from the last coai commit. d3: the check of the turn's own stream runs after every turn, before the commit.
/// d4: nothing here names a real vendor. d5: the prompts, byte for byte for claude, and the vendor's own for another. d6: the one block coai writes after the turn-0 prompt.
/// </summary>
[Collection("fakecli-env")]
public sealed class ACoderFixLoopThroughTheSeamTests
{
    private const string Session = "11111111-1111-1111-1111-111111111111";
    private const string Other = "22222222-2222-2222-2222-222222222222";
    private const string Bad = "PLANTED-BAD-ITEM";

    /// <summary>The test vendor's id: a name the route and the model lists know, so the route is an ordinary one. It is the table in the test that makes it ship.</summary>
    private const string VendorId = "codex";

    private static readonly string[] Two = ["codex/terra", "codex/astra"];

    private static CoderVendorSet Set() => CoderVendorSet.Of(CoderBoundaryTable.Parse("""
        { "version": 1, "judgement": "j.md", "shared": [],
          "vendors": [ { "id": "codex", "verdict": "ship", "results": "r.md",
            "launchable": true, "measured": [ { "release": "1.0.0", "resultsHeading": "H", "judgementHeading": "J", "programs": [ { "file": "x", "sha256": "0000000000000000000000000000000000000000000000000000000000000000" } ] } ],
            "properties": [], "pending": [] } ] }
        """), new SeamVendor());

    private static string Resumed(string[] argv) => argv.SkipWhile(arg => arg != "--resume").Skip(1).FirstOrDefault() ?? string.Empty;

    private static CoderRig.Launch Writes(string text, string session = Session, string answer = "") =>
        new(new Dictionary<string, string> { [CoderRig.StateFile] = text + "\n" }, answer.Length > 0 ? answer : CoderRig.Report(), Session: session);

    private static async Task<CoderRig> RigAsync(string? toolchain = null)
    {
        var rig = await CoderRig.CreateAsync(toolchain: toolchain);
        rig.Vendors = Set();
        rig.Rig.Setting("COAI_ROLE_CODEX_CLI", RoleJobWorld.FakeCli);

        return rig;
    }

    // ---------- d1: the loop through the seam ----------

    [Fact]
    public async Task D1_ATestVendor_GoesRedThenGreenAfterTwoFixTurns_InTheSessionOfTurnZero()
    {
        await using var rig = await RigAsync();
        rig.Says("terra", Writes(CoderRig.Red + " a"), Writes(CoderRig.Red + " b"), Writes(CoderRig.Green));
        var job = rig.NewJob(route: ["codex/terra"]);

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        var launches = rig.Launches(job);
        launches.Should().HaveCount(3);
        Resumed(launches[0]).Should().BeEmpty("turn 0 starts the session");
        launches.Skip(1).Select(Resumed).Should().OnlyContain(session => session == Session, "both fix turns resume the session of turn 0");
        rig.Envelope(job).GetProperty("fixTurns").GetInt32().Should().Be(2);
        rig.Envelope(job).GetProperty("green").GetBoolean().Should().BeTrue();
        rig.Rig.LedgerLines().Should().HaveCount(3, "one usage line for each launch");
    }

    [Fact]
    public async Task D1_AnInvalidReport_GetsOneRepairTurn_InTheSessionOfTurnZero()
    {
        await using var rig = await RigAsync();
        rig.Says("terra", new CoderRig.Launch(new Dictionary<string, string> { [CoderRig.StateFile] = CoderRig.Green + "\n" }, "not a report at all", Session: Session), Writes(CoderRig.Green));
        var job = rig.NewJob(route: ["codex/terra"]);

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        var launches = rig.Launches(job);
        launches.Should().HaveCount(2);
        Resumed(launches[1]).Should().Be(Session);
        launches[1][^1].Should().Contain("Your report was checked and has errors");
        rig.Envelope(job).GetProperty("schemaValid").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task D1_ARefusedVendor_IsNeverLaunched_AndTheRouteGoesOn()
    {
        await using var rig = await CoderRig.CreateAsync();
        rig.Rig.Setting("COAI_ROLE_CODEX_CLI", RoleJobWorld.FakeCli);
        rig.Says("terra", Writes(CoderRig.Green)).Says("sonnet", Writes(CoderRig.Green));
        var job = rig.NewJob(route: ["codex/terra", "claude/sonnet"]);

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        rig.Launches(job).Should().ContainSingle("the product's table ships no release of the vendor: FakeCli saw ONE launch, the claude model's");
        rig.Launches(job)[0].Should().Contain("sonnet");
    }

    // ---------- d2: a lost session ----------

    [Fact]
    public async Task D2_ALostSessionOnAFixTurn_IsACrash_AndTheNextModelStartsFromTheLastCoaiCommit_InANewSession()
    {
        await using var rig = await RigAsync();
        rig.Says("terra", Writes(CoderRig.Red + " a"), new CoderRig.Launch(new Dictionary<string, string> { ["stray.txt"] = "half\n" }, CoderRig.Report(), Session: Session, Stderr: "no rollout found for thread " + Session))
            .Says("astra", Writes(CoderRig.Green, Other));
        var job = rig.NewJob(route: Two);

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        var launches = rig.Launches(job);
        launches.Should().HaveCount(3);
        Resumed(launches[1]).Should().Be(Session, "the fix turn resumed the session of turn 0");
        Resumed(launches[2]).Should().BeEmpty("no fix turn starts a new session by itself: the next model starts from turn 0, in a session of its own");
        File.Exists(Path.Combine(rig.WorktreeOf(job), "stray.txt")).Should().BeFalse("the worktree was put back to the last coai commit");
        rig.Rig.LedgerLines().Should().HaveCount(3, "one usage line for each launch, the lost one too");
    }

    // ---------- d3: the check of the turn's own stream ----------

    [Fact]
    public async Task D3_ABadItemOnTurnZero_EndsTheJobCoderBoundary_AndNothingOfThatTurnIsCommitted()
    {
        await using var rig = await RigAsync();
        rig.Says("terra", Writes(CoderRig.Green, Session, CoderRig.Report(notes: Bad)));
        var job = rig.NewJob(route: ["codex/terra"]);

        var (_, ended) = await rig.RunAsync(job);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, RoleStopWords.CoderBoundary), ended.Reason);
        (await rig.GitAsync(rig.Repo, "rev-list", "--count", $"{rig.Base}..coder/{job.JobId}")).Should().Be("0", "the worktree holds no commit of the turn the check refused");
        rig.Launches(job).Should().ContainSingle("a stop of the boundary never fails over");
    }

    [Fact]
    public async Task D3_ABadItemOnAFixTurn_EndsTheJobCoderBoundary_BeforeThatTurnIsCommitted()
    {
        await using var rig = await RigAsync();
        rig.Says("terra", Writes(CoderRig.Red + " a"), Writes(CoderRig.Green, Session, CoderRig.Report(notes: Bad)));
        var job = rig.NewJob(route: ["codex/terra"]);

        var (_, ended) = await rig.RunAsync(job);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, RoleStopWords.CoderBoundary), ended.Reason);
        (await rig.GitAsync(rig.Repo, "rev-list", "--count", $"{rig.Base}..coder/{job.JobId}")).Should().Be("1", "turn 0's commit only: the fix turn's work was not committed");
    }

    [Fact]
    public async Task D3_ASessionThatIsNotTheHandles_EndsTheAttemptAsACrash_AndTheNextModelRuns()
    {
        await using var rig = await RigAsync();
        rig.Says("terra", Writes(CoderRig.Red + " a"), Writes(CoderRig.Green, Other)).Says("astra", Writes(CoderRig.Green, Other));
        var job = rig.NewJob(route: Two);

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        rig.Launches(job).Should().HaveCount(3, "the second model ran after the first one's fix turn answered in another session");
        rig.Envelope(job).GetProperty("headSha").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task D3_ClaudeGetsTheCheckThatSaysNothing_TheSameStreamIsNotRefused()
    {
        await using var rig = await CoderRig.CreateAsync();
        rig.Says("sonnet", Writes(CoderRig.Red + " a"), Writes(CoderRig.Green, Other, CoderRig.Report(notes: Bad)));
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, "the same planted item and another session are nothing to claude's check: " + ended.Reason);
    }

    // ---------- d4: nothing here names a real vendor ----------

    [Theory]
    [InlineData("src/Server/RoleJobs/CoderWork.cs")]
    [InlineData("src/Server/RoleJobs/CoderFixLoop.cs")]
    [InlineData("src/Server/RoleJobs/CoderPrompt.cs")]
    [InlineData("src/Server/RoleJobs/CoderTurnCheck.cs")]
    [InlineData("src/Server/RoleJobs/CoderEnvelope.cs")]
    [InlineData("tests_fakecli/CoderVerbs.cs")]
    public void D4_TheFilesOfThisTask_NameNeitherCodexNorAgy(string file)
    {
        var text = File.ReadAllText(Path.Combine(RepoPaths.Root(), "src_mcp", file));

        Regex.Matches(text, @"\b(codex|agy)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Should().BeEmpty($"{file} is the vendor-neutral road: s5 and s6 each add their vendor without a change here");
    }

    [Theory]
    [InlineData("src/Server/RoleJobs/RoleModelLauncher.cs", 3)]
    [InlineData("src/Server/RoleJobs/RoleFailover.cs", 1)]
    public void D4_TheFilesThatAlreadyNamedAVendor_NameNoMoreOfThemThanTheyDid(string file, int before)
    {
        var count = File.ReadAllLines(Path.Combine(RepoPaths.Root(), "src_mcp", file)).Count(line => Regex.IsMatch(line, @"\b(codex|agy)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));

        count.Should().BeLessThanOrEqualTo(before, $"the lines of {file} that name a vendor are the ones it had; the seam names none");
    }

    // ---------- d5: the prompts ----------

    private static CoderPromptInputs Inputs() => new("plan/epoch-1/e1-core/stories/s1-first/tasks/t1-a/task.yaml", "t1-a", "Write the thing", "code", ["src/thing.cs"], [new WorkDod("d1", "the thing works"), new WorkDod("d2", "the thing is tested")]);

    private static readonly DigestItem[] Digest = [new("test", "src/thing.cs", 3, "T1", "the state is red")];

    private static string Recorded(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "coder-prompts", name));

    [Fact]
    public void D5_TheSeamNamesTheEmbeddedResourceOfTheClaudePrompt_ThatTheServerEmbeds()
    {
        CoderVendors.Claude.PromptResource.Should().Be(CoderPrompt.PromptResource);
        CoderVendors.Claude.PromptId.Should().Be(CoderPrompt.PromptId);
    }

    [Fact]
    public void D5_TheShippedClaudePrompt_IsTheSameBytes()
    {
        var path = Path.Combine(RepoPaths.Root(), "src_mcp", "src", "Roles", "prompts", "coder.md");

        Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))).Should().Be("ee31db20b05e779b817131fdb615be1225271dfad715e3b647cfa38d7d92d7bf");
    }

    [Fact]
    public void D5_TheThreeClaudePrompts_AreByteForByteTheTextsRecordedFromTheCodeBeforeThisChange()
    {
        using var data = TempDir.For("coai-prompts-claude-");
        var prompts = new RolePrompts(data.Path);

        CoderPrompt.Build(Inputs(), prompts).Should().Be(Recorded("claude-turn0.txt"));
        CoderPrompt.Fix(2, 3, Digest, "the test step failed").Should().Be(Recorded("claude-fix.txt"));
        CoderPrompt.Repair(["the report has no dod item d2", "notes must be text"]).Should().Be(Recorded("claude-repair.txt"));
        CoderPrompt.Build(Inputs(), prompts, CoderVendors.Claude).Should().Be(Recorded("claude-turn0.txt"));
        CoderPrompt.Fix(2, 3, Digest, "the test step failed", CoderVendors.Claude).Should().Be(Recorded("claude-fix.txt"));
    }

    [Fact]
    public async Task D5_AnOverrideOfTheClaudePrompt_IsNeverSentToAnotherVendor_AndTheVendorsOwnIsReadByItsId()
    {
        await using var rig = await RigAsync();
        var prompts = new RolePrompts(rig.World.DataDir);
        prompts.Override("coder", "CLAUDE-OVERRIDE for {{taskId}}");
        prompts.Override("coder-seamprobe", "PROBE-OVERRIDE for {{taskId}}");
        rig.Says("terra", Writes(CoderRig.Red + " a"), Writes(CoderRig.Green)).Says("sonnet", Writes(CoderRig.Green));
        var other = rig.NewJob(route: ["codex/terra"]);
        var claude = rig.NewJob(route: ["claude/sonnet"]);

        await rig.RunAsync(other);
        await rig.RunAsync(claude);

        var probe = rig.Launches(other);
        probe[0][^1].Should().StartWith("PROBE-OVERRIDE for t1-a").And.NotContain("CLAUDE-OVERRIDE", "an override of the claude prompt is never sent to another vendor");
        probe[1][^1].Should().Contain("you run no command of your own").And.NotContain("you have no shell", "the fix prompt carries the vendor's own words");
        rig.Launches(claude)[0][^1].Should().StartWith("CLAUDE-OVERRIDE for t1-a").And.NotContain("PROBE-OVERRIDE");
    }

    [Fact]
    public async Task D5_TheEnvelopeNamesThePromptIdAndTheSha256OfTheRenderedTurnZeroPrompt_NeverItsText()
    {
        await using var rig = await RigAsync();
        rig.Says("terra", Writes(CoderRig.Green)).Says("sonnet", Writes(CoderRig.Green));
        var seam = rig.NewJob(route: ["codex/terra"]);
        var claude = rig.NewJob(route: ["claude/sonnet"]);

        await rig.RunAsync(seam);
        await rig.RunAsync(claude);

        foreach (var (job, id) in new[] { (seam, "coder-seamprobe"), (claude, "coder") })
        {
            var sent = rig.Launches(job)[0][^1];
            var prompt = rig.Envelope(job).GetProperty("prompt");
            prompt.GetProperty("id").GetString().Should().Be(id);
            prompt.GetProperty("sha256").GetString().Should().Be(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sent))), "the hash of the prompt FakeCli was sent");
            rig.World.Store.FolderOf(job.JobId)!.ReadResult().Should().NotContain("Write the thing", "no text of the prompt is in the envelope");
        }
    }

    // ---------- d6: the block ----------

    private static string Step(string script) => CoderRig.DefaultToolchain() + $"instructions:\n  argv:\n    - \"/bin/sh\"\n    - \"-c\"\n    - {ToolchainFixture.Quote(script)}\n";

    private const string StepScript = "printf 'instruction-%s-line\\n' canary";

    [Fact]
    public async Task D6_WithAStep_TheOutputIsAppendedToTheTurnZeroPromptOfEveryVendor_ClaudeToo_AsOneBlock()
    {
        await using var rig = await RigAsync(Step(StepScript));
        rig.Says("terra", Writes(CoderRig.Red + " a"), Writes(CoderRig.Green)).Says("sonnet", Writes(CoderRig.Red + " a"), Writes(CoderRig.Green));
        var seam = rig.NewJob(route: ["codex/terra"]);
        var claude = rig.NewJob(route: ["claude/sonnet"]);

        await rig.RunAsync(seam);
        await rig.RunAsync(claude);

        var plain = Recorded("claude-turn0.txt");
        foreach (var job in new[] { seam, claude })
        {
            var launches = rig.Launches(job);
            launches[0][^1].Should().EndWith($"\n\n## Run by coai before your turn\n\nCommand (argv, run with no shell): [\"/bin/sh\",\"-c\",\"{StepScript.Replace("\\", "\\\\", StringComparison.Ordinal)}\"]\nBase commit: {rig.Base}\n\nOutput:\n\n```\ninstruction-canary-line\n```\n");
            launches[1][^1].Should().NotContain("Run by coai before your turn", "a fix turn does not repeat the block").And.NotContain("instruction-canary-line");
        }

        rig.Launches(claude)[0][^1].Should().StartWith(plain.Split("Write the thing")[0], "with a step the claude prompt is today's text followed by the block");
    }

    [Fact]
    public async Task D6_WithNoStep_ClaudeIsSentNoBlock_AndAVendorWhosePromptPointsAtItIsSentTheBlockThatSaysNothingWasRun()
    {
        await using var rig = await RigAsync();
        rig.Says("terra", Writes(CoderRig.Green)).Says("sonnet", Writes(CoderRig.Green));
        var seam = rig.NewJob(route: ["codex/terra"]);
        var claude = rig.NewJob(route: ["claude/sonnet"]);

        await rig.RunAsync(seam);
        await rig.RunAsync(claude);

        rig.Launches(claude)[0][^1].Should().NotContain("Run by coai before your turn", "claude is sent no block when no step is declared");
        rig.Launches(seam)[0][^1].Should().EndWith("## Run by coai before your turn\n\nNothing was run: this repository declares no `instructions` step in `.coai/toolchain.yaml`, so no command of its instruction files was run for this job.\n");
    }

    [Fact]
    public async Task D6_ANewSessionAfterAFailover_CarriesTheBlockAgain()
    {
        await using var rig = await RigAsync(Step(StepScript));
        rig.Says("terra", new CoderRig.Launch(null, string.Empty, Exit: 1, Stderr: "the CLI fell over: boom")).Says("astra", Writes(CoderRig.Green));
        var job = rig.NewJob(route: Two);

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        rig.Launches(job).Select(argv => argv[^1]).Should().OnlyContain(prompt => prompt.Contains("instruction-canary-line"), "each session, the second one too, is sent the block");
    }

    [Fact]
    public void D6_TheFenceIsLongerThanAnyRunOfBackticksInTheOutput()
    {
        var ran = new CoderInstructionsOutcome.Ran(new CoderInstructionsRecord(true, ["tool"], 0, 10, "h", 1, []), "a ````` b ``` c\n", "abc123");

        var block = CoderPrompt.InstructionsBlock(ran, CoderVendors.Claude);

        block.Should().Contain("\n``````\na ````` b ``` c\n``````\n", "six backticks around a run of five");
        CoderPrompt.InstructionsBlock(new CoderInstructionsOutcome.None(), CoderVendors.Claude).Should().BeEmpty();
    }

    /// <summary>A vendor that stands in for a real one: claude's argv and unit, its own prompt id, its own words in the fix prompt, a block-aware prompt and a check of its stream — a planted item is a boundary fault, a session that is not the handle's or a lost one is a crash.</summary>
    private sealed class SeamVendor : ICoderVendor
    {
        public string Id => VendorId;

        public string PromptId => "coder-seamprobe";

        public string PromptResource => CoderVendors.Claude.PromptResource;

        public CoderSignInRecipe SignIn => CoderVendors.Claude.SignIn;

        public IConsultantRuntime Runtime => new ClaudeConsultant(new ClaudeRuntime(), VendorId);

        public string FixClause => "you run no command of your own; coai ran them";

        public bool PointsAtInstructionBlock => true;

        public CoderTurnVerdict CheckTurn(ProcessResult process, string expectedSession)
        {
            if (process.StdOut.Contains(Bad, StringComparison.Ordinal))
            {
                return new CoderTurnVerdict.Boundary("the stream of the turn holds an item outside the measured list");
            }

            if (process.StdErr.Contains("no rollout found", StringComparison.Ordinal))
            {
                return new CoderTurnVerdict.Crash("the session of the job was lost: no rollout found");
            }

            var said = Runtime.ReadHandle(process);

            return expectedSession.Length > 0 && said.Length > 0 && said != expectedSession
                ? new CoderTurnVerdict.Crash($"the session of the turn is not the handle's")
                : new CoderTurnVerdict.Fine();
        }

        public Task<CoderCapability> ProbeAsync(IProcessLauncher launcher, string executable, string workingDirectory, CancellationToken ct) => CoderVendors.Claude.ProbeAsync(launcher, executable, workingDirectory, ct);

        public IReadOnlyList<string> FreshArgv(string model, string worktree) => CoderVendors.Claude.FreshArgv(model, worktree);

        public IReadOnlyList<string> ResumedArgv(string model, string worktree, string sessionId) => CoderVendors.Claude.ResumedArgv(model, worktree, sessionId);

        public IReadOnlyList<string> UnitProperties(CoaiMcp.Runners.Toolchain.BoundaryHost host, CoaiMcp.Runners.Toolchain.BoundaryJob job, CoaiMcp.Runners.Toolchain.CoderProgram program, TimeSpan runtimeMax) => CoaiMcp.Runners.Toolchain.CoderUnit.Properties(host, job, program, runtimeMax, VendorId);

        public string WorkingDirectory(CoaiMcp.Runners.Toolchain.BoundaryJob job) => CoderVendors.Claude.WorkingDirectory(job);

        public IReadOnlyList<KeyValuePair<string, string>> Environment(CoaiMcp.Runners.Toolchain.BoundaryHost host, CoaiMcp.Runners.Toolchain.BoundaryJob job) => CoaiMcp.Runners.Toolchain.CoderUnit.Environment(host, job, VendorId);

        public CoderHomeLayout Home(CoaiMcp.Runners.Toolchain.BoundaryJob job) => new(job.CoderHomeOf(VendorId), job.CoderSignInFileOf(VendorId), job.CoderFoldersOf(VendorId));

        public CoaiMcp.Runners.Toolchain.CoderProgram Program(string executable, string path, CoaiMcp.Runners.Toolchain.BoundaryJob job) => CoderVendors.Claude.Program(executable, path, job);
    }
}
