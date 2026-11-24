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
/// <item>that nothing holds the gate — v2 has no <c>call_human</c>: <c>not_reviewed</c> and <c>good_enough</c> advance on
/// <c>resolve</c>, said out loud.</item>
/// </list>
/// <para><b>These cases read the STRING. The behaviour it promises is asserted elsewhere, and that is
/// deliberate</b> — <see cref="RoundMachineTests"/> owns the four outcomes a round can end in.
/// Writing those again here would be a second implementation of a guarantee that already has one.</para>
/// <para>The phrasings these pin are SHORT on purpose. The instruction block has a 2 KiB budget
/// (<see cref="TheInstructionsFitWhatAClientKeepsTests"/>), and the first draft of these three
/// paragraphs pushed the text to 2,966 characters — so the end of it, including `consult`, never
/// reached a session at all. The claim is what is asserted here; the wording is as long as the
/// budget allows.</para>
/// <para>A sentence on a shipping surface that the machine does not implement is worse than no
/// sentence, so each claim is read against the machine before it is written down.</para>
/// </remarks>
public sealed class TheServerSaysWhatOnlyTheRuleSaidTests
{
    private static string Instructions => Program.Instructions;

    [Fact]
    public void TheInstructionsSayNothingOfCommands_ARoundOrdersNothingInV2() =>
        Instructions.Should().NotContain("COMMANDS").And.NotContain("outrank");

    [Fact]
    public void RejectInRoundOneIsOnTheServersOwnSurface() =>
        Instructions.Should().Contain("Reject in round ONE")
            .And.Contain("converging",
                "the instruction without its argument reads as a preference, and is followed as one");

    [Fact]
    public void NothingHoldsTheGate_AndTheInstructionsSay() =>
        Instructions.Should().Contain("Nothing holds the gate")
            .And.Contain("advance on `resolve` like `proceed`")
            .And.NotContain("call_human", "v2 has no verdict that waits for a person")
            .And.NotContain("notice card", "no card is written")
            .And.NotContain("pass humanDecision", "the surface used to tell the AI to relay the person's decision (plan §1)")
            .And.NotContain("humanDecision: \"proceed\"", "no surface offers the argument as a way to proceed");

    /// <summary>
    /// The `resolve` tool description is the OTHER surface that states the gate rule, and it is
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
    public void TheResolveToolDescriptionSaysNothingHoldsTheGate()
    {
        var source = File.ReadAllText(ToolsSourcePath());

        source.Should().Contain("No verdict holds the gate");
        source.Should().NotContain("answer on the notice card");
        source.Should().NotContain("Never pass it on your own judgement", "the description used to tell the AI to relay the person's decision");
        source.Should().NotContain("pass humanDecision");
    }

    /// <summary>The `review_feature` description named a phone as a second surface; this repository has none (plan §2, "The phone").</summary>
    [Fact]
    public void NoToolDescriptionNamesAPhone() =>
        File.ReadAllText(ToolsSourcePath()).Should().NotContain("or phone");

    /// <summary>
    /// A retry asks only the reviewers that failed, so under policy auto a second round that fails again is <c>not_reviewed</c> whenever nobody
    /// ANSWERED in it — one of two failing is the common case — and <c>good_enough</c> otherwise. The description used to say every reviewer
    /// had to fail twice (Opus review of E1.S2, 2026-11-17).
    /// </summary>
    [Fact]
    public void TheReviewFeatureDescriptionSaysAFailedRetryIsNotReviewedWhenNobodyAnsweredInIt()
    {
        var source = string.Join(' ', File.ReadAllText(ToolsSourcePath()).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        source.Should().Contain("ends as `good_enough`,").And.Contain("or `not_reviewed` when nobody answered in it");
        source.Should().NotContain("every reviewer failed twice", "a retry asks only the reviewers that failed, so one of two failing again already leaves nobody answered")
            .And.NotContain("Under `auto` neither blocks");
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
