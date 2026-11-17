using System.Text.Json.Nodes;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A session opened under one policy ends its next round under the CURRENT one, and a hold raised by a build before policy auto keeps its
/// config as it was loaded (<c>todo/PLAN_auto_never_asks_a_person_E1_S1_spec.md</c>, § S1.10.4, D8).
/// </summary>
/// <remarks>
/// Real <c>review_plan</c> rounds over the fake CLI, in a throwaway repository and data folder. The stamp is the begin's, after the person's
/// decision is applied, so what a round runs on is never a config whose marker is absent.
/// </remarks>
[Collection("fakecli-env")]
public sealed class ASessionFollowsTheCurrentPolicyTests : FakeCliRoundTests
{
    private const string Branch = "epic-1";

    private const string Plan = "# PLAN — held\n\n## Build order\n\n1. one\n2. two\n";

    private const string OneBlockingFinding = """
        {"findings": [
          {"severity": "blocking", "category": "reliability", "file": "", "line": 0,
           "title": "the poll has no timeout", "why": "a hung peer hangs the loop", "fix": "add one"}
        ]}
        """;

    private const string AcceptTheFinding = """[{"finding":0,"action":"accept"}]""";

    private SessionStore Store => new(_data, Defaults().Rounds.Catalog);

    private PersistedSession Session() => Store.Load(_repo, Branch)!;

    private PanelService ServiceUnder(PanelConfig rounds) =>
        ServiceFor(Defaults() with { Rounds = rounds, CadenceMode = Core.Cadence.CadenceMode.Off, SplitPlan = false });

    /// <summary>The session FILE's <c>state.config</c>, as it is spelled on disk.</summary>
    private JsonObject ConfigOnDisk() =>
        JsonNode.Parse(File.ReadAllText(Store.FileFor(_repo, Branch)))!["state"]!["config"]!.AsObject();

    [Fact]
    public async Task ASessionOpenedUnderHuman_EndsItsNextRoundUnderAuto()
    {
        await using var harness = new CodeRoundHarness();
        await harness.InitializeAsync();

        await harness.Service(threshold: 0, onExhausted: StagePolicy.Human, maxRounds: 1).OpenAsync(harness.Repo, "feature");
        CodeRoundHarness.SetAnswer(CodeRoundHarness.OneMajor);

        var reply = Parse(await harness.Service(threshold: 0, maxRounds: 1, autonomous: true)
            .ReviewPlanAsync(harness.Repo, "feature", Plan, TestContext.Current.CancellationToken));

        reply.GetProperty("verdict").GetString().Should().Be("good_enough", reply.ToString());
        var session = new SessionStore(harness.Data).Load(harness.Repo, "feature")!;
        session.State.HumanGate.Should().BeFalse();
        session.State.Config.OnExhausted.Should().Be(StagePolicy.GoodEnough);
        session.State.Config.Autonomous.Should().BeTrue();
        Directory.Exists(Path.Combine(harness.Data, "escalations")).Should().BeFalse("no person was called, so no notice was written");
        JsonNode.Parse(File.ReadAllText(new SessionStore(harness.Data).FileFor(harness.Repo, "feature")))!["state"]!["config"]!["autonomous"]!
            .GetValue<bool>().Should().BeTrue("the file says what the round ran on");
    }

    [Fact]
    public async Task TogglingThePolicy_DoesNotRefillTheLadder()
    {
        // Two rounds per stage, threshold 0: one finding every round, each resolved with accept.
        var underEscalate = ServiceUnder(PanelConfig.Uniform(2, 0, StagePolicy.Escalate));
        await underEscalate.OpenAsync(_repo, Branch);
        Script(OneBlockingFinding);

        async Task<string> RoundAsync(PanelService service)
        {
            var verdict = Parse(await service.ReviewPlanAsync(_repo, Branch, Plan, TestContext.Current.CancellationToken))
                .GetProperty("verdict").GetString()!;
            Parse(await service.ResolveAsync(_repo, Branch, AcceptTheFinding)).TryGetProperty("error", out _)
                .Should().BeFalse("the round's findings are accepted so the next round may begin");
            return verdict;
        }

        (await RoundAsync(underEscalate)).Should().Be("revise");
        (await RoundAsync(underEscalate)).Should().Be("escalated");
        Session().State.EscalationsUsed.Should().Be(1);

        await RoundAsync(ServiceUnder(PanelConfig.Uniform(2, 0)));

        var afterAuto = Session().State;
        afterAuto.Config.OnExhausted.Should().Be(StagePolicy.GoodEnough, "the stored pair follows the settings");
        afterAuto.Config.Autonomous.Should().BeTrue();
        afterAuto.EscalationsUsed.Should().Be(1, "a policy toggle never refills the ladder");

        (await RoundAsync(underEscalate)).Should().Be("escalated");

        var last = Session().State;
        last.Config.OnExhausted.Should().Be(StagePolicy.Escalate);
        last.Config.Autonomous.Should().BeFalse();
        last.EscalationsUsed.Should().Be(2, "escalationsUsed never goes down");
    }

    [Fact]
    public async Task AHoldRaisedUnderHuman_OnAFileFromBeforeAuto_IsSavedWithTheMarker()
    {
        var human = ServiceUnder(PanelConfig.Uniform(1, 0, StagePolicy.Human));
        await human.OpenAsync(_repo, Branch);

        // The file as a build before policy auto left it: the session is not held and has no autonomous member.
        var file = Store.FileFor(_repo, Branch);
        var json = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        json["state"]!["config"]!.AsObject().Remove("autonomous").Should().BeTrue("the fixture strips the marker the real open wrote");
        File.WriteAllText(file, json.ToJsonString());
        Session().State.Config.PredatesAuto.Should().BeTrue("as the real loader reads the fixture");
        Session().State.HumanGate.Should().BeFalse();

        Script(OneBlockingFinding);
        var verdict = Parse(await human.ReviewPlanAsync(_repo, Branch, Plan, TestContext.Current.CancellationToken));

        verdict.GetProperty("verdict").GetString().Should().Be("call_human", verdict.ToString());
        var state = Session().State;
        state.HumanGate.Should().BeTrue();
        state.Config.Autonomous.Should().BeFalse("the begin stamped the session before the round ran, so the hold it raised is this build's");
        ConfigOnDisk()["autonomous"]!.GetValue<bool>().Should().BeFalse();

        // From the release on: the hold this build raised is the person's, whatever the policy is now.
        var notice = state.HoldQuestions.Should().ContainSingle().Subject;
        await ServiceUnder(PanelConfig.Uniform(1, 0)).StatusAsync(_repo, Branch);

        Session().State.HumanGate.Should().BeTrue("policy auto releases only a hold from before it");
        JsonNode.Parse(File.ReadAllText(Path.Combine(_data, "escalations", $"{notice}.json")))!["status"]!.GetValue<string>()
            .Should().BeEmpty("the notice is still open");
    }
}
