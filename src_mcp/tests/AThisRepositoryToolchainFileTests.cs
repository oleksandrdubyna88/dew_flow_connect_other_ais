using System.Diagnostics;
using CoaiMcp.Core.Toolchain;
using CoaiMcp.Core.Work;
using CoaiMcp.Runners.Collecting;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Toolchain;
using CoaiMcp.Runners.Worktrees;
using CoaiMcp.Work;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// This repository's own <c>.coai/toolchain.yaml</c> (epoch 3 E2 s1 t4): the <c>src_mcp</c> build and the MTP test executable — never <c>dotnet test</c>, which has no VSTest host here and aborts. It is the
/// fixture of the whole epic: the one repository a coder job is run on, so its checks must start a real test.
/// </summary>
/// <remarks>
/// The check runner is run on this file in two ways. Always, in <see cref="TheCommittedTestStep_StartsTheRealExecutable_AndItsRunIsGreen"/>: the committed test command with a narrowing filter, in the repository
/// root, on the executable this very run was built into. On request, in <see cref="ARecordedRunOnACleanCheckout"/>: the whole file on a clean clone of HEAD, build and the full suite, which takes minutes and
/// is the recorded run of the task — set <c>COAI_RECORD_CLEAN_CHECKOUT_RUN</c> to a folder for its logs. It is not part of the ordinary run: it would build while the suite that runs it is running.
/// </remarks>
[Collection("fakecli-env")]
public sealed class AThisRepositoryToolchainFileTests
{
    private const string RecordVariable = "COAI_RECORD_CLEAN_CHECKOUT_RUN";

    private static string CommittedText() => File.ReadAllText(Path.Combine(RepoPaths.Root(), ".coai", "toolchain.yaml"));

    private static RepoToolchain Committed()
    {
        var read = ToolchainFile.Read(new WorkFile.Text(CommittedText()));

        return read.Should().BeOfType<ToolchainRead.Configured>($"the committed file must pass the real reader: {(read as ToolchainRead.Refused)?.Sentence}").Subject.Toolchain;
    }

    [Fact]
    public void TheCommittedFile_IsReadByTheReader_AndHasTheSrcMcpProject_WithABuildAndATestArgv()
    {
        var toolchain = Committed();

        var project = toolchain.Projects.Should().ContainSingle().Subject;
        project.Path.Should().Be("src_mcp");
        project.Adapter.Should().Be("dotnet");
        project.Build.Argv.Should().Equal("dotnet", "build", "dew_flow_connect_other_ais.slnx", "-c", "Debug", "-m:{jobs}");
        project.Test.Stated.Should().BeTrue();
        project.Test.Argv.Should().ContainSingle("the test command is the executable alone: the runner decides nothing about its filter");
    }

    [Fact]
    public void TheCommittedFile_NamesTheTestFolderAndTheSelectedRunCommand_AndTheBuildAndTestArgvAreUnchanged()
    {
        // Epoch 3 E4 s5 t6 d1 (the owner's answer to question 3 of SPEC section N): the tests of src_mcp/tests are selected by file. A folder prefix of testFiles is relative to the project (src_mcp), so it is written tests.
        var project = Committed().Projects.Should().ContainSingle().Subject;

        project.TestFiles.Should().Equal("tests");
        project.TestFor.Stated.Should().BeTrue();
        project.TestFor.Argv.Should().Contain("{stem}", "the class of a test file is named by the stem of the file");
        project.TestFor.Argv.Should().NotContain("{file}");
        project.Restore.Argv.Should().Equal("dotnet", "restore", "dew_flow_connect_other_ais.slnx");
        project.Build.Argv.Should().Equal("dotnet", "build", "dew_flow_connect_other_ais.slnx", "-c", "Debug", "-m:{jobs}");
        project.Test.Argv.Should().Equal("./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests");
        project.Path.Should().Be("src_mcp");
        Committed().Projects.Should().ContainSingle("src_vs_code is not a project of this file: its tests need a compile step and its scenarios a VS Code host");
        CommittedText().Should().Contain("src_vs_code");
    }

