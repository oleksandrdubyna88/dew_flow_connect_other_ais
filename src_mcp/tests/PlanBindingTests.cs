using System.Text.Json;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E2 s5 t1 — a coder job's session (<c>coder/&lt;jobId&gt;</c>) inherits the proceed of the plan its job was started under, and only that. The binding is read from <c>job.json</c>, the authority, and refused unless the job
/// names a session whose plan proceeded, the task is inside the folder of the approved plan file, the plan file is at the base exactly as it was approved, and the base descends from the commit the plan round ran on.
/// d1 the accepted case; d2 the refused ones (a branch no job made, no proceeded plan, a changed plan); d3 the record is the authority; d4 the plan-gate rules. Every refusal has the accepted case beside it on the same
/// fixture, so a fixture the gate quietly declines would be seen.
/// </summary>
[Collection("fakecli-env")]
public sealed class PlanBindingTests
{
    private const string NoPlanSentence = "plan gate comes first";

    private static string Error(JsonElement answer) => answer.TryGetProperty("error", out var error) ? error.GetString()! : string.Empty;

    /// <summary>Everything about a session that a round or a resolve could move.</summary>
    private static (string Id, Stage Stage, int Rounds, int Run, bool Awaiting, bool Proceeded, int Rejections, string PlanText, string Inherited) Of(PersistedSession session) =>
        (session.State.SessionId, session.State.Stage, session.Rounds.Count, session.State.RoundsRunThisStage, session.State.AwaitingResolve, session.State.PlanProceeded, session.State.Rejections.Length, session.PlanText, session.State.Inherited?.ToString() ?? string.Empty);

    private static string Verdict(JsonElement answer) => answer.TryGetProperty("verdict", out var verdict) ? verdict.GetString()! : "(" + answer + ")";

    // ---------- d1: the accepted case ----------

    [Fact]
    public async Task D1_AJobStartedFromABranchWithAProceededPlan_IsReviewed_AndTheJobRecordsWhatItWasStartedUnder()
    {
        await using var rig = await PlanBindingRig.CreateAsync();
        var session = await rig.ApproveAsync();
        var head = await rig.HeadAsync();
        var jobId = await rig.JobWithBranchAsync();

        var link = rig.Jobs.Read(jobId)!.Plan;
        link.Should().NotBeNull("a coder job records the plan it was started under, at the start");
        link!.StartBranch.Should().Be("feature");
        link.SourceSession.Should().Be(session);
        link.PlanCommit.Should().Be(head, "the plan round ran on the tip of the feature branch");
        link.PlanPath.Should().Be(PlanBindingRig.PlanFile);
        link.PlanHash.Should().Be(await rig.GitOutAsync("rev-parse", $"{head}:{PlanBindingRig.PlanFile}"), "the git blob of the plan file as the round read it");
        rig.Jobs.Read(jobId)!.BaseSha.Should().Be(head);

        var round = await rig.ReviewCodeAsync(jobId);

        Error(round).Should().BeEmpty(round.ToString());
        Verdict(round).Should().Be("proceed");
    }

    [Fact]
    public async Task D1_TheSessionRecordsTheInheritedPlan_AndTheTextTheRoundIsJudgedAgainst()
    {
        await using var rig = await PlanBindingRig.CreateAsync();
        var session = await rig.ApproveAsync();
        var head = await rig.HeadAsync();
        var jobId = await rig.JobWithBranchAsync();
        await rig.Service.OpenAsync(rig.Repo, $"coder/{jobId}");

        await rig.ReviewCodeAsync(jobId);

        var status = PlanBindingRig.Parse(await rig.Service.StatusAsync(rig.Repo, $"coder/{jobId}"));
        status.GetProperty("planProceeded").GetBoolean().Should().BeTrue();
        var inherited = status.GetProperty("inherited");
        inherited.GetProperty("sourceSession").GetString().Should().Be(session);
        inherited.GetProperty("startBranch").GetString().Should().Be("feature");
        inherited.GetProperty("planCommit").GetString().Should().Be(head);
        inherited.GetProperty("planPath").GetString().Should().Be(PlanBindingRig.PlanFile);
        inherited.GetProperty("planHash").GetString().Should().MatchRegex("^[0-9a-f]{40}$");
        inherited.GetProperty("jobId").GetString().Should().Be(jobId);
        var saved = new SessionStore(rig.Data).Load(rig.Repo, $"coder/{jobId}")!;
        saved.PlanText.Should().Be(PlanBindingRig.PlanText, "the round is judged against the plan revision that was approved, as the commit holds it");
        saved.State.Inherited!.SourceSession.Should().Be(session);
    }

