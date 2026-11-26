using System.Diagnostics;
using CoaiMcp.Core.Toolchain;
using CoaiMcp.Core.Work;
using CoaiMcp.Runners.Collecting;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Toolchain;
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

        flow.Should().OnlyContain(line => (line.Contains("{jobs}", StringComparison.Ordinal) || line.Contains("{file}", StringComparison.Ordinal)) && line.TrimStart().StartsWith("- \"", StringComparison.Ordinal), "only the quoted {jobs} and {file} placeholders carry a brace");
    }

    [Fact]
    public async Task TheCommittedTestStep_StartsTheRealExecutable_AndItsRunIsGreen()
    {
        var executable = Path.Combine(RepoPaths.Root(), "src_mcp", "tests", "bin", "Debug", "net10.0", OperatingSystem.IsWindows() ? "CoaiMcp.Tests.exe" : "CoaiMcp.Tests");
        Assert.SkipUnless(File.Exists(executable), "the Debug executable the committed file names is not built here");
        using var logs = TempDir.For("coai-repo-checks-");
        var committed = Committed();
        var project = committed.Projects.Single();
        // The committed test command, narrowed to one cheap class, and no build: this run must not rebuild the binary it is running.
        var narrowed = committed with
        {
            Projects = [project with { Build = new ToolchainCommand(true, []), Test = new ToolchainCommand(true, [.. project.Test.Argv, "--filter-class", "*AToolchainSchemaAgreesWithTheReaderTests", "--progress", "off"]) }],
        };

        var result = await new CheckRunner(new ProcessLauncher()).RunAsync(new ToolchainRead.Configured(narrowed), new CheckRequest(RepoPaths.Root(), logs.At("run"), 2));

        var log = result.Steps.Count > 0 ? File.ReadAllText(result.Steps[0].LogPath) : string.Empty;
        result.Green.Should().BeTrue($"{result.Note}\n{log}");
        result.Steps.Should().ContainSingle().Which.Argv[0].Should().Be("./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests", "the committed argv, started from the repository root as the file writes it");
        log.Should().Contain("Test run summary: Passed!").And.Contain("failed: 0");
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
