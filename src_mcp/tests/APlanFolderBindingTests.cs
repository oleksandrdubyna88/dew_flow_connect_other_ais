using System.Text.Json;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Core.Work;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E3 s3 t4 (d2 to d4): a coder job started under a FOLDER plan round is bound to what the reviewers read — its task is inside the scope, its base descends from the round's commit, and the approval id computed at the base is the approved
/// one. A change after the approval to a scope file, <c>feature.yaml</c>, the plan file, an ancestor's file, a spec, or the id or the title of an outside dependency each refuses a coder start and names the first path or node that differs; a new
/// RESULTS file, a file the pack never held and a node nobody in the scope depends on do not. The accepted case is on the same fixture beside every refusal.
/// </summary>
[Collection("fakecli-env")]
public sealed class APlanFolderBindingTests
{
    private const string Feature = "plan";

    /// <summary>The story of the second epic: it depends on a story and a task of the first, which are outside its scope.</summary>
    private const string Scope = "plan/epoch-1/e2-more/stories/s1-more";

    private const string ScopeTask = Scope + "/tasks/t1-m/task.yaml";

    /// <summary>The story of the first epic, whose epic has a spec file.</summary>
    private const string SpecScope = "plan/epoch-1/e1-core/stories/s1-first";

    private static string Error(JsonElement answer) => answer.TryGetProperty("error", out var error) ? error.GetString()! : string.Empty;

    private static string Verdict(JsonElement answer) => answer.TryGetProperty("verdict", out var verdict) ? verdict.GetString()! : "(" + answer + ")";

    /// <summary>The fixture: the binding rig's plan, widened so that a story has dependencies outside it and a sibling nobody depends on; committed on <c>feature</c>.</summary>
    private static async Task<PlanBindingRig> RigAsync()
    {
        var rig = await PlanBindingRig.CreateAsync();
        BaScenario.Write(rig.Repo, "plan/feature.yaml", "id: demo\ntitle: \"Demo\"\nplan: PLAN.md\nepochs:\n  - epoch-1\n");
        BaScenario.Write(rig.Repo, $"{Scope}/story.yaml", WorkPlanYaml.Story(new BaStory("s1-more", "More, part one", ["s1-first"], [])));
        BaScenario.Write(rig.Repo, ScopeTask, WorkPlanYaml.Task(new BaTask("t1-m", "Write more", "code", 3, ["t1-a"], false, ["src/more.cs"], [new BaDod("d1", "more works")], null)));
        BaScenario.Write(rig.Repo, "plan/epoch-1/e2-more/stories/s2-other/story.yaml", WorkPlanYaml.Story(new BaStory("s2-other", "Another part", [], [])));
        BaScenario.Write(rig.Repo, "plan/epoch-1/e2-more/stories/s2-other/tasks/t1-o/task.yaml", WorkPlanYaml.Task(new BaTask("t1-o", "Write another", "code", 3, [], false, ["src/other.cs"], [new BaDod("d1", "other works")], null)));
        await rig.CommitAsync("the plan folder, widened");

        return rig;
    }

    /// <summary>The feature's session takes a folder round for <paramref name="scope"/> to <c>proceed</c>; answers the session id.</summary>
    private static async Task<string> ApproveAsync(PlanBindingRig rig, string scope)
    {
        var opened = PlanBindingRig.Parse(await rig.Service.OpenAsync(rig.Repo, "feature"));
        var planned = PlanBindingRig.Parse(await rig.Service.ReviewPlanAsync(rig.Repo, "feature", new PlanInput(string.Empty, string.Empty, string.Empty) { FeatureFolder = Feature, WorkItem = scope }, TestContext.Current.CancellationToken));
        planned.TryGetProperty("error", out var error).Should().BeFalse($"the folder round ran, got: {error}");
        planned.GetProperty("verdict").GetString().Should().Be("proceed", planned.ToString());
        PlanBindingRig.Parse(await rig.Service.ResolveAsync(rig.Repo, "feature", "[]")).GetProperty("stage").GetString().Should().Be("CodeReview");

        return opened.GetProperty("sessionId").GetString()!;
    }

    private static async Task<string> JobAsync(PlanBindingRig rig, string taskFile, string? baseSha = null)
    {
        var jobId = await rig.StartJobAsync(baseSha, taskFile);
        jobId.Should().NotStartWith("error:");
        await rig.MakeCoderBranchAsync(jobId, rig.Jobs.ReadRequest(jobId)!.BaseSha);

        return jobId;
    }

