using CoaiMcp.Core.Notices;

namespace CoaiMcp.Core.QuestionConsult;

/// <summary>
/// A context that has been through <see cref="SecretCheck"/> and carries no secret shape — the only
/// kind of context the api question prompt accepts (PLAN_question_consultant.md, A9, D10).
/// </summary>
/// <remarks>
/// Constructible by the check alone, so "after SecretCheck" is a TYPE rather than a call order
/// somebody has to remember: a composer that takes a <see cref="CheckedContext"/> cannot be handed a
/// text the check refused, because a refusal produces no value to hand it.
/// </remarks>
public sealed record CheckedContext
{
    internal CheckedContext(string text) => Text = text;

    /// <summary>The caller's context, exactly as it arrived — nothing redacted.</summary>
    public string Text { get; }

    /// <summary>No context at all.</summary>
    public static CheckedContext Empty { get; } = new(string.Empty);
}

/// <summary>What the check decided: the context, cleared; or a refusal naming the class and its cure.</summary>
public abstract record SecretCheckResult
{
    public sealed record Clean(CheckedContext Context) : SecretCheckResult;

    /// <param name="Class">The redaction pass that would have fired — <c>bearer</c>, <c>vendor-key</c>, <c>private-key</c>, … — never the value.</param>
    public sealed record Refused(string Class, string Reason, string Cure) : SecretCheckResult;

    private SecretCheckResult() { }
}

/// <summary>
/// The secret half alone: a <c>none</c> row on a hosted runtime may carry code, paths and config (A9 —
/// not the web row's code ban), and may carry no secret shape. Refuses, never redacts.
/// </summary>
/// <remarks>
/// Recognition is the notice redaction's own passes (<see cref="Redaction.FirstSecretClass"/>), so the
/// shapes this refuses are exactly the shapes <c>server-notices.jsonl</c> redacts — one list, kept in
/// step with the extension by the parity harness. The refusal never quotes what it found: a refusal is
/// written into the question's record, and a secret in a reason is a secret on disk.
/// </remarks>
public static class SecretCheck
{
    /// <summary>The cure every refusal carries — the consultant rule's own words.</summary>
    public const string Cure =
        "remove it, or replace it with a placeholder and say you did: the context leaves this machine for a hosted "
        + "model, and a consultation leaves a thread in the vendor's own store that nobody here can delete";

    /// <summary>
    /// The class of the first secret shape in <paramref name="text"/>, or empty — <see cref="Redaction.FirstSecretClass"/>
    /// over the text as it arrived and then over the text as a model reads it (<see cref="TextAsRead.Normalised"/>).
    /// </summary>
    /// <remarks>
    /// BOTH readings, never only the folded one (S4b item 4): folding can also join what the raw text kept apart — a
    /// ligature before <c>sk-</c> becomes ASCII letters, and the boundary the vendor pattern needs is gone — so a shape
    /// either reading reveals is a secret.
    /// </remarks>
    public static string WhichSecret(string text) =>
        Redaction.FirstSecretClass(text) is { Length: > 0 } raw ? raw : Redaction.FirstSecretClass(TextAsRead.Normalised(text));

    /// <summary>The context cleared for a hosted row, or refused naming the class of what it carries.</summary>
    public static SecretCheckResult Inspect(string text) => Inspect(text, "context");

    /// <summary>
    /// <paramref name="text"/> cleared for a hosted row, or refused naming the class of what it carries — and
    /// <paramref name="what"/> it was (<c>context</c>, <c>question</c>), so the refusal says which text to fix.
    /// </summary>
    /// <remarks>
    /// The QUESTION travels to a none, disk or api row as well as the context, so both are checked (S4b item 1): a web
    /// row's question already passed this check inside <see cref="WebQuestionSanitiser"/>; the others were handed it raw.
    /// </remarks>
    public static SecretCheckResult Inspect(string text, string what)
    {
        var found = WhichSecret(text);

        return found.Length == 0
            ? new SecretCheckResult.Clean(new CheckedContext(text))
            : new SecretCheckResult.Refused(found, Reason(found, what), Cure);
    }

    private static string Reason(string found, string what) => found == Redaction.Unredactable
        ? $"the {what} could not be checked for secrets in time, and a text nobody can vouch for does not leave this machine"
        : $"the {what} carries a secret shape ({found}) — the same shape the notice redaction would take out";
}
