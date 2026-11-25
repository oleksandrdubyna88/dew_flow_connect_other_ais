using System.Text.RegularExpressions;
using CoaiMcp.Core.Notices;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E3 s2 t4 — the scenarios of the read-only roles with FakeCli (spec A): a declared refusal goes to the next model with one switch line and one notice, an exhausted list ends <c>failed</c> with every attempt named, and a tree
/// that changes during an attempt ends <c>tree-changed</c> with no failover and no answer (S6, S7); with a codex row and an agy row as scout and as judge the recorded argv is the read-only one of each CLI and agy's read-only folder is
/// prepared, and no scout or judge launch can carry the coder case of <c>LaunchConfinement</c> (S10).
/// </summary>
/// <remarks>The agy row runs with a temp <c>XDG_DATA_HOME</c> and <c>HOME</c>, so its read-only folder is made there and never in the real home. In the serial <c>fakecli-env</c> collection.</remarks>
[Collection("fakecli-env")]
public sealed partial class AScoutAndJudgeScenariosTests
{
    /// <summary>What differs between the two roles: the route, the answers and the descriptor.</summary>
    public sealed record Kind(string Role, string First, string Second, string FirstModel, string SecondModel)
    {
        public override string ToString() => Role;

        internal RoleDescriptor Descriptor => Role == "scout" ? ReadOnlyScenario.ScoutDescriptor() : JudgeScenario.JudgeDescriptor();

        internal string Valid => (Role == "scout" ? ReadOnlyScenario.Scout() : JudgeScenario.Judge()).ToJsonString();

        internal IReadOnlyList<string> Route => [First, Second];
    }

    private static readonly Kind[] AllKinds =
    [
        new("scout", "claude/sonnet", "claude/haiku", "sonnet", "haiku"),
        new("judge", "claude/fable", "codex/astra", "fable", "astra"),
    ];

    public static TheoryData<Kind> Kinds => [.. AllKinds];

    private static Kind Of(string role) => AllKinds.Single(kind => kind.Role == role);

    private static async Task<RoleFailoverRig> RigAsync()
    {
        var rig = await RoleFailoverRig.CreateAsync();
        ReadOnlyScenario.Lay(rig.Repo);
        await rig.Git.CommitAsync("the files");

        return rig;
    }

    private static object Says(Kind kind, string model, string text) => kind.Role == "judge" && model == kind.SecondModel ? FakeRoleAnswers.CodexSays(text) : FakeRoleAnswers.ClaudeSays(text);

    private static Task<(int Exit, RoleJobRecord Job)> RunAsync(RoleFailoverRig rig, Kind kind, IReadOnlyList<string>? route = null) =>
        rig.RunAsync(rig.NewBriefJob(kind.Role, route ?? kind.Route, ReadOnlyScenario.Brief), [kind.Descriptor]);