    [Fact]
    public async Task TheSelectedRunCommand_GivenAChangedTestFile_StartsOnlyThatClass_AndItIsGreen()
    {
        var executable = Path.Combine(RepoPaths.Root(), "src_mcp", "tests", "bin", "Debug", "net10.0", OperatingSystem.IsWindows() ? "CoaiMcp.Tests.exe" : "CoaiMcp.Tests");
        Assert.SkipUnless(File.Exists(executable) && !OperatingSystem.IsWindows(), "the Debug executable the committed file names is not built here");
        var project = Committed().Projects.Single();
        var selected = TestSelection.Select(["src_mcp/tests/AToolchainSchemaAgreesWithTheReaderTests.cs", "src_mcp/src/Server/Program.cs", "src_vs_code/tests/Other.ts"], project.Path, project.TestFiles);
        selected.State.Should().Be("files");
        selected.Files.Should().Equal("src_mcp/tests/AToolchainSchemaAgreesWithTheReaderTests.cs");
        var argv = TestSelection.Argv(project.TestFor.Argv, selected.Files, ".", 4);

        var result = await new ProcessLauncher().RunAsync(new ProcessRequest(argv[0], [.. argv.Skip(1)], RepoPaths.Root()) { Timeout = TimeSpan.FromMinutes(2) });

        result.ExitCode.Should().Be(0, result.StdOut + result.StdErr);
        result.StdOut.Should().Contain("Test run summary: Passed!").And.NotContain("total: 0");
    }

    [Fact]
    public void TheTestArgv_NamesTheMtpExecutableOfTheTestProject_NeverDotnetTest()
    {
        var test = Committed().Projects.Single().Test.Argv;

        test[0].Should().Be($"./src_mcp/tests/bin/Debug/net10.0/{typeof(AThisRepositoryToolchainFileTests).Assembly.GetName().Name}", "the executable the test project builds, named by its own assembly");
        test.Should().NotContain("test", "`dotnet test` aborts here: there is no VSTest host");
        Path.GetFileName(test[0]).Should().NotBe("dotnet");
    }

    [Fact]
    public void TheBuildArgv_NamesASolutionThatIsInTheRepository_AndTheProjectFolderExists()
    {
        var root = RepoPaths.Root();

        File.Exists(Path.Combine(root, Committed().Projects.Single().Build.Argv[2])).Should().BeTrue("a build of a file that is not there checks nothing");
        Directory.Exists(Path.Combine(root, Committed().Projects.Single().Path)).Should().BeTrue();
    }

    [Fact]
    public void TheCommittedFile_IsOfTheStrictSubset_NoFlowListButTheEmptyOne()
    {
        // The strict reader refuses a flow list or map by name (a test of it is AToolchainFileTests); this holds the committed text to it where it can be seen without the reader.
        var flow = CommittedText().Split('\n').Where(line => !line.TrimStart().StartsWith('#')).Where(line => line.Contains('[') || line.Contains('{')).ToList();

        flow.Should().OnlyContain(line => (line.Contains("{jobs}", StringComparison.Ordinal) || line.Contains("{file}", StringComparison.Ordinal) || line.Contains("{stem}", StringComparison.Ordinal)) && line.TrimStart().StartsWith("- \"", StringComparison.Ordinal), "only the quoted {jobs}, {file} and {stem} placeholders carry a brace");
    }

    [Fact]
    public async Task TheCommittedTestStep_StartsTheRealExecutable_AndItsRunIsGreen()
    {
        var executable = Path.Combine(RepoPaths.Root(), "src_mcp", "tests", "bin", "Debug", "net10.0", OperatingSystem.IsWindows() ? "CoaiMcp.Tests.exe" : "CoaiMcp.Tests");
        Assert.SkipUnless(File.Exists(executable), "the Debug executable the committed file names is not built here");
        using var logs = TempDir.For("coai-repo-checks-");
        var committed = Committed();
        var project = committed.Projects.Single();
        // The committed test command, narrowed to one cheap class, and no restore or build: this run must not rebuild the binary it is running.
        var narrowed = committed with
        {
            Projects = [project with { Restore = new ToolchainCommand(true, []), Build = new ToolchainCommand(true, []), Test = new ToolchainCommand(true, [.. project.Test.Argv, "--filter-class", "*AToolchainSchemaAgreesWithTheReaderTests", "--progress", "off"]) }],
        };

        var result = await new CheckRunner(new ProcessLauncher()).RunAsync(new ToolchainRead.Configured(narrowed), new CheckRequest(RepoPaths.Root(), logs.At("run"), 2));

        var log = result.Steps.Count > 0 ? File.ReadAllText(result.Steps[0].LogPath) : string.Empty;
        result.Green.Should().BeTrue($"{result.Note}\n{log}");
        result.Steps.Should().ContainSingle().Which.Argv[0].Should().Be("./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests", "the committed argv, started from the repository root as the file writes it");
        log.Should().Contain("Test run summary: Passed!").And.Contain("failed: 0");
    }

    private const string SelectedRunVariable = "COAI_RECORD_SELECTED_RUN";

    private const string ProbeFile = "src_mcp/tests/ASelectedRunProbeTests.cs";

    private static string ProbeText(bool fixedProbe) =>
        "using Xunit;\n\nnamespace CoaiMcp.Tests;\n\npublic sealed class ASelectedRunProbeTests\n{\n    [Fact]\n    public void TheProbe() => "
        + (fixedProbe ? "Assert.True(true);" : "Assert.Fail(\"the probe class is red on purpose (E4 s5 t6 d2)\");")
        + "\n}\n";

