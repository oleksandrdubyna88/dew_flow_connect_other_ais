using CoaiMcp.Core.Toolchain;
using CoaiMcp.Runners.Toolchain;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E4 s3 t2 (security): the step plan of a resolved toolchain — restore, build, typecheck, test — runs every step of a coder job through the check boundary of E2, with the network state of the table, the project folder as the working folder, and no property of the unit changed. Real units
/// (skipped where there is no systemd user manager) and pure checks of the plan.
/// </summary>
[Collection("fakecli-env")]
public sealed class AStepPlanRunsInsideTheBoundaryTests
{
    private static void NeedAUserManager() => Assert.SkipUnless(SystemdUser.Works(out var why), why);

    private static ToolchainProject FileProject(string path, string adapter, string? restore, string build, string test) =>
        new(path, adapter, new ToolchainCommand(true, ["/bin/sh", "-c", build]), new ToolchainCommand(true, ["/bin/sh", "-c", test]), null)
        {
            Restore = restore is null ? ToolchainCommand.NotStated : new ToolchainCommand(true, ["/bin/sh", "-c", restore]),
        };

    private static DetectedStep Step(string name, string folder, params string[] argv) =>
        new(name, argv, name == "restore" ? DefaultNetwork.Host : DefaultNetwork.None, folder, "web/package.json", []);

    private static DetectedProject Detected(string folder, params DetectedStep[] steps) =>
        new("node", $"{folder}/package.json", folder, steps, [new("packageManager", "npm")], [], TestsNone: false);

    private static ResolvedToolchain Resolved(ToolchainSource source, RepoToolchain? file, params ResolvedProject[] projects) => new(source, projects, file, [], []);

    private static ResolvedProject DetectedProjectOf(DetectedProject project, string origin = "detected") => new(project.Id, origin, false, null, project);

    private static ResolvedProject OfFile(ToolchainProject project) => new(project.Path, "file", false, project, null);

    private static ToolchainResolution.Resolved Wrap(ResolvedToolchain toolchain) => new(toolchain);

    // ---- d1: names, order, the first failure ends the run, nothing to check is not_configured ----

    [Fact]
    public void ThePlan_HasTheStepsOfEveryProjectInTheProjectOrder_AndInAProjectInTheOrderOfARound()
    {
        var web = Detected("web", Step("restore", "web", "npm", "ci"), Step("build", "web", "npm", "run", "build"), Step("typecheck", "web", "npm", "run", "typecheck"), Step("test", "web", "npm", "test"));
        var file = FileProject("app", "dotnet", "echo restore", "echo build", "echo test");
        var toolchain = Resolved(ToolchainSource.FileAndDefaults, new RepoToolchain([file], [], ToolchainTimeouts.Default), OfFile(file), DetectedProjectOf(web, "defaults"));

        var plan = StepPlan.Of(toolchain, ToolchainDefaults.Current, 3);

        plan.Select(step => (step.Project, step.Step)).Should().Equal(("app", CheckStep.Restore), ("app", CheckStep.Build), ("app", CheckStep.Test), ("web/package.json", CheckStep.Restore), ("web/package.json", CheckStep.Build), ("web/package.json", CheckStep.Typecheck), ("web/package.json", CheckStep.Test));
        plan.Select(step => step.Step.Wire()).Distinct().Should().BeSubsetOf(["restore", "build", "typecheck", "test"]);
    }

    [Fact]
    public void JobsIsReplacedInEveryItem_AndEveryArgvIsAListOfItems_ForNoShell()
    {
        var file = new ToolchainProject("app", "dotnet", new ToolchainCommand(true, ["dotnet", "build", "-m:{jobs}", "--x={jobs}{jobs}"]), ToolchainCommand.NotStated, null);

        var plan = StepPlan.OfFile(new RepoToolchain([file], [], ToolchainTimeouts.Default), 4);

        plan.Single().Argv.Should().Equal("dotnet", "build", "-m:4", "--x=44");
    }

