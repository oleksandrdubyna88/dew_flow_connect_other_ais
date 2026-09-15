using Xunit;
using FluentAssertions;

namespace CoaiMcp.Tests;

/// <summary>
/// Three things a session must know reached it only through a shared rule file, and now reach it from
/// the server itself.
/// </summary>
/// <remarks>
/// <para>The family's conventions repository is removing product detail from its shared rules: a rule
/// that carries one product's protocol is a rule every other repository must edit when that product
/// changes. The obligation — run an independent review in addition to your own — stays shared and
/// vendor-neutral; the protocol goes to the product that serves it.</para>
/// <para>That move has a precondition, and this is it. The gate's own instructions carried the
/// protocol but not these three, so removing the shared copy first would have taken them off every
/// surface a session can see:</para>
/// <list type="number">
/// <item>that a round's reply can carry COMMANDS which outrank the caller's defaults — the
/// highest-consequence of the three, because autonomy, story splitting and model routing all ride on
/// it;</item>
/// <item>reject in round ONE, which is the argument for why the loop converges at all;</item>
/// <item>that <c>call_human</c> is an ENFORCED stop — <c>review_plan</c> and <c>review_code</c> refuse
/// while it stands, and recording decisions does not clear it.</item>
/// </list>
/// <para>Each assertion below is checked against the machine rather than against the prose: the
/// refusal is <see cref="CoaiMcp.Core.Rounds.RoundMachine"/>'s <c>HumanGate</c> guard at all three
/// round entries, and the gate survives <c>resolve</c> unless a person passes
/// <c>humanDecision: "proceed"</c>. A sentence in the instructions that the server does not implement
/// would be worse than no sentence at all.</para>
/// </remarks>
public sealed class TheServerSaysWhatOnlyTheRuleSaidTests
{
    private static string Instructions => Program.Instructions;

    [Fact]
    public void ARoundsReplyCanCarryCommandsThatOutrankTheCallersDefaults() =>
        Instructions.Should().Contain("COMMANDS")
            .And.Contain("outrank")
            .And.Contain("empty list",
                "a caller who never sees a commands list must know that empty is the default, not a fault");

    [Fact]
    public void RejectInRoundOneIsOnTheServersOwnSurface() =>
        Instructions.Should().Contain("Reject in round ONE")
            .And.Contain("converging",
                "the instruction without its argument reads as a preference, and is followed as one");

    [Fact]
    public void CallHumanIsAnEnforcedStopAndTheToolsSaySo() =>
        Instructions.Should().Contain("call_human")
            .And.Contain("REFUSE")
            .And.Contain("does not reopen",
                "the verdict stopping the shipping and the tools refusing are different claims, and only "
                + "the second one is what the round machine does");

    [Fact]
    public void OnlyAPersonClearsTheGate() =>
        Instructions.Should().Contain("humanDecision: \"proceed\"")
            .And.Contain("refused while rounds remain",
                "an AI that could grant itself the override is an AI the stop does not stop");

    [Fact]
    public void TheProtocolIsStillThereForACallerThatHasNoRuleFile() =>
        Instructions.Should().Contain("open").And.Contain("review_plan").And.Contain("review_code")
            .And.Contain("resolve",
                "these instructions are the surface that reaches a session with no conventions mounted");
}