    [Fact]
    public async Task D1_TheFeaturesOwnSession_IsNotChangedByTheCoderBranch()
    {
        await using var rig = await PlanBindingRig.CreateAsync();
        await rig.ApproveAsync();
        var before = new SessionStore(rig.Data).Load(rig.Repo, "feature")!;
        var jobId = await rig.JobWithBranchAsync();

        await rig.Service.OpenAsync(rig.Repo, $"coder/{jobId}");
        await rig.ReviewCodeAsync(jobId);

        var after = new SessionStore(rig.Data).Load(rig.Repo, "feature")!;
        Of(after).Should().Be(Of(before), "a coder branch only READS the feature's session");
    }

    // ---------- d2: the refused ones ----------

    [Theory]
    [InlineData("coder/x")]
    [InlineData("coder/r20261123101010-00000000")]
    public async Task D2_ABranchNamedCoder_ThatNoJobMade_IsRefusedAsToday(string branch)
    {
        await using var rig = await PlanBindingRig.CreateAsync();
        await rig.ApproveAsync();
        await rig.GitAsync("branch", branch, "feature");
        await rig.GitAsync("checkout", branch);
        BaScenario.Write(rig.Repo, "src/thing.cs", "written by hand\n");
        await rig.CommitAsync("by hand");
        await rig.GitAsync("checkout", "feature");
        await rig.Service.OpenAsync(rig.Repo, branch);

        var round = PlanBindingRig.Parse(await rig.Service.ReviewCodeAsync(rig.Repo, branch, "feature", PlanBindingRig.PlanText, false, TestContext.Current.CancellationToken));

        Error(round).Should().Contain(NoPlanSentence, "no plan round has reached proceed in this session: the sentence it always had");
        PlanBindingRig.Parse(await rig.Service.StatusAsync(rig.Repo, branch)).GetProperty("planProceeded").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task D2_AJobStartedFromABranchWithNoProceededPlan_IsRefused_NamingTheBranch()
    {
        await using var rig = await PlanBindingRig.CreateAsync();
        await rig.ApproveAsync();
        await rig.GitAsync("checkout", "-b", "side");
        var jobId = await rig.JobWithBranchAsync();
        await rig.Service.OpenAsync(rig.Repo, $"coder/{jobId}");

        var round = await rig.ReviewCodeAsync(jobId);

        Error(round).Should().Contain("'side'").And.Contain("no plan that proceeded").And.Contain(NoPlanSentence);
        rig.Jobs.Read(jobId)!.Plan!.StartBranch.Should().Be("side", "the job still records where it started, with no session to inherit from");
        PlanBindingRig.Parse(await rig.Service.StatusAsync(rig.Repo, $"coder/{jobId}")).GetProperty("planProceeded").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task D2_AJobWhoseSourcePlanChanged_TheHashDiffers_IsRefused()
    {
        await using var rig = await PlanBindingRig.CreateAsync();
        await rig.ApproveAsync();
        var jobId = await rig.JobWithBranchAsync();
        (await rig.ReviewCodeAsync(jobId)).TryGetProperty("verdict", out _).Should().BeTrue("the accepted case first, on this very fixture");
        rig.Jobs.Change(jobId, job => job with { Plan = job.Plan! with { PlanHash = new string('0', 40) } });

        var round = await rig.ReviewCodeAsync(jobId, again: true);

        Error(round).Should().Contain("changed since the job started").And.Contain("hash");
    }

    [Fact]
    public async Task D2_AJobWhoseSourceSessionIsNotTheOneItWasStartedFrom_IsRefused()
    {
        await using var rig = await PlanBindingRig.CreateAsync();
        await rig.ApproveAsync();
        var jobId = await rig.JobWithBranchAsync();
        foreach (var file in Directory.GetFiles(Path.Combine(rig.Data, "sessions"), "*.json"))
        {
            File.Delete(file);
        }

        var newSession = await rig.ApproveAsync();

        newSession.Should().NotBe(rig.Jobs.Read(jobId)!.Plan!.SourceSession, "the feature was reviewed again in a session of its own");
        Error(await rig.ReviewCodeAsync(jobId)).Should().Contain("session").And.Contain(rig.Jobs.Read(jobId)!.Plan!.SourceSession);
    }

    // ---------- d3: the record is the authority ----------

    [Fact]
    public async Task D3_ACoderJobWithNoPlanRecord_IsRefused_TheBranchNameIsNotEnough()
    {
        await using var rig = await PlanBindingRig.CreateAsync();
        await rig.ApproveAsync();
        var jobId = await rig.JobWithBranchAsync();
        (await rig.ReviewCodeAsync(jobId)).TryGetProperty("verdict", out _).Should().BeTrue("the accepted case first");
        rig.Jobs.Change(jobId, job => job with { Plan = null });

        var round = await rig.ReviewCodeAsync(jobId, again: true);

        Error(round).Should().Contain("records no plan").And.Contain(NoPlanSentence);
    }

    [Fact]
    public async Task D3_AJobOfAnotherRole_OrAnotherRepository_IsNotACoderJobOfThisBranch()
    {
        await using var rig = await PlanBindingRig.CreateAsync();
        await rig.ApproveAsync();
        var jobId = await rig.JobWithBranchAsync();
        rig.Jobs.Change(jobId, job => job with { Role = RoleDescriptors.Probe });
        await rig.Service.OpenAsync(rig.Repo, $"coder/{jobId}");

        var round = await rig.ReviewCodeAsync(jobId, scope: PlanBindingRig.PlanText);

        Error(round).Should().Contain(NoPlanSentence, "a probe job made no coder branch: as today");
        rig.Jobs.Change(jobId, job => job with { Role = RoleDescriptors.Coder, Repo = Path.Combine(rig.Repo, "elsewhere") });
        Error(await rig.ReviewCodeAsync(jobId, scope: PlanBindingRig.PlanText)).Should().Contain(NoPlanSentence, "a job of another repository made no branch of this one: as today");
    }

    [Fact]
    public async Task D3_AnInheritedSession_IsCheckedAgainEveryRound_NotTrustedFromTheLast()
    {
        await using var rig = await PlanBindingRig.CreateAsync();
        await rig.ApproveAsync();
        var jobId = await rig.JobWithBranchAsync();
        await rig.Service.OpenAsync(rig.Repo, $"coder/{jobId}");
        Verdict(await rig.ReviewCodeAsync(jobId)).Should().Be("proceed");
        PlanBindingRig.Parse(await rig.Service.ResolveAsync(rig.Repo, $"coder/{jobId}", "[]")).GetProperty("stage").GetString().Should().Be("Done");
        rig.Jobs.Change(jobId, job => job with { Plan = job.Plan! with { SourceSession = "ffffffff" } });

        Error(await rig.ReviewCodeAsync(jobId, again: true)).Should().Contain("session");
    }

    // ---------- d4: the plan-gate rules ----------

    [Fact]
    public async Task D4_ATaskOutsideTheFolderOfTheApprovedPlanFile_IsRefused()
    {
        await using var rig = await PlanBindingRig.CreateAsync();
        BaScenario.Write(rig.Repo, "plan/epoch-2/PLAN_E2.md", "# A plan of another epoch\n\nIt names no task of epoch 1.\n");
        await rig.CommitAsync("another plan");
        await rig.ApproveAsync(planFile: "plan/epoch-2/PLAN_E2.md");
        var jobId = await rig.JobWithBranchAsync();
        await rig.Service.OpenAsync(rig.Repo, $"coder/{jobId}");

        var round = await rig.ReviewCodeAsync(jobId);

        Error(round).Should().Contain("does not belong").And.Contain("plan/epoch-2");
    }

    [Fact]
    public async Task D4_ABaseThatDoesNotDescendFromThePlansCommit_IsRefused()
    {
        await using var rig = await PlanBindingRig.CreateAsync();
        var older = await rig.HeadAsync();
        BaScenario.Write(rig.Repo, "later.txt", "a commit the plan round saw\n");
        await rig.CommitAsync("a later commit");
        await rig.ApproveAsync();
        var jobId = await rig.JobWithBranchAsync(baseSha: older);
        await rig.Service.OpenAsync(rig.Repo, $"coder/{jobId}");

        var round = await rig.ReviewCodeAsync(jobId);

        Error(round).Should().Contain("descend").And.Contain(older[..12]);
    }

    [Fact]
    public async Task D4_APlanFileThatChangedBetweenTheApprovalAndTheBase_IsRefused()
    {
        await using var rig = await PlanBindingRig.CreateAsync();
        await rig.ApproveAsync();
        BaScenario.Write(rig.Repo, PlanBindingRig.PlanFile, PlanBindingRig.PlanText + "\nA new requirement nobody reviewed.\n");
        await rig.CommitAsync("the plan changed after its approval");
        var jobId = await rig.JobWithBranchAsync();
        await rig.Service.OpenAsync(rig.Repo, $"coder/{jobId}");

        var round = await rig.ReviewCodeAsync(jobId);

        Error(round).Should().Contain("differs from the revision that was approved").And.Contain(PlanBindingRig.PlanFile);
    }

    [Fact]
    public async Task D4_APlanThatWasPastedAsText_CannotHaveATaskBelongToIt_AndIsRefused()
    {
        await using var rig = await PlanBindingRig.CreateAsync();
        await rig.Service.OpenAsync(rig.Repo, "feature");
        PlanBindingRig.Parse(await rig.Service.ReviewPlanAsync(rig.Repo, "feature", PlanBindingRig.PlanText)).GetProperty("verdict").GetString().Should().Be("proceed");
        await rig.Service.ResolveAsync(rig.Repo, "feature", "[]");
        var jobId = await rig.JobWithBranchAsync();
        await rig.Service.OpenAsync(rig.Repo, $"coder/{jobId}");

        var round = await rig.ReviewCodeAsync(jobId);

        Error(round).Should().Contain("pasted").And.Contain("committedPlan");
    }
}
