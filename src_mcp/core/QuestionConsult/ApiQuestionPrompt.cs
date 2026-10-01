using System.Text;
using CoaiMcp.Core.Consultation;

namespace CoaiMcp.Core.QuestionConsult;

/// <summary>Everything an <c>api</c> question row's first turn is built from. Pure data; the fan-out fills it.</summary>
/// <param name="Instruction">The row's base prompt, from the catalog — "you are the best developer in the world", or a person's own.</param>
/// <param name="Question">The caller's question.</param>
/// <param name="Context">The caller's context, AFTER <see cref="SecretCheck"/> — the type is the guarantee (A9, D10).</param>
/// <param name="Outline">The outline of the repository at HEAD (signatures, no bodies), or empty when none was built.</param>
/// <param name="FollowUps">How many follow-up turns may serve <c>sourceRequests</c>; 0 means none will.</param>
/// <param name="Nonce">The turn's fence nonce — a parameter, never minted here (<see cref="ConsultationFence"/>).</param>
public sealed record ApiQuestionInput(
    string Instruction,
    string Question,
    CheckedContext Context,
    string Outline,
    int FollowUps,
    string Nonce);

/// <summary>
/// Composes the prompt an <c>api</c> question row reads: the instruction, what it has (no checkout, no
/// tools, an outline, source on request), the outline and the context fenced as MATERIAL, and the
/// question LAST — the thing that must survive a long turn goes where recency is (the rule
/// <c>ConsultantPrompt</c> took from <c>chatPrompt.ts</c>).
/// </summary>
/// <remarks>
/// The stated limit of A9 is said to the model rather than hidden from it: source is served from the
/// committed HEAD, so anything uncommitted reaches it only through the context. A model told that asks
/// for what it can be given.
/// </remarks>
public static class ApiQuestionPrompt
{
    public const string OutlineHeading = "## The outline of the repository at HEAD";

    public const string ContextHeading = "## The context the caller gave";

    public const string QuestionHeading = "## The question";

    public static string Compose(ApiQuestionInput input)
    {
        var text = new StringBuilder();
        text.AppendLine(input.Instruction.TrimEnd()).AppendLine();
        text.AppendLine("## What you have");
        text.AppendLine(WhatYouHave(input));
        text.AppendLine();
        AppendMaterial(text, OutlineHeading, "the outline", input.Nonce, input.Outline);
        AppendMaterial(text, ContextHeading, "the caller's context", input.Nonce, input.Context.Text);
        text.AppendLine(QuestionHeading);
        text.Append(input.Question.Trim());

        return text.ToString();
    }

    private static string WhatYouHave(ApiQuestionInput input) =>
        "No checkout and no tools: this prompt is everything you have. "
        + (input.Outline.Length > 0
            ? "Below is an OUTLINE of the repository at HEAD — signatures, no bodies. "
            : string.Empty)
        + Source(input.FollowUps)
        + " Answer as JSON in the schema you were given: `answer` is your advice to the AI that asked, "
        + "and `sourceRequests` is null when you need nothing. The AI asking is blocked on your answer.";

    private static string Source(int followUps) => followUps > 0
        ? $"You may ask for source by name in `sourceRequests` — a file, a symbol the outline names, or a span of lines — "
          + $"and it is served in the next turn, for up to {followUps} follow-up turn{(followUps == 1 ? string.Empty : "s")}. "
          + "Source is served from the committed HEAD; anything uncommitted reaches you only through the context."
        : "`sourceRequests` is ignored on this row: answer from what you have.";

    private static void AppendMaterial(StringBuilder text, string heading, string title, string nonce, string body)
    {
        if (body.Length == 0)
        {
            return;
        }

        text.AppendLine(heading);
        text.AppendLine(ConsultationFence.Material(title, nonce, body));
        text.AppendLine();
    }
}
