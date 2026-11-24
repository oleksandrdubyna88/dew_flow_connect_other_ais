using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Gate;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What to do when the rounds run out: take what is true and move on.
/// </summary>
/// <remarks>
/// <para>The reviewers found real things, they are not worth another round each, and the right move is to
/// READ them, apply the ones that hold, say why the rest were rejected, and go. v2 has no other way for the
/// rounds to run out: no person is called and nothing is proceeded over untouched.</para>
/// </remarks>
public sealed class GoodEnoughPolicyTests
{
    private static SessionState Fresh() =>
        new("s", "D:/r", "main", PanelConfig.Uniform(1, 0));

    private static GateResult TwoOpen() =>
        GateRule.Evaluate([Finding("token compared with =="), Finding("no timeout on the fetch")], [], 0);

    [Fact]
    public void GoodEnough_AdvancesTheStage_LikeAProceedDoes()
    {
        var ok = (Transition.Ok)RoundMachine.CompleteRound(
            Fresh(), TwoOpen(), ReviewerSummary.AllAnswered(2));

        ok.State.AdvanceOnResolve.Should().BeTrue("it is a decision to move on, not to stop");
    }

    [Fact]
    public void ItsVerdict_TellsTheCallerToWorkTheFindingsFirst()
    {
        var ok = (Transition.Ok)RoundMachine.CompleteRound(
            Fresh(), TwoOpen(), ReviewerSummary.AllAnswered(2));

        var verdict = ok.Verdict.Should().BeOfType<RoundVerdict.GoodEnough>().Subject;
        verdict.Gate.GatingCount.Should().Be(2, "the findings travel with it — they are the work");
    }

    [Fact]
    public void WhenNobodyAnswered_ItIsNotReviewed()
    {
        // An empty result is the absence of evidence, not evidence of absence: the stage proceeds WITHOUT a review — said out loud.
        var nobody = new ReviewerSummary(2, 0, ["codex: exit 1", "antigravity: timeout"]);

        var ok = (Transition.Ok)RoundMachine.CompleteRound(
            new SessionState("s", "D:/r", "main", PanelConfig.Uniform(1, 0)), GateResult.Empty, nobody);

        ok.Verdict.Should().BeOfType<RoundVerdict.NotReviewed>();
        ok.State.AdvanceOnResolve.Should().BeTrue();
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

        verdicts.Should().BeEquivalentTo(["Proceed", "Revise", "GoodEnough", "NotReviewed"], "v2 has four outcomes; a fifth is a decision this test is the reason to look at");

        var source = File.ReadAllText(Path.Combine(RepoPaths.Root(), "src_mcp", "src", "Server", "Rounds", "RoundEngine.cs"));
        foreach (var name in verdicts)
        {
            source.Should().Contain($"RoundVerdict.{name}",
                $"{name} has no case in the instruction switch, so a caller would get a verdict with no guidance");
        }
    }
}
