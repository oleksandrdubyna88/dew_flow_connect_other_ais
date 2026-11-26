using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CoaiMcp.Core.Notices;
using CoaiMcp.Core.Toolchain;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Toolchain;
using CoaiMcp.Server.RoleJobs;
using CoaiMcp.Core.Work;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E3 s7 t3 — the instruction step (SPEC C9). d1: the toolchain file gets ONE optional top-level key, <c>instructions</c>, read at the base commit, with <c>argv</c>, <c>perFile</c> and <c>timeoutSeconds</c>; the schema file and the reader are held equal; coai never reads an instruction
/// file to find a command. d2 to d4 run the step in a real check unit (the second half of this class); d5 is the file of this repository.
/// </summary>
public sealed class ACoderInstructionStepTests
{
    private const string Head = "version: 1\nprojects:\n  - path: src_mcp\n    adapter: dotnet\n";

    private static ToolchainRead Read(string text) => ToolchainFile.Read(new WorkFile.Text(text.ReplaceLineEndings("\n") + "\n"));

    private static RepoToolchain Right(string text) => Read(text).Should().BeOfType<ToolchainRead.Configured>($"the file is right: {text}").Subject.Toolchain;

    private static ToolchainRead.Refused Refused(string text) => Read(text).Should().BeOfType<ToolchainRead.Refused>($"the file is wrong: {text}").Subject;

    // ---------- d1: the key ----------

    [Fact]
    public void D1_AFileWithNoInstructionsKey_DeclaresNoStep_AndEverythingElseIsAsItWas()
    {
        var toolchain = Right(Head);

        toolchain.Instructions.Should().Be(ToolchainInstructions.None);
        toolchain.Instructions.Declared.Should().BeFalse();
    }

    [Fact]
    public void D1_AStepWithOnlyAnArgv_GetsTheDefaultTimeoutOfSixtySeconds_AndNoPerFile()
    {
        var step = Right(Head + "instructions:\n  argv:\n    - node\n    - tool.mjs\n").Instructions;

        step.Declared.Should().BeTrue();
        step.Argv.Should().Equal("node", "tool.mjs");
        step.PerFile.Should().BeEmpty();
        step.TimeoutSeconds.Should().Be(60);
    }

    [Fact]
    public void D1_APerFileItem_IsAppendedOnceForEachFileOfTheTask_WithTheFileInPlaceOfThePlaceholder()
    {
        var step = Right(Head + "instructions:\n  argv:\n    - node\n    - read.mjs\n  perFile:\n    - --file\n    - \"{file}\"\n  timeoutSeconds: 120\n").Instructions;

        step.TimeoutSeconds.Should().Be(120);
        step.ArgvFor(["a/one.cs", "b/two.cs"]).Should().Equal("node", "read.mjs", "--file", "a/one.cs", "--file", "b/two.cs");
        step.ArgvFor([]).Should().Equal("node", "read.mjs");
    }

    [Theory]
    [InlineData("instructions:\n  argv:\n    - node\n  shell: true\n", "'shell' is not a key of instructions")]
    [InlineData("instructions:\n  argv: []\n", "'argv' of instructions is empty")]
    [InlineData("instructions:\n  perFile:\n    - x\n", "the key 'argv' is missing from instructions")]
    [InlineData("instructions:\n  argv:\n    - node\n    - \"{file}\"\n", "{file}")]
    [InlineData("instructions:\n  argv:\n    - node\n  timeoutSeconds: 0\n", "'timeoutSeconds' of instructions must be a whole number from 1 to 300")]
    [InlineData("instructions:\n  argv:\n    - node\n  timeoutSeconds: 301\n", "'timeoutSeconds' of instructions must be a whole number from 1 to 300")]
    [InlineData("instructions:\n  argv:\n    - node\n  timeoutSeconds: \"60\"\n", "'timeoutSeconds'")]
    [InlineData("instructions: node\n", "'instructions' must be a map")]
    [InlineData("instructions:\n  argv: node\n", "'argv' of instructions must be a list of strings")]
    public void D1_AnInstructionsKeyThatIsWrong_IsToolchainRefused_WithTheLine(string block, string saying)
    {
        var refused = Refused(Head + block);

        refused.Sentence.Should().Contain(saying).And.Contain("line ");
        refused.InstructionFaults.Should().NotBeEmpty("a fault of this key is told apart from a fault elsewhere in the file").And.OnlyContain(fault => refused.Faults.Contains(fault));
    }

