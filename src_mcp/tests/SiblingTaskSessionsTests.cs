using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E2 s5 t2 — scenarios over the plan binding. Two coder jobs of one feature each run a code round with FakeCli reviewers; what one resolves, counts or rejects is its own, and the feature's session is only read (d1).
/// A coder branch's status names the plan it inherited and the session it came from, and a second code round with <c>again</c> works there (d2). No model is called.
/// </summary>
[Collection("fakecli-env")]
public sealed class SiblingTaskSessionsTests
{
    private const string Reason = "constant time is not required for this token";

    private static string Branch(string jobId) => $"coder/{jobId}";

    private static string Decide(string action) => action == "reject"
        ? $$"""[{"finding": 0, "action": "reject", "reason": "{{Reason}}"}]"""
        : """[{"finding": 0, "action": "accept"}]""";

    // ---------- d1: two siblings ----------

    [Fact]
    public async Task D1_TwoCoderJobsOfOneFeature_ResolveTheirFindingsIndependently()
    {
        await using var rig = await PlanBindingRig.CreateAsync(threshold: 0);
        await rig.ApproveAsync();
        var featureAtStart = rig.SnapshotOf("feature");
        var first = await rig.JobWithBranchAsync();
        var second = await rig.JobWithBranchAsync();
        CodeRoundHarness.SetAnswer(CodeRoundHarness.OneMajor);

        var roundOne = await rig.ReviewCodeAsync(first);
        var roundTwo = await rig.ReviewCodeAsync(second);

        roundOne.GetProperty("verdict").GetString().Should().Be("revise", roundOne.ToString());
        roundTwo.GetProperty("verdict").GetString().Should().Be("revise", roundTwo.ToString());
        var secondBefore = rig.SnapshotOf(Branch(second));
        secondBefore.Should().Be((secondBefore.Id, Stage.CodeReview, 1, 1, true, true, 0, 1, secondBefore.PlanText, secondBefore.Inherited), "one round run, one finding waiting for its decision");

        var resolved = PlanBindingRig.Parse(await rig.Service.ResolveAsync(rig.Repo, Branch(first), Decide("reject")));

        resolved.TryGetProperty("error", out var error).Should().BeFalse(error.ToString());
        var firstAfter = rig.SnapshotOf(Branch(first));
        (firstAfter.Awaiting, firstAfter.Rejections, firstAfter.Pending).Should().Be((false, 1, 0), "the first job's own decision landed on the first job");
        rig.SnapshotOf(Branch(second)).Should().Be(secondBefore, "a resolve in one task changes nothing of the other: not its stage, its round count, its pending finding or its rejections");
        rig.SnapshotOf("feature").Should().Be(featureAtStart, "the feature's own session is only read by the coder branches");
    }

    [Fact]
    public async Task D1_ARejectionInOneSibling_IsNotAStandingRejectionInTheOther()
    {
        await using var rig = await PlanBindingRig.CreateAsync(threshold: 0);
        await rig.ApproveAsync();
        var first = await rig.JobWithBranchAsync();
        var second = await rig.JobWithBranchAsync();
        CodeRoundHarness.SetAnswer(CodeRoundHarness.OneMajor);
        await rig.ReviewCodeAsync(first);
        await rig.Service.ResolveAsync(rig.Repo, Branch(first), Decide("reject"));
        await rig.CommitOnAsync(Branch(first), "src/more.cs");
        await rig.CommitOnAsync(Branch(second), "src/more.cs");

        var again = await rig.ReviewCodeAsync(first, again: true);
        var other = await rig.ReviewCodeAsync(second);

        again.GetProperty("verdict").GetString().Should().Be("proceed", "the same remark is a standing rejection in the session that rejected it");
        again.GetProperty("discounted").GetArrayLength().Should().Be(1);
        other.GetProperty("verdict").GetString().Should().Be("revise", "the sibling never rejected it: the same remark gates there");
        other.GetProperty("discounted").GetArrayLength().Should().Be(0);
    }

    // ---------- d2: status and a second round ----------

    [Fact]
    public async Task D2_StatusOnACoderBranch_NamesThePlanItInherited_AndTheSessionItCameFrom()
    {
        await using var rig = await PlanBindingRig.CreateAsync();
        var featureSession = await rig.ApproveAsync();
        var job = await rig.JobWithBranchAsync();
        await rig.ReviewCodeAsync(job);

        var status = PlanBindingRig.Parse(await rig.Service.StatusAsync(rig.Repo, Branch(job)));

        status.GetProperty("sessionId").GetString().Should().NotBe(featureSession, "the coder branch has a session of its own");
        status.GetProperty("planProceeded").GetBoolean().Should().BeTrue();
        status.GetProperty("inherited").GetProperty("sourceSession").GetString().Should().Be(featureSession);
        status.GetProperty("inherited").GetProperty("planPath").GetString().Should().Be(PlanBindingRig.PlanFile);
        PlanBindingRig.Parse(await rig.Service.StatusAsync(rig.Repo, "feature")).TryGetProperty("inherited", out _).Should().BeFalse("the feature's own session inherited nothing, and its answer reads as it always did");
    }

    [Fact]
    public async Task D2_ASecondCodeRound_WithAgain_WorksOnTheCoderBranch()
    {
        await using var rig = await PlanBindingRig.CreateAsync();
        await rig.ApproveAsync();
        var job = await rig.JobWithBranchAsync();
        (await rig.ReviewCodeAsync(job)).GetProperty("verdict").GetString().Should().Be("proceed");
        PlanBindingRig.Parse(await rig.Service.ResolveAsync(rig.Repo, Branch(job), "[]")).GetProperty("stage").GetString().Should().Be("Done");
        await rig.CommitOnAsync(Branch(job), "src/more.cs");

        var again = await rig.ReviewCodeAsync(job, again: true);

        again.TryGetProperty("error", out var error).Should().BeFalse(error.ToString());
        again.GetProperty("verdict").GetString().Should().Be("proceed");
        var status = PlanBindingRig.Parse(await rig.Service.StatusAsync(rig.Repo, Branch(job)));
        status.GetProperty("rounds").GetArrayLength().Should().Be(2, "both code rounds are on the record of the coder branch");
        status.GetProperty("inherited").GetProperty("jobId").GetString().Should().Be(job);
        PlanBindingRig.Parse(await rig.Service.ResolveAsync(rig.Repo, Branch(job), "[]")).GetProperty("stage").GetString().Should().Be("Done");
    }
}
