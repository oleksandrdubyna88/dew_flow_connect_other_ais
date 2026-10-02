using System.Globalization;

namespace CoaiMcp.Server;

/// <summary>
/// The lifecycle of <c>escalations/*.json</c> (<c>todo/PLAN_question_consultant.md</c> A4, D11): answered and
/// expired questions, orphan answers and stray temp files go seven days after they ended; a question a live
/// session still holds is kept.
/// </summary>
/// <remarks>
/// <para>Before this the directory had no retention at all — every question ever asked stayed. The clock is the
/// moment a question ENDED: the answer's own stamp for an answered pair, the expiry stamp for an expired one, the
/// asking for a question nobody answered and nobody holds; a file that cannot be read is litter by its write time.</para>
/// <para><b>Held is kept, whatever its age or status.</b> A hold is bound to its question by identity
/// (<c>SessionState.HoldQuestions</c>, <c>RequestQuestions</c>): deleting the file would leave the hold with an id
/// nothing can answer. A session that no longer holds the id releases it to the clock.</para>
/// <para>Each file is judged under the turn its readers and writers take, through <see cref="Escalations"/>' own
/// readers — there is one reader of a question file and one of an answer file, and this is not a third.</para>
/// </remarks>
public sealed class EscalationRetention(Escalations escalations, Func<string, bool> isHeld, Action<string>? warn = null)
{
    /// <summary>How long a finished question outlives its end (A4: seven days).</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    private const string Answer = ".answer.json";

    /// <summary>Sweeps the directory once; how many files went.</summary>
    public int Sweep(DateTime nowUtc)
    {
        if (!Directory.Exists(escalations.Directory))
        {
            return 0;
        }

        return Directory.EnumerateFiles(escalations.Directory).ToList().Sum(path => SweepOne(path, nowUtc));
    }

    private int SweepOne(string path, DateTime nowUtc)
    {
        var name = Path.GetFileName(path);
        if (name.EndsWith(".tmp", StringComparison.Ordinal))
        {
            return Older(File.GetLastWriteTimeUtc(path), nowUtc) ? Deleted(path) : 0;
        }

        if (name.EndsWith(Answer, StringComparison.Ordinal))
        {
            return OrphanAnswer(path, name[..^Answer.Length], nowUtc);
        }

        return name.EndsWith(".json", StringComparison.Ordinal) ? Question(path, Path.GetFileNameWithoutExtension(path), nowUtc) : 0;
    }

    /// <summary>An answer whose question is gone: judged alone, by its own stamp. One with a question is judged with it.</summary>
    private int OrphanAnswer(string path, string id, DateTime nowUtc)
    {
        if (File.Exists(escalations.QuestionPath(id)))
        {
            return 0;
        }

        var ended = Parsed(escalations.ReadAnswer(id)?.AnsweredUtc) ?? File.GetLastWriteTimeUtc(path);

        return Older(ended, nowUtc) ? Deleted(path) : 0;
    }

    private int Question(string path, string id, DateTime nowUtc)
    {
        if (escalations.Read(id) is not { } question)
        {
            return Older(File.GetLastWriteTimeUtc(path), nowUtc) ? Deleted(path) : 0;
        }

        if (escalations.ReadAnswer(id) is { } answer)
        {
            var answered = Parsed(answer.AnsweredUtc) ?? File.GetLastWriteTimeUtc(escalations.AnswerPath(id));

            return Older(answered, nowUtc) ? Deleted(path) + Deleted(escalations.AnswerPath(id)) : 0;
        }

        return !isHeld(id) && Older(Ended(question, path), nowUtc) ? Deleted(path) : 0;
    }

    /// <summary>When an unanswered question ended: its expiry, or — never expired — when it was asked.</summary>
    private static DateTime Ended(EscalationQuestion question, string path) =>
        (question.Status == EscalationStatuses.Expired ? Parsed(question.ExpiredUtc) : Parsed(question.AskedUtc))
        ?? File.GetLastWriteTimeUtc(path);

    private static bool Older(DateTime ended, DateTime nowUtc) => nowUtc - ended > Retention;

    private static DateTime? Parsed(string? stamp) =>
        DateTime.TryParse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed.ToUniversalTime()
            : null;

    private int Deleted(string path)
    {
        try
        {
            File.Delete(path);

            return 1;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Retried on the next sweep — and SAID, because a retention that silently never runs is a directory
            // that grows for ever with nothing anywhere reporting it.
            warn?.Invoke($"{Path.GetFileName(path)} is past retention and could not be removed: {e.Message}");

            return 0;
        }
    }
}