    [Fact]
    public void D1_ASecretInTheStepsArgv_IsRefusedByTheRuleOfTheReader_AndANoteOfTheFaultNeverRepeatsIt()
    {
        var text = Head + "instructions:\n  argv:\n    - tool\n    - ghp_abcdefghijklmnopqrstuvwxyz0123456789ABCD\n";

        var refused = Refused(text);

        refused.Sentence.Should().Contain("looks like a credential").And.NotContain("ghp_abcdefghijkl");
    }

    [Fact]
    public void D1_TheFilePlaceholderFault_NeverRepeatsTheItem_ItsPlaceAndLengthAreSaidInstead()
    {
        var refused = Refused(Head + "instructions:\n  argv:\n    - tool\n    - \"https://user:s3cr3t-value@host/{file}\"\n");

        refused.Sentence.Should().Contain("{file} in its item 2").And.Contain("line ").And.NotContain("s3cr3t-value").And.NotContain("https://");
        refused.InstructionFaults.Should().OnlyContain(fault => !fault.Contains("s3cr3t-value", StringComparison.Ordinal));
    }

    [Fact]
    public void D1_AnEmptyArgv_IsRefused_AndTheSchemaSaysTheSame()
    {
        Refused(Head + "instructions:\n  argv: []\n").Sentence.Should().Contain("'argv' of instructions is empty");
        Refused(Head + "instructions:\n  argv:\n").Faults.Should().NotBeEmpty("an argv with no item is no step");
    }

    [Fact]
    public void D1_CoaiNeverReadsAnInstructionFileToFindACommand_TheStepIsWhatTheToolchainFileDeclares()
    {
        foreach (var file in new[] { "src/Server/RoleJobs/CoderInstructions.cs", "core/Toolchain/ToolchainParse.cs", "core/Toolchain/ToolchainModel.cs" })
        {
            var code = string.Join('\n', File.ReadAllLines(Path.Combine(RepoPaths.Root(), "src_mcp", file)).Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

            code.Should().NotContain("AGENTS.md", $"{file} names no instruction file in code").And.NotContain("CLAUDE.md");
        }
    }

    [Fact]
    public void D1_AFaultElsewhereInTheFile_IsNotAFaultOfTheInstructionsKey()
    {
        var refused = Refused("version: 1\nprojects:\n  - path: src_mcp\n    adapter: cobol\ninstructions:\n  argv:\n    - node\n");

        refused.InstructionFaults.Should().BeEmpty();
        refused.Sentence.Should().Contain("adapter");
    }
}

[Collection("fakecli-env")]
public sealed class ACoderInstructionStepRunTests
{
    private const string Canary = "instruction-canary-line";

    private static string InstructionsBlock(string script, string tail = "", string shell = "/bin/sh") =>
        $"instructions:\n  argv:\n    - \"{shell}\"\n    - \"-c\"\n    - {ToolchainFixture.Quote(script)}\n{tail}";

    /// <summary>The fixture toolchain of the coder tests, with this instruction step.</summary>
    private static string With(string script, string tail = "", string shell = "/bin/sh") => CoderRig.DefaultToolchain() + InstructionsBlock(script, tail, shell);

    private static CoderRig.Launch Green() => new(new Dictionary<string, string> { [CoderRig.StateFile] = CoderRig.Green + "\n" }, CoderRig.Report());

