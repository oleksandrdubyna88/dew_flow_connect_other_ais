using System.Text.Json;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E4 s3 t4: the envelope of a coder job says where its checks came from — the toolchain's source, projects, facts and notes, and what differs between the base and the head — and every step of <c>checks</c> carries its network state and working folder beside the fields of E2.
/// The real coder rig (FakeCli for the model; real git, real units), so the envelope is the one a client reads.
/// </summary>
[Collection("fakecli-env")]
public sealed class ACoderEnvelopeNamesItsToolchainTests
{
    private static readonly string[] E2CheckFields = ["project", "step", "argv", "exitCode", "durationMs", "logRef"];

    private static readonly string[] E2TopFields = ["green", "checksState", "checksNote", "format", "checks", "rounds", "fixRounds", "lastDigest"];

    private const string Library = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup></Project>";

    private static CoderRig.Launch Writes(string text, string file = CoderRig.StateFile) =>
        new(new Dictionary<string, string> { [file] = text + "\n" }, CoderRig.Report());

    [Fact]
    public async Task D1_AToolchainFile_IsNamedFile_WithItsProject_ItsSteps_AndEveryCheckStepHasItsNetworkAndFolder()
    {
        await using var rig = await CoderRig.CreateAsync();
        rig.Says("sonnet", Writes(CoderRig.Green));
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        var envelope = rig.Envelope(job);
        var toolchain = envelope.GetProperty("toolchain");
        toolchain.GetProperty("source").GetString().Should().Be("file");
        var project = toolchain.GetProperty("projects").EnumerateArray().Should().ContainSingle().Subject;
        project.GetProperty("path").GetString().Should().Be("app");
        project.GetProperty("adapter").GetString().Should().Be("dotnet");
        project.GetProperty("steps").EnumerateArray().Select(step => step.GetProperty("name").GetString()).Should().Equal("build", "test");
        toolchain.GetProperty("notes").GetArrayLength().Should().Be(0);
        toolchain.GetProperty("changedBetweenBaseAndHead").GetArrayLength().Should().Be(0);

        var checks = envelope.GetProperty("checks").EnumerateArray().ToList();
        checks.Select(step => (step.GetProperty("step").GetString(), step.GetProperty("network").GetString(), step.GetProperty("workingFolder").GetString())).Should().Equal(("build", "host", "."), ("test", "none", "."));
    }

    [Fact]
    public async Task D1_ADetectedToolchain_IsNamedDetected_WithTheFactsTheDetectorRead()
    {
        await using var rig = await CoderRig.CreateAsync(toolchain: string.Empty, extra: new Dictionary<string, string> { ["App.csproj"] = Library, [".gitignore"] = "bin/\nobj/\n" });
        rig.Says("sonnet", Writes(CoderRig.Green, "notes/state.txt"));
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        var envelope = rig.Envelope(job);
        envelope.GetProperty("green").GetBoolean().Should().BeTrue();
        var toolchain = envelope.GetProperty("toolchain");
        toolchain.GetProperty("source").GetString().Should().Be("detected");
        var project = toolchain.GetProperty("projects").EnumerateArray().Should().ContainSingle().Subject;
        project.GetProperty("path").GetString().Should().Be("App.csproj");
        project.GetProperty("adapter").GetString().Should().Be("dotnet");
        project.GetProperty("facts").GetProperty("kind").GetString().Should().Be("project");
        project.GetProperty("facts").GetProperty("tests").GetString().Should().Be("none");
        project.GetProperty("steps").EnumerateArray().Select(step => (step.GetProperty("name").GetString(), step.GetProperty("network").GetString())).Should().Equal(("restore", "host"), ("build", "none"));
        toolchain.GetProperty("changedBetweenBaseAndHead").GetArrayLength().Should().Be(0, "the head changed no manifest");
        envelope.GetProperty("checks").EnumerateArray().Select(step => step.GetProperty("step").GetString()).Should().Equal("restore", "build");
    }

    [Fact]
    public async Task D1_AHeadThatAddsAProjectFile_IsRefusedByTheFrozenGate_TheRoundSaysSoAndNoToolchainSectionClaimsAProject()
    {
        await using var rig = await CoderRig.CreateAsync(toolchain: string.Empty, extra: new Dictionary<string, string> { ["App.csproj"] = Library, [".gitignore"] = "bin/\nobj/\n" });
        rig.Says("sonnet", new CoderRig.Launch(new Dictionary<string, string> { ["notes/state.txt"] = "x\n", ["Other.csproj"] = Library }, CoderRig.Report()));
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, "toolchain-refused"), ended.Reason);
        var envelope = rig.Envelope(job);
        envelope.GetProperty("checks").GetArrayLength().Should().Be(0);
        envelope.GetProperty("rounds").EnumerateArray().Should().ContainSingle().Which.GetProperty("state").GetString().Should().Be("toolchain_refused");
    }

    [Fact]
    public async Task D2_EveryFieldOfE2_KeepsItsNameAndItsMeaning()
    {
        await using var rig = await CoderRig.CreateAsync();
        rig.Says("sonnet", Writes(CoderRig.Green));
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        var envelope = rig.Envelope(job);
        foreach (var name in E2TopFields)
        {
            envelope.TryGetProperty(name, out _).Should().BeTrue($"'{name}' is a field of E2");
        }

        envelope.GetProperty("green").GetBoolean().Should().BeTrue();
        envelope.GetProperty("checksState").GetString().Should().Be("green");
        envelope.GetProperty("format").GetProperty("state").GetString().Should().Be("not_configured");
        foreach (var step in envelope.GetProperty("checks").EnumerateArray())
        {
            foreach (var name in E2CheckFields)
            {
                step.TryGetProperty(name, out _).Should().BeTrue($"'{name}' is a field of E2's check step");
            }

            step.GetProperty("exitCode").GetInt32().Should().Be(0);
            step.GetProperty("argv").EnumerateArray().Select(arg => arg.GetString()).Should().StartWith(["/bin/sh", "-c"]);
        }

        envelope.GetProperty("rounds").EnumerateArray().Should().ContainSingle().Which.GetProperty("state").GetString().Should().Be("green");
    }

    [Fact]
    public async Task T5_ARestoreThatLeavesAFileGitWouldAdd_IsTreeChanged_NeverGreen()
    {
        var toolchain = "version: 1\nprojects:\n  - path: \"app\"\n    adapter: node\n    restore:\n      - \"/bin/sh\"\n      - \"-c\"\n      - \"echo junk > leftover-of-the-restore.txt\"\n    build:\n      - \"/bin/sh\"\n      - \"-c\"\n      - \"echo built\"\n    test:\n      - \"/bin/sh\"\n      - \"-c\"\n      - \"echo tested\"\n";
        await using var rig = await CoderRig.CreateAsync(toolchain: toolchain);
        rig.Says("sonnet", Writes(CoderRig.Green));
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, "checks-changed-tree"));
        ended.Reason.Should().Contain("leftover-of-the-restore.txt");
        var envelope = rig.Envelope(job);
        envelope.GetProperty("green").GetBoolean().Should().BeFalse();
        envelope.GetProperty("checks").EnumerateArray().Select(step => step.GetProperty("step").GetString()).Should().Equal("restore", "build", "test");
    }
}