    private static async Task GitInAsync(IProcessLauncher launcher, string folder, params string[] args)
    {
        var done = await launcher.RunAsync(new ProcessRequest("git", ["-c", "user.name=recorded-run", "-c", "user.email=recorded-run@example.invalid", "-c", "commit.gpgsign=false", .. args], folder) { Timeout = TimeSpan.FromMinutes(10) });
        done.ExitCode.Should().Be(0, $"git {string.Join(' ', args)}: {done.StdErr}");
    }

    private static string Describe(string label, CheckResult result, TimeSpan total, List<string> lines)
    {
        lines.Add($"{label}: {result.State.Wire()} in {total} - {result.Note}");
        var summary = new List<string>();
        foreach (var step in result.Steps)
        {
            lines.Add($"  {step.Project} {step.Step} scope {step.Scope} (files {step.ScopeFiles}): exit {step.ExitCode}, {step.Duration}, nothingSelected {step.NothingSelected}, network {step.Boundary.Network}, argv {string.Join(' ', step.Argv)}, log {step.LogPath}");
            var log = File.ReadAllText(step.LogPath);
            summary.AddRange(log.Split('\n').Where(line => line.Contains("Test run summary", StringComparison.Ordinal) || new[] { "total:", "failed:", "succeeded:", "skipped:", "failed CoaiMcp" }.Any(start => line.TrimStart().StartsWith(start, StringComparison.Ordinal))).Select(line => "    " + line.Trim()));
            lines.AddRange(summary);
            summary.Clear();
        }

        return string.Join('\n', result.Steps.Where(step => step.Step == CheckStep.Test).Select(step => File.ReadAllText(step.LogPath)));
    }

    /// <summary>
    /// The recorded run of the task (E4 s5 t6 d2, S8), under the boundary, on a clean clone, with no model: the commits are made here. Run one: a commit that adds ONE failing test class gives a red SELECTED run with that test in the digest and no full run.
    /// Run two: the fix gives a green selected run and then the full suite, green. Skipped unless <see cref="SelectedRunVariable"/> names an absent folder on a real disk (not /tmp); it builds and runs the whole suite.
    /// </summary>
    [Fact]
    public async Task ARecordedSelectedRunUnderTheBoundary()
    {
        var folder = Environment.GetEnvironmentVariable(SelectedRunVariable) ?? string.Empty;
        Assert.SkipWhen(folder.Length == 0, $"set {SelectedRunVariable} to a new folder on a real disk with a short path to record the selected run and the full suite of this repository inside the boundary (it builds and runs the whole suite)");
        Assert.SkipUnless(SystemdUser.Works(out var why), why);
        Directory.Exists(folder).Should().BeFalse($"{folder} must not exist: the run makes it");
        var launcher = new ProcessLauncher();
        var main = Path.Combine(folder, "m");
        Directory.CreateDirectory(folder);
        await GitInAsync(launcher, folder, "clone", "--no-hardlinks", "--quiet", RepoPaths.Root(), main);
        await GitInAsync(launcher, main, "submodule", "update", "--init", "--depth", "1", ".agents/conventions");
        var baseSha = (await launcher.RunAsync(new ProcessRequest("git", ["rev-parse", "HEAD"], main))).StdOut.Trim();
        var started = await new CoderWorktree(launcher, Path.Combine(folder, "c")).StartAsync(main, "m1", baseSha);
        var tree = started.Should().BeOfType<CoderWorktreeOutcome.Started>(started.ToString()).Subject;
        await GitInAsync(launcher, tree.Path, "submodule", "update", "--init", "--depth", "1", ".agents/conventions");
        var npm = await launcher.RunAsync(new ProcessRequest("npm", ["ci", "--ignore-scripts", "--prefix", ".agents/conventions"], tree.Path) { Timeout = TimeSpan.FromMinutes(5) });
        npm.ExitCode.Should().Be(0, npm.StdErr);
        var opened = CoderTree.Open(tree.Path, main).Should().BeOfType<CoderTreeOpening.Opened>().Subject.Tree;
        var checks = new ChecksAtBase(new GitHistory(launcher), new CheckRunner(launcher, new CheckBoundary(launcher, BoundaryHost.Current())));
        var lines = new List<string> { $"recorded selected run of .coai/toolchain.yaml, base {baseSha}, worktree {tree.Path}" };
        BoundaryJob JobFor(string name) => BoundaryJob.ForCoder("m1", opened, Path.Combine(folder, name)) with { MemoryMegabytes = 8000 };

        // Run one: a commit that adds one failing test class.
        await File.WriteAllTextAsync(Path.Combine(tree.Path, ProbeFile), ProbeText(fixedProbe: false));
        await GitInAsync(launcher, tree.Path, "add", ProbeFile);
        await GitInAsync(launcher, tree.Path, "commit", "--quiet", "-m", "test: one failing test class");
        var clock = Stopwatch.StartNew();
        var red = await checks.RunCoderJobAsync(new DiskWorkFolder(tree.Path), baseSha, JobFor("j1"), Path.Combine(folder, "logs1"), 4);
        var redText = Describe("run 1 (the failing class)", red, clock.Elapsed, lines);

        // Run two: the fix.
        await File.WriteAllTextAsync(Path.Combine(tree.Path, ProbeFile), ProbeText(fixedProbe: true));
        await GitInAsync(launcher, tree.Path, "add", ProbeFile);
        await GitInAsync(launcher, tree.Path, "commit", "--quiet", "-m", "test: the fix");
        clock.Restart();
        var green = await checks.RunCoderJobAsync(new DiskWorkFolder(tree.Path), baseSha, JobFor("j2"), Path.Combine(folder, "logs2"), 4);
        Describe("run 2 (the fix)", green, clock.Elapsed, lines);
        await File.WriteAllLinesAsync(Path.Combine(folder, "RESULT.txt"), lines);
        foreach (var line in lines)
        {
            Console.Error.WriteLine(line);
        }

        red.State.Should().Be(CheckState.Failed, red.Note);
        red.Steps.Select(step => (step.Step, step.Scope, step.ExitCode == 0)).Should().Equal((CheckStep.Build, "full", true), (CheckStep.Test, "selected", false));  // a red selected run ends the round: the full command is not run
        red.Steps[1].ScopeFiles.Should().Be(1);
        red.Steps[1].Argv.Should().Contain("ASelectedRunProbeTests", "the testFor wrapper is given the stem of the added file");
        FailureDigest.Of(CheckStep.Test, "dotnet", redText, tree.Path).Items.Should().Contain(item => item.Code.Contains("ASelectedRunProbeTests", StringComparison.Ordinal), "the failing test is in the digest");
        green.State.Should().Be(CheckState.Green, green.Note);
        green.Steps.Select(step => (step.Step, step.Scope, step.ExitCode)).Should().Equal((CheckStep.Build, "full", 0), (CheckStep.Test, "selected", 0), (CheckStep.Test, "full", 0));
    }