    [Fact]
    public void SkipOfRestore_TakesThatStepOutOfADefaultedProject_ButNotOutOfAProjectThatStatesIt()
    {
        var web = Detected("web", Step("restore", "web", "npm", "ci"), Step("test", "web", "npm", "test"));
        var stated = FileProject("app", "dotnet", "echo restore", "echo build", "echo test");
        var toolchain = Resolved(ToolchainSource.FileAndDefaults, new RepoToolchain([stated], ["restore"], ToolchainTimeouts.Default), OfFile(stated), DetectedProjectOf(web, "defaults"));

        var plan = StepPlan.Of(toolchain, ToolchainDefaults.Current, 2);

        plan.Where(step => step.Project == "web/package.json").Select(step => step.Step).Should().Equal(CheckStep.Test);
        plan.Where(step => step.Project == "app").Select(step => step.Step).Should().Contain(CheckStep.Restore);
    }

    [Fact]
    public void RestoreAndTypecheckTakeTheBuildTimeout_TestTakesTheTestTimeout()
    {
        var web = Detected("web", Step("restore", "web", "npm", "ci"), Step("typecheck", "web", "tsc"), Step("test", "web", "npm", "test"));
        var timeouts = new ToolchainTimeouts(TimeSpan.FromMinutes(7), TimeSpan.FromMinutes(11));

        var plan = StepPlan.Of(Resolved(ToolchainSource.FileAndDefaults, new RepoToolchain([], [], timeouts), DetectedProjectOf(web)), ToolchainDefaults.Current, 2);

        plan.Select(step => step.Timeout).Should().Equal(TimeSpan.FromMinutes(7), TimeSpan.FromMinutes(7), TimeSpan.FromMinutes(11));
    }

    [Fact]
    public async Task APlanWithOnlyARestore_IsNotConfigured_NeverGreen_AndNothingStarts()
    {
        var web = Detected("web", Step("restore", "web", "/bin/true"));
        var started = 0;
        var runner = new CheckRunner(new CoaiMcp.Runners.Processes.ProcessLauncher());

        var result = await runner.RunResolvedAsync(Wrap(Resolved(ToolchainSource.Detected, null, DetectedProjectOf(web))), new CheckRequest("/nonexistent", "/nonexistent/logs", 2));

        result.State.Should().Be(CheckState.NotConfigured);
        result.Green.Should().BeFalse();
        result.Steps.Should().BeEmpty();
        started.Should().Be(0);
    }

    [Fact]
    public async Task NotConfiguredAndRefused_PlanNoStep()
    {
        var runner = new CheckRunner(new CoaiMcp.Runners.Processes.ProcessLauncher());
        var request = new CheckRequest("/nonexistent", "/nonexistent/logs", 2);

        (await runner.RunResolvedAsync(new ToolchainResolution.NotConfigured("nothing found"), request)).Should().Match<CheckResult>(result => result.State == CheckState.NotConfigured && result.Steps.Count == 0 && !result.Green);
        (await runner.RunResolvedAsync(new ToolchainResolution.Refused("the worktree changed scripts.test of web/package.json"), request)).Should().Match<CheckResult>(result => result.State == CheckState.ToolchainRefused && result.Steps.Count == 0 && !result.Green && result.Note.Contains("scripts.test"));
    }

    [Fact]
    public async Task TheFirstStepThatDoesNotPass_EndsTheRun_InRealUnits()
    {
        NeedAUserManager();
        await using var bed = await BoundaryBed.CreateAsync();
        var file = FileProject("app", "dotnet", "echo restored", "exit 3", "echo never");
        var logs = TempDir.For("coai-sp-logs-");

        var result = await new CheckRunner(bed.Launcher, bed.Boundary()).RunResolvedAsync(Wrap(Resolved(ToolchainSource.File, new RepoToolchain([file], [], ToolchainTimeouts.Default), OfFile(file))), new CheckRequest(bed.Worktree, logs.At("run"), 2) { Job = bed.Job });

        using (logs)
        {
            result.State.Should().Be(CheckState.Failed);
            result.Steps.Select(step => (step.Step, step.ExitCode)).Should().Equal((CheckStep.Restore, 0), (CheckStep.Build, 3));
            result.Note.Should().Contain("the build step of app exited 3").And.Contain("were not run");
        }
    }

