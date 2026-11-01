using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// When the cloud reviewers have all answered and found at most one remark between them, the local
/// reviewer finishes the launch it is on and starts no more — issue #485, behind a switch that is off.
/// </summary>
public sealed class TheLocalReviewerStandsDownTests
{
    private const string Engine = "http://127.0.0.1:11434/v1";
    private const string HeldOnTheCard = "--held-on-the-card";
    private const string Fails = "--fails";

    /// <summary>A cloud row that answers ONE finding with exactly the title that follows — the plan-incomplete token.</summary>
    private const string AnswersOnly = "--only-title:";

    private static ReviewerWork Local(string role, bool held = false) =>
        new(new ReviewerInvocation("local", role, new ProcessRequest("dotnet", [held ? HeldOnTheCard : "--quick"], "."), SharedResource: Engine));

    private static ReviewerWork Cloud(string provider, string role, int remarks = 0, bool fails = false, string answersOnly = "") =>
        new(new ReviewerInvocation(provider, role, new ProcessRequest("dotnet", [fails ? Fails : answersOnly.Length > 0 ? AnswersOnly + answersOnly : $"--remarks={remarks}"], ".")));

    /// <summary>The first local row runs on the card until released; the rest are queued behind it.</summary>
    private static ReviewerWork[] Round(params ReviewerWork[] cloud) =>
        [Local(RoleCatalog.ArchitectureRole, held: true), Local(RoleCatalog.SecurityRole), Local(RoleCatalog.UxDxRole), .. cloud];

    /// <summary>Runs the round, releasing the held local row only once every cloud row has finished.</summary>
    private static async Task<IReadOnlyList<(ReviewerInvocation Invocation, ReviewerOutcome Outcome)>> Run(
        ReviewerWork[] work, bool switchedOn = true, Func<ReviewerOutcome, ReviewerOutcome>? settle = null)
    {
        var card = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cloudRows = work.Count(w => !w.Invocation.IsOnEngine);
        var cloudFinished = 0;
        var scheduler = new BoundedScheduler(globalCap: 4, perProviderCap: 3, sharedResourceCap: 1);
        var run = scheduler.RunAllAsync(
            work,
            new ReviewerExecutor(new CardLauncher(card.Task)),
            TestContext.Current.CancellationToken,
            p =>
            {
                if (p.Provider != "local" && p.Outcome is not null && Interlocked.Increment(ref cloudFinished) == cloudRows)
                {
                    card.TrySetResult();
                }
            },
            switchedOn ? StandDown.For(work) : null,
            settle);
        if (cloudRows == 0)
        {
            card.TrySetResult();
        }

        return await run;
    }

    private static IEnumerable<string> StoodDownRoles(IReadOnlyList<(ReviewerInvocation Invocation, ReviewerOutcome Outcome)> results) =>
        results.Where(r => r.Outcome is ReviewerOutcome.StoodDown).Select(r => r.Invocation.Role);

    [Fact]
    public async Task AQuietCloud_LetsTheRunningLocalLaunchFinish_AndStartsNoOther()
    {
        var results = await Run(Round(Cloud("codex", RoleCatalog.ArchitectureRole, remarks: 1), Cloud("gemini", RoleCatalog.ArchitectureRole)));

        results.Single(r => r.Invocation.Role == RoleCatalog.ArchitectureRole && r.Invocation.Provider == "local").Outcome
            .Should().BeOfType<ReviewerOutcome.Ok>("the launch already on the card finishes");
        StoodDownRoles(results).Should().BeEquivalentTo([RoleCatalog.SecurityRole, RoleCatalog.UxDxRole]);
        results.OfType<(ReviewerInvocation, ReviewerOutcome)>().Select(r => r.Item2).OfType<ReviewerOutcome.StoodDown>()
            .Should().AllSatisfy(s => s.Reason.Should().Contain("1 remark"));
    }