    /// <summary>
    /// The recorded run of the task (t4 d2): the checks of THIS repository, at HEAD, on a clean clone — the build step ends with exit 0 and the test step starts the real executable of the suite. Skipped unless
    /// <see cref="RecordVariable"/> names a folder for the logs; it runs for minutes.
    /// </summary>
    [Fact]
    public async Task ARecordedRunOnACleanCheckout()
    {
        var logs = Environment.GetEnvironmentVariable(RecordVariable) ?? string.Empty;
        Assert.SkipWhen(logs.Length == 0, $"set {RecordVariable} to a folder to record the run of the toolchain on a clean checkout (it builds and runs the whole suite)");
        var launcher = new ProcessLauncher();
        await using var clone = await TempGitRepo.CloneAsync(launcher, RepoPaths.Root(), "coai-clean-checkout-");
        // What CI does before it builds (ci.yml): the rules mount, and the node packages the tests that read it need. A checkout is "clean" the way a fresh clone of this repository is, not less.
        await clone.GitAsync("submodule", "update", "--init", "--depth", "1", ".agents/conventions");
        var npm = await launcher.RunAsync(new ProcessRequest("npm", ["ci", "--ignore-scripts", "--prefix", ".agents/conventions"], clone.Path) { Timeout = TimeSpan.FromMinutes(5) });
        npm.ExitCode.Should().Be(0, npm.StdErr);
        var head = await clone.HeadAsync();
        var checks = new ChecksAtBase(new GitHistory(launcher), new CheckRunner(launcher));
        var clock = Stopwatch.StartNew();

        var result = await checks.RunAsync(new DiskWorkFolder(clone.Path), head, new CheckRequest(clone.Path, logs, 4));

        Console.Error.WriteLine($"recorded run of .coai/toolchain.yaml at {head} on a clean clone, {clock.Elapsed}: {result.State.Wire()} — {result.Note}");
        foreach (var step in result.Steps)
        {
            Console.Error.WriteLine($"  {step.Project} {step.Step}: exit {step.ExitCode}, {step.Duration}, log {step.LogPath}, argv {string.Join(' ', step.Argv)}");
        }

        result.Steps.Should().NotBeEmpty();
        result.Steps[0].Step.Should().Be(CheckStep.Build);
        result.Steps[0].ExitCode.Should().Be(0, "the build of a clean checkout");
        result.Steps.Should().HaveCount(2, "the test step was started");
    }
}
