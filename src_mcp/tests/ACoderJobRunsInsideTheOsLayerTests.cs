using System.Text.Json;
using CoaiMcp.Core.Consultation;
using CoaiMcp.Core.Roles;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Toolchain;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E2, the s2/s3 integration: the coder job of <see cref="CoderWork"/> — turn 0, the repair turn — launches claude INSIDE the OS layer (<see cref="CoderTurns"/>, the one road), keeping the failover's model order, classification, ledger and
/// notices. The real runner, worktree, hardened git, launcher and a real <c>systemd-run --user</c> unit; FakeCli stands in for claude, so no model is called. The decision it carries is decision 8 (<c>v2/architecture/server.md</c> § 16): a change of the main
/// checkout during a coder job is EVIDENCE in the envelope and never fails the job, while the read-only roles keep <c>tree-changed</c>; and a stop by the layer — a write refused, a boundary that does not hold — ends the job failed, never green.
/// </summary>
/// <remarks>
/// <para>The cases that need a real unit are skipped, with the reason, without a systemd user manager (<see cref="CoderRig.CreateAsync"/>); the cases of a machine with no boundary at all (<c>/bin/false</c> for <c>systemd-run</c>, native Windows) need none.</para>
/// <para>Every case that says "never" also has its control: the worktree write of the same coder DID land, the person's edit DID show in the evidence, the preflight DID say no — so an assertion of an absence is not a coder that did nothing.</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class ACoderJobRunsInsideTheOsLayerTests
{
    private static JsonElement MainCheckout(CoderRig rig, RoleJobRecord job) => rig.Envelope(job).GetProperty("mainCheckout");

    private static string[] ChangedPaths(JsonElement evidence) =>
        [.. evidence.GetProperty("changes").EnumerateArray().Select(change => change.GetProperty("path").GetString()!)];

    private static string[] Drop(IReadOnlyList<string> arguments, string what, string with)
    {
        var at = arguments.ToList().IndexOf(what);
        at.Should().BeGreaterThan(-1, $"the degrade must find '{what}' in the real argument list, or it proves nothing");

        return [.. arguments.Take(at), with, .. arguments.Skip(at + 1)];
    }

    // ---------- (a) a person's edit of the main checkout: evidence only ----------

    [Fact]
    public async Task APersonsEditOfTheMainCheckout_DuringACoderJob_DoesNotFailTheJob_ItIsEvidenceInTheEnvelope()
    {
        await using var rig = await CoderRig.CreateAsync();
        rig.Says("sonnet", CoderRig.Edits("src/thing.cs"), CoderRig.Report(), sleepMs: 3000);
        var job = rig.NewJob();
        var running = Task.Run(() => rig.RunAsync(job), TestContext.Current.CancellationToken);
        RoleJobWorld.WaitFor(() => File.Exists(Path.Combine(rig.WorktreeOf(job), "src", "thing.cs")), "the fake coder to write its edit, inside its unit");

        await File.WriteAllTextAsync(Path.Combine(rig.Repo, "plan.md"), "a person edited the plan\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(rig.Repo, "a-person-added-this.txt"), "new\n", TestContext.Current.CancellationToken);
        var (exit, ended) = await running;

        exit.Should().Be(RoleRunner.Done);
        ended.State.Should().Be(RoleJobStates.Succeeded, $"a person's edit is not the coder's failure: {ended.Reason}");
        ended.Attempts.Should().ContainSingle().Which.Stop.Should().BeEmpty();
        var evidence = MainCheckout(rig, job);
        evidence.GetProperty("taken").GetBoolean().Should().BeTrue();
        evidence.GetProperty("changed").GetBoolean().Should().BeTrue("POSITIVE CONTROL: the snapshots did see the person's edit — the job did not fail because nobody looked");
        ChangedPaths(evidence).Should().Contain(["plan.md", "a-person-added-this.txt"]);
        evidence.GetProperty("note").GetString().Should().Contain("evidence only").And.Contain("cannot say who wrote");
        var envelope = rig.Envelope(job);
        envelope.GetProperty("filesChanged").EnumerateArray().Select(entry => entry.GetProperty("path").GetString()).Should().Equal(["src/thing.cs"], "the person's files are in the main checkout, not in the coder's commit");
        envelope.GetProperty("headSha").GetString().Should().NotBe(rig.Base);
        (await rig.GitAsync(rig.Repo, "rev-parse", "HEAD")).Should().Be(rig.Base, "the main checkout's HEAD never moved");
    }

    [Fact]
    public async Task ACoderJobWhoseMainCheckoutIsLeftAlone_RecordsNoChange()
    {
        await using var rig = await CoderRig.CreateAsync();
        rig.Says("sonnet", CoderRig.Edits("src/thing.cs"), CoderRig.Report());
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        var evidence = MainCheckout(rig, job);
        evidence.GetProperty("taken").GetBoolean().Should().BeTrue();
        evidence.GetProperty("changed").GetBoolean().Should().BeFalse();
        evidence.GetProperty("note").GetString().Should().Contain("looked the same");
    }

    // ---------- (b) a coder that writes into the main checkout: stopped by the layer ----------

    [Fact]
    public async Task ACoderThatWritesIntoTheMainCheckout_IsStoppedByTheLayer_TheJobIsNotSucceeded_AndThePathIsNamed()
    {
        await using var rig = await CoderRig.CreateAsync();
        var inMain = Path.Combine(rig.Repo, "stopped-by-the-layer.txt");
        var beside = Path.Combine(rig.Root, "beside-the-worktree.txt");
        rig.Says("sonnet", new CoderRig.Launch(CoderRig.Edits("src/thing.cs"), CoderRig.Report(), TryWrites: [inMain, beside]))
            .Says("opus", new CoderRig.Launch(null, CoderRig.Report()));
        var job = rig.NewJob(route: ["claude/sonnet", "claude/opus"]);
        var before = await CoderTestBed.SnapshotOfAsync(rig.Repo);

        var (_, ended) = await rig.RunAsync(job);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, RoleStopWords.CoderBoundary), "a coder the layer had to stop did not do its work, whatever its report says");
        ended.Reason.Should().Contain(inMain).And.Contain(beside, "the sentence names each path the OS refused").And.Contain("OS layer refused");
        File.Exists(rig.World.Store.FolderOf(job.JobId)!.ResultFile).Should().BeFalse("nothing is published");
        File.Exists(inMain).Should().BeFalse();
        File.Exists(beside).Should().BeFalse();
        (await CoderTestBed.SnapshotOfAsync(rig.Repo)).Should().BeEquivalentTo(before, "the main checkout is byte-equal");
        File.Exists(Path.Combine(rig.WorktreeOf(job), "src", "thing.cs")).Should().BeTrue("CONTROL: the same coder's write into its worktree landed — the layer stopped the paths, not the coder");
        (await rig.GitAsync(rig.Repo, "rev-parse", $"refs/heads/coder/{job.JobId}")).Should().Be(rig.Base, "nothing was committed");
        rig.Launches(job).Should().ContainSingle("the next model of the route was NOT launched: it would meet the same layer");
        ended.Attempts.Should().ContainSingle().Which.Stop.Should().Be(RoleStopWords.CoderBoundary);
        rig.Rig.LedgerLines().Should().ContainSingle("the coder ran, so the launch is a line of the ledger").Which.GetProperty("outcome").GetString().Should().Be("write-stopped");
    }

    // ---------- (c) no effective boundary: refused before any launch ----------

    [Fact]
    public async Task APreflightThatSaysNo_RefusesTheJob_BeforeAnyLaunch_AndNothingIsCommitted()
    {
        await using var rig = await CoderRig.CreateAsync();
        var main = await rig.GitAsync(rig.Repo, "rev-parse", "--show-toplevel");
        rig.Degrade = arguments => Drop(arguments, $"BindReadOnlyPaths={main}", $"BindPaths={main}");
        rig.Says("sonnet", CoderRig.Edits("src/thing.cs"), CoderRig.Report());
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, RoleStopWords.CoderBoundary));
        ended.Reason.Should().Contain("main-readonly", "the row that said no").And.Contain("refused before the coder was launched");
        NothingLaunched(rig, job);
        (await rig.GitAsync(rig.Repo, "rev-parse", $"refs/heads/coder/{job.JobId}")).Should().Be(rig.Base, "no commit on the job's branch");
        Directory.Exists(rig.WorktreeOf(job)).Should().BeTrue("CONTROL: the worktree was made — the refusal is the layer's, after it, and before the coder");
    }

    [Theory]
    [InlineData("no-user-manager")]
    [InlineData("windows")]
    public async Task AMachineWithNoUserManager_OrNativeWindows_RefusesTheJob_BeforeAnyLaunch(string machine)
    {
        await using var rig = await CoderRig.CreateAsync(needsLayer: false);
        rig.OnHost = host => machine == "windows" ? host with { Platform = BoundaryPlatform.Windows } : host with { SystemdRun = "/bin/false" };
        rig.Says("sonnet", CoderRig.Edits("src/thing.cs"), CoderRig.Report());
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, RoleStopWords.CoderBoundary));
        ended.Reason.Should().Contain(machine == "windows" ? "Windows" : "user-manager");
        NothingLaunched(rig, job);
        (await rig.GitAsync(rig.Repo, "rev-parse", $"refs/heads/coder/{job.JobId}")).Should().Be(rig.Base);
    }

    private static void NothingLaunched(CoderRig rig, RoleJobRecord job)
    {
        rig.Launches(job).Should().BeEmpty("the coder was never started");
        File.Exists(Path.Combine(rig.WorktreeOf(job), "src", "thing.cs")).Should().BeFalse("the fake coder would have written it");
        File.Exists(rig.World.Store.FolderOf(job.JobId)!.ResultFile).Should().BeFalse();
        rig.Rig.LedgerLines().Should().BeEmpty("a launch that never started is no line of the ledger");
        rig.World.Store.Read(job.JobId)!.Attempts.Should().ContainSingle().Which.Stop.Should().Be(RoleStopWords.CoderBoundary);
    }

    // ---------- the one road: a coder is never launched outside the layer ----------

    [Fact]
    public async Task ACoderLaunch_WithNoLayerGiven_IsRefusedBeforeAnythingStarts()
    {
        await using var rig = await CoderRig.CreateAsync(needsLayer: false);
        rig.Says("sonnet", CoderRig.Edits("src/thing.cs"), CoderRig.Report());
        var launcher = new RoleModelLauncher(name => name == RoleModelLauncher.CliKey("claude") ? RoleJobWorld.FakeCli : null, NoProcessTracking.Instance) { Coder = new LaunchConfinement.Coder("r20261123000000-aabbccdd", rig.Root) };

        var launched = await launcher.LaunchAsync(new ModelRef("sonnet", "claude"), "write it", rig.Repo, Path.Combine(rig.Root, "out"), TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        launched.Launch.Should().BeNull("nothing was launched");
        launched.Refused.Should().BeOfType<ConsultFailure.LaunchRefused>().Which.Said.Should().Contain("only inside the OS layer");
        Directory.GetFiles(rig.Root).Should().BeEmpty();
    }

    // ---------- (d) a read-only role keeps tree-changed, exactly as before ----------

    [Fact]
    public async Task ABaJob_WhoseRepositoryChangedWhileItRan_StillEndsTreeChanged_ThoughACoderJobTakesTheSameChangeAsEvidence()
    {
        await using var rig = await RoleFailoverRig.CreateAsync();
        Environment.SetEnvironmentVariable("FAKECLI_SIDE_EFFECT", Path.Combine(rig.Repo, "written-by-the-model.txt"));
        rig.Says("fable", FakeRoleAnswers.ClaudeSays(FakeRoleAnswers.Ok)).Says("opus", FakeRoleAnswers.ClaudeSays(FakeRoleAnswers.Ok));
        var job = rig.NewJob(["claude/fable", "claude/opus"]);

        var (_, after) = await rig.RunAsync(job);

        (after.State, after.Stop).Should().Be((RoleJobStates.Failed, "tree-changed"));
        after.Reason.Should().Contain("written-by-the-model.txt");
        rig.Models().Should().Equal("fable");
    }
}
