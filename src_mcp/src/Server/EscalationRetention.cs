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
/// <para><b>Judged first, held asked second, once per sweep</b> (<c>todo/PLAN_an_idle_server_is_idle_and_starts_at_once.md</c>,
/// D1, 2026-10-06). What the sessions hold costs a read of every session file; asked per file, before the file was even
/// judged, it made each one-minute beat (escalation files × session files) JSON parses — 40 % of a core per idle
/// server. Now a file is judged first, and only a file that is DUE asks — the set read lazily, once per
/// <see cref="Sweep(DateTime)"/>. The outcome is unchanged: a held id still keeps all its files. A set that could not
/// be read whole (<see cref="HeldQuestions.Complete"/>) keeps every due file this beat.</para>
/// <para>Each file is judged under the turn its readers and writers take, through <see cref="Escalations"/>' own
/// readers — there is one reader of a question file and one of an answer file, and this is not a third.</para>
/// </remarks>
public sealed class EscalationRetention(Escalations escalations, Func<HeldQuestions> heldQuestions, Action<string>? warn = null)
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

        return Sweep([.. Directory.EnumerateFiles(escalations.Directory)], nowUtc);
    }

    /// <summary>Sweeps these files in this order — the seam that lets a test hold the order a platform's listing does not promise.</summary>
    internal int Sweep(IReadOnlyList<string> paths, DateTime nowUtc)
    {
        var held = new Lazy<HeldQuestions>(heldQuestions, LazyThreadSafetyMode.None);

        return paths.Sum(path => SweepOne(path, nowUtc, held));
    }

    private int SweepOne(string path, DateTime nowUtc, Lazy<HeldQuestions> held)
    {
        // HELD WINS, whatever the file says (S4b item 7): an answered pair, an orphan answer, a torn question — the hold
        // is bound to the id, and deleting any of its files leaves it unanswerable. Asked only of a file that is DUE.
        var due = DueFiles(path, nowUtc);

        return due.Paths.Count == 0 || (due.Id.Length > 0 && held.Value.MayHold(due.Id)) ? 0 : due.Paths.Sum(Deleted);
    }

    /// <summary>What the end of this file's question would take now, and the id a hold would be bound to (none for a temp file).</summary>
    private sealed record Due(string Id, IReadOnlyList<string> Paths);

    private Due DueFiles(string path, DateTime nowUtc)
    {
        // Gone since the listing — its pair's question took it, or a session answered and cleaned up. Judging it anyway
        // reads a missing file's write time as 1601 and counts a deletion that did not happen.
        if (!File.Exists(path))
        {
            return new(string.Empty, []);
        }

        var name = Path.GetFileName(path);

        return name.EndsWith(".tmp", StringComparison.Ordinal)
            ? new(string.Empty, DueIf(File.GetLastWriteTimeUtc(path), nowUtc, path))
            : Judged(name, path, nowUtc);
    }

    private Due Judged(string name, string path, DateTime nowUtc)
    {
        var id = IdOf(name, path);

        return id.Length == 0 ? new(id, []) : new(id, DueOf(name, path, id, nowUtc));
    }

    private static string IdOf(string name, string path) =>
        name.EndsWith(Answer, StringComparison.Ordinal) ? name[..^Answer.Length]
        : name.EndsWith(".json", StringComparison.Ordinal) ? Path.GetFileNameWithoutExtension(path)
        : string.Empty;

    /// <summary>The files the end of this question would take now — none when it is not yet seven days over.</summary>
    private IReadOnlyList<string> DueOf(string name, string path, string id, DateTime nowUtc) =>
        name.EndsWith(Answer, StringComparison.Ordinal) ? OrphanAnswer(path, id, nowUtc) : Question(path, id, nowUtc);

    /// <summary>An answer whose question is gone: judged alone, by its own stamp. One with a question is judged with it.</summary>
    private IReadOnlyList<string> OrphanAnswer(string path, string id, DateTime nowUtc)
    {
        if (File.Exists(escalations.QuestionPath(id)))
        {
            return [];
        }

        return DueIf(Parsed(escalations.ReadAnswer(id)?.AnsweredUtc) ?? File.GetLastWriteTimeUtc(path), nowUtc, path);
    }

    /// <summary>A question: torn (by its write time), answered (the pair, by the answer's stamp), or open/expired.</summary>
    private IReadOnlyList<string> Question(string path, string id, DateTime nowUtc) =>
        escalations.Read(id) is not { } question ? DueIf(File.GetLastWriteTimeUtc(path), nowUtc, path)
        : escalations.ReadAnswer(id) is { } answer ? AnsweredPair(answer.AnsweredUtc, path, id, nowUtc)
        : DueIf(Ended(question, path), nowUtc, path);

    /// <summary>An answered pair goes together, seven days after the ANSWER's stamp (its file's write time when it has none).</summary>
    private IReadOnlyList<string> AnsweredPair(string? answeredUtc, string path, string id, DateTime nowUtc) =>
        DueIf(Parsed(answeredUtc) ?? File.GetLastWriteTimeUtc(escalations.AnswerPath(id)), nowUtc, path, escalations.AnswerPath(id));

    /// <summary>These paths when what they belong to ended more than seven days ago — none otherwise.</summary>
    private static IReadOnlyList<string> DueIf(DateTime ended, DateTime nowUtc, params string[] paths) =>
        Older(ended, nowUtc) ? paths : [];

    /// <summary>When an unanswered question ended: its expiry, or — never expired — when it was asked.</summary>
    private static DateTime Ended(EscalationQuestion question, string path) =>
        (question.Status == EscalationStatuses.Expired ? Parsed(question.ExpiredUtc) : Parsed(question.AskedUtc))
        ?? File.GetLastWriteTimeUtc(path);

    private static bool Older(DateTime ended, DateTime nowUtc) => nowUtc - ended > Retention;

    private static DateTime? Parsed(string? stamp) => RecordFiles.Parsed(stamp);

    /// <summary>One file gone (1) or refused and said (0) — <see cref="RecordFiles.Delete"/>.</summary>
    private int Deleted(string path) => RecordFiles.Delete(path, warn, Path.GetFileName(path)) ? 1 : 0;
}