    // ---- d2 (T6): the network of every step and its reason, in every record ----

    [Fact]
    public async Task T6_RestoreRunsOnTheHostNetwork_BuildOfAFileProjectOnTheHost_TypecheckAndTestWithOnlyALoopbackDevice_EachRecordHoldsItsReason()
    {
        NeedAUserManager();
        await using var bed = await BoundaryBed.CreateAsync();
        var file = FileProject("app", "dotnet", "echo restore", "echo build", "echo test");
        var web = Detected("web", Step("restore", ".", "/bin/sh", "-c", "echo r"), Step("build", ".", "/bin/sh", "-c", "echo b"), Step("typecheck", ".", "/bin/sh", "-c", "echo t"), Step("test", ".", "/bin/sh", "-c", "echo x"));
        var logs = TempDir.For("coai-sp-logs-");
        var toolchain = Resolved(ToolchainSource.FileAndDefaults, new RepoToolchain([file], [], ToolchainTimeouts.Default), OfFile(file), DetectedProjectOf(web, "defaults"));

        var result = await new CheckRunner(bed.Launcher, bed.Boundary()).RunResolvedAsync(Wrap(toolchain), new CheckRequest(bed.Worktree, logs.At("run"), 2) { Job = bed.Job with { BuildNetwork = NetworkChoice.OnHost("the job's own build state"), TestNetwork = NetworkChoice.None("the job's own test state") } });

        using (logs)
        {
            result.Green.Should().BeTrue(result.Note);
            result.Steps.Select(step => (step.Project, step.Step.Wire(), step.Boundary.Network)).Should().Equal(
                ("app", "restore", "host"), ("app", "build", "host"), ("app", "test", "none"),
                ("web/package.json", "restore", "host"), ("web/package.json", "build", "none"), ("web/package.json", "typecheck", "none"), ("web/package.json", "test", "none"));
            result.Steps.Should().OnlyContain(step => step.Boundary.NetworkReason.Length > 0 && step.Boundary.Bounded);
            result.Steps[0].Boundary.NetworkReason.Should().Contain("fetches packages", "a restore of a file project says why it has the network");
            result.Steps[1].Boundary.NetworkReason.Should().Be("the job's own build state");
            result.Steps[3].Boundary.NetworkReason.Should().Contain("shared/toolchains.json").And.Contain("RESULTS_s1_node.md");
            result.Steps[4].Boundary.NetworkReason.Should().Contain("shared/toolchains.json").And.Contain("loopback");
            result.Steps[2].Boundary.Properties.Should().Contain("PrivateNetwork=yes");
            result.Steps[0].Boundary.Properties.Should().Contain("PrivateNetwork=no");
        }
    }

    [Fact]
    public async Task ARestoreOfAFileProject_HasTheHostNetwork_EvenWhenTheJobsBuildHasNone()
    {
        NeedAUserManager();
        await using var bed = await BoundaryBed.CreateAsync();
        var file = FileProject("app", "node", "echo restore", "echo build", "echo test");
        var logs = TempDir.For("coai-sp-logs-");

        var result = await new CheckRunner(bed.Launcher, bed.Boundary()).RunResolvedAsync(Wrap(Resolved(ToolchainSource.File, new RepoToolchain([file], [], ToolchainTimeouts.Default), OfFile(file))), new CheckRequest(bed.Worktree, logs.At("run"), 2) { Job = bed.Job });

        using (logs)
        {
            result.Steps[0].Boundary.Network.Should().Be("host");
            result.Steps[1].Boundary.Network.Should().Be("none", "the job's own build state is the bed's (a loopback device only)");
        }
    }

    // ---- d2: no road around CheckBoundary ----

