using CoaiMcp.Core.Authors;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// <see cref="AttributionInput.Validate"/> names every way an input contradicts the contract — one fact per violation — so that
/// <c>Assign</c> can refuse it and E5 can record <c>unobserved: invalid input</c>, and so that no scenario passes on a fixture the
/// code quietly declines (testing.md, A fixture the code REJECTS proves nothing).
/// </summary>
/// <remarks>
/// The three positives — <c>AWellFormedScenario_HasNoViolations</c>, <c>AnAgentWithNoParentOrAnUndeclaredOne_IsNoViolation</c> and
/// <c>AMetaWithoutATranscriptAndNoCallInstant_IsNoViolation</c> — cannot fail against a <c>Validate()</c> that reports nothing; they are proven by
/// the negative facts beside them.
/// </remarks>
public sealed class TheAttributionInputSaysWhatIsWrongWithItTests
{
    private static AttributionScenario Well() => AttributionScenario.For()
        .Gate("A", "D:/x").Opened(0).Code(1, 60, 70)
        .Main(10)
        .Agent("a1", launchedAt: 8).Message("a1", 12);

    private static AttributionInput Well(Func<AttributionScenario, AttributionScenario> change) => change(Well()).BuildUnchecked("A", "CodeReview", 1);

    private static AttributionInput With(Func<AttributionInput, AttributionInput> change) => change(Well().BuildUnchecked("A", "CodeReview", 1));

    private static void Names(AttributionInput input, string fragment) =>
        input.Validate().Should().ContainSingle(violation => violation.Contains(fragment, StringComparison.Ordinal), "the violation says what is wrong");

    [Fact]
    public void AWellFormedScenario_HasNoViolations() => Well().BuildUnchecked("A", "CodeReview", 1).Validate().Should().BeEmpty();

    [Fact]
    public void AnAgentWithNoParentOrAnUndeclaredOne_IsNoViolation()
    {
        var input = Well(s => s.Agent("orphan", parent: string.Empty, launchedAt: 20).Agent("stray", parent: "nobody", launchedAt: 21));

        input.Validate().Should().BeEmpty("it has no parent, so it propagates nothing");
    }

    [Fact]
    public void AMetaWithoutATranscriptAndNoCallInstant_IsNoViolation()
    {
        var input = With(i => i with { Caller = i.Caller with { Agents = [.. i.Caller.Agents, new ObservedAgent(new AgentKey("gone"), AgentKey.Main, "general-purpose", string.Empty, null, false)] } });

        input.Validate().Should().BeEmpty("it is inert: nothing reads its first line");
    }

    [Fact]
    public void AMessageOfAnUndeclaredAgent_IsAViolation() => Names(Well(s => s.Message("ghost", 11)), "ghost");

    [Fact]
    public void AWriteByAnUndeclaredAgent_IsAViolation() => Names(Well(s => s.Write("ghost", 11, "D:/x/a.cs")), "ghost");

    [Fact]
    public void RoundsOutOfJournalOrder_IsAViolation() =>
        Names(Well(s => s.Round("CodeReview", 2, 50, 55)), "journal order");

    [Fact]
    public void ARoundNotInTheJournal_IsAViolation() => Names(Well().BuildUnchecked("A", "CodeReview", 7), "not in the gate's journal");

    [Fact]
    public void ACallerOtherThanTheRoundsCaller_IsAViolation() =>
        Names(With(i => i with { Caller = i.Caller with { CallerSessionId = "c9" } }), "caller");

    [Fact]
    public void ADuplicateMessageId_IsAViolation() =>
        Names(With(i => i with { Caller = i.Caller with { Messages = [.. i.Caller.Messages, i.Caller.Messages[0] with { At = AttributionScenario.At(30) }] } }), "m-0001");

    /// <summary>E1's <c>c13</c>: round 1 listed twice. Tiled, the copy would open an empty window and the round would be looked up twice.</summary>
    [Fact]
    public void ARoundListedTwice_IsAViolation() => Names(Well(s => s.Code(1, 60, 70)), "listed twice");

    /// <summary>Both ends of the loop are named: each is an agent whose chain never ends.</summary>
    [Fact]
    public void ALoopingParentChain_IsAViolation() =>
        Well(s => s.Agent("p", parent: "q", launchedAt: 1).Agent("q", parent: "p", launchedAt: 2)).Validate()
            .Should().HaveCount(2).And.OnlyContain(violation => violation.Contains("parent chain", StringComparison.Ordinal));

    [Fact]
    public void AnAgentThatIsItsOwnParent_IsAViolation() => Names(Well(s => s.Agent("self", parent: "self", launchedAt: 3)), "parent chain");

    [Fact]
    public void APresentAgentWithoutAFirstLine_IsAViolation() =>
        Names(With(i => i with { Caller = i.Caller with { Agents = [.. i.Caller.Agents, new ObservedAgent(new AgentKey("blank"), AgentKey.Main, "general-purpose", string.Empty, null, true)] } }), "first line");

    [Fact]
    public void AResultBeforeItsCall_IsAViolation() =>
        Names(Well(s => s.Write("a1", 12, "D:/x/a.cs", answeredAt: 11)), "answered before");

    [Fact]
    public void AnAgentDeclaredTwice_IsAViolation() => Names(Well(s => s.Agent("a1", launchedAt: 9)), "declared twice");

    [Fact]
    public void AttributingARunningRound_IsAViolation() =>
        Names(AttributionScenario.For().Gate("A", "D:/x").Opened(0).Round("CodeReview", 1, 60, 65, status: "running").BuildUnchecked("A", "CodeReview", 1), "settled");

    [Theory]
    [InlineData("CodeReview", "done", "skipped")]
    [InlineData("CodeReview", "interrupted", "proceed")]
    [InlineData("FeatureReview", "done", "proceed")]
    [InlineData("DocumentReview", "done", "proceed")]
    public void AttributingASkippedOrInterruptedRound_IsAViolation(string stage, string status, string verdict)
    {
        var input = AttributionScenario.For().Gate("A", "D:/x").Opened(0).Round(stage, 1, 60, 70, status: status, verdict: verdict).BuildUnchecked("A", stage, 1);

        Names(input, "settled");
    }

    [Fact]
    public void ASecondGatesRoundListedTwice_IsAViolationToo() =>
        Names(Well(s => s.Gate("B", "D:/y").Opened(0).Code(1, 20, 30).Code(1, 20, 30)), "listed twice");

    [Fact]
    public void TheViolationsAreInAFixedOrder()
    {
        var input = Well(s => s.Message("ghost", 11).Write("ghost", 11, "D:/x/a.cs"));

        input.Validate().Should().Equal(input.Validate(), "equal inputs name the same violations in the same order");
        input.Validate().Should().HaveCount(2);
    }
}
