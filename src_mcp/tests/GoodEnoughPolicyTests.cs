using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Gate;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A fourth thing to do when the rounds run out: take what is true and move on.
/// </summary>
/// <remarks>
/// <para>The three that existed are a person, a bare proceed, and the ladder. The gap between them
/// is the ordinary case: the reviewers found real things, they are not worth another round each,
/// and the right move is to READ them, apply the ones that hold, say why the rest were rejected,
/// and go. <c>Continue, and say so</c> is not that \u2014 it proceeds and leaves every finding
/// untouched, which is how a gate becomes decoration.</para>
/// <para>It advances the stage like `continue` does, and differs entirely in the INSTRUCTION it
/// returns: this verdict tells the caller to work the findings first.</para>
/// </remarks>
public sealed class GoodEnoughPolicyTests
{
    private static SessionState Fresh(StagePolicy policy) =>
        new("s", "D:/r", "main", PanelConfig.Uniform(1, 0, policy));

    private static GateResult TwoOpen() =>
        GateRule.Evaluate([Finding("token compared with =="), Finding("no timeout on the fetch")], [], 0);

    [Fact]
    public void GoodEnough_AdvancesTheStage_LikeAProceedDoes()
    {
        var ok = (Transition.Ok)RoundMachine.CompleteRound(
            Fresh(StagePolicy.GoodEnough), TwoOpen(), ReviewerSummary.AllAnswered(2));

        ok.State.AdvanceOnResolve.Should().BeTrue("it is a decision to move on, not to stop");
        ok.State.HumanGate.Should().BeFalse("nobody is being called");
    }

    [Fact]
    public void ItsVerdict_TellsTheCallerToWorkTheFindingsFirst()
    {
        var ok = (Transition.Ok)RoundMachine.CompleteRound(
            Fresh(StagePolicy.GoodEnough), TwoOpen(), ReviewerSummary.AllAnswered(2));

        var verdict = ok.Verdict.Should().BeOfType<RoundVerdict.GoodEnough>().Subject;
        verdict.Gate.GatingCount.Should().Be(2, "the findings travel with it — they are the work");
    }

    [Fact]
    public void ItIsNotTheSameAsContinue()
    {
        // Continue proceeds and leaves every finding untouched. If the two produced the same verdict
        // the setting would be a label with no behaviour behind it.
        var goodEnough = (Transition.Ok)RoundMachine.CompleteRound(
            Fresh(StagePolicy.GoodEnough), TwoOpen(), ReviewerSummary.AllAnswered(2));
        var carryOn = (Transition.Ok)RoundMachine.CompleteRound(
            Fresh(StagePolicy.Continue), TwoOpen(), ReviewerSummary.AllAnswered(2));

        goodEnough.Verdict.Should().NotBeOfType<RoundVerdict.ContinueAnyway>();
        carryOn.Verdict.Should().BeOfType<RoundVerdict.ContinueAnyway>();
    }

    [Fact]
    public void WhenNobodyAnswered_ItStillCallsAHuman()
    {
        // The one case no policy may pass: an empty result is the absence of evidence, not evidence
        // of absence, and "take what is true" has nothing to take.
        var nobody = new ReviewerSummary(2, 0, ["codex: exit 1", "antigravity: timeout"]);

        var ok = (Transition.Ok)RoundMachine.CompleteRound(Fresh(StagePolicy.GoodEnough), GateResult.Empty, nobody);

        ok.Verdict.Should().BeOfType<RoundVerdict.CallHuman>();
    }

    [Fact]
    public void TheSettingIsReadFromItsOwnName()
    {
        var settings = PanelSettings.FromEnvironment(name => name == "COAI_ON_EXHAUSTED" ? "good_enough" : null);

        settings.Rounds.OnExhausted.Should().Be(StagePolicy.GoodEnough);
    }

    private static Finding Finding(string title) =>
        new(Severity.Major, Category.Reliability, "a.cs", 1, title, "why", "fix", []);
}

/// <summary>
/// Every verdict the machine can produce has an instruction a caller can act on.
/// </summary>
/// <remarks>
/// The switch that maps verdicts to instructions ended in <c>_ =&gt; ("unknown", null, "")</c>: a new
/// verdict fell into it and came back as a name with no guidance behind it, which is the same
/// silence a button wired to nothing produces. Reflection over the closed union is the only check
/// that fails on the commit that ADDS the next verdict.
/// </remarks>
public sealed class VerdictInstructionTests
{
    [Fact]
    public void EveryVerdictInTheUnion_IsHandledByTheAnswer()
    {
        var verdicts = typeof(RoundVerdict).GetNestedTypes()
            .Where(t => t.IsSubclassOf(typeof(RoundVerdict)))
            .Select(t => t.Name)
            .ToList();

        verdicts.Should().HaveCountGreaterThan(4, "the union has grown; this test is the reason to look");

        var source = File.ReadAllText(Path.Combine(RepoRoot(), "src_mcp", "src", "Server", "PanelService.cs"));
        foreach (var name in verdicts)
        {
            source.Should().Contain($"RoundVerdict.{name}",
                $"{name} has no case in the instruction switch, so a caller would get a verdict with no guidance");
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src_mcp")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("the repository root was not found");
    }
}
