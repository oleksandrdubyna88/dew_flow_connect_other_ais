using CoaiMcp.Core.Consultation;

namespace CoaiMcp.Server;

/// <summary>
/// Whether a follow-up may run on a record, and the sentence when it may not. Pure: every refusal is
/// a table test, and every one names its cure.
/// </summary>
internal static class ConsultationRules
{
    public static string? Refusal(ConsultationRecord record, string caller, DateTime nowUtc, TimeSpan idle, string problem) =>
        record.Caller != caller
            ? $"consultation {record.Id} belongs to another caller session — start a new consultation of your own"
        : record.Status == ConsultationStatuses.Failed
            ? $"consultation {record.Id} failed ({record.Reason}) — start a new consultation with a fresh problem statement"
        : record.Status == ConsultationStatuses.Closed
            ? $"consultation {record.Id} is closed ({record.Reason}) — a fresh problem statement opens a new one"
        : record.Status == ConsultationStatuses.Asking
            ? $"consultation {record.Id} is still running a turn — wait for its answer before asking again"
        : Later(record, nowUtc, idle, problem);

    private static string? Later(ConsultationRecord record, DateTime nowUtc, TimeSpan idle, string problem) =>
        ConsultationStore.IdleFor(record, nowUtc) > idle
            ? $"consultation {record.Id} sat idle for more than {idle.TotalMinutes:0} minutes and is closed; its vendor conversation was dropped — start a new consultation"
        : record.Budget.Exhausted
            ? $"consultation {record.Id} has used all {record.MaxTurns} of its turns and is closed — a fresh problem statement opens a new one (COAI_CONSULT_TURNS sets the cap)"
        : ProblemText.RepeatsTurn([.. record.Turns.Select(t => t.Problem)], problem) is { } turn
            ? $"the problem text repeats turn {turn} of consultation {record.Id} — a follow-up reports what you VERIFIED since the last advice (the command you ran, the error you got, the line it named), it does not re-ask"
        : null;
}
