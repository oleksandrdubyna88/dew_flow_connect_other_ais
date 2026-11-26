using CoaiMcp.Core.Toolchain;
using CoaiMcp.Runners.Collecting;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Toolchain;
using CoaiMcp.Work;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E4 s3 t3 (security): <see cref="ChecksAtBase"/> resolves the checks of a coder job through <see cref="ToolchainResolve"/> — the file at the base, or the manifests of the base and of the head, all read from the MAIN repository's objects — and a coder still cannot change its own checks.
/// Real units, real git, a fixture repository per test (skipped where there is no systemd user manager).
/// </summary>
[Collection("fakecli-env")]
public sealed class ADetectedChecksOfACoderJobTests
{
    private const string Library = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>";

    private const string NodeLock = "{ \"name\": \"p\", \"version\": \"1.0.0\", \"lockfileVersion\": 3, \"requires\": true, \"packages\": { \"\": { \"name\": \"p\", \"version\": \"1.0.0\" } } }";

    private const string NodePackage = "{ \"name\": \"p\", \"version\": \"1.0.0\", \"private\": true, \"type\": \"module\", \"scripts\": { \"build\": \"node -e 0\", \"test\": \"node --test\" } }";

    private const string NodeTest = "import test from 'node:test';\ntest('passes', () => {});\n";

    private const string NodeHomeVariable = "COAI_NODE_UNIT_HOME";

    private static void NeedAUserManager() => Assert.SkipUnless(SystemdUser.Works(out var why), why);

    private static ChecksAtBase Checks(BoundaryBed bed, BoundaryHost? host = null, string? detect = null) =>
        new(new GitHistory(bed.Launcher), new CheckRunner(bed.Launcher, bed.Boundary(host: host))) { Environment = name => name == ToolchainResolve.DetectVariable ? detect : null };

    private static async Task<CheckResult> RunAsync(BoundaryBed bed, ChecksAtBase checks)
    {
        using var logs = TempDir.For("coai-dc-logs-");

        return await checks.RunCoderJobAsync(new DiskWorkFolder(bed.Worktree), bed.BaseSha, bed.Job, logs.At("run"), 2);
    }

    /// <summary>Writes files into the worktree and commits them there: the head commit coai would have made.</summary>
    private static async Task CommitHeadAsync(BoundaryBed bed, IReadOnlyDictionary<string, string?> changes)
    {
        foreach (var (name, text) in changes)
        {
            var path = Path.Combine(bed.Worktree, name);
            if (text is null)
            {
                File.Delete(path);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, text);
            }
        }

