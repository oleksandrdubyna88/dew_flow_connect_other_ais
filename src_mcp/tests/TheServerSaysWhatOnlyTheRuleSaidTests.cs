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
/// <para><b>These cases read the STRING. The behaviour it promises is asserted elsewhere, and that is
/// deliberate</b> — <see cref="HumanGateHoldsTests"/> drives the refusal at both round entries, that
/// <c>resolve</c> does not clear the gate, and that only a person's decision does;
/// <see cref="RoundMachineTests"/> owns the override rule, including the sharp half of it (an
/// exhausted escalate stage has no rounds left either, so "no rounds" is not "a person was asked").
/// Writing those again here would be a second implementation of a guarantee that already has one.</para>
/// <para>The phrasings these pin are SHORT on purpose. The instruction block has a 2 KiB budget
/// (<see cref="TheInstructionsFitWhatAClientKeepsTests"/>), and the first draft of these three
/// paragraphs pushed the text to 2,966 characters — so the end of it, including `consult`, never
/// reached a session at all. The claim is what is asserted here; the wording is as long as the
/// budget allows.</para>
/// <para>A sentence on a shipping surface that the machine does not implement is worse than no
/// sentence, so each claim was read against the machine before it was written down. That caught one:
/// the draft said the override is "refused while rounds remain", which is the rule the code USED to
/// have and corrected — and the shipped <c>resolve</c> tool description still carried it. Both say the
/// real rule now: the override applies only after a <c>call_human</c> verdict.</para>
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
            .And.Contain("does not clear it",
                "the verdict stopping the shipping and the tools refusing are different claims, and only "
                + "the second one is what the round machine does");

    [Fact]
    public void OnlyAPersonClearsTheGate() =>
        Instructions.Should().Contain("humanDecision: \"proceed\"")
            .And.Contain("ONLY after that verdict",
                "an AI that could grant itself the override is an AI the stop does not stop — and the "
                + "rule is what the override would CHANGE, not how many rounds are left");

    /// <summary>
    /// The `resolve` tool description is the OTHER surface that states the override rule, and it is
    /// the one a caller reads first.
    /// </summary>
    /// <remarks>
    /// <para>It carried the stale claim too, which is how this was found: correcting the server
    /// instructions alone would have left a caller reading "refused while rounds remain" in the tool
    /// metadata beside a server saying something else.</para>
    /// <para>Read from the source of <c>Tools.cs</c>, the way <see cref="ScenarioCoverageTests"/>
    /// reads the tool names: the registry needs a live host to enumerate, and this is a check about
    /// TEXT. Both directions are asserted — the corrected rule present AND the old one absent —
    /// because a source-read test that matches only a fragment survives its own break.</para>
    /// </remarks>
    [Fact]
    public void TheResolveToolDescriptionStatesTheSameOverrideRule()
    {
        var source = File.ReadAllText(ToolsSourcePath());

        source.Should().Contain("applies only after that verdict",
            "the tool description is where a caller meets the rule before any round runs");
        source.Should().NotContain("refused while rounds remain",
            "that is the rule the round machine had and corrected: the override is judged by what it "
            + "would CHANGE, not by how many rounds are left");
    }

    private static string ToolsSourcePath()
    {
        var here = new DirectoryInfo(AppContext.BaseDirectory);
        while (here is not null && !File.Exists(Path.Combine(here.FullName, "src_mcp", "src", "Tools.cs")))
        {
            here = here.Parent;
        }

        here.Should().NotBeNull("Tools.cs was not found above the test binary");

        return Path.Combine(here!.FullName, "src_mcp", "src", "Tools.cs");
    }

    [Fact]
    public void TheProtocolIsStillThereForACallerThatHasNoRuleFile() =>
        Instructions.Should().Contain("open").And.Contain("review_plan").And.Contain("review_code")
            .And.Contain("resolve",
                "these instructions are the surface that reaches a session with no conventions mounted");
}
