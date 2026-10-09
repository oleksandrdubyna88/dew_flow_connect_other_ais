using CoaiMcp.Core.Consultation;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A consultant reads its catalog row's system prompt — the person's own instruction for that model — as a reviewer does
/// (the cadence consultation for epics 1–3 of research/PLAN_one_model_catalog.md, finding C2, step C2c).
/// </summary>
/// <remarks>
/// It goes after the product's consultant instruction and before what the consultant is shown, so the rules of the turn
/// — read-only, the budget, the question last — still come after it.
/// </remarks>
public sealed class AConsultantReadsItsRowsInstructionTests
{
    private static ConsultantPromptInput Turn(string rowInstruction) =>
        new("You are a CONSULTANT to another AI that is stuck.", new TurnBudget(5, 0), "ab12cd34", "the parser returns 3", [],
            "feat/x", "0123abc", "diff --git a/A.cs b/A.cs", RowInstruction: rowInstruction);

    [Fact]
    public void TheRowsInstruction_IsReadAfterTheProductsAndBeforeTheTurnsRules()
    {
        var prompt = ConsultantPrompt.Compose(Turn("Answer in plain English, in five lines."));

        var at = prompt.IndexOf("Answer in plain English, in five lines.", StringComparison.Ordinal);
        at.Should().BeGreaterThan(prompt.IndexOf("You are a CONSULTANT", StringComparison.Ordinal));
        at.Should().BeLessThan(prompt.IndexOf("## What you have", StringComparison.Ordinal), "the read-only rule comes after it again");
        prompt.Should().Contain("## What the person asked of this consultant");
        prompt.TrimEnd().Should().EndWith("the parser returns 3", "the question stays last");
    }

    [Fact]
    public void NoInstruction_IsNoSection()
    {
        ConsultantPrompt.Compose(Turn(string.Empty)).Should().NotContain("What the person asked");
    }
}