    /// <summary>Commits one change after the approval, starts a job for <paramref name="taskFile"/> on top of it and asks the gate; answers the gate's answer.</summary>
    private static async Task<JsonElement> AfterAsync(PlanBindingRig rig, string taskFile, string path, Func<string, string> change)
    {
        var current = File.ReadAllText(Path.Combine(rig.Repo, path.Replace('/', Path.DirectorySeparatorChar)));
        BaScenario.Write(rig.Repo, path, change(current));
        await rig.CommitAsync($"after the approval: {path}");

        return await rig.ReviewCodeAsync(await JobAsync(rig, taskFile));
    }

    // ---------- d2: the accepted case, and the refusals about the task and the base ----------

    [Fact]
    public async Task D2_ATaskInsideTheScope_IsBound_TheRoundIsJudgedAgainstThePack_AndTheSessionRecordsTheApprovalId()
    {
        await using var rig = await RigAsync();
        var session = await ApproveAsync(rig, Scope);
        var head = await rig.HeadAsync();
        var jobId = await JobAsync(rig, ScopeTask);

        var round = await rig.ReviewCodeAsync(jobId);

        Error(round).Should().BeEmpty(round.ToString());
        Verdict(round).Should().Be("proceed");
        var link = rig.Jobs.Read(jobId)!.Plan!;
        link.SourceSession.Should().Be(session);
        link.PlanCommit.Should().Be(head);
        link.PlanPath.Should().Be(Scope);
        link.PlanHash.Should().MatchRegex("^[0-9a-f]{64}$", "the approval id, SHA-256 in hex");
        var saved = new SessionStore(rig.Data).Load(rig.Repo, $"coder/{jobId}")!;
        saved.PlanText.Should().Contain("**Not sent**").And.Contain($"## {ScopeTask}").And.Contain("## plan/PLAN.md");
        saved.State.Inherited!.PlanHash.Should().Be(link.PlanHash);
        var approved = new SessionStore(rig.Data).Load(rig.Repo, "feature")!.Rounds.Last(round => round.PlanSource is not null).PlanSource!;
        approved.Origin.Should().Be("folder");
        approved.Blob.Should().Be(link.PlanHash);
        approved.Path.Should().Be(Scope);
    }

    [Fact]
    public async Task D2_ATaskOutsideTheScope_IsRefused_WithASentenceOfItsOwn()
    {
        await using var rig = await RigAsync();
        await ApproveAsync(rig, Scope);
        var inside = await rig.ReviewCodeAsync(await JobAsync(rig, ScopeTask));
        Error(inside).Should().BeEmpty("the accepted case first, on this very fixture");

        var outside = await rig.ReviewCodeAsync(await JobAsync(rig, PlanBindingRig.TaskFile));

        Error(outside).Should().ContainAll("outside the approved scope", Scope, PlanBindingRig.TaskFile);
    }

    [Fact]
    public async Task D2_AWholeFeatureRound_BindsEveryTaskOfTheFeature()
    {
        await using var rig = await RigAsync();
        await ApproveAsync(rig, string.Empty);

        Error(await rig.ReviewCodeAsync(await JobAsync(rig, ScopeTask))).Should().BeEmpty();
        Error(await rig.ReviewCodeAsync(await JobAsync(rig, PlanBindingRig.TaskFile))).Should().BeEmpty();
    }

    [Fact]
    public async Task D2_ABaseThatDoesNotDescendFromTheRoundsCommit_IsRefused()
    {
        await using var rig = await RigAsync();
        var older = await rig.HeadAsync();
        BaScenario.Write(rig.Repo, "later.txt", "a commit the plan round saw\n");
        await rig.CommitAsync("a later commit");
        await ApproveAsync(rig, Scope);

        var round = await rig.ReviewCodeAsync(await JobAsync(rig, ScopeTask, older));

        Error(round).Should().Contain("descend").And.Contain(older[..12]);
    }

    // ---------- d3: a change of what the reviewers read refuses a coder start, and names the first difference ----------

    // ---------- d6: the pack format is in the approval id ----------