    // ---------- S6: a declared refusal, an exhausted list ----------

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task S6_ADeclaredRefusal_GoesToTheNextModel_WithOneSwitchLine_AndOneNotice(Kind kind)
    {
        await using var rig = await RigAsync();
        rig.Says(kind.FirstModel, Says(kind, kind.FirstModel, FakeRoleAnswers.Refusal)).Says(kind.SecondModel, Says(kind, kind.SecondModel, kind.Valid));

        var (exit, job) = await RunAsync(rig, kind);

        exit.Should().Be(RoleRunner.Done);
        job.State.Should().Be(RoleJobStates.Succeeded, job.Reason);
        job.Model.Should().Be(kind.Second);
        job.Attempts.Select(attempt => (attempt.Model, attempt.Stop)).Should().Equal((kind.FirstModel, "refusal"), (kind.SecondModel, string.Empty));
        job.Attempts[0].Reason.Should().Contain("I will not write this plan");
        rig.Models().Should().Equal(kind.FirstModel, kind.SecondModel);
        rig.Launches().Should().OnlyContain(argv => !argv.Contains("--resume"), "the next model starts from turn 0 in a new session");
        rig.SwitchLines().Should().ContainSingle().Which.Should().StartWith($"role job {job.JobId} ({kind.Role}) switched {kind.First} -> {kind.Second} after refusal: ");
        rig.NoticesOf(ServerNoticeCodes.RoleModelSwitched).Should().ContainSingle().Which.Provider.Should().Be(kind.Second.Split('/')[0]);
        rig.World.Store.FolderOf(job.JobId)!.ReadResult().Should().Contain("\"status\":\"ok\"");
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task S6_EveryModelRefusing_EndsFailed_NamingEveryAttempt_WithNoResult(Kind kind)
    {
        await using var rig = await RigAsync();
        rig.Says(kind.FirstModel, Says(kind, kind.FirstModel, FakeRoleAnswers.Refusal)).Says(kind.SecondModel, Says(kind, kind.SecondModel, FakeRoleAnswers.Refusal));

        var (_, job) = await RunAsync(rig, kind);

        job.State.Should().Be(RoleJobStates.Failed);
        job.Stop.Should().Be("models-exhausted");
        job.Reason.Should().Contain(kind.First).And.Contain(kind.Second).And.Contain("refusal");
        job.Attempts.Select(attempt => (attempt.Model, attempt.Stop)).Should().Equal((kind.FirstModel, "refusal"), (kind.SecondModel, "refusal"));
        rig.World.Store.FolderOf(job.JobId)!.ReadResult().Should().BeEmpty();
        rig.SwitchLines().Should().ContainSingle("one switch, from the first to the second; the end of the list is not a switch");
        rig.NoticesOf(ServerNoticeCodes.RoleJobFailed).Should().ContainSingle();
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task S6_ARefusalThatHasNoReason_StillGoesOn_AndABadRefusalShapeIsRepaired(Kind kind)
    {
        await using var rig = await RigAsync();
        rig.Says(kind.FirstModel, Says(kind, kind.FirstModel, """{"status":"refused"}"""), Says(kind, kind.FirstModel, kind.Valid));

        var (_, job) = await RunAsync(rig, kind);

        job.State.Should().Be(RoleJobStates.Succeeded, job.Reason);
        job.RepairTurns.Should().Be(1, "a refusal with no reason is a shape error of the refused arm, and it goes back");
        rig.Models().Should().Equal(kind.FirstModel, kind.FirstModel);
    }

    // ---------- S7: a tree that changes ----------

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task S7_ATreeThatChangesDuringAnAttempt_EndsTreeChanged_WithNoFailover_AndNoAnswer(Kind kind)
    {
        await using var rig = await RigAsync();
        Environment.SetEnvironmentVariable("FAKECLI_SIDE_EFFECT", Path.Combine(rig.Repo, "written-by-the-model.txt"));
        rig.Says(kind.FirstModel, Says(kind, kind.FirstModel, kind.Valid)).Says(kind.SecondModel, Says(kind, kind.SecondModel, kind.Valid));

        var (_, job) = await RunAsync(rig, kind);

        (job.State, job.Stop).Should().Be((RoleJobStates.Failed, "tree-changed"));
        job.Reason.Should().Contain("written-by-the-model.txt");
        job.Attempts.Should().ContainSingle().Which.Stop.Should().Be("tree-changed");
        rig.Models().Should().Equal(kind.FirstModel);
        File.Exists(rig.World.Store.FolderOf(job.JobId)!.ResultFile).Should().BeFalse("an answer formed on a tree that changed is never published");
        rig.SwitchLines().Should().BeEmpty();
        rig.NoticesOf(ServerNoticeCodes.RoleModelSwitched).Should().BeEmpty();
        rig.NoticesOf(ServerNoticeCodes.RoleJobFailed).Should().ContainSingle();
    }

    // ---------- S10: a codex row and an agy row ----------

    private static readonly string[] NeverInAReadOnlyArgv =
    [
        "workspace-write", "danger-full-access", "--dangerously-bypass-approvals-and-sandbox", "--dangerously-bypass-hook-trust", "--dangerously-skip-permissions",
        "--add-dir", "--worktree", "--trust", "--remote-agent", "--remote-control", "acceptEdits", "accept-edits", "--restricted", "--continue",
    ];

    [Theory]
    [InlineData("scout", "terra")]
    [InlineData("judge", "astra")]
    public async Task S10_OnACodexRow_TheRecordedArgvIsCodexsReadOnlyOne(string role, string model)
    {
        await using var rig = await RigAsync();
        var kind = Of(role);
        rig.Says(model, FakeRoleAnswers.CodexSays(kind.Valid));

        var (_, job) = await RunAsync(rig, kind, [$"codex/{model}"]);

        job.State.Should().Be(RoleJobStates.Succeeded, job.Reason);
        var argv = rig.Launches().Should().ContainSingle().Subject;
        argv.Should().Contain("exec").And.Contain("-s").And.Contain("read-only");
        argv[Array.IndexOf(argv, "-s") + 1].Should().Be("read-only");
        argv.Should().NotContain(NeverInAReadOnlyArgv);
        argv[Array.IndexOf(argv, "-C") + 1].Should().Be(rig.Repo, "codex runs in the repository, read-only");
    }

    [Theory]
    [InlineData("scout")]
    [InlineData("judge")]
    public async Task S10_OnAnAgyRow_TheRecordedArgvIsAgysReadOnlyOne_AndTheReadOnlyFolderIsPrepared(string role)
    {
        await using var rig = await RigAsync();
        using var home = TempDir.For("coai-agy-home-");
        Directory.CreateDirectory(home.At(".local", "share"));
        var kind = Of(role);
        rig.Setting("COAI_ROLE_AGY_CLI", RoleJobWorld.FakeCli);
        rig.Says("gemini-3.8-flash-low", FakeRoleAnswers.AgySays(kind.Valid));
        var savedHome = Environment.GetEnvironmentVariable("HOME");
        var savedData = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        Environment.SetEnvironmentVariable("HOME", home);
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", home.At(".local", "share"));
        try
        {
            var (_, job) = await RunAsync(rig, kind, ["agy/gemini-3.8-flash-low"]);

            job.State.Should().Be(RoleJobStates.Succeeded, job.Reason);
            var argv = rig.Launches().Should().ContainSingle().Subject;
            argv.Should().Contain("--print=").And.Contain("--mode");
            argv[Array.IndexOf(argv, "--mode") + 1].Should().Be("plan");
            argv.Should().Contain("--agent").And.Contain(AntigravityReadOnly.AgentName, "the reader agent and its hook are what make agy read-only");
            argv.Should().NotContain(NeverInAReadOnlyArgv.Where(word => word != "--add-dir"), "agy may be given the checkout through --add-dir, as every read-only consult is");
            Directory.Exists(Path.Combine(home, ".local", "share", "coai-agy")).Should().BeTrue("the read-only folder of agy was prepared, in the temp home");
        }
        finally
        {
            Environment.SetEnvironmentVariable("HOME", savedHome);
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", savedData);
        }
    }

    [Fact]
    public void S10_NoScoutOrJudgeLaunch_CanCarryTheCodersCaseOfLaunchConfinement()
    {
        var coderCase = new Regex(@"LaunchConfinement\s*\.\s*Coder\b|\bCoderOsLayer\b|\bCoder\s*=|\bOsLayer\s*=|\bCoderLaunches\b|\bCoderHandle\b|\bCoderWork\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        var readOnlyRoles = new[] { "ReadOnlyRoleWork", "ScoutWork", "JudgeWork", "EvidenceJudge", "ReadOnlyEnvelope", "ReadOnlyPrompt", "ReadOnlyRoleRequest", "RoleEvidenceFiles", "ScoutPrompt", "JudgePrompt", "JudgeRoute" };

        foreach (var file in readOnlyRoles.Select(name => $"src_mcp/src/Server/RoleJobs/{name}.cs"))
        {
            coderCase.IsMatch(ProductionSources.CodeOf(file)).Should().BeFalse($"{file} is a read-only role's code: it never names the coder's write case");
        }

        coderCase.IsMatch(ProductionSources.CodeOf("src_mcp/src/Server/RoleJobs/CoderWork.cs")).Should().BeTrue("companion: the scan does see the coder's own work");
    }

    [Fact]
    public void S10_TheCodersCaseIsBuiltInOnePlaceOnly_CoderWork_SoNoOtherRoleHasAWayToIt()
    {
        var built = ProductionSources.FilesMatching(new Regex(@"new\s+LaunchConfinement\s*\.\s*Coder\s*\(", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)));

        built.Keys.Select(path => path.Replace('\\', '/')).Should().OnlyContain(path => path.EndsWith("/CoderWork.cs", StringComparison.Ordinal));
        built.Should().NotBeEmpty("companion: the scan finds the one place there is");
    }

    [Fact]
    public void S10_TheReadOnlyRolesWork_BuildsItsFailoverWithNeitherACoderNorAnOsLayer()
    {
        var code = ProductionSources.CodeOf("src_mcp/src/Server/RoleJobs/ReadOnlyRoleWork.cs");

        code.Should().Contain("new RoleFailover(run, judge.Judge)").And.Contain("MaxRepairTurns = RepairTurns");
        code.Should().NotContain("Coder").And.NotContain("OsLayer").And.NotContain("Follow =").And.NotContain("Restore =");
    }
}
