namespace CoaiMcp.Core.QuestionConsult;

/// <summary>
/// How one row of a question ended, as a word (PLAN_question_consultant.md §4) — the vocabulary the
/// fan-out writes (S2), the sidebar and the log read (S4). Here, in the core, so the writer and every
/// reader spell it from one list.
/// </summary>
public static class RowOutcomes
{
    /// <summary>The row answered within its deadline.</summary>
    public const string Answered = "answered";

    /// <summary>The row ran past its deadline; the others' answers still return (A1).</summary>
    public const string TimedOut = "timed_out";

    /// <summary>The process ended without an answer — a non-zero exit, nothing parseable, not started.</summary>
    public const string Failed = "failed";

    /// <summary>The row's input was refused before any launch — a web question the sanitiser refused, a context carrying a secret.</summary>
    public const string Refused = "refused";

    /// <summary>The row's runtime cannot do its prompt's capability (A3), or the vendor cannot answer in this build; never launched.</summary>
    public const string Blocked = "blocked";

    /// <summary>The row is switched off.</summary>
    public const string Disabled = "disabled";

    /// <summary>The six, in the order the plan lists them.</summary>
    public static IReadOnlyList<string> All { get; } = [Answered, TimedOut, Failed, Refused, Blocked, Disabled];

    public static bool IsKnown(string word) => All.Contains(word, StringComparer.Ordinal);
}