    [Fact]
    public void NoCodeOfTheToolchainFolder_StartsAProcessButTheFourKnownRoads_AndTheScanSeesThem()
    {
        var folder = Path.Combine(RepoPaths.Root(), "src_mcp", "runners", "Toolchain");
        var starters = Directory.GetFiles(folder, "*.cs").Where(path => File.ReadAllText(path).Contains("new ProcessRequest(", StringComparison.Ordinal)).Select(Path.GetFileName).Order().ToList();

        starters.Should().Equal("CheckBoundary.cs", "CheckRunner.cs", "CoderBoundary.cs", "UnitControl.cs");
        File.ReadAllText(Path.Combine(folder, "CheckRunner.cs")).Split("launcher.RunAsync(").Length.Should().Be(2, "the one unbounded launch of the runner is in RunStepAsync, reached only when the request names no coder job");
        File.ReadAllText(Path.Combine(folder, "ChecksAtBase.cs")).Should().NotContain("ProcessRequest").And.NotContain("Process.Start");
    }

    [Fact]
    public async Task ARunnerWithNoBoundary_RefusesACoderJobsPlan_AndStartsNothing()
    {
        await using var bed = await BoundaryBed.CreateAsync();
        var file = FileProject("app", "dotnet", "echo restore", "echo build", "echo test");
        var logs = TempDir.For("coai-sp-logs-");
        var before = bed.Launcher.Requests.Count;

        var result = await new CheckRunner(bed.Launcher).RunResolvedAsync(Wrap(Resolved(ToolchainSource.File, new RepoToolchain([file], [], ToolchainTimeouts.Default), OfFile(file))), new CheckRequest(bed.Worktree, logs.At("run"), 2) { Job = bed.Job });

        using (logs)
        {
            result.State.Should().Be(CheckState.BoundaryRefused);
            bed.Launcher.Requests.Count.Should().Be(before);
        }
    }

    // ---- d3 (T2): the unit's property list and the names of its environment equal E2's; only the adapter switches are added ----

    [Fact]
    public async Task T2_ThePropertyListOfANodeStepAndOfADotnetStep_IsEqualToE2sForTheSameNetworkState_ItemForItem()
    {
        await using var bed = await BoundaryBed.CreateAsync();
        var timeout = TimeSpan.FromMinutes(5) + TimeSpan.FromMinutes(1);

        foreach (var network in new[] { CheckNetwork.Private, CheckNetwork.Host })
        {
            var e2 = BoundaryUnit.Properties(bed.Host, bed.Job, network, timeout);
            var viaTheBoundary = bed.Boundary().PropertiesFor(bed.Job, network, timeout);

            viaTheBoundary.Should().Equal(e2, "the plan adds no bind, no writable path and no property: the list a node step and a dotnet step get is E2's list for that network state");
            e2.Count(property => property.StartsWith("BindPaths=", StringComparison.Ordinal)).Should().Be(1 + bed.Job.CheckFolders.Count + 2, "the worktree, the job's check folders and /tmp and /var/tmp: no bind of a project folder was added");
        }
    }

    [Fact]
    public async Task T2_TheEnvironmentNames_AreE2sPlusTheAdapterSwitches_AClosedTableWithConstantValues_NothingFromCoaisOwnEnvironment()
    {
        await using var bed = await BoundaryBed.CreateAsync();
        var table = ToolchainDefaults.Current;
        var e2 = BoundaryUnit.Environment(bed.Host, bed.Job, new Dictionary<string, string?>()).Select(pair => pair.Key).ToList();

        var node = BoundaryUnit.Environment(bed.Host, bed.Job, table.Stacks["node"].Switches.ToDictionary(pair => pair.Key, pair => (string?)pair.Value));
        var dotnet = BoundaryUnit.Environment(bed.Host, bed.Job, table.Stacks["dotnet"].Switches.ToDictionary(pair => pair.Key, pair => (string?)pair.Value));

        node.Select(pair => pair.Key).Should().Equal([.. e2, "NO_COLOR", "CI"]);
        dotnet.Select(pair => pair.Key).Should().Equal([.. e2, "NO_COLOR", "DOTNET_NOLOGO", "DOTNET_CLI_TELEMETRY_OPTOUT", "MSBUILDDISABLENODEREUSE"]);
        e2.Should().Contain(["PATH", "HOME", "DOTNET_CLI_HOME", "NUGET_PACKAGES", "npm_config_cache", "TMPDIR", "LANG", "TERM", "GIT_TERMINAL_PROMPT"]);
        node.Single(pair => pair.Key == "CI").Value.Should().Be("true");
        node.Concat(dotnet).Select(pair => pair.Key).Should().NotContain(name => name.StartsWith("COAI_", StringComparison.Ordinal) || name.Contains("KEY", StringComparison.Ordinal) || name.Contains("TOKEN", StringComparison.Ordinal));
    }

