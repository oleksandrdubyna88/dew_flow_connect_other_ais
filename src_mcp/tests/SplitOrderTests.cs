using System.Text.Json;
using Xunit;
using FluentAssertions;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using CoaiMcp.Store;
using Serilog.Core;

namespace CoaiMcp.Tests;

/// <summary>
/// The split order through the whole server: given once, on a plan that passed, to a caller that
/// has not had one — and never twice.
/// </summary>
/// <remarks>
/// <para>The unit tests say what <see cref="Core.Commands.GateCommands"/> returns for a context.
/// This says what a real <c>review_plan</c> call actually puts in its answer, which is the thing an
/// AI reads. Both of the operator's questions are here as tests:</para>
/// <list type="number">
/// <item>with the box unticked, nothing goes; with it ticked, the order goes after the plan
/// passes — and NOT after a plan that was told to revise;</item>
/// <item>the epics that split produces come back for their own plan review, on their own branches,
/// and are told they are pieces rather than told to split again. Without that the process has no
/// floor: epics of epics, for ever.</item>
/// </list>
/// </remarks>
[Collection("fakecli-env")]
public sealed class SplitOrderTests : FakeCliRoundTests
{
    private const string ThreeMajors = """
        {"findings": [
          {"severity": "major", "category": "security", "file": "app.cs", "line": 10,
           "title": "token compared with ==", "why": "timing side channel", "fix": "FixedTimeEquals"},
          {"severity": "major", "category": "reliability", "file": "app.cs", "line": 40,
           "title": "no timeout", "why": "a hung peer hangs the request", "fix": "add one"},
          {"severity": "major", "category": "architecture", "file": "app.cs", "line": 70,
           "title": "layers cross", "why": "the parser reaches into the transport", "fix": "invert it"}
        ]}
        """;

    /// <summary>Big and broad on both axes, so the verdict is EPICS and the wording is unambiguous.</summary>
    private static readonly string BigPlan =
        "# PLAN — something large\n\n## Build order\n\n"
        + string.Join("\n", Enumerable.Range(1, 8).Select(i => $"{i}. step {i}"))
        + "\n\n"
        + string.Join("\n", Enumerable.Range(0, 16).Select(i => $"- `src_mcp/a{i}.cs` and `src_vs_code/b{i}.ts`"))
        + "\n\n- under `.github/` and `research/` and `prompts/`\n"
        + string.Join("\n", Enumerable.Range(0, 400).Select(i => $"prose line {i}"));

    private PanelService Service(bool splitPlan, Core.Commands.GateScope gatePer = Core.Commands.GateScope.Epic) =>
        ServiceFor(Defaults() with { SplitPlan = splitPlan, GatePer = gatePer });

    [Theory]
    [InlineData(Core.Commands.GateScope.Epic, "per EPIC")]
    [InlineData(Core.Commands.GateScope.Task, "for the WHOLE task")]
    public async Task TheGateScopeSetInThePanel_IsTheOneTheOrderGives(Core.Commands.GateScope scope, string words)
    {
        // Issue #131, through a real round: the setting reaches the order, and no order gates a story.
        var order = CommandsOf(await PlanRound(Service(splitPlan: true, scope), "feature", BigPlan))[0];

        order.Should().Contain(Core.Commands.GateCommands.GateOrderMarker + " " + words);
        order.Should().NotContain("After EVERY story");
    }

    [Fact]
    public async Task WhatARoundOrdered_IsWrittenDown_ExactlyAsTheCallerReceivedIt()
    {
        // Issue #131, symptom 3: the orders and the size must be readable after the fact, and what is
        // stored must be what was SENT — never a second rendering of the same decision.
        var answer = await PlanRound(Service(splitPlan: true), "feature", BigPlan);
        var round = RoundsQuery.Read(_data).Rounds.Should().ContainSingle().Subject;

        var orders = RoundsQuery.FindingsOf(_data, round.SessionId, round.Stage, round.Number).Orders;

        orders.Should().NotBeNull();
        orders!.Commands.Should().Equal(CommandsOf(answer));
        orders.PlanShape.Should().StartWith(Core.Commands.PlanShapeReader.Of(BigPlan).Verdict.ToString())
            .And.Contain("build step(s)");
    }

    [Fact]
    public async Task WithTheBoxUnticked_NoCommandArrivesAtAll()
    {
        var answer = await PlanRound(Service(splitPlan: false), "feature", BigPlan);

        answer.GetProperty("verdict").GetString().Should().Be("proceed");
        CommandsOf(answer).Should().BeEmpty("a switch nobody set changes nothing");
        var hasPreamble = answer.TryGetProperty("commandsPreamble", out var preamble)
            && preamble.ValueKind != JsonValueKind.Null;
        hasPreamble.Should().BeFalse("an introduction to nothing is still something the AI has to read");
    }

    [Fact]
    public async Task WithTheBoxTicked_TheOrderArrivesOnThePlanThatPassed()
    {
        var answer = await PlanRound(Service(splitPlan: true), "feature", BigPlan);

        var commands = CommandsOf(answer);
        commands.Should().ContainSingle();
        commands[0].Should().Contain("EPICS").And.Contain("review_code").And.Contain("commit");
        answer.GetProperty("commandsPreamble").GetString().Should().Contain("outrank");
    }

