using System.Collections.Immutable;
using System.Text.Json;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// E1.S4 of <c>todo/PLAN_the_plan_gate_reads_the_committed_plan_E1.md</c> (parent D12): every plan reviewer is told
/// where the plan ends, and a reviewer that says the plan did not reach it is a failed reviewer, not an answer.
/// </summary>
/// <remarks>
/// The sentinel is a detector, not a proof — it catches a cut tail, a pointer-only body and a reviewer's own statement
/// that it saw no plan. The hollow pass it exists for (a plan the classifier cut, reviewed clean) was a
/// <c>planText</c> round, so these rounds are <c>planText</c> rounds.
/// </remarks>
[Collection("fakecli-env")]
public sealed class EveryPlanReviewerSeesWhereThePlanEndsTests : FakeCliRoundTests
{
    private const string Plan = "# PLAN — a thing\n\n## Build order\n\n1. one\n2. two\n\nthe very last line of the plan";

    private static readonly string Id = DocumentRules.ArtifactIdOf(Plan);

    private static string TokenFinding(string id) => $$"""
        {"severity": "blocking", "category": "completeness", "file": "", "line": 0,
         "title": "PLAN_INCOMPLETE {{id}}", "why": "the plan did not reach me", "fix": "send it whole"}
        """;

    private static string Answer(params string[] findings) => "{\"findings\": [" + string.Join(",", findings) + "]}";

    private const string RealFinding = """
        {"severity": "major", "category": "reliability", "file": "", "line": 0,
         "title": "no timeout on the poll", "why": "a hung peer hangs the loop", "fix": "add one"}
        """;

    private const string OtherFinding = """
        {"severity": "minor", "category": "clarity", "file": "", "line": 0,
         "title": "the second step is vague", "why": "it names no owner", "fix": "name one"}
        """;

    private static ReviewerInvocation Anyone => new("codex", "plan", new ProcessRequest("x", [], "."));

    private static Finding Titled(string title) =>
        new(Severity.Blocking, Category.Completeness, string.Empty, 0, title, "why", "fix", ["codex"]);

    private static ReviewerOutcome.Ok Answered(params Finding[] findings) =>
        new(new NormalisedReview([.. findings], ImmutableArray<RejectedEntry>.Empty), false, Usage.None);

    private static IReadOnlyList<(ReviewerInvocation Invocation, ReviewerOutcome Outcome)> One(ReviewerOutcome outcome) =>
        [(Anyone, outcome)];

    private static ReviewerOutcome Folded(Stage stage, ReviewerOutcome outcome, string plan = Plan) =>
        PlanIncomplete.Fold(stage, plan, One(outcome)).Single().Outcome;

    private PanelService TwoVendors() =>
        ServiceFor(Defaults() with
        {
            Providers = [new("codex") { ExecutablePath = FakeCliExe }, new("gemini") { ExecutablePath = FakeCliExe }],
        });

    /// <summary>Codex reads the file after <c>-o</c>, gemini reads stdout: two answers in one round.</summary>
    private static void Script(string codex, string gemini)
    {
        Environment.SetEnvironmentVariable("FAKECLI_OUTFILE_TEXT", codex);
        Environment.SetEnvironmentVariable("FAKECLI_STDOUT", gemini);
        Environment.SetEnvironmentVariable("FAKECLI_EXIT", "0");
    }

    private async Task<(JsonElement Answer, string[] Prompts)> RoundRecordingPrompts(PanelService service, string plan)
    {
        var record = Directory.CreateTempSubdirectory("coai-plan-end-").FullName;
        Environment.SetEnvironmentVariable("FAKECLI_RECORD_DIR", record);
        try
        {
            await service.OpenAsync(_repo, "feature");
            var answer = Parse(await service.ReviewPlanAsync(_repo, "feature", plan, TestContext.Current.CancellationToken));
            string[] prompts =
            [
                .. Directory.GetFiles(record, "*.argv").Select(f => LaunchRecords.Read(f).Split('\0')[^1]),
            ];

            return (answer, prompts);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAKECLI_RECORD_DIR", null);
            Directory.Delete(record, recursive: true);
        }
    }

