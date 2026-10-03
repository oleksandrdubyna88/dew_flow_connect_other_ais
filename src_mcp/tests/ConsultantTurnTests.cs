using CoaiMcp.Core.Consultation;
using CoaiMcp.Core.Findings;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// One consultation turn makes one launch or two — never three — and bills the two once; the prompt
/// names the tools a vendor actually has, for the vendor that has said which.
/// </summary>
/// <remarks>
/// The scenario half (the real streams, the whole <c>consult</c> flow) is
/// <see cref="ConsultAntigravityDenialScenarioTests"/>. This half pins the decisions those runs cannot
/// isolate: that the tree is NOT asked again when no follow-up is coming, that no time left means no
/// follow-up, and the billing rule's three shapes.
/// </remarks>
public sealed class ConsultantTurnTests
{
    private const string Conversation = "5e1f2a3b-4c5d-4e6f-8a9b-0c1d2e3f4a5b";

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "antigravity", name));

    private static ReviewerInvocation First(TimeSpan timeout) =>
        new AntigravityConsultant(new AntigravityRuntime()).Build(new ConsultantLaunch(
            Path.GetTempPath(), "where is the sweep invoked?", string.Empty, Path.GetTempPath(),
            new ReviewerSettings("antigravity") { Timeout = timeout }));

    /// <summary>Answers each launch from a script, in order, and remembers what it was handed.</summary>
    private sealed class Scripted(params ProcessResult[] answers) : IProcessLauncher
    {
        public List<ProcessRequest> Launched { get; } = [];

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            Launched.Add(request);

            return Task.FromResult(answers[Math.Min(Launched.Count, answers.Length) - 1]);
        }
    }

    private static ProcessResult Denied => new(0, Fixture("consult-denied.ndjson"), Fixture("consult-denied.stderr.txt"), false);

    private static ProcessResult Advice => new(0, Fixture("consult-advice.ndjson"), string.Empty, false);

    private static async Task<(ConsultantTurnResult Result, Scripted Launcher, int TreeAsked)> Run(
        ProcessResult[] script,
        bool clean = true,
        TimeSpan? timeout = null,
        TimeSpan? treeTakes = null)
    {
        var launcher = new Scripted(script);
        var asked = 0;
        var landed = new List<ReviewerLaunch>();
        var result = await ConsultantTurn.RunAsync(
            new ReviewerExecutor(launcher),
            new AntigravityConsultant(new AntigravityRuntime()),
            First(timeout ?? TimeSpan.FromMinutes(1)),
            async token =>
            {
                asked++;
                await Task.Delay(treeTakes ?? TimeSpan.Zero, token);
                return clean ? [] : (IReadOnlyList<TreeChange>)[new TreeChange("written.txt", "appeared")];
            },
            landed.Add,
            TestContext.Current.CancellationToken);

        landed.Should().HaveCount(launcher.Launched.Count, "every launch is handed to the caller as it lands");

        return (result, launcher, asked);
    }

    [Fact]
    public async Task ADeniedLaunch_IsFollowedUpOnce_AndTheSecondIsTheFinal()
    {
        var (result, launcher, asked) = await Run([Denied, Advice]);

        launcher.Launched.Should().HaveCount(2);
        asked.Should().Be(1, "the tree is asked once, before the follow-up");
        result.FollowedUp.Should().BeTrue();
        result.Launches.Should().HaveCount(2);
        result.Final.Answer.Should().Contain("git grep -n QUOKKA");
        result.SurvivingHandle.Should().Be(Conversation);
        // STRICTLY less: the first launch and the tree check took some of the minute, and the follow-up gets
        // only what is left. `<=` stayed green with the cap deleted (epic 1's review).
        launcher.Launched[1].Timeout.Should().BeLessThan(TimeSpan.FromMinutes(1), "capped by what the turn has left");
    }

    [Fact]
    public async Task ATreeCheckThatSpendsTheBudget_LeavesNoFollowUp_AndNothingIsSpawned()
    {
        // The remainder is measured AFTER the tree check. A snapshot that took the rest of the budget
        // leaves nothing, and a process handed a zero timeout is started and killed in the same breath —
        // `ReviewerExecutor.NoRepairToRun` refuses that for the repair, and so does the follow-up.
        var (result, launcher, asked) = await Run([Denied, Advice], timeout: TimeSpan.FromMilliseconds(300), treeTakes: TimeSpan.FromMilliseconds(600));

        asked.Should().Be(1);
        launcher.Launched.Should().ContainSingle("no time was left after the tree check, so no follow-up was spawned");
        result.FollowedUp.Should().BeFalse();
    }

    [Theory]
    [InlineData("write_file")]
    [InlineData("mcp")]
    public async Task ADenialOutsideTheAllowlist_GetsNoFollowUp(string action)
    {
        var denied = new ProcessResult(0,
            Fixture("consult-denied.ndjson").Replace("\"action\":\"command\"", $"\"action\":\"{action}\"", StringComparison.Ordinal),
            string.Empty, false);

        var (result, launcher, _) = await Run([denied, Advice]);

        launcher.Launched.Should().ContainSingle($"a '{action}' denial is not one a follow-up was measured to cure");
        result.FollowedUp.Should().BeFalse();
    }

    [Fact]
    public async Task AnAnswerBesideADenial_IsTheAnswer_AndNoFollowUpRuns()
    {
        // Measured twice on 2026-10-02: the model answered with a plan AND had a command denied. The
        // decision is the TURN's, not the adapter's — the adapter would offer a follow-up for this stream.
        var answeredBeside = new ProcessResult(0,
            Fixture("consult-denied.ndjson").Replace("\"response\":\"\"", "\"response\":\"a plan, beside the denial\"", StringComparison.Ordinal),
            Fixture("consult-denied.stderr.txt"), false);

        var (result, launcher, asked) = await Run([answeredBeside, Advice]);

        launcher.Launched.Should().ContainSingle();
        asked.Should().Be(0);
        result.Final.Answer.Should().Be("a plan, beside the denial");
    }

    [Fact]
    public async Task ALaunchThatEndedOnItsOwnFailure_IsNotAnEmptyAnswer_AndNoFollowUpRuns()
    {
        // Timed out with the denied stream already printed: a failure with a reason of its own, which
        // continuing the conversation would only bill twice.
        var killed = new ProcessResult(-1, Fixture("consult-denied.ndjson"), Fixture("consult-denied.stderr.txt"), TimedOut: true);

        var (result, launcher, _) = await Run([killed, Advice]);

        launcher.Launched.Should().ContainSingle();
        result.Final.Terminal.Should().BeOfType<ReviewerOutcome.TimedOut>();
        result.SurvivingHandle.Should().Be(Conversation, "a killed launch's stream still names the conversation");
    }

    [Fact]
    public async Task AFollowUpDeniedAgain_IsNotFollowedUpAgain()
    {
        var (result, launcher, _) = await Run([Denied, Denied, Advice]);

        launcher.Launched.Should().HaveCount(2, "never a third launch");
        result.FollowedUp.Should().BeTrue();
        ConsultantTurn.AnsweredNothing(result.Final).Should().BeTrue();
    }

    [Fact]
    public async Task ALaunchThatAnswered_IsAlone_AndTheTreeIsNotAskedAgain()
    {
        var (result, launcher, asked) = await Run([Advice]);

        launcher.Launched.Should().ContainSingle();
        asked.Should().Be(0, "no follow-up is coming, so there is nothing to guard");
        result.FollowedUp.Should().BeFalse();
        result.Launches.Should().ContainSingle();
        result.Breached.Should().BeFalse();
    }

    [Fact]
    public async Task ATreeThatChangedBeforeTheFollowUp_IsABreach_WithOneLaunch()
    {
        var (result, launcher, asked) = await Run([Denied, Advice], clean: false);

        launcher.Launched.Should().ContainSingle();
        asked.Should().Be(1);
        result.Breached.Should().BeTrue();
        result.ChangesBeforeFollowUp.Should().ContainSingle().Which.Path.Should().Be("written.txt", "the changes that stopped the turn travel on it");
        result.FollowedUp.Should().BeFalse();
        result.SurvivingHandle.Should().Be(Conversation, "the first launch's conversation is still the record's");
    }

    [Fact]
    public async Task NoTimeLeft_MeansNoFollowUp()
    {
        // A zero budget: whatever the first launch took, nothing remains for a second.
        var (result, launcher, asked) = await Run([Denied, Advice], timeout: TimeSpan.Zero);

        launcher.Launched.Should().ContainSingle();
        asked.Should().Be(0);
        result.FollowedUp.Should().BeFalse();
    }

    [Fact]
    public async Task AKilledSecondLaunch_KeepsTheFirstsConversation_AndItsTokens()
    {
        var killed = new ProcessResult(-1, string.Empty, string.Empty, TimedOut: true);

        var (result, _, _) = await Run([Denied, killed]);

        result.Final.Terminal.Should().BeOfType<ReviewerOutcome.TimedOut>();
        result.SurvivingHandle.Should().Be(Conversation);
        result.TurnUsage.TokensIn.Should().Be(36652, "the timed-out second reported Usage.None, and the maximum keeps the first's");
    }

    [Fact]
    public void TheSurvivingHandle_IsTheLatestThatNamesOne()
    {
        var runtime = new AntigravityConsultant(new AntigravityRuntime());
        ReviewerLaunch Said(string stdout) => new(null, string.Empty, Usage.None, string.Empty, new ProcessResult(0, stdout, string.Empty, false));
        var other = """{"event":"init","conversation_id":"005ad061-c4ed-42e9-a09b-f51ccac80a46"}""";

        ConsultantTurn.SurvivingHandle(runtime, [Said(Fixture("consult-denied.ndjson")), Said(other)]).Should().Be("005ad061-c4ed-42e9-a09b-f51ccac80a46");
        ConsultantTurn.SurvivingHandle(runtime, [Said(Fixture("consult-denied.ndjson")), Said(string.Empty)]).Should().Be(Conversation);
        ConsultantTurn.SurvivingHandle(runtime, []).Should().BeEmpty();
    }

    // ---------- billing ----------

    [Fact]
    public void ACumulativeVendor_IsBilledTheFieldWiseMaximum()
    {
        ConsultationUsage.OfTwoLaunches(true, new Usage(36652, 874, null, TokensCached: 100), new Usage(34218, 3494, null, TokensCached: 50))
            .Should().Be(new Usage(36652, 3494, null, TokensCached: 100));
    }

    [Fact]
    public void APerLaunchVendor_IsBilledTheSum()
    {
        ConsultationUsage.OfTwoLaunches(false, new Usage(100, 10, 0.25), new Usage(50, 5, 0.5))
            .Should().Be(new Usage(150, 15, 0.75));
    }

    [Fact]
    public void ASecondThatReportedNothing_NeverErasesTheFirst()
    {
        var first = new Usage(36652, 874, 0.4);

        ConsultationUsage.OfTwoLaunches(true, first, Usage.None).Should().Be(first);
        ConsultationUsage.OfTwoLaunches(false, first, Usage.None).Should().Be(first);
        ConsultationUsage.OfTwoLaunches(true, Usage.None, Usage.None).CostUsd.Should().BeNull("neither launch priced anything");
        ConsultationUsage.OfTwoLaunches(true, first, Usage.Unknown).NotCaptured.Should().BeTrue("an uncaptured share makes the figure a floor");
    }

    // ---------- the toolbox ----------

    private static ConsultantPromptInput Prompt(string toolbox) =>
        new("You are a CONSULTANT.", new TurnBudget(5, 0), "ab12cd34", "why?", [], "main", "0123abc", Toolbox: toolbox);

    [Fact]
    public void TheToolbox_IsSaidUnderWhatYouHave_ForAntigravity()
    {
        var toolbox = ((IConsultantRuntime)new AntigravityConsultant(new AntigravityRuntime())).Toolbox;

        var prompt = ConsultantPrompt.Compose(Prompt(toolbox));

        var have = prompt.IndexOf("## What you have", StringComparison.Ordinal);
        var budget = prompt.IndexOf("## The budget", StringComparison.Ordinal);
        prompt.IndexOf(toolbox, StringComparison.Ordinal).Should().BeInRange(have, budget, "it belongs to what the consultant HAS");
        prompt.Should().NotContain(AntigravityFollowUps.StaysDenied, "a first turn must never look like a follow-up");
    }

    [Fact]
    public void EveryOtherConsultant_SaysNothingAboutTools_AndThePromptIsUnchanged()
    {
        IConsultantRuntime[] others =
        [
            new ClaudeConsultant(new ClaudeRuntime()),
            new CodexConsultant(new CodexRuntime()),
            new LocalConsultant(new LocalRuntime("local", "http://127.0.0.1:11434/v1"), "local"),
        ];

        others.Should().OnlyContain(runtime => runtime.Toolbox.Length == 0);
        ConsultantPrompt.Compose(Prompt(string.Empty)).Should().NotContain("view_file");
    }
}