    /// <summary>
    /// Issue #485 meets D12 of the plan gate: a cloud reviewer that answered ONLY <c>PLAN_INCOMPLETE &lt;id&gt;</c> found one
    /// finding — and used to count as one quiet remark, so with the switch on the local reviewer (the one reviewer that could
    /// still read the plan) was never started. The count reads the settled outcome now: that reviewer FAILED.
    /// </summary>
    [Fact]
    public async Task ACloudReviewerThatDidNotGetThePlan_IsNotAQuietCloud()
    {
        const string plan = "# PLAN — a thing\n\nthe very last line of the plan";
        var title = PlanIncomplete.TitleOf(plan);

        var results = await Run(
            Round(Cloud("codex", RoleCatalog.ArchitectureRole, answersOnly: title)),
            settle: outcome => PlanIncomplete.Settle(Stage.PlanReview, title, outcome));

        StoodDownRoles(results).Should().BeEmpty("a cloud that never saw the plan is a failed row, not a quiet one — the local reviewer must start");
        results.Single(r => r.Invocation.Provider == "codex").Outcome.Should().BeOfType<ReviewerOutcome.Unparseable>();
        results.Where(r => r.Invocation.Provider == "local").Select(r => r.Outcome).Should().AllBeOfType<ReviewerOutcome.Ok>();
    }

    [Fact]
    public async Task AStoodDownReviewer_IsNotAsked_NeverFailed()
    {
        var results = await Run(Round(Cloud("codex", RoleCatalog.ArchitectureRole)));

        var summary = ReviewerSummaryFactory.From(results);

        summary.Failures.Should().BeEmpty("standing down is a decision, not a failure");
        summary.NotAsked.Select(s => s.Role).Should().BeEquivalentTo(["local/" + RoleCatalog.SecurityRole, "local/" + RoleCatalog.UxDxRole]);
        summary.Sentence.Should().Contain("not asked");
    }

    [Fact]
    public async Task AQuietCloud_StillRunsTheQueuedSecurityLane()
    {
        var results = await Run([.. Round(Cloud("codex", RoleCatalog.ArchitectureRole)),
            Local("redteam-general") with { IsSecurity = true }]);

        StoodDownRoles(results).Should().BeEquivalentTo([RoleCatalog.SecurityRole, RoleCatalog.UxDxRole]);
        results.Single(r => r.Invocation.Role == "redteam-general").Outcome.Should().BeOfType<ReviewerOutcome.Ok>();
    }

    [Fact]
    public async Task SecurityLaneRemarks_DoNotChangeTheOrdinaryCloudStandDownDecision()
    {
        var results = await Run(Round(Cloud("codex", RoleCatalog.ArchitectureRole),
            Cloud("gemini", "redteam-general", remarks: 2) with { IsSecurity = true }));

        StoodDownRoles(results).Should().BeEquivalentTo([RoleCatalog.SecurityRole, RoleCatalog.UxDxRole]);
        results.Single(r => r.Invocation.Role == "redteam-general").Outcome.Should().BeOfType<ReviewerOutcome.Ok>();
    }

    [Theory]
    [InlineData(2, false, true, "two remarks are not almost nothing")]
    [InlineData(0, true, true, "a failed cloud reviewer's zero is not 'found little'")]
    [InlineData(0, false, false, "the switch is off")]
    public async Task TheLocalReviewerKeepsGoing_WhenTheCloudWasNotQuiet_OrTheSwitchIsOff(int remarks, bool oneFails, bool switchedOn, string because)
    {
        var results = await Run(
            Round(Cloud("codex", RoleCatalog.ArchitectureRole, remarks), Cloud("gemini", RoleCatalog.ArchitectureRole, fails: oneFails)),
            switchedOn);

        StoodDownRoles(results).Should().BeEmpty(because);
    }