    private static JsonElement Step(JsonElement envelope) => envelope.GetProperty("instructions");

    private static string Sha(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static string Kept(CoderRig rig, RoleJobRecord job) => File.ReadAllText(Path.Combine(rig.World.Store.FolderOf(job.JobId)!.Location, "instructions", "stdout.txt"));

    // ---------- d2: it runs once, in a check unit at the base, before any model ----------

    [Fact]
    public async Task D2_ADeclaredStep_RunsOnce_InTheWorktreeAtTheBase_ItsOutputIsKeptAndTheEnvelopeHoldsOnlyItsHash()
    {
        await using var rig = await CoderRig.CreateAsync(toolchain: With($"printf 'instruction-%s-line\\n' canary; head -1 .coai/toolchain.yaml; echo run >> {BoundaryUnit.UnitTemp}/steps"));
        rig.Says("sonnet", Green());
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        var kept = Kept(rig, job);
        kept.Should().Be($"{Canary}\nversion: 1\n", "the step ran in the worktree at the base commit, where the toolchain file is the base's");
        File.ReadAllLines(Path.Combine(rig.JobFolder(job), "tmp", "steps")).Should().ContainSingle("the step ran ONCE for the job, in the check unit's own temp folder");
        var step = Step(rig.Envelope(job));
        step.GetProperty("declared").GetBoolean().Should().BeTrue();
        step.GetProperty("argv").EnumerateArray().Select(item => item.GetString()).Should().HaveCount(3).And.StartWith(["/bin/sh", "-c"]);
        step.GetProperty("exitCode").GetInt32().Should().Be(0);
        step.GetProperty("bytes").GetInt32().Should().Be(Encoding.UTF8.GetByteCount(kept));
        step.GetProperty("sha256").GetString().Should().Be(Sha(kept));
        step.GetProperty("ms").GetInt64().Should().BeGreaterThanOrEqualTo(0);
        rig.World.Store.FolderOf(job.JobId)!.ReadResult().Should().NotContain(Canary, "no text of the output is in the envelope");
        rig.Launches(job).Should().ContainSingle();
    }

    [Fact]
    public async Task D2_TheStepIsRunOnceForTheJobWhateverTheNumberOfAttempts_AndNoTextOfItsOutputIsInALogLine()
    {
        await using var rig = await CoderRig.CreateAsync(toolchain: With($"printf 'instruction-%s-line\\n' canary; echo run >> {BoundaryUnit.UnitTemp}/steps"));
        rig.Says("sonnet", new CoderRig.Launch(null, string.Empty, Exit: 1, Stderr: "the CLI fell over: boom")).Says("opus", Green());
        var job = rig.NewJob(route: ["claude/sonnet", "claude/opus"]);

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        rig.Launches(job).Should().HaveCount(2, "a failover started a second session");
        File.ReadAllLines(Path.Combine(rig.JobFolder(job), "tmp", "steps")).Should().ContainSingle("the second attempt used the output of the first run again");
        rig.World.LogText.Should().NotContain(Canary);
    }

    [Fact]
    public async Task D2_AJobWhoseBaseDeclaresNoStep_RunsNothing_AndTheEnvelopeSaysDeclaredFalse()
    {
        await using var rig = await CoderRig.CreateAsync();
        rig.Says("sonnet", Green());
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        var step = Step(rig.Envelope(job));
        step.GetProperty("declared").GetBoolean().Should().BeFalse();
        (step.GetProperty("argv").GetArrayLength(), step.GetProperty("exitCode").GetInt32(), step.GetProperty("bytes").GetInt32(), step.GetProperty("sha256").GetString(), step.GetProperty("ms").GetInt32()).Should().Be((0, 0, 0, string.Empty, 0));
        Directory.Exists(Path.Combine(rig.World.Store.FolderOf(job.JobId)!.Location, "instructions")).Should().BeFalse();
    }

    [Fact]
    public async Task D2_TheStepIsReadAtTheBaseCommit_ATaskThatAddsOneInTheWorktreeCannotInstructItself()
    {
        await using var rig = await CoderRig.CreateAsync();
        var edits = new Dictionary<string, string> { [".coai/toolchain.yaml"] = With("echo planted"), [CoderRig.StateFile] = CoderRig.Green + "\n" };
        rig.Says("sonnet", new CoderRig.Launch(edits, CoderRig.Report()));
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        Step(rig.Envelope(job)).GetProperty("declared").GetBoolean().Should().BeFalse("the base declares none; the coder's own toolchain file runs nothing for its own job");
        ended.Should().NotBeNull();
    }

    private static string Probe(string file, int port) =>
        $"if cat {file} >/dev/null 2>&1; then echo signin=readable; else echo signin=hidden; fi; if (exec 3<>/dev/tcp/127.0.0.1/{port}) 2>/dev/null; then echo net=reachable; else echo net=unreachable; fi";

    [Fact]
    public async Task D2_OnARealUnit_AStepCannotReadAStandInSignInFile_NorReachAListenerOnTheHostLoopback_AndAControlSeesBoth()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        await using var rig = await CoderRig.CreateAsync(toolchainOf: home => With(Probe(Path.Combine(home, ".claude", ".credentials.json"), port), shell: "/bin/bash"));
        rig.Says("sonnet", Green());
        var job = rig.NewJob();
        var control = await new ProcessLauncher().RunAsync(new ProcessRequest("/bin/bash", ["-c", Probe(Path.Combine(rig.Host.RealHome, ".claude", ".credentials.json"), port)], rig.Repo), TestContext.Current.CancellationToken);

        var (_, ended) = await rig.RunAsync(job);

        control.StdOut.Should().Be("signin=readable\nnet=reachable\n", "outside the unit the same step sees the file and the listener, so what the unit shows is the unit's doing");
        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        Kept(rig, job).Should().Be("signin=hidden\nnet=unreachable\n");
    }

