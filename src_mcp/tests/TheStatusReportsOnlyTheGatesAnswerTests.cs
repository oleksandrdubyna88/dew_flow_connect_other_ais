using System.Text.Json;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// <c>status</c> reports the person's decision only when it is the answer the gate would act on
/// (<c>research/PLAN_ask_human_is_for_the_gate.md</c>, G5).
/// </summary>
/// <remarks>
/// Found 2026-10-03 on the operator's own machine: an AI's A-or-B question had become a card, the operator pressed
/// "Stop and act on the findings" on it, and <c>status</c> for that session answered <c>humanDecision: "fix"</c> —
/// a gate decision nobody made, read by any AI that resumed the session. <c>status</c> read the NEWEST answered
/// card of the session, whatever it was; the gate itself already read answers by identity
/// (<see cref="CurrentAnswer"/>). The third reader is now moved onto the same rule.
/// </remarks>
public sealed class TheStatusReportsOnlyTheGatesAnswerTests : IAsyncLifetime
{
    private readonly ProcessLauncher _launcher = new();
    private readonly string _data = Directory.CreateTempSubdirectory("coai-status-answer-").FullName;
    private TempGitRepo _repo = null!;

    public async ValueTask InitializeAsync()
    {
        _repo = await TempGitRepo.InitAsync(_launcher, "coai-status-answer-repo-");
        await _repo.WriteAsync("a.txt", "a\n");
        await _repo.CommitAsync("base");
    }

    public async ValueTask DisposeAsync()
    {
        await _repo.DisposeAsync();
        try
        {
            Directory.Delete(_data, recursive: true);
        }
        catch (IOException) { }
    }

    private PanelService Service() =>
        new(new PanelSettings { DataDir = _data, Providers = [] }, VaultKeys.None("no vault"), default,
            _launcher, Serilog.Core.Logger.None, Noticing.None);

    private void Save(SessionState state) => new SessionStore(_data).Save(new PersistedSession(state, []));

    private SessionState Session(bool held, IReadOnlyList<string> holdQuestions) =>
        new("s-status", _repo.Path, "main", PanelConfig.Uniform(3, 2))
        {
            PlanProceeded = true,
            Stage = Stage.CodeReview,
            HumanGate = held,
            HoldQuestions = holdQuestions,
        };

    private void Answered(string id, string decision, string words)
    {
        var escalations = new Escalations(_data);
        escalations.Notify(new EscalationQuestion(id, "s-status", _repo.Path, "main", "a question", "a question", "en", string.Empty, [], DateTime.UtcNow.ToString("O")));
        File.WriteAllText(escalations.AnswerPath(id), JsonSerializer.Serialize(new { id, answer = words, decision, answeredUtc = DateTime.UtcNow.ToString("O") }));
    }

    private async Task<JsonElement> Status() =>
        JsonDocument.Parse(await Service().StatusAsync(_repo.Path, "main")).RootElement;

    [Fact]
    public async Task StatusDoesNotReportAnAnswerToAQuestionTheGateNeverAsked()
    {
        Save(Session(held: false, holdQuestions: []));
        Answered("aibquestion1", "fix", "Stop and act on the findings");

        var status = await Status();

        status.GetProperty("humanDecision").GetString().Should().BeEmpty(
            "the button was pressed on the AI's own question, which the gate never asked — it is not a decision about the gate");
        status.GetProperty("humanAnswer").GetString().Should().BeEmpty();
    }

    [Fact]
    public async Task TheHoldsOwnAnswer_IsStillReported()
    {
        Save(Session(held: true, holdQuestions: ["holdnotice01"]));
        Answered("holdnotice01", "discuss", "Stop and talk to me");
        Thread.Sleep(20);
        Answered("aibquestion2", "fix", "a later press on a card the gate never asked");

        var status = await Status();

        status.GetProperty("humanDecision").GetString().Should().Be("discuss",
            "the hold's own question was answered — and the NEWER answer to another card, which the old read returned, is not the gate's");
        status.GetProperty("humanAnswer").GetString().Should().Be("Stop and talk to me");
    }

    /// <summary>
    /// The other half of what status reports: on a feature review with no hold, the person's answer to a question asked
    /// for round 2 (<see cref="CurrentAnswer.ForRequest"/>) — through the real <c>status</c> call, addressed by the plan
    /// path as a caller passes it, so the feature session's lookup and both reported fields are what is tested.
    /// </summary>
    [Fact]
    public async Task OnAFeatureReview_TheRequestsAnswerIsWhatStatusReports()
    {
        const string plan = "PLAN_x.md";
        await _repo.WriteAsync(plan, "# PLAN — x\n");
        await _repo.CommitAsync("the plan");
        var round = DateTime.UtcNow.AddMinutes(-10);
        new SessionStore(_data).Save(new PersistedSession(
            new SessionState("s-status", _repo.Path, SessionKey.FeatureBranch, PanelConfig.Uniform(2, 0))
            {
                Stage = Stage.FeatureReview,
                Feature = DocumentReader.IdentityOf(_repo.Path, plan, DocumentReader.FollowLink),
                RoundsRunThisStage = 1,
                RequestQuestions = ["request0001"],
            },
            [new RoundRecord(nameof(Stage.FeatureReview), 1, "good_enough", 1, "all 1 reviewers answered", round)]));
        Answered("request0001", "continue", "run it again");

        var status = JsonDocument.Parse(await Service().StatusAsync(_repo.Path, "any-branch", string.Empty, string.Empty, feature: plan)).RootElement;

        status.GetProperty("humanDecision").GetString().Should().Be("continue", status.ToString());
        status.GetProperty("humanAnswer").GetString().Should().Be("run it again");
    }
}