    /// <summary>
    /// The count must know a cloud outcome before anybody is TOLD of it. Recorded after the report, a local
    /// row the report woke could check the count in between, find it one short and start — a race the
    /// code round's own reviewer found in the tests above, which release the card on that very report.
    /// </summary>
    [Fact]
    public async Task WhenTheLastCloudOutcomeIsReported_TheCountAlreadyHoldsIt()
    {
        var work = Round(Cloud("codex", RoleCatalog.ArchitectureRole), Cloud("gemini", RoleCatalog.ArchitectureRole));
        var card = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var standDown = StandDown.For(work);
        var cloudFinished = 0;
        var quietWhenTold = new List<bool>();

        await new BoundedScheduler(globalCap: 4, perProviderCap: 3, sharedResourceCap: 1).RunAllAsync(
            work,
            new ReviewerExecutor(new CardLauncher(card.Task)),
            TestContext.Current.CancellationToken,
            p =>
            {
                if (p.Provider != "local" && p.Outcome is not null && Interlocked.Increment(ref cloudFinished) == 2)
                {
                    quietWhenTold.Add(standDown.Quiet);
                    card.TrySetResult();
                }
            },
            standDown);

        quietWhenTold.Should().Equal([true], "the report of the last cloud outcome comes after the count holds it");
    }

    /// <summary>
    /// A lens is spent by being ASKED. With lenses dealt, a stood-down local row never used its prompt, so
    /// marking it spent would skip that lens until the pool resets — CodeRabbit, on the pull request.
    /// </summary>
    [Fact]
    public async Task AStoodDownRow_DoesNotSpendItsLens()
    {
        ReviewerWork[] work =
        [
            Local(RoleCatalog.ArchitectureRole, held: true) with { Prompt = "arch-lens" },
            Local(RoleCatalog.SecurityRole) with { Prompt = "security-lens" },
            Cloud("codex", RoleCatalog.ArchitectureRole) with { Prompt = "codex-lens" },
        ];

        var results = await Run(work);

        StoodDownRoles(results).Should().Equal([RoleCatalog.SecurityRole], "the premise: the second local row stood down");
        CoaiMcp.Server.RoundEngine.SpentPrompts(work, results).Should().BeEquivalentTo(
            ["arch-lens", "codex-lens"], "a row that stood down was never asked, so its lens is still unspent");
    }

    [Fact]
    public async Task ARoundWithNoCloudReviewer_NeverStopsTheLocalOne()
    {
        var results = await Run(Round());

        StoodDownRoles(results).Should().BeEmpty("there was nobody to find little");
    }

    /// <summary>
    /// The held local row waits for the card to be released; cloud rows answer with the number of remarks
    /// they were given, or fail; every other local row answers at once.
    /// </summary>
    private sealed class CardLauncher(Task card) : IProcessLauncher
    {
        public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            var argument = request.Arguments[0];
            if (argument.StartsWith(AnswersOnly, StringComparison.Ordinal))
            {
                return new ProcessResult(0, OneFinding(argument[AnswersOnly.Length..]), string.Empty, false);
            }

            if (argument == HeldOnTheCard)
            {
                await card.WaitAsync(ct);
            }

            return argument == Fails
                ? new ProcessResult(1, string.Empty, "boom", false)
                : new ProcessResult(0, Answer(argument), string.Empty, false);
        }

        private static string OneFinding(string title) =>
            $$"""{"status":"FINDINGS","findings":[{{Finding(title)}}]}""";

        private static string Finding(string title) =>
            $$"""{"severity":"minor","category":"clarity","file":null,"line":null,"title":"{{title}}","why":"w","fix":"f","trigger":"input","mechanism":"missing check","consequence":"unintended output"}""";

        private static string Answer(string argument)
        {
            var remarks = argument.StartsWith("--remarks=", StringComparison.Ordinal) ? int.Parse(argument["--remarks=".Length..]) : 0;
            var one = Finding("t");

            return $$"""{"status":"{{(remarks == 0 ? "SECURE" : "FINDINGS")}}","findings":[{{string.Join(",", Enumerable.Repeat(one, remarks))}}]}""";
        }
    }
}