    // ---------- d3: a step that cannot run refuses the job, before any model ----------

    private static async Task<(CoderRig Rig, RoleJobRecord Job, RoleJobRecord Ended)> FailsAsync(string toolchain, Action<CoderRig>? prepare = null)
    {
        var rig = await CoderRig.CreateAsync(toolchain: toolchain);
        prepare?.Invoke(rig);
        rig.Says("sonnet", Green());
        var job = rig.NewJob();
        var (_, ended) = await rig.RunAsync(job);

        return (rig, job, ended);
    }

    private static void RefusedWithNoModel(CoderRig rig, RoleJobRecord job, RoleJobRecord ended, string saying)
    {
        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, RoleStopWords.InstructionsFailed), ended.Reason);
        ended.Reason.Should().Contain(saying);
        rig.Launches(job).Should().BeEmpty("FakeCli records ZERO launches: no model was launched, so there is no failover and no green");
        rig.Rig.LedgerLines().Should().BeEmpty();
        var step = Step(rig.Envelope(job));
        step.GetProperty("declared").GetBoolean().Should().BeTrue();
        step.GetProperty("argv").GetArrayLength().Should().BeGreaterThan(0);
        rig.Envelope(job).TryGetProperty("green", out var green).Should().BeFalse($"nothing is green: {green}");
    }

    [Fact]
    public async Task D3_AStepThatExitsNonZero_EndsTheJob_AndTheEnvelopeNamesTheExitAndTheLastLinesOfStandardError()
    {
        var (rig, job, ended) = await FailsAsync(With("echo to-stdout; echo first-line >&2; echo the-last-line >&2; exit 3"));
        await using var _ = rig;

        RefusedWithNoModel(rig, job, ended, "exited 3");
        var step = Step(rig.Envelope(job));
        step.GetProperty("exitCode").GetInt32().Should().Be(3);
        step.GetProperty("bytes").GetInt32().Should().Be("to-stdout\n".Length);
        step.GetProperty("stderrTail").EnumerateArray().Select(line => line.GetString()).Should().Equal("first-line", "the-last-line");
    }

    [Fact]
    public async Task D3_AStepThatRunsPastItsTime_EndsTheJob()
    {
        var (rig, job, ended) = await FailsAsync(With("sleep 30", "  timeoutSeconds: 1\n"));
        await using var _ = rig;

        RefusedWithNoModel(rig, job, ended, "ran past its 1 s");
        (await rig.UnitsOfAsync(job)).Should().BeEmpty("the unit was stopped");
    }

    [Fact]
    public async Task D3_AStepThatPrintsMoreThan64KiB_EndsTheJob_AndNothingIsCut()
    {
        var (rig, job, ended) = await FailsAsync(With("head -c 70000 /dev/zero | tr '\\0' a"));
        await using var _ = rig;

        RefusedWithNoModel(rig, job, ended, "more than 65536");
        Step(rig.Envelope(job)).GetProperty("bytes").GetInt32().Should().Be(70000);
    }

    [Fact]
    public async Task D3_AStepThatPrintsBytesThatAreNotUtf8_EndsTheJob()
    {
        var (rig, job, ended) = await FailsAsync(With("printf 'ok\\377\\376\\n'"));
        await using var _ = rig;

        RefusedWithNoModel(rig, job, ended, "not UTF-8");
    }

    [Fact]
    public async Task D3_AStepThatChangesTheTree_EndsTheJob_AndNamesThePath()
    {
        var (rig, job, ended) = await FailsAsync(With("echo stray > planted-by-the-step.txt; echo fine"));
        await using var _ = rig;

        RefusedWithNoModel(rig, job, ended, "planted-by-the-step.txt");
    }

    [Fact]
    public async Task D3_AUnitThatCannotStart_EndsTheJob()
    {
        var (rig, job, ended) = await FailsAsync(With("echo fine"), rig => rig.OnHost = host => host with { SystemdRun = "no-such-systemd-run-program" });
        await using var _ = rig;

        RefusedWithNoModel(rig, job, ended, "could not be started");
    }

    [Fact]
    public async Task D3_AStepThatPrintsWhatLooksLikeACredential_EndsTheJob_NamingTheClassNeverTheText_AndKeepsNothingOfIt()
    {
        var (rig, job, ended) = await FailsAsync(With("printf 'remote: ghp_%s\\n' abcdefghijklmnopqrstuvwxyz0123456789"));
        await using var _ = rig;

        RefusedWithNoModel(rig, job, ended, "looks like a credential");
        const string Token = "ghp_abcdefghijklmnopqrstuvwxyz0123456789";
        ended.Reason.Should().NotContain(Token);
        rig.World.Store.FolderOf(job.JobId)!.ReadResult().Should().NotContain(Token);
        rig.World.LogText.Should().NotContain(Token);
        Directory.EnumerateFiles(rig.World.Store.FolderOf(job.JobId)!.Location, "*", SearchOption.AllDirectories).Should().OnlyContain(file => !File.ReadAllText(file).Contains(Token, StringComparison.Ordinal), "nothing of the output is kept: not in the job folder, not in a file");
        Directory.Exists(Path.Combine(rig.World.Store.FolderOf(job.JobId)!.Location, "instructions")).Should().BeFalse();
    }

    [Fact]
    public async Task D3_AStepThatPrintsAControlCharacter_EndsTheJob()
    {
        var (rig, job, ended) = await FailsAsync(With("printf 'ok\\033[31mred\\n'"));
        await using var _ = rig;

        RefusedWithNoModel(rig, job, ended, "control character");
    }

    [Fact]
    public async Task D3_ADeclaredStepInAFileThatIsRefusedForAnotherReason_EndsToolchainRefused_BeforeAnyModel_AFlowListAndAFaultElsewhere()
    {
        foreach (var toolchain in new[]
        {
            CoderRig.DefaultToolchain() + "instructions:\n  argv: [node, tool.mjs]\n",
            CoderRig.DefaultToolchain().Replace("adapter: dotnet", "adapter: cobol", StringComparison.Ordinal) + InstructionsBlock("echo fine"),
        })
        {
            await using var rig = await CoderRig.CreateAsync(toolchain: toolchain);
            rig.Says("sonnet", Green());
            var job = rig.NewJob();

            var (_, ended) = await rig.RunAsync(job);

            (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, "toolchain-refused"), ended.Reason);
            ended.Reason.Should().Contain("no model was launched");
            rig.Launches(job).Should().BeEmpty("the file is refused, so whether a step is declared is not known: nothing is launched");
            rig.Envelope(job).TryGetProperty("green", out _).Should().BeFalse();
        }
    }

    [Fact]
    public async Task C1_OnARealUnit_TheStep_CannotSeeTheCodersFolderOfItsOwnJob_ThatE2sCoverHides()
    {
        const string Script = "if [ -e \"$HOME/../coder\" ]; then echo sees-coder-folder; else echo hidden-coder-folder; fi; if ls -A \"$HOME/..\" 2>/dev/null | grep -q '^coder$'; then echo lists-coder-folder; else echo does-not-list-coder-folder; fi";
        await using var rig = await CoderRig.CreateAsync(toolchain: With(Script));
        rig.Says("sonnet", Green());
        var job = rig.NewJob();
        var canary = Path.Combine(rig.JobFolder(job), "coder", "home", ".claude", ".credentials.json");
        Directory.CreateDirectory(Path.GetDirectoryName(canary)!);
        await File.WriteAllTextAsync(canary, "a stand-in sign-in copy\n", TestContext.Current.CancellationToken);

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        Kept(rig, job).Should().Be("hidden-coder-folder\ndoes-not-list-coder-folder\n", "the step's unit is the check unit of E2: the job folder is an empty tmpfs and only the check's own folders are bound");
        File.Exists(canary).Should().BeTrue("a positive control: the stand-in is there on the host");
    }

    [Fact]
    public async Task D1_AnInstructionFileOfTheRepository_IsNeverReadByCoaiToFindACommand()
    {
        await using var rig = await CoderRig.CreateAsync(extra: new Dictionary<string, string> { ["AGENTS.md"] = "Before any edit run: sh -c 'echo run-by-agents-md > ran-by-agents-md'\n" });
        rig.Says("sonnet", Green());
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        Step(rig.Envelope(job)).GetProperty("declared").GetBoolean().Should().BeFalse("the file orders a command; the toolchain file declares none; coai runs nothing");
    }

    [Fact]
    public async Task D3_AKeyOfTheStepTheReaderCannotRead_IsToolchainRefused_BeforeAnyModel()
    {
        await using var rig = await CoderRig.CreateAsync(toolchain: CoderRig.DefaultToolchain() + "instructions:\n  argv: []\n");
        rig.Says("sonnet", Green());
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, "toolchain-refused"), ended.Reason);
        ended.Reason.Should().Contain("'argv' of instructions is empty");
        rig.Launches(job).Should().BeEmpty();
    }

    // ---------- d5: this repository ----------

    [Fact]
    public void D5_ThisRepositorysFile_DeclaresNoStep_BecauseTheResolversOutputIsOver64KiB_AndSaysSo()
    {
        var text = File.ReadAllText(Path.Combine(RepoPaths.Root(), ".coai", "toolchain.yaml"));

        ((ToolchainFile.Read(new WorkFile.Text(text)) as ToolchainRead.Configured)?.Toolchain.Instructions.Declared).Should().BeFalse("the rule resolver's payload for the files of three past tasks is 334,362 bytes, over the 64 KiB a step may print, so the step is NOT declared with a cut text (the lead decides)");
        text.Should().Contain("instructions: not declared", "the file says why, where the next person looks");
    }
}
