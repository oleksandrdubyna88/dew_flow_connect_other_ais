using System.Text.Json;
using Xunit;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Context;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog.Core;

namespace CoaiMcp.Tests;

/// <summary>
/// **Every** class that launches the fake CLI belongs here, not only the ones that set
/// `FAKECLI_*`.
/// </summary>
/// <remarks>
/// Environment variables are process-wide, and xUnit runs collections in parallel: a class that
/// merely launches the fake with argv verbs would find `FAKECLI_MODE=vendor` set underneath it by
/// a class running beside it, take the vendor branch, and answer something the caller never asked
/// for. It failed exactly that way on a CI runner —
/// `FullLoop_FlawedPlan_RevisesToTheGate_ThenCodeRounds_ThenDone` got an error answer where a
/// verdict belonged — and passed locally every time, because the interleaving needs the timing.
/// </remarks>
[CollectionDefinition("fakecli-env", DisableParallelization = true)]
public sealed class FakeCliEnvCollection;

/// <summary>
/// The whole orchestrator against real git and the vendor-mode fake CLI: this is the epic-04 DoD
/// line — the full loop, from a plain caller, with no vendor anywhere near CI.
/// </summary>
[Collection("fakecli-env")]
public sealed class PanelServiceTests : IAsyncLifetime
{
    private const string Scope = CodeRoundHarness.Scope;

    private const string CleanReview = CodeRoundHarness.CleanReview;

    private const string OneMajor = CodeRoundHarness.OneMajor;

    private readonly CodeRoundHarness _h = new();

    public ValueTask InitializeAsync() => _h.InitializeAsync();

    public ValueTask DisposeAsync() => _h.DisposeAsync();

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public async Task Open_IsIdempotentPerRepoAndBranch()
    {
        var service = _h.Service();
        var first = Parse(await service.OpenAsync(_h.Repo, "feature"));
        var second = Parse(await service.OpenAsync(_h.Repo, "feature"));

        second.GetProperty("sessionId").GetString().Should().Be(first.GetProperty("sessionId").GetString());
    }

    [Fact]
    public async Task ReviewCode_BeforePlanProceed_Refuses()
    {
        var service = _h.Service();
        await service.OpenAsync(_h.Repo, "feature");

        var answer = Parse(await service.ReviewCodeAsync(_h.Repo, "feature", "main", Scope));

        answer.GetProperty("error").GetString().Should().Contain("plan gate comes first");
    }

    [Fact]
    public async Task ReviewPlan_MergesTheSameFindingAcrossProviders_IntoOne()
    {
        CodeRoundHarness.SetAnswer(OneMajor);
        var service = _h.Service(threshold: 0);
        await service.OpenAsync(_h.Repo, "feature");

        var answer = Parse(await service.ReviewPlanAsync(_h.Repo, "feature", "the plan"));

        answer.GetProperty("verdict").GetString().Should().Be("revise");
        var findings = answer.GetProperty("findings");
        findings.GetArrayLength().Should().Be(1, "codex and gemini raised the SAME defect");
        findings[0].GetProperty("providers").GetArrayLength().Should().Be(2);
        answer.GetProperty("reviewers").GetString().Should().Contain("all 2 reviewers answered");
    }

    [Fact]
    public async Task FullLoop_PlanProceeds_CodePasses_SessionDone()
    {
        var service = _h.Service();
        await service.OpenAsync(_h.Repo, "feature");

        Parse(await service.ReviewPlanAsync(_h.Repo, "feature", "the plan"))
            .GetProperty("verdict").GetString().Should().Be("proceed");
        Parse(await service.ResolveAsync(_h.Repo, "feature", "[]"))
            .GetProperty("instruction").GetString().Should().Contain("review_code");

        Parse(await service.ReviewCodeAsync(_h.Repo, "feature", "main", Scope))
            .GetProperty("verdict").GetString().Should().Be("proceed");
        Parse(await service.ResolveAsync(_h.Repo, "feature", "[]"))
            .GetProperty("stage").GetString().Should().Be("Done");
    }