    [Fact]
    public async Task T2_TheEnvironmentOfARealNodeStep_HasExactlyTheNamesOfTheClosedTable()
    {
        NeedAUserManager();
        await using var bed = await BoundaryBed.CreateAsync();
        Environment.SetEnvironmentVariable("CI", "from-coai-own-environment");
        try
        {
            var web = Detected("web", Step("build", ".", "/usr/bin/env"), Step("test", ".", "/bin/true"));
            var logs = TempDir.For("coai-sp-logs-");

            var result = await new CheckRunner(bed.Launcher, bed.Boundary()).RunResolvedAsync(Wrap(Resolved(ToolchainSource.Detected, null, DetectedProjectOf(web))), new CheckRequest(bed.Worktree, logs.At("run"), 2) { Job = bed.Job });

            using (logs)
            {
                result.Green.Should().BeTrue(result.Note);
                var printed = File.ReadAllText(result.Steps[0].LogPath).Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(line => line.Contains('=')).Select(line => line.Split('=')[0]).ToList();
                printed.Should().BeEquivalentTo([.. BoundaryUnit.Environment(bed.Host, bed.Job, new Dictionary<string, string?>()).Select(pair => pair.Key), "NO_COLOR", "CI"]);
                File.ReadAllText(result.Steps[0].LogPath).Should().Contain("CI=true").And.NotContain("from-coai-own-environment");
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("CI", null);
        }
    }

    // ---- d4 (T3): the project folder is the working folder; it must be a real folder inside the worktree ----

    [Fact]
    public async Task T3_ADetectedStep_RunsFromItsProjectFolder_AStepOfTheFileFromTheRoot()
    {
        NeedAUserManager();
        await using var bed = await BoundaryBed.CreateAsync();
        Directory.CreateDirectory(Path.Combine(bed.Worktree, "web"));
        var web = Detected("web", Step("build", "web", "/bin/pwd"), Step("test", "web", "/bin/true"));
        var file = FileProject("app", "dotnet", "pwd", "pwd", "true");
        var logs = TempDir.For("coai-sp-logs-");

        var result = await new CheckRunner(bed.Launcher, bed.Boundary()).RunResolvedAsync(Wrap(Resolved(ToolchainSource.FileAndDefaults, new RepoToolchain([file], [], ToolchainTimeouts.Default), OfFile(file), DetectedProjectOf(web, "defaults"))), new CheckRequest(bed.Worktree, logs.At("run"), 2) { Job = bed.Job });

        using (logs)
        {
            result.Green.Should().BeTrue(result.Note);
            var fileBuild = result.Steps.Single(step => step.Project == "app" && step.Step == CheckStep.Build);
            var detectedBuild = result.Steps.Single(step => step.Project == "web/package.json" && step.Step == CheckStep.Build);
            File.ReadAllText(fileBuild.LogPath).Trim().Should().Be(bed.Worktree);
            File.ReadAllText(detectedBuild.LogPath).Trim().Should().Be(Path.Combine(bed.Worktree, "web"));
            (fileBuild.WorkingFolder, detectedBuild.WorkingFolder).Should().Be((".", "web"));
        }
    }

    [Fact]
    public async Task T3_AWorkingFolderThatIsALinkOutOfTheWorktree_IsRefusedBeforeAUnitIsDescribed()
    {
        NeedAUserManager();
        await using var bed = await BoundaryBed.CreateAsync();
        var outside = Path.Combine(bed.Parent(), "outside-folder");
        Directory.CreateDirectory(outside);
        Directory.CreateSymbolicLink(Path.Combine(bed.Worktree, "linked"), outside);
        var web = Detected("linked", Step("build", "linked", "/bin/pwd"), Step("test", "linked", "/bin/true"));
        var logs = TempDir.For("coai-sp-logs-");
        var before = bed.Launcher.Requests.Count(request => request.Arguments.Contains("coai-check-" + bed.JobId + "-s1") || request.Arguments.Any(argument => argument.StartsWith("--unit=", StringComparison.Ordinal) && argument.Contains("-s1")));

        var result = await new CheckRunner(bed.Launcher, bed.Boundary()).RunResolvedAsync(Wrap(Resolved(ToolchainSource.Detected, null, DetectedProjectOf(web))), new CheckRequest(bed.Worktree, logs.At("run"), 2) { Job = bed.Job });

        using (logs)
        {
            result.State.Should().Be(CheckState.Failed);
            var record = result.Steps.Should().ContainSingle().Subject;
            record.Started.Should().BeFalse();
            File.ReadAllText(record.LogPath).Should().Contain("refused before a unit was described").And.Contain("not a real folder inside the worktree");
            bed.Launcher.Requests.Count(request => request.Arguments.Any(argument => argument.Contains("-s1", StringComparison.Ordinal) && argument.Contains(bed.JobId, StringComparison.Ordinal))).Should().Be(before, "no unit was described for the refused step");
        }
    }

    [Theory]
    [InlineData("../elsewhere")]
    [InlineData("/etc")]
    [InlineData("a/../../b")]
    [InlineData("web/does-not-exist")]
    public void T3_ARelativeFolderThatLeavesTheTreeOrIsNotThere_IsAProblem(string folder)
    {
        using var tree = TempDir.For("coai-sp-tree-");
        Directory.CreateDirectory(Path.Combine(tree.Path, "web"));

        CheckRunner.WorkingFolderOf(tree.Path, folder).Problem.Should().NotBeEmpty();
    }

    [Fact]
    public void T3_ARealFolderInsideTheTree_AndTheRoot_AreFine_AFileIsNot()
    {
        using var tree = TempDir.For("coai-sp-tree-");
        Directory.CreateDirectory(Path.Combine(tree.Path, "web", "sub"));
        File.WriteAllText(Path.Combine(tree.Path, "afile"), "x");

        CheckRunner.WorkingFolderOf(tree.Path, ".").Problem.Should().BeEmpty();
        CheckRunner.WorkingFolderOf(tree.Path, "web/sub").Should().Be((Path.Combine(Path.GetFullPath(tree.Path), "web", "sub"), string.Empty));
        CheckRunner.WorkingFolderOf(tree.Path, "afile").Problem.Should().NotBeEmpty();
    }

    // ---- d5 (T4): a program that is not in the unit is a failed step that names it ----

    [Fact]
    public async Task T4_AStepWhoseProgramIsNotInTheUnit_IsAFailedStepWhoseLogNamesTheProgram_NeverSkipped()
    {
        NeedAUserManager();
        await using var bed = await BoundaryBed.CreateAsync();
        var web = Detected("web", Step("build", ".", "coai-no-such-program-xyz", "--version"), Step("test", ".", "/bin/true"));
        var logs = TempDir.For("coai-sp-logs-");

        var result = await new CheckRunner(bed.Launcher, bed.Boundary()).RunResolvedAsync(Wrap(Resolved(ToolchainSource.Detected, null, DetectedProjectOf(web))), new CheckRequest(bed.Worktree, logs.At("run"), 2) { Job = bed.Job });

        using (logs)
        {
            result.State.Should().Be(CheckState.Failed);
            var record = result.Steps.Should().ContainSingle().Subject;
            record.Passed.Should().BeFalse();
            record.ExitCode.Should().Be(127);
            File.ReadAllText(record.LogPath).Should().Contain("coai-no-such-program-xyz");
            result.Note.Should().Contain("the build step of web/package.json exited 127").And.Contain("were not run");
        }
    }
}