    private static string Instruction(string id) =>
        $"The plan below is marked by `BEGIN OF PLAN {id}` (the first line under its heading) and `END OF PLAN {id}` (its last "
        + "line). If you cannot see BOTH, or the section holds no plan body (only a path or a pointer), answer with exactly "
        + $"one blocking finding titled exactly `PLAN_INCOMPLETE {id}` and nothing else.";

    private static string Restated(string id) =>
        $"Reminder: if the first line under the heading was not `BEGIN OF PLAN {id}`, answer with exactly one blocking "
        + $"finding titled exactly `PLAN_INCOMPLETE {id}` and nothing else.";

    [Fact]
    public void ThePlanSection_IsTheInstruction_TheHeading_ItsBeginLine_ThePlan_TheReminder_AndItsEndLine()
    {
        PlanSection.Compose(Plan, string.Empty).Should().Be(
            $"{Instruction(Id)}\n\n## The plan under review\nBEGIN OF PLAN {Id}\n\n{Plan}\n\n{Restated(Id)}\nEND OF PLAN {Id}");
    }

    [Fact]
    public void ASourceLine_SitsBetweenTheBeginLineAndThePlan()
    {
        const string source = "(read by the server from todo/PLAN_x.md at commit 0123456789ab, blob fedcba987654, 70 bytes)";

        PlanSection.Compose(Plan, source).Should()
            .Contain($"## The plan under review\nBEGIN OF PLAN {Id}\n{source}\n\n{Plan}\n\n{Restated(Id)}\nEND OF PLAN {Id}");
    }

    [Fact]
    public async Task APlanTextRound_IsTodaysPromptPlusTwoLines()
    {
        // T14, the prompt half: everything from the heading on is today's bytes, with the instruction line before
        // the heading and the end line after the plan — and nothing else added anywhere.
        Script(Clean, Clean);

        var (_, prompts) = await RoundRecordingPrompts(TwoVendors(), Plan);

        prompts.Should().HaveCount(2);
        foreach (var prompt in prompts)
        {
            var heading = prompt.IndexOf("## The plan under review", StringComparison.Ordinal);
            prompt[heading..].Should().Be($"## The plan under review\nBEGIN OF PLAN {Id}\n\n{Plan}\n\n{Restated(Id)}\nEND OF PLAN {Id}");
            prompt[..heading].Should().EndWith($"{Instruction(Id)}\n\n");
            prompt.Split("END OF PLAN").Should().HaveCount(3, "the instruction names the line once and the section ends with it once");
            prompt.Split("BEGIN OF PLAN").Should().HaveCount(4, "the instruction, the begin line and the reminder each name it once");
        }
    }

    [Fact]
    public void TheIdCannotBeForgedByThePlan()
    {
        // A plan that carries BOTH markers under another id — at its start and at its end — must not confuse either one.
        var forged = "BEGIN OF PLAN 0000000000000000\n" + Plan + "\n\nEND OF PLAN 0000000000000000";
        var id = DocumentRules.ArtifactIdOf(forged);

        var section = PlanSection.Compose(forged, string.Empty);

        id.Should().NotBe("0000000000000000");
        section.Should().EndWith($"\n\nEND OF PLAN 0000000000000000\n\n{Restated(id)}\nEND OF PLAN {id}", "the real line is the LAST line");
        section.Should().Contain($"## The plan under review\nBEGIN OF PLAN {id}\n\nBEGIN OF PLAN 0000000000000000\n", "the real begin line is the FIRST line");
        section.Should().Contain($"`PLAN_INCOMPLETE {id}`");
    }

    [Theory]
    [InlineData("PLAN_INCOMPLETE ")]
    [InlineData("  plan_incomplete ")]
    public void AnAnswerThatIsOnlyTheToken_IsAFailedReviewer_NotAnAnswer(string spelling)
    {
        var outcome = Folded(Stage.PlanReview, Answered(Titled(spelling + Id + " ")));

        outcome.Should().BeOfType<ReviewerOutcome.Unparseable>()
            .Which.Reason.Should().Be("the plan did not reach this reviewer whole (PLAN_INCOMPLETE)");
    }