    [Fact]
    public async Task Resolve_RejectionWithoutAReason_RefusesTheWholeCall()
    {
        CodeRoundHarness.SetAnswer(OneMajor);
        var service = _h.Service(threshold: 0);
        await service.OpenAsync(_h.Repo, "feature");
        await service.ReviewPlanAsync(_h.Repo, "feature", "the plan");

        var answer = Parse(await service.ResolveAsync(_h.Repo, "feature", """[{"finding": 0, "action": "reject"}]"""));

        answer.GetProperty("error").GetString().Should().Contain("without a reason");
    }

    [Fact]
    public async Task RejectedWithReason_DoesNotGate_TheNextRound()
    {
        CodeRoundHarness.SetAnswer(OneMajor);
        var service = _h.Service(threshold: 0);
        await service.OpenAsync(_h.Repo, "feature");
        await service.ReviewPlanAsync(_h.Repo, "feature", "the plan");
        await service.ResolveAsync(_h.Repo, "feature",
            """[{"finding": 0, "action": "reject", "reason": "constant time is not required for this token"}]""");

        // The reviewers repeat the same remark with the same argument next round.
        var answer = Parse(await service.ReviewPlanAsync(_h.Repo, "feature", "the plan v2"));

        answer.GetProperty("verdict").GetString().Should().Be("proceed", "a standing rejection does not count");
        answer.GetProperty("discounted").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task Status_SurvivesAServerRestart()
    {
        var service = _h.Service();
        await service.OpenAsync(_h.Repo, "feature");
        await service.ReviewPlanAsync(_h.Repo, "feature", "the plan");
        await service.ResolveAsync(_h.Repo, "feature", "[]");

        var reborn = _h.Service(); // a fresh instance over the same data dir IS the restart
        var status = Parse(await reborn.StatusAsync(_h.Repo, "feature"));

        status.GetProperty("stage").GetString().Should().Be("CodeReview");
        status.GetProperty("rounds").GetArrayLength().Should().Be(1);
        status.GetProperty("rounds")[0].GetProperty("verdict").GetString().Should().Be("proceed");
    }

    [Fact]
    public async Task TheRepairLaunch_IsGivenNoWorkspace_BecauseItIsAskingForAnAnswer()
    {
        // A repair says "your last answer was not the schema's JSON, send the JSON". Handing it
        // the checkout again invites the same exploration that produced the empty answer — the
        // plan stage learned this at ten minutes a round, and antigravity failed intermittently on
        // the code stage until the repair stopped carrying a workspace.
        var repairs = await RepairLaunchesOfACodeRoundAsync();

        repairs.Should().NotBeEmpty("the first answers were not the schema's JSON, so every reviewer was repaired — a test that saw no repair saw nothing");
        repairs.Should().OnlyContain(repair => Path.GetFileName(repair.WorkingDirectory).StartsWith("coai-repair-", StringComparison.Ordinal),
            "the repair launch runs in a directory of its own, never in the checkout it was refused");
        repairs.Should().OnlyContain(repair => repair.EntriesAtLaunch.Count == 0,
            "an empty directory is what makes an agentic CLI answer instead of explore");
    }

    /// <summary>
    /// The positive control of the repair test must be a control of the CODE round: in a workspace mode where the review launch runs in an
    /// empty folder, it has to FAIL — and not be satisfied by a plan-round launch made earlier through the same recording launcher
    /// (code gate, 2026-10-19: the control used to read every review launch since the service was built).
    /// </summary>
    [Fact]
    public async Task TheRepairTestsControl_IsRedWhenTheCodeRoundsReviewsRanInAnEmptyFolder()
    {
        var act = () => RepairLaunchesOfACodeRoundAsync(workspace: "none");

        await act.Should().ThrowAsync<Xunit.Sdk.XunitException>("the code round's review launches saw an empty folder, so the control has nothing to prove with");
    }

    /// <summary>
    /// A code round whose first answers are not the schema's JSON, so each reviewer is launched a second time with the repair prompt;
    /// the answers to the repair are clean. Returns the repair launches, each with its folder's entries read BEFORE it started.
    /// </summary>
    private async Task<IReadOnlyList<CodeRoundHarness.RepairLaunch>> RepairLaunchesOfACodeRoundAsync(string workspace = "worktree")
    {
        var round = await _h.RepairedCodeRoundAsync(workspace);
        round.Reviews.Should().Contain(review => review.EntriesAtLaunch.Count > 0, "in worktree mode the review launched in the checkout, so the repair folder being empty is a choice and not an accident");

        return round.Repairs;
    }

    [Fact]
    public async Task CodeStage_FansOutThreeRolesPerProvider_WithThreeDistinctPrompts()
    {
        using var record = TempDir.For("coai-record-");
        Environment.SetEnvironmentVariable("FAKECLI_RECORD_DIR", record);
        try
        {
            var service = _h.Service();
            await service.OpenAsync(_h.Repo, "feature");
            await service.ReviewPlanAsync(_h.Repo, "feature", "the plan");
            await service.ResolveAsync(_h.Repo, "feature", "[]");
            Directory.GetFiles(record).Should().HaveCount(2, "the plan stage is one reviewer per provider");
            foreach (var file in Directory.GetFiles(record))
            {
                File.Delete(file);
            }

            await service.ReviewCodeAsync(_h.Repo, "feature", "main", Scope);

            var argvs = Directory.GetFiles(record, "*.argv")
                .Select(f => LaunchRecords.Read(f).Split('\0'))
                .ToList();
            argvs.Should().HaveCount(6, "two providers x three roles — dealing is opt-in and off here");
            // Both vendors take the prompt on STDIN, which the fake records as the last field.
            var prompts = argvs.Select(a => a[^1]).Distinct().ToList();
            prompts.Should().HaveCount(3, "three roles, three distinct prompts — each vendor gets the same three");
            prompts.Should().Contain(p => p.Contains("ARCHITECTURE reviewer"));
            prompts.Should().Contain(p => p.Contains("SECURITY AND RELIABILITY"));
            prompts.Should().Contain(p => p.Contains("CODE ONLY: no browser"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAKECLI_RECORD_DIR", null);
        }
    }

    [Fact]
    public async Task ThePlanStage_GivesReviewersNoCheckout_ToExplore()
    {
        // A ten-minute plan round was an agentic CLI reading the repository it had been handed.
        // The role is to judge the DOCUMENT; only the code stage needs a tree.
        using var record = TempDir.For("coai-cwd-");
        Environment.SetEnvironmentVariable("FAKECLI_RECORD_DIR", record);
        try
        {
            var service = _h.Service();
            await service.OpenAsync(_h.Repo, "feature");
            await service.ReviewPlanAsync(_h.Repo, "feature", "the plan");

            var worktrees = Path.Combine(_h.Data, "worktrees");
            var created = Directory.Exists(worktrees) ? Directory.GetDirectories(worktrees) : [];
            created.Should().BeEmpty("the plan stage checks out nothing at all");

            var pointedAt = Directory.GetFiles(record, "*.argv")
                .Select(f => LaunchRecords.Read(f).Split('\0'))
                .Select(a => Array.IndexOf(a, "-C") is var i and >= 0 ? a[i + 1] : string.Empty)
                .Where(d => d.Length > 0)
                .ToList();
            pointedAt.Should().OnlyContain(d => !d.Contains("coai-wt-"), "and points nobody at one");
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAKECLI_RECORD_DIR", null);
        }
    }

    [Fact]
    public async Task ThePlanStage_IsToldItHasNoCheckout_NotMerelyGivenNone()
    {
        // The other half of the test above, and the one the prompts got wrong for months: taking the
        // checkout away is not the same as SAYING so. A plan reviewer stood in an empty scratch
        // directory while its prompt claimed a read-only checkout was under it — and an agentic CLI
        // that believes that goes looking, is refused the permission headlessly, and answers nothing.
        using var record = TempDir.For("coai-plan-said-");
        Environment.SetEnvironmentVariable("FAKECLI_RECORD_DIR", record);
        try
        {
            var service = _h.Service();
            await service.OpenAsync(_h.Repo, "feature");
            await service.ReviewPlanAsync(_h.Repo, "feature", "the plan");

            var prompts = Directory.GetFiles(record, "*.argv")
                .Select(f => LaunchRecords.Read(f).Split('\0')[^1])
                .ToList();

            prompts.Should().NotBeEmpty("the round has to have launched something to have said anything");
            prompts.Should().OnlyContain(p => p.Contains("no tool you can call"));
            prompts.Should().OnlyContain(p => !p.Contains("READ-ONLY checkout"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAKECLI_RECORD_DIR", null);
        }
    }

    [Fact]
    public async Task MaxRoundsExhausted_YieldsCallHuman_WithTheOpenCount()
    {
        CodeRoundHarness.SetAnswer(OneMajor);
        var service = _h.Service(threshold: 0, maxRounds: 1);
        await service.OpenAsync(_h.Repo, "feature");

        var answer = Parse(await service.ReviewPlanAsync(_h.Repo, "feature", "the plan"));

        answer.GetProperty("verdict").GetString().Should().Be("call_human");
        answer.GetProperty("instruction").GetString().Should().Contain("do not proceed on your own");
    }

    /// <summary>
    /// With dealing ON, a round asks every lens once instead of asking one lens twice.
    /// </summary>
    /// <remarks>
    /// Opt-in, because of what it trades: with it off two vendors answer the same question and the
    /// dedup merges what they agree on, which is the strongest signal this product produces. On, the
    /// round costs half the launches and every lens gets used. The operator chose to have the choice.
    /// </remarks>
    [Fact]
    public async Task WithDealingOn_ACodeRound_AsksEachRoleOnce_AcrossTheVendors()
    {
        using var record = TempDir.For("coai-deal-");
        Environment.SetEnvironmentVariable("FAKECLI_RECORD_DIR", record);
        try
        {
            var service = _h.Service(deal: true);
            await service.OpenAsync(_h.Repo, "feature");
            await service.ReviewPlanAsync(_h.Repo, "feature", "the plan");
            await service.ResolveAsync(_h.Repo, "feature", "[]");
            foreach (var file in Directory.GetFiles(record))
            {
                File.Delete(file);
            }

            await service.ReviewCodeAsync(_h.Repo, "feature", "main", Scope);

            var argvs = Directory.GetFiles(record, "*.argv").Select(f => LaunchRecords.Read(f).Split((char)0)).ToList();
            argvs.Should().HaveCount(3, "three roles dealt across two vendors, one each");
            argvs.Select(a => a[^1]).Distinct().Should().HaveCount(3, "and no prompt asked twice");
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAKECLI_RECORD_DIR", null);
        }
    }

    [Fact]
    public async Task WithDealingOn_ThePlanRound_SpendsADifferentLensPerVendor()
    {
        using var record = TempDir.For("coai-deal-plan-");
        Environment.SetEnvironmentVariable("FAKECLI_RECORD_DIR", record);
        try
        {
            var service = _h.Service(deal: true);
            await service.OpenAsync(_h.Repo, "feature");
            await service.ReviewPlanAsync(_h.Repo, "feature", "the plan");

            var prompts = Directory.GetFiles(record, "*.argv")
                .Select(f => LaunchRecords.Read(f).Split((char)0)[^1])
                .ToList();
            prompts.Should().HaveCount(2, "one per vendor");
            prompts.Distinct().Should().HaveCount(2,
                "each vendor got a DIFFERENT lens — asking both the same one is what this replaces");
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAKECLI_RECORD_DIR", null);
        }
    }

    /// <summary>
    /// A family mount with one rule of the plan tier in it, and one rule that is not.
    /// </summary>
    private void WriteFamilyRules()
    {
        File.WriteAllText(Path.Combine(_h.Repo, ".gitmodules"),
            "[submodule \"conventions\"]\n path = .agents/conventions\n url = https://example.invalid/rules\n");
        var common = Directory.CreateDirectory(Path.Combine(_h.Repo, ".agents", "conventions", "common")).FullName;
        File.WriteAllText(Path.Combine(common, "reuse-first.md"), "Look before you build.");
        File.WriteAllText(Path.Combine(common, "logging-serilog.md"), "Serilog, coloured, one file per run.");
    }

    /// <summary>The prompt a reviewer was actually handed, from the fake CLI's own recording.</summary>
    /// <remarks>
    /// NUL-joined argv with the stdin text last — asserted on the RECORDED launch rather than on the
    /// context a test passed to <c>BuildWork</c>, because the question here is whether the STAGE
    /// builds the rules into its context, and a test that hands the context in cannot answer it.
    /// </remarks>
    private static string RecordedPrompt(string record) =>
        Directory.GetFiles(record, "*.argv").Select(f => LaunchRecords.Read(f).Split('\0')[^1]).First();

    /// <summary>
    /// The plan gate is judged against the rules this project wrote down.
    /// </summary>
    /// <remarks>
    /// Until this shipped the stage said so in its own log line — "no diff and no rules at this
    /// stage" — so a plan reviewer held this repository's plan to its own taste, and a conventions
    /// finding has nothing to quote.
    /// </remarks>
    [Fact]
    public async Task APlanRound_IsGivenTheRulesItIsJudgedAgainst()
    {
        WriteFamilyRules();
        var record = Directory.CreateTempSubdirectory("coai-plan-rules-").FullName;
        Environment.SetEnvironmentVariable("FAKECLI_RECORD_DIR", record);
        try
        {
            var service = _h.Service();
            await service.OpenAsync(_h.Repo, "feature");
            await service.ReviewPlanAsync(_h.Repo, "feature", "the plan");

            var prompt = RecordedPrompt(record);
            prompt.Should().Contain("The rules this project has written down");
            prompt.Should().Contain("Look before you build", "a rule of the plan tier");
            prompt.Should().NotContain("one file per run",
                "a logging recipe is not what a PLAN is judged against");

            // The rules come BEFORE the thing they judge. A prompt carrying the right rules after
            // the plan has already been read is a prompt that taught the reviewer nothing in time.
            prompt.IndexOf("The rules this project has written down", StringComparison.Ordinal)
                .Should().BeLessThan(prompt.IndexOf("The plan under review", StringComparison.Ordinal));
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAKECLI_RECORD_DIR", null);
            Directory.Delete(record, recursive: true);
        }
    }

    /// <summary>
    /// A partial mount says how much of the tier it had — in the PROMPT, where the reviewer is.
    /// </summary>
    /// <remarks>
    /// The failure this prevents: a round judged against one rule of seven reads exactly like a round
    /// judged against all seven, so a reviewer's silence about a rule it never saw looks like
    /// compliance. Raised on story 1.2's code round and again on this one's plan round.
    /// </remarks>
    [Fact]
    public async Task APlanRoundWhoseTierIsOnlyPartlyHere_SaysSoToTheReviewer()
    {
        WriteFamilyRules();
        var record = Directory.CreateTempSubdirectory("coai-plan-partial-").FullName;
        Environment.SetEnvironmentVariable("FAKECLI_RECORD_DIR", record);
        try
        {
            var service = _h.Service();
            await service.OpenAsync(_h.Repo, "feature");
            await service.ReviewPlanAsync(_h.Repo, "feature", "the plan");

            // WriteFamilyRules carries exactly one rule of the plan tier.
            RecordedPrompt(record).Should().Contain($"1 of the {StageRules.Plan.Length} rules");
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAKECLI_RECORD_DIR", null);
            Directory.Delete(record, recursive: true);
        }
    }

    /// <summary>
    /// A repository carrying NONE of the tier is told so in the strongest words the prompt has.
    /// </summary>
    [Fact]
    public async Task APlanRoundWithNoneOfItsTier_TellsTheReviewerNotToReadItAsCompliance()
    {
        var record = Directory.CreateTempSubdirectory("coai-plan-none-").FullName;
        Environment.SetEnvironmentVariable("FAKECLI_RECORD_DIR", record);
        try
        {
            var service = _h.Service();
            await service.OpenAsync(_h.Repo, "feature");
            await service.ReviewPlanAsync(_h.Repo, "feature", "the plan");

            var prompt = RecordedPrompt(record);
            prompt.Should().Contain("NONE of the");
            prompt.Should().Contain("Do not read their absence as compliance");
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAKECLI_RECORD_DIR", null);
            Directory.Delete(record, recursive: true);
        }
    }

    /// <summary>
    /// The plan stage is handed no checkout, and this is what keeps that true.
    /// </summary>
    /// <remarks>
    /// Its working directory is an empty scratch folder on purpose: an agentic CLI handed a checkout
    /// goes exploring, which is what made a plan round take ten minutes. It is also why the rules are
    /// collected from <c>repoPath</c> — collecting from the working directory would gather nothing
    /// while looking like it worked.
    /// </remarks>
    [Fact]
    public async Task APlanRound_StillGetsNoWorktree()
    {
        WriteFamilyRules();
        var service = _h.Service();
        await service.OpenAsync(_h.Repo, "feature");

        await service.ReviewPlanAsync(_h.Repo, "feature", "the plan");

        var worktrees = await _h.Launcher.RunAsync(new ProcessRequest(
            "git", ["worktree", "list", "--porcelain"], _h.Repo));
        worktrees.StdOut.Split("worktree ", StringSplitOptions.RemoveEmptyEntries)
            .Should().ContainSingle("the plan stage is handed no checkout — that is what keeps an "
                + "agentic CLI answering instead of exploring, and it is why the rules come from repoPath");
    }

    /// <summary>
    /// The CODE stage is untouched by the two gates that gained rules.
    /// </summary>
    /// <remarks>
    /// It collects with the default order, not a stage tier — so a rule outside the plan tier still
    /// reaches a code reviewer. Without this, a later change that gave every stage a tier would pass
    /// the two new tests while quietly narrowing the one stage that was already working.
    /// </remarks>
    [Fact]
    public async Task TheCodeStage_StillCollectsEveryRule_NotAStageTier()
    {
        WriteFamilyRules();

        // Committed, because the code stage reads its rules from the round's WORKTREE at the
        // reviewed SHA — the rules as of the commit under review, not as of this afternoon. That is
        // the property the plan and document stages deliberately do NOT share: they have no commit.
        await _h.Git("add", ".");
        await _h.Git("commit", "-m", "the family rules");

        var record = Directory.CreateTempSubdirectory("coai-code-rules-").FullName;
        Environment.SetEnvironmentVariable("FAKECLI_RECORD_DIR", record);
        try
        {
            var service = _h.Service();
            await service.OpenAsync(_h.Repo, "feature");
            await service.ReviewPlanAsync(_h.Repo, "feature", "the plan");
            await service.ResolveAsync(_h.Repo, "feature", "[]");
            foreach (var file in Directory.GetFiles(record))
            {
                File.Delete(file);
            }

            await service.ReviewCodeAsync(_h.Repo, "feature", "main", Scope);

            var prompts = Directory.GetFiles(record, "*.argv")
                .Select(f => LaunchRecords.Read(f).Split('\0')[^1]);
            prompts.Should().Contain(p => p.Contains("one file per run", StringComparison.Ordinal),
                "the logging rule is outside the PLAN tier and must still reach a code reviewer");
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAKECLI_RECORD_DIR", null);
            Directory.Delete(record, recursive: true);
        }
    }
}