        foreach (var args in new[] { new[] { "add", "-A" }, ["-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", "commit", "-q", "-m", "head"] })
        {
            var done = await bed.Launcher.RunAsync(new ProcessRequest("git", args, bed.Worktree) { Timeout = TimeSpan.FromSeconds(60) });
            done.ExitCode.Should().Be(0, done.StdErr);
        }
    }

    private static int UnitsStarted(BoundaryBed bed) => bed.Launcher.Requests.Count(request => request.Executable == "systemd-run" && request.Arguments.Any(argument => argument.Contains("-s1", StringComparison.Ordinal) || argument.Contains("-s2", StringComparison.Ordinal)));

    // ---- d1 (T1): detected steps in real units, to green ----

    [Fact]
    public async Task T1_ADotnetUnitWithNoFile_RunsTheDetectedSteps_InRealUnits_AndEndsGreen()
    {
        NeedAUserManager();
        await using var bed = await BoundaryBed.CreateAsync(files: new Dictionary<string, string> { ["App.csproj"] = Library, ["Class1.cs"] = "public class Class1 { }\n" });

        var result = await RunAsync(bed, Checks(bed));

        result.Green.Should().BeTrue(result.Note);
        result.Steps.Select(step => step.Step.Wire()).Should().Equal("restore", "build");
        result.Steps.Select(step => step.Argv[1]).Should().Equal("restore", "build");
        result.Steps.Select(step => step.Boundary.Network).Should().Equal("host", "none");
        result.Toolchain.Should().NotBeNull().And.Match<ResolvedToolchain>(toolchain => toolchain.Source == ToolchainSource.Detected && toolchain.Projects.Single().Id == "App.csproj");
    }

    [Fact]
    public async Task T1_ANodeProjectWithNoFile_RunsTheDetectedSteps_InRealUnits_AndEndsGreen()
    {
        NeedAUserManager();
        // The stand-in home must be hidden from the unit (ProtectHome=tmpfs hides /home), so it cannot be a temp folder: the operator names a new folder under /home, as for the other recorded runs.
        var homeFolder = Environment.GetEnvironmentVariable(NodeHomeVariable) ?? string.Empty;
        Assert.SkipWhen(homeFolder.Length == 0, $"set {NodeHomeVariable} to a new folder under /home to run the detected node steps in real units (a copy of node is made there; node must be under ~/.nvm in coai's PATH)");
        Directory.Exists(homeFolder).Should().BeFalse($"{homeFolder} must not exist: the run makes it");
        var nvm = Environment.GetEnvironmentVariable("PATH")?.Split(':').FirstOrDefault(entry => entry.Contains("/.nvm/versions/node/", StringComparison.Ordinal) && Directory.Exists(entry));
        Assert.SkipWhen(nvm is null, "node is not installed under ~/.nvm on this machine; the real-unit node step needs a copy of it");
        await using var bed = await BoundaryBed.CreateAsync(files: new Dictionary<string, string> { ["package.json"] = NodePackage, ["package-lock.json"] = NodeLock, ["t.test.js"] = NodeTest });
        var version = Path.GetDirectoryName(nvm!.TrimEnd('/'))!;
        var target = Path.Combine(homeFolder, ".nvm", "versions", "node", Path.GetFileName(version));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var copied = await bed.Launcher.RunAsync(new ProcessRequest("cp", ["-a", version, target], homeFolder) { Timeout = TimeSpan.FromMinutes(3) });
        copied.ExitCode.Should().Be(0, copied.StdErr);
        var host = bed.Host with { RealHome = homeFolder, Path = Path.Combine(target, "bin") };

        var result = await RunAsync(bed, Checks(bed, host));

        result.Green.Should().BeTrue(result.Note);
        result.Steps.Select(step => step.Step.Wire()).Should().Equal("restore", "build", "test");
        result.Steps.Select(step => step.Boundary.Network).Should().Equal("host", "none", "none");
        result.Steps.Should().OnlyContain(step => step.WorkingFolder == ".");
    }

    [Fact]
    public async Task AFileAtTheBase_StatesTheSteps_AndTheManifestsOfTheRepositoryDecideNothing()
    {
        NeedAUserManager();
        var toolchain = ToolchainFixture.Shell("echo built", "echo tested");
        await using var bed = await BoundaryBed.CreateAsync(toolchain, new Dictionary<string, string> { ["package.json"] = NodePackage, ["package-lock.json"] = NodeLock, ["Cargo.toml"] = "[package]\nname = \"x\"\n" });

        var result = await RunAsync(bed, Checks(bed));

        result.Green.Should().BeTrue(result.Note);
        result.Steps.Select(step => (step.Project, step.Step.Wire())).Should().Equal(("app", "build"), ("app", "test"));
        result.Toolchain!.Source.Should().Be(ToolchainSource.File);
        bed.Launcher.Requests.Should().NotContain(request => request.Arguments.Contains("ls-tree"), "a file that states every command reads no manifest");
    }

    // ---- d2 (D3): a head that deletes the test script, the lock file or a test project is refused, and no step runs ----

    [Fact]
    public async Task D3_AHeadThatDeletesTheTestScript_IsToolchainRefused_AndNoStepRuns()
    {
        NeedAUserManager();
        await using var bed = await BoundaryBed.CreateAsync(files: new Dictionary<string, string> { ["package.json"] = NodePackage, ["package-lock.json"] = NodeLock });
        await CommitHeadAsync(bed, new Dictionary<string, string?> { ["package.json"] = NodePackage.Replace(", \"test\": \"node --test\"", string.Empty, StringComparison.Ordinal) });

        var result = await RunAsync(bed, Checks(bed));

        result.State.Should().Be(CheckState.ToolchainRefused);
        result.Steps.Should().BeEmpty();
        result.Green.Should().BeFalse();
        result.Note.Should().Contain("frozen build definition", "the interim gate refuses it before the name-level compare is asked (second layer: ACoderCannotChangeADetectedCheckTests)");
        UnitsStarted(bed).Should().Be(0);
    }

    [Fact]
    public async Task D3_AHeadThatReplacesTheTestScriptWithEchoPassed_IsToolchainRefused_BeforeAnyStep()
    {
        NeedAUserManager();
        await using var bed = await BoundaryBed.CreateAsync(files: new Dictionary<string, string> { ["package.json"] = NodePackage, ["package-lock.json"] = NodeLock });
        await CommitHeadAsync(bed, new Dictionary<string, string?> { ["package.json"] = NodePackage.Replace("node --test", "echo passed", StringComparison.Ordinal) });

        var result = await RunAsync(bed, Checks(bed));

        result.State.Should().Be(CheckState.ToolchainRefused);
        result.Note.Should().Contain("frozen build definition");
        result.Steps.Should().BeEmpty();
        UnitsStarted(bed).Should().Be(0);
    }

    [Fact]
    public async Task D3_AHeadThatDeletesTheLockFile_IsToolchainRefused()
    {
        NeedAUserManager();
        await using var bed = await BoundaryBed.CreateAsync(files: new Dictionary<string, string> { ["package.json"] = NodePackage, ["package-lock.json"] = NodeLock });
        await CommitHeadAsync(bed, new Dictionary<string, string?> { ["package-lock.json"] = null });

        var result = await RunAsync(bed, Checks(bed));

        result.State.Should().Be(CheckState.ToolchainRefused);
        result.Note.Should().Contain("frozen build definition");
        UnitsStarted(bed).Should().Be(0);
    }

    [Fact]
    public async Task D3_AHeadThatDeletesATestProject_IsToolchainRefused()
    {
        NeedAUserManager();
        var tests = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><PackageReference Include=\"Microsoft.NET.Test.Sdk\" Version=\"17.14.1\" /></ItemGroup></Project>";
        await using var bed = await BoundaryBed.CreateAsync(files: new Dictionary<string, string> { ["App.slnx"] = "<Solution />", ["src/App.csproj"] = Library, ["tests/T.csproj"] = tests });
        await CommitHeadAsync(bed, new Dictionary<string, string?> { ["tests/T.csproj"] = null });

        var result = await RunAsync(bed, Checks(bed));

        result.State.Should().Be(CheckState.ToolchainRefused);
        result.Note.Should().Contain("frozen build definition");
        UnitsStarted(bed).Should().Be(0);
    }

    [Fact]
    public async Task D3_AHeadThatAddsACargoToml_IsToolchainRefused_ByTheNameOfTheStack()
    {
        NeedAUserManager();
        await using var bed = await BoundaryBed.CreateAsync(files: new Dictionary<string, string> { ["App.csproj"] = Library });
        await CommitHeadAsync(bed, new Dictionary<string, string?> { ["native/Cargo.toml"] = "[package]\nname = \"n\"\n" });

        var result = await RunAsync(bed, Checks(bed));

        result.State.Should().Be(CheckState.ToolchainRefused);
        result.Note.Should().Contain("rust").And.Contain("native/Cargo.toml");
    }

    [Fact]
    public async Task D3_AHeadThatAddsAToolchainFile_IsReportedAsAdded_AndRefusedByTheFrozenGate_NothingRuns()
    {
        NeedAUserManager();
        await using var bed = await BoundaryBed.CreateAsync(files: new Dictionary<string, string> { ["App.csproj"] = Library, ["Class1.cs"] = "public class Class1 { }\n" });
        await CommitHeadAsync(bed, new Dictionary<string, string?> { [".coai/toolchain.yaml"] = ToolchainFixture.Shell("echo ran-the-new-file > the-new-file-ran", "true") });

        var result = await RunAsync(bed, Checks(bed));

        result.ToolchainChange.Should().Be(ToolchainChange.Added);
        result.State.Should().Be(CheckState.ToolchainRefused, result.Note);
        result.Note.Should().Contain("frozen build definition");
        result.Steps.Should().BeEmpty();
        File.Exists(Path.Combine(bed.Worktree, "the-new-file-ran")).Should().BeFalse();
    }

    [Fact]
    public async Task D14_AProjectOnlyTheHeadHas_IsRefusedByTheFrozenGate_BeforeAnyStep()
    {
        NeedAUserManager();
        await using var bed = await BoundaryBed.CreateAsync(files: new Dictionary<string, string> { ["App.csproj"] = Library, ["Class1.cs"] = "public class Class1 { }\n", [".gitignore"] = "bin/\nobj/\n" });
        await CommitHeadAsync(bed, new Dictionary<string, string?> { ["Other.csproj"] = Library, ["Class2.cs"] = "public class Class2 { }\n" });

        var result = await RunAsync(bed, Checks(bed));

        result.State.Should().Be(CheckState.ToolchainRefused, result.Note);
        result.Note.Should().Contain("frozen build definition");
        result.Steps.Should().BeEmpty("nothing ran: the head added a project file, and that is a frozen build definition (interim rule, the owner decides)");
    }

    // ---- d3: the head is the COMMIT; nothing of the working tree is read ----

    [Fact]
    public async Task AnUntrackedOrIgnoredManifestInTheWorktree_ChangesNothing()
    {
        NeedAUserManager();
        await using var bed = await BoundaryBed.CreateAsync(files: new Dictionary<string, string> { ["App.csproj"] = Library, ["Class1.cs"] = "public class Class1 { }\n", [".gitignore"] = "ignored/\nbin/\nobj/\n" });
        Directory.CreateDirectory(Path.Combine(bed.Worktree, "ignored"));
        await File.WriteAllTextAsync(Path.Combine(bed.Worktree, "ignored", "Cargo.toml"), "[package]\n");
        await File.WriteAllTextAsync(Path.Combine(bed.Worktree, "package.json"), "{ not even json");
        await File.WriteAllTextAsync(Path.Combine(bed.Worktree, "Cargo.toml"), "[package]\n");

        var result = await RunAsync(bed, Checks(bed));

        result.Green.Should().BeTrue(result.Note);
        result.Steps.Select(step => step.Step.Wire()).Should().Equal("restore", "build");
    }

    [Fact]
    public async Task DetectionOff_GivesTheAnswerOfE2_NoFileIsNotConfigured()
    {
        NeedAUserManager();
        await using var bed = await BoundaryBed.CreateAsync(files: new Dictionary<string, string> { ["App.csproj"] = Library });

        var result = await RunAsync(bed, Checks(bed, detect: "off"));

        result.State.Should().Be(CheckState.NotConfigured);
        result.Steps.Should().BeEmpty();
        UnitsStarted(bed).Should().Be(0);
    }

    [Fact]
    public async Task ARepositoryWithNothingToCheck_IsNotConfigured_NeverGreen()
    {
        NeedAUserManager();
        await using var bed = await BoundaryBed.CreateAsync(files: new Dictionary<string, string> { ["README.md"] = "documents only\n" });

        var result = await RunAsync(bed, Checks(bed));

        result.State.Should().Be(CheckState.NotConfigured);
        result.Green.Should().BeFalse();
    }

    [Fact]
    public async Task TheWorkflowOfTheNoCoderJobRoad_IsUnchanged_ARunWithNoJobReadsTheFileOnly()
    {
        using var tree = TempDir.For("coai-dc-tree-");
        var launcher = new ProcessLauncher();
        await launcher.RunAsync(new ProcessRequest("git", ["init", "-q", "-b", "main"], tree.Path));
        await File.WriteAllTextAsync(Path.Combine(tree.Path, "package.json"), NodePackage);
        foreach (var args in new[] { new[] { "add", "-A" }, ["-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", "commit", "-q", "-m", "c"] })
        {
            await launcher.RunAsync(new ProcessRequest("git", args, tree.Path));
        }

        var sha = (await launcher.RunAsync(new ProcessRequest("git", ["rev-parse", "HEAD"], tree.Path))).StdOut.Trim();
        using var logs = TempDir.For("coai-dc-logs-");

        var result = await new ChecksAtBase(new GitHistory(launcher), new CheckRunner(launcher)).RunAsync(new DiskWorkFolder(tree.Path), sha, new CheckRequest(tree.Path, logs.At("run"), 2));

        result.State.Should().Be(CheckState.NotConfigured, "a run with no coder job is not detected: it has the file or nothing, as in E2");
    }

    // ---- S9: a change in a shared package alone runs the tests of the app that depends on it, and they go red (real node: needs COAI_NODE_UNIT_HOME) ----

    private const string SharedPackage = "{ \"name\": \"shared\", \"version\": \"1.0.0\", \"type\": \"module\", \"main\": \"index.js\" }";

    private static readonly string SharedLock = "{\"name\":\"shared\",\"version\":\"1.0.0\",\"lockfileVersion\":3,\"requires\":true,\"packages\":{\"\":{\"name\":\"shared\",\"version\":\"1.0.0\"}}}";

    private const string AppTest = "import test from 'node:test';\nimport assert from 'node:assert/strict';\nimport { value } from 'shared';\ntest('uses the shared value', () => assert.equal(value, 1));\n";

    private static async Task<BoundaryHost?> NodeHostAsync(BoundaryBed bed, string suffix)
    {
        var folder = Environment.GetEnvironmentVariable(NodeHomeVariable) ?? string.Empty;
        Assert.SkipWhen(folder.Length == 0, $"set {NodeHomeVariable} to a new folder under /home to run the node steps in real units");
        var nvm = Environment.GetEnvironmentVariable("PATH")?.Split(':').FirstOrDefault(entry => entry.Contains("/.nvm/versions/node/", StringComparison.Ordinal) && Directory.Exists(entry));
        Assert.SkipWhen(nvm is null, "node is not installed under ~/.nvm on this machine");
        var home = folder + suffix;
        Directory.Exists(home).Should().BeFalse($"{home} must not exist: the run makes it");
        var version = Path.GetDirectoryName(nvm!.TrimEnd('/'))!;
        var target = Path.Combine(home, ".nvm", "versions", "node", Path.GetFileName(version));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var copied = await bed.Launcher.RunAsync(new ProcessRequest("cp", ["-a", version, target], home) { Timeout = TimeSpan.FromMinutes(3) });
        copied.ExitCode.Should().Be(0, copied.StdErr);

        return bed.Host with { RealHome = home, Path = Path.Combine(target, "bin") };
    }

    [Fact]
    public async Task S9_AChangeInASharedPackageAlone_RunsTheTestsOfTheAppThatDependsOnItThroughAFileDependency_AndTheyGoRed()
    {
        NeedAUserManager();
        await using var bed = await BoundaryBed.CreateAsync(files: new Dictionary<string, string>
        {
            ["shared/package.json"] = SharedPackage, ["shared/package-lock.json"] = SharedLock, ["shared/index.js"] = "export const value = 1;\n",
            ["app/package.json"] = "{ \"name\": \"app\", \"version\": \"1.0.0\", \"private\": true, \"type\": \"module\", \"scripts\": { \"test\": \"node --test\" }, \"dependencies\": { \"shared\": \"file:../shared\" } }",
            ["app/package-lock.json"] = "{\n  \"name\": \"app\",\n  \"version\": \"1.0.0\",\n  \"lockfileVersion\": 3,\n  \"requires\": true,\n  \"packages\": {\n    \"\": {\n      \"name\": \"app\",\n      \"version\": \"1.0.0\",\n      \"dependencies\": {\n        \"shared\": \"file:../shared\"\n      }\n    },\n    \"../shared\": {\n      \"version\": \"1.0.0\"\n    },\n    \"node_modules/shared\": {\n      \"resolved\": \"../shared\",\n      \"link\": true\n    }\n  }\n}\n", ["app/app.test.js"] = AppTest,
        });
        var host = await NodeHostAsync(bed, "-s9-file");
        await CommitHeadAsync(bed, new Dictionary<string, string?> { ["shared/index.js"] = "export const value = 2;\n" });

        var result = await RunAsync(bed, Checks(bed, host));

        result.State.Should().Be(CheckState.Failed, result.Note);
        result.Selection!.Everything.Should().BeFalse();
        result.Selection.Runs("app/package.json").Should().BeTrue("a node project always runs: nothing in a manifest shows that a change in shared does not reach it");
        result.Steps.Last().Should().Match<StepRecord>(step => step.Project == "app/package.json" && step.Step == CheckStep.Test && step.ExitCode != 0);
    }

    [Fact]
    public async Task S9_AChangeInAWorkspaceMemberAlone_RunsTheRootsTests_AndTheyGoRed()
    {
        NeedAUserManager();
        await using var bed = await BoundaryBed.CreateAsync(files: new Dictionary<string, string>
        {
            ["package.json"] = "{ \"name\": \"mono\", \"version\": \"1.0.0\", \"private\": true, \"type\": \"module\", \"workspaces\": [\"shared\", \"app\"], \"scripts\": { \"test\": \"node --test app\" } }",
            ["package-lock.json"] = "{\n  \"name\": \"mono\",\n  \"version\": \"1.0.0\",\n  \"lockfileVersion\": 3,\n  \"requires\": true,\n  \"packages\": {\n    \"\": {\n      \"name\": \"mono\",\n      \"version\": \"1.0.0\",\n      \"workspaces\": [\n        \"shared\",\n        \"app\"\n      ]\n    },\n    \"app\": {\n      \"version\": \"1.0.0\",\n      \"dependencies\": {\n        \"shared\": \"1.0.0\"\n      }\n    },\n    \"node_modules/app\": {\n      \"resolved\": \"app\",\n      \"link\": true\n    },\n    \"node_modules/shared\": {\n      \"resolved\": \"shared\",\n      \"link\": true\n    },\n    \"shared\": {\n      \"version\": \"1.0.0\"\n    }\n  }\n}\n",
            ["shared/package.json"] = SharedPackage, ["shared/index.js"] = "export const value = 1;\n",
            ["app/package.json"] = "{ \"name\": \"app\", \"version\": \"1.0.0\", \"private\": true, \"type\": \"module\", \"dependencies\": { \"shared\": \"1.0.0\" } }", ["app/app.test.js"] = AppTest,
        });
        var host = await NodeHostAsync(bed, "-s9-workspace");
        await CommitHeadAsync(bed, new Dictionary<string, string?> { ["shared/index.js"] = "export const value = 2;\n" });

        var result = await RunAsync(bed, Checks(bed, host));

        result.State.Should().Be(CheckState.Failed, result.Note);
        result.Toolchain!.Projects.Should().ContainSingle("a workspace root is one project and its members are not projects");
        result.Steps.Last().Should().Match<StepRecord>(step => step.Project == "package.json" && step.Step == CheckStep.Test && step.ExitCode != 0);
    }
}
