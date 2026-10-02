using System.Globalization;
using CoaiMcp.Core.QuestionConsult;

namespace CoaiMcp.Core.Commands;

/// <summary>
/// What the server knows about the question consultant when it hands back orders
/// (<c>todo/PLAN_question_consultant.md</c>, S3): the mode, and the number the order says.
/// </summary>
/// <param name="Mode">The enforcement; <see cref="QuestionMode.Off"/> gives no order.</param>
/// <param name="FreeBatches">What fills <c>{freeBatches}</c> — the settings' number, defaulting to <see cref="QuestionPolicy.FreeBatches"/>.</param>
/// <remarks>Off by default, so a context built without it says what every release before it said.</remarks>
public sealed record QuestionConsultFacts(QuestionMode Mode, int FreeBatches)
{
    /// <summary>No question consultant: no order, whatever else is set.</summary>
    public static QuestionConsultFacts Off { get; } = new(QuestionMode.Off, QuestionPolicy.FreeBatches);
}

/// <summary>
/// The order that amends the autonomy order (§0 item 2 of the plan): before the person is asked, the question
/// consultant is — the phase rule as the server enforces it, the number filled from ONE constant, and the
/// enforcement sentence by mode.
/// </summary>
/// <remarks>
/// The marker stays in code, as every order's does (issue #467): a person may reword the text and the order is
/// still recognised as this one. Never the word "critical" — A8; the guard sweeps are in
/// <c>CadenceOrdersTests</c> and <c>TheOrderTextAgreesWithThePolicyTests</c>.
/// </remarks>
public static class QuestionConsultOrder
{
    /// <summary>Whether this call gives the order: the autonomy order it amends is on, and the mode says something.</summary>
    public static bool Gives(CommandContext context) =>
        context.Autonomous && context.QuestionConsult.Mode != QuestionMode.Off;

    public static string For(QuestionConsultFacts facts, CommandTexts texts) =>
        $"{GateCommands.QuestionConsultMarker} " + texts.Text(CommandTexts.QuestionConsult)
            .Replace("{freeBatches}", facts.FreeBatches.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{enforced}", Enforced(facts.Mode), StringComparison.Ordinal);

    private static string Enforced(QuestionMode mode) => mode == QuestionMode.Require
        ? " The gate ENFORCES this: ask_human refuses a question in that phase until it carries a consultId — and stands down, saying so, when no consultant can be had."
        : " This gate reminds; it does not refuse.";
}
