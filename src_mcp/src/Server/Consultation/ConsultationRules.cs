using CoaiMcp.Core.Consultation;

namespace CoaiMcp.Server;

/// <summary>
/// Whether a follow-up may run on a record, and the sentence when it may not. Pure: every refusal is
/// a table test, and every one names its cure.
/// </summary>
internal static class ConsultationRules
{
    /// <summary>
    /// Three questions, in order: whose it is, what STATE it is in, and whether the turn may run.
    /// </summary>
    /// <remarks>
    /// Split because they are three, not because of a count: ownership is about the caller, the
    /// status refusals are about the record as the server last left it, and the rest is about this
    /// turn. Each half is now a table nobody has to read past to find the one that applies.
    /// (CodeRabbit, on the pull request — the ceiling is this repository's own rule.)
    /// </remarks>
    public static string? Refusal(ConsultationRecord record, string caller, DateTime nowUtc, TimeSpan idle, string problem) =>
        record.Caller != caller
            ? $"consultation {record.Id} belongs to another caller session — start a new consultation of your own"
            : ByStatus(record) ?? Later(record, nowUtc, idle, problem);

    /// <summary>The refusals a record's own STATUS decides, before anything about this turn.</summary>
    private static string? ByStatus(ConsultationRecord record) =>
        record.Status == ConsultationStatuses.Failed
            ? $"consultation {record.Id} failed ({record.Reason}) — start a new consultation with a fresh problem statement"
        : record.Status == ConsultationStatuses.Closed
            ? $"consultation {record.Id} is closed ({record.Reason}) — a fresh problem statement opens a new one"
        : record.Status == ConsultationStatuses.Asking
            ? $"consultation {record.Id} is still running a turn — wait for its answer before asking again"
        : null;

    private static string? Later(ConsultationRecord record, DateTime nowUtc, TimeSpan idle, string problem) =>
        ConsultationStore.IdleFor(record, nowUtc) > idle
            ? $"consultation {record.Id} sat idle for more than {idle.TotalMinutes:0} minutes and is closed; its vendor conversation was dropped — start a new consultation"
        : record.Budget.Exhausted
            ? $"consultation {record.Id} has used all {record.MaxTurns} of its turns and is closed — a fresh problem statement opens a new one (COAI_CONSULT_TURNS sets the cap)"
        : ProblemText.RepeatsTurn([.. record.Turns.Select(t => t.Problem)], problem) is { } turn
            ? $"the problem text repeats turn {turn} of consultation {record.Id} — a follow-up reports what you VERIFIED since the last advice (the command you ran, the error you got, the line it named), it does not re-ask"
        : null;
}