    [Fact]
    public async Task APlanTheGateSentBack_IsNotToldToStartBuildingIt()
    {
        // Three majors against a threshold of two: `revise`. The order to split and commit follows
        // permission to build, and this plan has not got it.
        var service = Service(splitPlan: true);
        await service.OpenAsync(_repo, "feature");
        Script(ThreeMajors);
        var answer = Parse(await service.ReviewPlanAsync(_repo, "feature", BigPlan));

        answer.GetProperty("verdict").GetString().Should().Be("revise");
        CommandsOf(answer).Should().BeEmpty();
    }

    [Fact]
    public async Task AnEpicOnItsOwnBranch_IsToldItIsAPiece_NotToSplitAgain()
    {
        // The operator's second question, end to end. The epic is a NEW session — our own plan
        // stage happens once per session, so the epic cannot come back on the same one — and that
        // is precisely why the memory is keyed by the CALLER and not by the session.
        var service = Service(splitPlan: true);
        CommandsOf(await PlanRound(service, "feature", BigPlan))[0].Should().Contain("EPICS");

        var epic = await PlanRound(service, "epic-1", BigPlan);

        var commands = CommandsOf(epic);
        commands.Should().ContainSingle();
        commands[0].Should().Contain("do NOT split it again");
        commands[0].Should().NotContain("EPICS", "the loop has to have a floor");
    }

    [Fact]
    public async Task ASecondServerProcess_RemembersTheFirstOnesOrder()
    {
        // The client respawns the server between calls in real use, so a memory in a field would
        // forget exactly when the epics start arriving.
        CommandsOf(await PlanRound(Service(splitPlan: true), "feature", BigPlan))[0].Should().Contain("EPICS");

        var epic = await PlanRound(Service(splitPlan: true), "epic-1", BigPlan);

        CommandsOf(epic)[0].Should().Contain("do NOT split it again");
    }

    [Fact]
    public async Task AClientThatNamesNoSession_IsStillFollowedOntoTheEpicBranch()
    {
        // The fallback used to be OUR session id, which is repo+branch — so every epic, arriving on
        // its own branch, looked like a brand new caller and was ordered to split again. The loop
        // survived exactly where the guard was supposed to be. Raised as Blocking by gemini in this
        // change's plan round; the fallback is the CHECKOUT, which is what the epics share.
        var claude = Environment.GetEnvironmentVariable("CLAUDE_CODE_SESSION_ID");
        Environment.SetEnvironmentVariable("COAI_CALLER_SESSION", null);
        Environment.SetEnvironmentVariable("CLAUDE_CODE_SESSION_ID", null);
        try
        {
            var service = Service(splitPlan: true);
            CommandsOf(await PlanRound(service, "feature", BigPlan))[0].Should().Contain("EPICS");

            var epic = await PlanRound(service, "epic-1", BigPlan);

            CommandsOf(epic)[0].Should().Contain("do NOT split it again");
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAUDE_CODE_SESSION_ID", claude);
            Environment.SetEnvironmentVariable("COAI_CALLER_SESSION", _caller);
        }
    }

    [Fact]
    public async Task ACodexCaller_IsToldTheModelsConfiguredForCodex_EndToEnd()
    {
        // Issue #117, through the real round: the caller KIND comes from the vendor's own session
        // variable, and the pair comes from the setting — neither is a parameter a test can hand
        // the command directly. `COAI_CALLER_SESSION` stays set (it is identity, never a kind).
        var claude = Environment.GetEnvironmentVariable("CLAUDE_CODE_SESSION_ID");
        var codex = Environment.GetEnvironmentVariable("CODEX_SESSION_ID");
        Environment.SetEnvironmentVariable("CLAUDE_CODE_SESSION_ID", null);
        Environment.SetEnvironmentVariable("CODEX_SESSION_ID", "codex-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var service = new PanelService(
                new PanelSettings
                {
                    Providers = [new("codex") { ExecutablePath = FakeCliExe }],
                    Rounds = PanelConfig.Uniform(3, 2),
                    DataDir = _data,
                    ReviewerTimeout = TimeSpan.FromSeconds(30),
                    RateLimitBackoff = TimeSpan.FromMilliseconds(5),
                    SplitPlan = true,
                    SplitWithFable = true,
                    CommandModels = new Dictionary<string, Core.Commands.ModelPair>
                    {
                        ["codex"] = new("gpt-6-astra", "gpt-6-luna"),
                    },
                },
                VaultKeys.None("no vault in tests"),
                default,
                _launcher,
                Logger.None, Noticing.None);

            var commands = CommandsOf(await PlanRound(service, "feature", BigPlan));

            commands.Should().HaveCount(2);
            commands[1].Should().Contain("gpt-6-astra").And.Contain("gpt-6-luna");
            commands[1].Should().NotContain("Fable").And.NotContain("Opus");
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAUDE_CODE_SESSION_ID", claude);
            Environment.SetEnvironmentVariable("CODEX_SESSION_ID", codex);
        }
    }

    [Fact]
    public async Task ADifferentClaude_IsOwedItsOwnSplitOrder()
    {
        // Two people working in one repository at once is the ordinary case, and the second one's
        // plan is not a piece of the first one's split.
        CommandsOf(await PlanRound(Service(splitPlan: true), "feature", BigPlan))[0].Should().Contain("EPICS");

        Environment.SetEnvironmentVariable("COAI_CALLER_SESSION", _caller + "-other");
        var theirs = await PlanRound(Service(splitPlan: true), "epic-1", BigPlan);

        CommandsOf(theirs)[0].Should().Contain("EPICS");
    }
}