    [Fact]
    public void TheFoldedReviewer_KeepsItsUsage()
    {
        var usage = new Usage(1234, 56, 0.12);
        var answered = new ReviewerOutcome.Ok(new NormalisedReview([Titled("PLAN_INCOMPLETE " + Id)], []), false, usage);

        Folded(Stage.PlanReview, answered).Should().BeOfType<ReviewerOutcome.Unparseable>()
            .Which.Usage.Should().Be(usage, "the tokens were spent and the ledger must say so");
    }

    [Fact]
    public void TwoIdenticalTokens_AreStillAFailedReviewer_NotTwoFindings()
    {
        // The sentinel is a statement that the plan did not arrive, however many times it is made: a reviewer that says it
        // twice must not slip under the threshold as an answer with two findings and no review.
        var outcome = Folded(Stage.PlanReview, Answered(Titled("PLAN_INCOMPLETE " + Id), Titled("PLAN_INCOMPLETE " + Id)));

        outcome.Should().BeOfType<ReviewerOutcome.Unparseable>().Which.Reason.Should().Be("the plan did not reach this reviewer whole (PLAN_INCOMPLETE)");
    }

    [Fact]
    public void TheTokenBesideRealFindings_IsAFailedReviewer_AndTheNoteSaysHowManyWereSetAside()
    {
        var answered = Answered(Titled("PLAN_INCOMPLETE " + Id), Titled("no timeout"), Titled("layers cross"));

        Folded(Stage.PlanReview, answered).Should().BeOfType<ReviewerOutcome.Unparseable>().Which.Reason
            .Should().Be("the plan did not reach this reviewer whole (PLAN_INCOMPLETE); 2 other findings set aside");
    }

    [Fact]
    public void OneSetAsideFinding_IsSaidInTheSingular()
    {
        Folded(Stage.PlanReview, Answered(Titled("PLAN_INCOMPLETE " + Id), Titled("no timeout"))).Should()
            .BeOfType<ReviewerOutcome.Unparseable>().Which.Reason.Should().EndWith("; 1 other finding set aside");
    }

    [Fact]
    public void ARetriedReviewer_KeepsEveryEarlierLaunchAndItsWarning_WhenItIsFolded()
    {
        // BoundedScheduler carries what earlier attempts cost on `EarlierLaunches`, and LiveRound.Finish totals
        // `TotalUsage`: reclassifying the outcome must not make a billed attempt free.
        var earlier = new Usage(900, 40, 0.5);
        var turn = new TurnUsage(1, new Usage(70, 3, 0.01), TimeSpan.FromSeconds(4));
        var answered = new ReviewerOutcome.Ok(new NormalisedReview([Titled("PLAN_INCOMPLETE " + Id)], []), false, new Usage(1234, 56, 0.12))
        {
            EarlierLaunches = earlier,
            EarlierTurns = [turn],
            Warning = "an unmeasured CLI",
            CliVersion = "1.2.3",
        };

        var folded = Folded(Stage.PlanReview, answered);

        folded.Should().BeOfType<ReviewerOutcome.Unparseable>();
        folded.TotalUsage.Should().Be(answered.TotalUsage, "the retry's spend is still spent");
        folded.EarlierLaunches.Should().Be(earlier);
        folded.EarlierTurns.Should().Equal(turn);
        folded.Warning.Should().Be("an unmeasured CLI");
        folded.CliVersion.Should().Be("1.2.3");
    }

    [Fact]
    public void ATokenForAnotherPlan_IsNotThisPlansToken()
    {
        var answered = Answered(Titled("PLAN_INCOMPLETE 0000000000000000"));

        Folded(Stage.PlanReview, answered).Should().BeSameAs(answered);
    }

    [Fact]
    public void ACodeStageAnswerWithThatTitle_IsNeverFolded()
    {
        var answered = Answered(Titled("PLAN_INCOMPLETE " + Id));

        foreach (var stage in (Stage[])[Stage.CodeReview, Stage.DocumentReview])
        {
            Folded(stage, answered).Should().BeSameAs(answered, $"{stage} has no plan to be cut");
        }
    }