    private static string IdOf(IEnumerable<string> lines) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Concat(lines.Select(line => line + "\n")))));

    [Fact]
    public async Task D6_AnApprovalMadeWithoutTheVLine_OrUnderALowerFormat_BindsNoJob_WithASentenceThatNamesNoPath_AndTheCurrentOneBinds()
    {
        await using var rig = await RigAsync();
        await ApproveAsync(rig, Scope);
        var approved = new SessionStore(rig.Data).Load(rig.Repo, "feature")!.Rounds.Last(round => round.PlanSource is not null).PlanSource!;
        var job = rig.Jobs.Read(await JobAsync(rig, ScopeTask))!;
        var pack = ((PackOutcome.Packed)WorkPack.Build(ListedFolder.OfDisk(Path.Combine(rig.Repo, "plan")), new StrictYamlReader(), Feature, Scope)).Pack;
        var withoutV = IdOf(pack.Approval.Skip(1).Select(line => line.Text));
        var lower = IdOf(pack.Approval.Skip(1).Select(line => line.Text).Prepend("V 1"));
        pack.ApprovalId.Should().Be(approved.Blob, "CONTROL: the pack of this build, at the round's commit, is the approved one");
        var git = new CoaiMcp.Runners.Collecting.GitHistory(new CoaiMcp.Runners.Processes.ProcessLauncher());

        (await CoaiMcp.Server.RoleJobs.FolderPlanBinding.AtBaseAsync(git, rig.Repo, job, approved, TestContext.Current.CancellationToken)).Should().BeEmpty("the same scope approved under the current format binds");
        foreach (var old in new[] { withoutV, lower })
        {
            var refused = await CoaiMcp.Server.RoleJobs.FolderPlanBinding.AtBaseAsync(git, rig.Repo, job, approved with { Blob = old }, TestContext.Current.CancellationToken);

            refused.Should().Contain("pack format").And.Contain("new plan round").And.NotContain(Scope).And.NotContain("plan/", "the sentence names no path");
        }
    }

    [Fact]
    public async Task D3_AChangedScopeFile_RefusesTheStart_AndNamesTheFile()
    {
        await using var rig = await RigAsync();
        await ApproveAsync(rig, Scope);

        var round = await AfterAsync(rig, ScopeTask, ScopeTask, text => text + "# a requirement added after the approval\n");

        Error(round).Should().ContainAll(ScopeTask, "approved", "new plan round");
    }

    [Fact]
    public async Task D3_AChangedFeatureFile_RefusesTheStart_AndNamesTheFile()
    {
        await using var rig = await RigAsync();
        await ApproveAsync(rig, Scope);

        var round = await AfterAsync(rig, ScopeTask, "plan/feature.yaml", text => text + "# changed\n");

        Error(round).Should().ContainAll("plan/feature.yaml", "approved");
    }

    [Fact]
    public async Task D3_AChangedPlanFile_RefusesTheStart_AndNamesTheFile()
    {
        await using var rig = await RigAsync();
        await ApproveAsync(rig, Scope);

        var round = await AfterAsync(rig, ScopeTask, "plan/PLAN.md", text => text + "\nA new requirement nobody reviewed.\n");

        Error(round).Should().ContainAll("plan/PLAN.md", "approved");
    }

    [Theory]
    [InlineData("plan/epoch-1/epoch.yaml")]
    [InlineData("plan/epoch-1/e2-more/epic.yaml")]
    public async Task D3_AChangedAncestorFile_RefusesTheStart_AndNamesTheFile(string ancestor)
    {
        await using var rig = await RigAsync();
        await ApproveAsync(rig, Scope);

        var round = await AfterAsync(rig, ScopeTask, ancestor, text => text + "# changed\n");

        Error(round).Should().ContainAll(ancestor, "approved");
    }

    [Fact]
    public async Task D3_AChangedSpecOfTheEpic_RefusesTheStart_AndNamesTheFile()
    {
        await using var rig = await RigAsync();
        await ApproveAsync(rig, SpecScope);

        var round = await AfterAsync(rig, PlanBindingRig.TaskFile, "plan/epoch-1/e1-core/SPEC.md", text => text + "\nA new line of the spec.\n");

        Error(round).Should().ContainAll("plan/epoch-1/e1-core/SPEC.md", "approved");
    }

    [Theory]
    [InlineData("plan/epoch-1/e1-core/stories/s1-first/story.yaml", "A story", "epoch-1/e1-core/s1-first", "the title of a story")]
    [InlineData("plan/epoch-1/e1-core/stories/s1-first/tasks/t1-a/task.yaml", "Write the thing", "epoch-1/e1-core/s1-first/t1-a", "the title of a task")]
    public async Task D3_ATitleOfAnOutsideDependency_RefusesTheStart_AndNamesTheNode(string file, string title, string node, string because)
    {
        await using var rig = await RigAsync();
        await ApproveAsync(rig, Scope);

        var round = await AfterAsync(rig, ScopeTask, file, text => text.Replace(title, title + ", renamed", StringComparison.Ordinal));

        Error(round).Should().ContainAll(node, "approved", because.Contains("story", StringComparison.Ordinal) ? "A story, renamed" : "Write the thing, renamed");
    }

    [Fact]
    public async Task D3_TheIdOfAnOutsideDependency_RefusesTheStart_AndNamesTheFirstDifference()
    {
        await using var rig = await RigAsync();
        await ApproveAsync(rig, Scope);
        // The dependency's folder and id become s1-firsts and the scope story names it so: two scope-side edits, the first of which (in pack order) is the scope's own story file.
        await rig.GitAsync("mv", "plan/epoch-1/e1-core/stories/s1-first", "plan/epoch-1/e1-core/stories/s1-firsts");
        var moved = "plan/epoch-1/e1-core/stories/s1-firsts/story.yaml";
        BaScenario.Write(rig.Repo, moved, File.ReadAllText(Path.Combine(rig.Repo, moved)).Replace("id: s1-first", "id: s1-firsts", StringComparison.Ordinal));
        BaScenario.Write(rig.Repo, $"{Scope}/story.yaml", File.ReadAllText(Path.Combine(rig.Repo, $"{Scope}/story.yaml")).Replace("s1-first", "s1-firsts", StringComparison.Ordinal));
        await rig.CommitAsync("the dependency has another id");

        var round = await rig.ReviewCodeAsync(await JobAsync(rig, ScopeTask));

        Error(round).Should().ContainAll($"{Scope}/story.yaml", "approved");
    }

    // ---------- d4: what is not in the pack does not break the binding ----------

    [Fact]
    public async Task D4_ANewResultsFileInsideTheScope_AFileThePackNeverHeld_AndANodeNobodyInTheScopeDependsOn_DoNotBreakTheBinding()
    {
        await using var rig = await RigAsync();
        await ApproveAsync(rig, Scope);
        var before = await rig.ReviewCodeAsync(await JobAsync(rig, ScopeTask));
        Error(before).Should().BeEmpty("the accepted case first");

        BaScenario.Write(rig.Repo, $"{Scope}/RESULTS_t1-m.md", "# what was found\n\nnothing the reviewers read.\n");
        BaScenario.Write(rig.Repo, "plan/architecture/notes.md", "# a file the pack did not hold\n");
        BaScenario.Write(rig.Repo, "plan/epoch-1/e1-core/RESULTS_e1.md", "# results of the first epic\n");
        BaScenario.Write(rig.Repo, "plan/epoch-1/e2-more/stories/s2-other/story.yaml", File.ReadAllText(Path.Combine(rig.Repo, "plan/epoch-1/e2-more/stories/s2-other/story.yaml")).Replace("Another part", "Another part, renamed", StringComparison.Ordinal));
        BaScenario.Write(rig.Repo, "plan/epoch-1/e2-more/stories/s2-other/tasks/t1-o/task.yaml", File.ReadAllText(Path.Combine(rig.Repo, "plan/epoch-1/e2-more/stories/s2-other/tasks/t1-o/task.yaml")).Replace("Write another", "Write another, changed", StringComparison.Ordinal));
        BaScenario.Write(rig.Repo, "plan/epoch-1/e2-more/stories/s3-new/story.yaml", WorkPlanYaml.Story(new BaStory("s3-new", "A new story", [], [])));
        BaScenario.Write(rig.Repo, "plan/epoch-1/e2-more/stories/s3-new/tasks/t1-n/task.yaml", WorkPlanYaml.Task(new BaTask("t1-n", "Write the new", "code", 3, [], false, ["src/new.cs"], [new BaDod("d1", "new works")], null)));
        await rig.CommitAsync("things the reviewers did not read");

        var round = await rig.ReviewCodeAsync(await JobAsync(rig, ScopeTask));

        Error(round).Should().BeEmpty(round.ToString());
        Verdict(round).Should().Be("proceed");
    }

    [Fact]
    public async Task D4_AChangeAfterTheApproval_ThatEditsAnotherEpicsTaskTheScopeDoesNotDependOn_DoesNotBreakIt()
    {
        await using var rig = await RigAsync();
        await ApproveAsync(rig, SpecScope);
        BaScenario.Write(rig.Repo, "plan/epoch-1/e2-more/epic.yaml", File.ReadAllText(Path.Combine(rig.Repo, "plan/epoch-1/e2-more/epic.yaml")) + "# an epic nobody in the scope depends on\n");
        await rig.CommitAsync("the other epic changed");

        Error(await rig.ReviewCodeAsync(await JobAsync(rig, PlanBindingRig.TaskFile))).Should().BeEmpty();
    }
}
