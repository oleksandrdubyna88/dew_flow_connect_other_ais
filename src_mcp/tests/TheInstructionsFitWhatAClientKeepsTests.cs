using Xunit;
using FluentAssertions;

namespace CoaiMcp.Tests;

/// <summary>
/// The instruction block has a BUDGET, and going past it deletes the end of it silently.
/// </summary>
/// <remarks>
/// <para>Measured 2026-09-15, in a live session against an installed build rather than in this
/// suite: a client truncates the server instructions at 2 KiB and appends its own
/// <c>[truncated]</c> marker. The text had grown to 2,966 characters, so everything past ~2,031 was
/// gone — the enforced-stop paragraph, and <c>consult</c>, which had shipped weeks earlier and was
/// still believed to be arriving.</para>
/// <para><b>Nothing caught it.</b> The string was correct in the binary — a UTF-16 search of the
/// published executable found every sentence — the suite was green, and the surface tests that assert
/// each instruction is PRESENT still passed, because presence in the constant is not arrival at a
/// caller. The only thing that noticed was asking a fresh session what it had been told.</para>
/// <para>So the budget is asserted here, where a change to the text meets it. The limit is deliberately
/// below the observed 2,048: a client is free to spend some of the window on its own framing, and a
/// margin costs a sentence while an overrun costs the tail of the file without saying so.</para>
/// <para>When this fails, do NOT raise the number. Move the detail into the tool description of the
/// tool it is about, where a caller meets it at the moment it matters and where the budget is
/// separate. That is the shape the text now has: the instructions are a map, and each tool carries
/// its own paragraph.</para>
/// </remarks>
public sealed class TheInstructionsFitWhatAClientKeepsTests
{
    /// <summary>What a client was measured to keep, in characters.</summary>
    private const int ObservedLimit = 2048;

    /// <summary>What this server is allowed to send, leaving the client room for its own framing.</summary>
    private const int Budget = 2000;

    [Fact]
    public void TheInstructionsFitInsideTheBudget() =>
        Program.Instructions.Length.Should().BeLessThanOrEqualTo(Budget,
            "a client keeps about {0} characters and drops the rest with a [truncated] marker, so every "
            + "sentence past the budget is one no session ever reads — including ones that shipped long "
            + "before the change that pushed them out", ObservedLimit);

    [Fact]
    public void TheLastParagraphSurvives() =>
        // The half that was actually lost, asserted at its own end rather than by its presence: a
        // sentence 400 characters past the cut is "present" in the constant and absent everywhere else.
        Program.Instructions.TrimEnd().Should().EndWith("the `consult` PROMPT.",
            "the end of the text is what a budget overrun eats first, so the end is what is pinned");

    [Fact]
    public void TheThreeThatOnlyLivedInASharedRuleAreStillHere() =>
        Program.Instructions.Should().Contain("COMMANDS")
            .And.Contain("Reject in round ONE")
            .And.Contain("ENFORCED stop")
            .And.Contain("ONLY after that verdict",
                "these are the reason the shared rule can be reduced; losing them to a budget would "
                + "take them off every surface at once");
}