    [Fact]
    public void AFailureThatIsAlreadyAFailure_PassesThroughUntouched()
    {
        var failure = new ReviewerOutcome.Unparseable("not JSON");

        Folded(Stage.PlanReview, failure).Should().BeSameAs(failure);
    }

    [Fact]
    public async Task OneReviewerThatDidNotGetThePlan_IsOneOfTwoAnswered_AndItsTokenIsNotMerged()
    {
        // T15. Codex answers the token, gemini answers a finding.
        Script(Answer(TokenFinding(Id)), Answer(RealFinding));
        var service = TwoVendors();
        await service.OpenAsync(_repo, "feature");

        var answer = Parse(await service.ReviewPlanAsync(_repo, "feature", Plan, TestContext.Current.CancellationToken));

        answer.GetProperty("reviewers").GetString().Should().Contain("1 of 2 reviewers answered");
        var titles = answer.GetProperty("findings").EnumerateArray().Select(f => f.GetProperty("title").GetString()).ToArray();
        titles.Should().Equal(["no timeout on the poll"], "the token's finding is a failure, never a merged finding");
    }

    [Fact]
    public async Task EveryReviewerThatDidNotGetThePlan_IsNoAnswerAtAll_AndTheGateCallsAHuman()
    {
        Script(Answer(TokenFinding(Id)), Answer(TokenFinding(Id)));
        var service = TwoVendors();
        await service.OpenAsync(_repo, "feature");

        var answer = Parse(await service.ReviewPlanAsync(_repo, "feature", Plan, TestContext.Current.CancellationToken));

        answer.GetProperty("verdict").GetString().Should().Be("call_human");
        answer.GetProperty("reviewers").GetString().Should().Contain("0 of 2 reviewers answered");
    }

    [Fact]
    public async Task TheTokenBesideTwoRealFindings_IsNoAnswer_AndNoneOfTheThreeIsMerged()
    {
        // T19, the fold half, as decided on the cadence consultation: a reviewer that says the plan did not reach it did
        // not review the plan, so its other findings are set aside — and the reviewer line says how many.
        Script(Answer(TokenFinding(Id), RealFinding, OtherFinding), Answer(TokenFinding(Id), RealFinding, OtherFinding));
        var service = ServiceFor(Defaults());
        await service.OpenAsync(_repo, "feature");

        var answer = Parse(await service.ReviewPlanAsync(_repo, "feature", Plan, TestContext.Current.CancellationToken));

        answer.GetProperty("reviewers").GetString().Should().Contain("0 of 1 reviewers answered").And.Contain("2 other findings set aside");
        answer.GetProperty("findings").GetArrayLength().Should().Be(0);
        answer.GetProperty("verdict").GetString().Should().Be("call_human");
    }

    [Fact]
    public async Task TwoIdenticalTokensFromTheOnlyReviewer_IsNoAnswer_AndTheGateCallsAHuman()
    {
        // The defect: two copies stayed an `Ok`, counted as answered, and under the plan threshold the round proceeded
        // with no review at all.
        Script(Answer(TokenFinding(Id), TokenFinding(Id)), Answer(TokenFinding(Id), TokenFinding(Id)));
        var service = ServiceFor(Defaults());
        await service.OpenAsync(_repo, "feature");

        var answer = Parse(await service.ReviewPlanAsync(_repo, "feature", Plan, TestContext.Current.CancellationToken));

        answer.GetProperty("verdict").GetString().Should().Be("call_human");
        answer.GetProperty("reviewers").GetString().Should().Contain("0 of 1 reviewers answered");
    }

    [Fact]
    public async Task ATokenForAnotherPlanBesideRealFindings_IsAnAnswer_AndMerged()
    {
        Script(Answer(TokenFinding("0000000000000000"), RealFinding), Answer(TokenFinding("0000000000000000"), RealFinding));
        var service = ServiceFor(Defaults());
        await service.OpenAsync(_repo, "feature");

        var answer = Parse(await service.ReviewPlanAsync(_repo, "feature", Plan, TestContext.Current.CancellationToken));

        answer.GetProperty("reviewers").GetString().Should().Contain("all 1 reviewers answered");
        answer.GetProperty("findings").GetArrayLength().Should().Be(2);
    }
}
