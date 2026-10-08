using System.Text;
using CoaiMcp.Core.Consultation;

namespace CoaiMcp.Core.QuestionConsult;

/// <summary>Everything a CLI question row's prompt is built from. Pure data; the fan-out fills it.</summary>
/// <param name="Instruction">The row's base prompt — shipped, overridden, or a person's own.</param>
/// <param name="Capability">What the row may touch, which decides what it is GIVEN (D10).</param>
/// <param name="Question">The caller's question — the sanitised one, for a web row.</param>
/// <param name="Context">The caller's context AFTER <see cref="SecretCheck"/>; <see cref="CheckedContext.Empty"/> on a web row.</param>
/// <param name="Outline">The outline of the repository at HEAD, for a <c>none</c> row; empty otherwise.</param>
/// <param name="Roots">The folders a <c>disk</c> row may read, in the operator's order; empty otherwise.</param>
/// <param name="Nonce">The turn's fence nonce — a parameter, never minted here.</param>
public sealed record QuestionPromptInput(
    string Instruction,
    Capability Capability,
    string Question,
    CheckedContext Context,
    string Outline,
    IReadOnlyList<string> Roots,
    string Nonce)
{
    /// <summary>
    /// What the row's own runtime adds under "## What you have" — an antigravity disk row's <c>coai-lookup</c> block
    /// (todo/PLAN_agy_searches_through_coai.md); empty for every other row, whose prompt is then unchanged.
    /// </summary>
    public string Toolbox { get; init; } = string.Empty;
}

/// <summary>
/// Composes the prompt a CLI question row reads — claude, codex, antigravity, local — by capability:
/// the instruction, what the row has, the material it is given fenced, and the question LAST.
/// </summary>
/// <remarks>
/// <para><b>A web row is given strictly the question</b> (A2), and this is where that promise is a
/// CONTRACT rather than a habit: a web input carrying a context, an outline or a root is refused by
/// name, because the sanitiser decided what the row may say and nothing may be added after it. The
/// <c>api</c> row has its own composer (<see cref="ApiQuestionPrompt"/>), which speaks the schema its
/// shim is bound to.</para>
/// <para>The question comes last for the reason <c>ConsultantPrompt</c> took from <c>chatPrompt.ts</c>:
/// the thing that must survive a long turn goes where recency is.</para>
/// </remarks>
public static class QuestionPrompt
{
    public const string WhatYouHaveHeading = "## What you have";

    public const string OutlineHeading = ApiQuestionPrompt.OutlineHeading;

    public const string ContextHeading = ApiQuestionPrompt.ContextHeading;

    public const string RootsHeading = "## The folders you may read";

    public const string QuestionHeading = ApiQuestionPrompt.QuestionHeading;

    public static string Compose(QuestionPromptInput input)
    {
        MustMatchItsCapability(input);
        var text = new StringBuilder();
        text.AppendLine(input.Instruction.TrimEnd()).AppendLine();
        text.AppendLine(WhatYouHaveHeading);
        text.AppendLine(WhatYouHave(input.Capability));
        if (input.Toolbox.Trim().Length > 0)
        {
            text.AppendLine().AppendLine(input.Toolbox.Trim());
        }

        text.AppendLine();
        AppendRoots(text, input.Roots);
        AppendMaterial(text, OutlineHeading, "the outline", input.Nonce, input.Outline);
        AppendMaterial(text, ContextHeading, "the caller's context", input.Nonce, input.Context.Text);
        text.AppendLine(QuestionHeading);
        text.Append(input.Question.Trim());

        return text.ToString();
    }

    /// <summary>What a web row may carry: the question. Anything else is a leak the sanitiser never saw.</summary>
    private static void MustMatchItsCapability(QuestionPromptInput input)
    {
        if (input.Capability == Capability.Web && (input.Context.Text.Length > 0 || input.Outline.Length > 0 || input.Roots.Count > 0))
        {
            throw new ArgumentException(
                "a web row is given strictly the question (A2) — no context, no outline, no root may be composed into its prompt", nameof(input));
        }

        if (input.Capability != Capability.Disk && input.Roots.Count > 0)
        {
            throw new ArgumentException(
                $"a '{input.Capability.Spelled()}' row reads no folder — roots belong to a disk row's prompt alone", nameof(input));
        }
    }

    private static string WhatYouHave(Capability capability) => capability switch
    {
        Capability.Disk =>
            "You may READ the folders listed below — other projects on this machine — and nothing outside them. "
            + "Study them for something similar to what is asked. Change nothing: you are read-only, and the "
            + "folders are watched. " + Closing,
        Capability.Web =>
            "You may search the web. You are given the question and nothing else — no code, no paths, no "
            + "context from the machine the question came from — so ask the web what the question asks. " + Closing,
        _ =>
            "No checkout and no tools: this prompt is everything you have. Where an OUTLINE of the repository at "
            + "HEAD is below, it is signatures and no bodies. " + Closing,
    };

    private const string Closing =
        "Answer in prose, to the AI that asked — it is blocked on your answer. Advice, never orders.";

    private static void AppendRoots(StringBuilder text, IReadOnlyList<string> roots)
    {
        if (roots.Count == 0)
        {
            return;
        }

        text.AppendLine(RootsHeading);
        foreach (var root in roots)
        {
            text.Append("- ").AppendLine(root);
        }

        text.AppendLine();
    }

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
