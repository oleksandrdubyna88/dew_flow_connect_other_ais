using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CoaiMcp.Server;

/// <summary>
/// One JSON file per consultation in the data directory both halves share — the escalation channel's
/// shape: written whole under the turn, read without forbidding a writer, a torn file is nothing.
/// </summary>
public sealed partial class ConsultationStore(
    string dataDir,
    Action<string>? warn = null,
    Action<ConsultationRecord>? projected = null)
{
    /// <summary>How long a finished consultation is kept for the log and the card before its file goes.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex WellFormedId();

    public string Directory => Path.Combine(dataDir, "consultations");

    /// <summary>A fresh id: a GUID's 32 hex digits, which is also a safe file name.</summary>
    public static string NewId() => Guid.NewGuid().ToString("N");

    /// <summary>An id a CALLER handed us is a file name only when it is shaped like one of ours.</summary>
    /// <remarks>
    /// It takes <c>string?</c> because one of its callers reads the id off a DESERIALISED record,
    /// where a JSON file with no <c>id</c> yields null however non-nullable the property is — the
    /// state the remark on <see cref="All"/> records having met in a live run. `Regex.IsMatch(null)`
    /// throws, `All` is what `Sweep` enumerates, and so one malformed file in the directory stopped
    /// the sweep for every record behind it. (CodeRabbit, on the pull request.)
    /// </remarks>
    public static bool IsWellFormedId(string? id) => id is not null && WellFormedId().IsMatch(id);

    public string PathFor(string id) => Path.Combine(Directory, $"{id}.json");

    /// <summary>
    /// The record on disk, and the projection after it.
    /// </summary>
    /// <remarks>
    /// <para>ONE place, which is why the projection hangs here rather than in the service: every
    /// state a consultation reaches is written through this method — the turns, and the sweep that
    /// flips a dead <c>asking</c> record or closes an idle one. A projection wired in the service
    /// would have recorded the turns and silently missed both sweeps, so the log would show
    /// consultations that never ended.</para>
    /// <para>The file FIRST and the projection second, never the other way round: the file is the
    /// source of truth, and the database is a view of it that is allowed to be behind.</para>
    /// </remarks>
    public void Write(ConsultationRecord record)
    {
        System.IO.Directory.CreateDirectory(Directory);
        AtomicJson.Write(PathFor(record.Id), JsonSerializer.Serialize(record, ConsultationJsonContext.Default.ConsultationRecord));
        projected?.Invoke(record);
    }

    /// <summary>The record, or null for an id nobody wrote, a torn file, or one being replaced right now.</summary>
    public ConsultationRecord? Read(string id) =>
        IsWellFormedId(id) ? ReadFile(PathFor(id)) : null;

    /// <summary>
    /// Every consultation in the directory — and nothing else that happens to be JSON in it.
    /// </summary>
    /// <remarks>
    /// <b>The NAME is the guard.</b> A consultation file is named by a consultation id; anything else
    /// in this directory is not one, whatever it deserialises to. Found by story 2's live check: the
    /// local consultant's answer schema was written here, came back as a record with a null id and no
    /// status, and the sweep would then have written and deleted files named after nothing. The schema
    /// has moved out of this directory as well — both, because a shared data directory acquires files
    /// nobody planned for, and a reader that trusts an extension will meet the next one too.
    /// </remarks>
    public IReadOnlyList<ConsultationRecord> All()
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return [];
        }

        return [.. System.IO.Directory.EnumerateFiles(Directory, "*.json")
            .Where(path => IsWellFormedId(Path.GetFileNameWithoutExtension(path)))
            .Select(ReadFile)
            .OfType<ConsultationRecord>()
            .Where(record => IsWellFormedId(record.Id))];
    }

    /// <summary>
    /// What a restarted server owes the records a previous one left: an <c>asking</c> record whose pid
    /// is gone becomes <c>interrupted</c> when it holds a handle and <c>failed</c> otherwise; an open
    /// record idle past its budget is closed and its handle dropped; a finished one past retention is
    /// deleted. Returns how many records changed or went.
    /// </summary>
    /// <remarks>
    /// The pid check is what keeps a SECOND server sharing this directory from killing the first
    /// one's live consultation — the same rule <c>SessionStore.SweepOrphanedRounds</c> follows.
    /// </remarks>
    public int Sweep(Func<int, bool> isAlive, DateTime nowUtc, TimeSpan idle, TimeSpan retention)
    {
        var changed = 0;
        foreach (var record in All())
        {
            changed += SweepOne(record, isAlive, nowUtc, idle, retention) ? 1 : 0;
        }

        return changed + SweepAnswers(nowUtc, retention);
    }

    /// <summary>
    /// The prompt and answer files a launch leaves behind, once they are older than any record.
    /// </summary>
    /// <remarks>
    /// <para>Every turn writes at least one: codex its <c>-o</c> answer, the local shim a
    /// <c>.prompt</c> and a <c>.json</c>. They hold the working-tree diff and the caller's problem —
    /// somebody's source code — and nothing was deleting them. Named as a growth surface here rather
    /// than discovered as a full disk: one turn is kilobytes, a busy week is a few hundred of them,
    /// and they go on the same clock as the record they belong to. (gemini, this story's plan round.)</para>
    /// <para>Deleted when it is OURS and old. A file cannot say which consultation it came from, so
    /// the clock stands in for that — one older than the retention window belongs to a record that is
    /// itself gone, and a file a running turn is still writing is younger than the window by
    /// definition. But age is not ownership: <see cref="OurOwn"/> is what keeps a note somebody left
    /// in this directory out of it. (codex, code round.)</para>
    /// </remarks>
    private int SweepAnswers(DateTime nowUtc, TimeSpan retention)
    {
        var answers = Path.Combine(Directory, "answers");
        if (!System.IO.Directory.Exists(answers))
        {
            return 0;
        }

        var removed = 0;
        foreach (var path in System.IO.Directory.EnumerateFiles(answers))
        {
            try
            {
                // OURS by name, as well as old. A person or another component putting a file under
                // this directory would otherwise have it deleted on the retention clock, and a sweep
                // that removes what it did not write is a sweep nobody can trust with a directory.
                // (codex, code round.)
                if (OurOwn(Path.GetFileName(path)) && nowUtc - File.GetLastWriteTimeUtc(path) > retention)
                {
                    File.Delete(path);
                    removed++;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                warn?.Invoke($"a consultation answer file at {path} is past retention and could not be removed: {e.Message}");
            }
        }

        return removed;
    }

    /// <summary>
    /// A file this product wrote into the answers directory: a consultant's answer, or the prompt the
    /// local shim was handed.
    /// </summary>
    /// <remarks>
    /// The rule lives with the adapters that NAME these files, not here: the writer and the deleter
    /// are in different projects, and an ad-hoc check in the sweep would leak files the day an
    /// adapter renamed its output. A sweep that removes what it did not write is a sweep nobody can
    /// trust with a directory.
    /// </remarks>
    private static bool OurOwn(string name) => Runners.Consultation.ConsultantArtefacts.Ours(name);

    /// <summary>
    /// One record's sweep: expired, orphaned by a dead process, or idle past its budget — decided while
    /// HOLDING the repository's lock, on the record as it is then.
    /// </summary>
    /// <remarks>
    /// <para>The lock is what every writer of a consultation in this repository takes: a follow-up
    /// takes it, re-reads the record and only then marks it <c>asking</c>. So a held lock means somebody
    /// is deciding about this repository right now, and the sweep leaves it to the next beat — the pid
    /// alone was not enough, since a second server in another container reads a live process as gone.
    /// (CodeRabbit and codex, earlier rounds.)</para>
    /// <para>And the decision is made on a FRESH read under that lock, not on the copy the enumeration
    /// handed in. The lock used to be taken and released just to check it was free, and the write
    /// came after, from the copy: a follow-up starting in between had its <c>asking</c> overwritten by a
    /// stale close. A sweep once per start made that window rare; a sweep every minute made it worth
    /// closing (<c>research/PLAN_consult_limits_kinds_and_help.md</c>, the code round).</para>
    /// <para>Expiry is decided the same way (CodeRabbit, the pull request): a lapsed cadence consultation past
    /// retention can be given its verdict at any moment — which makes it the group's evidence — so its
    /// delete, too, is decided on the record as it is under the lock.</para>
    /// </remarks>
    internal bool SweepOne(ConsultationRecord record, Func<int, bool> isAlive, DateTime nowUtc, TimeSpan idle, TimeSpan retention)
    {
        var rule = new SweepRule(isAlive, nowUtc, idle, retention);
        if (StepFor(record, rule).Action == SweepAction.Keep)
        {
            return false; // the cheap look, before any lock: most records are simply in use
        }

        using var held = RepositoryLock.TryTakeAsync(dataDir, record.RepoPath, TimeSpan.Zero).GetAwaiter().GetResult();

        return held is not null && Settled(record.Id, rule);
    }

    /// <summary>What one sweep compares a record against: who is alive, the time, and the two budgets.</summary>
    private readonly record struct SweepRule(Func<int, bool> IsAlive, DateTime NowUtc, TimeSpan Idle, TimeSpan Retention);

    /// <summary>What the sweep does to one record.</summary>
    private enum SweepAction { Keep, Delete, Rewrite }

    /// <summary>The action, and for <see cref="SweepAction.Rewrite"/> the record to write.</summary>
    private readonly record struct SweepStep(SweepAction Action, ConsultationRecord Record)
    {
        public static SweepStep Keep(ConsultationRecord record) => new(SweepAction.Keep, record);
    }

    /// <summary>Under the lock: the record re-read, and its step applied — a record gone since is left alone.</summary>
    private bool Settled(string id, SweepRule rule) =>
        Read(id) is { } now && Applied(StepFor(now, rule));

    private static SweepStep StepFor(ConsultationRecord record, SweepRule rule) =>
        record.IsOver ? ExpiryOf(record, rule) : LiveStepOf(record, rule);

    /// <summary>A finished record: deleted past retention, unless it is cadence evidence, which is kept.</summary>
    private static SweepStep ExpiryOf(ConsultationRecord record, SweepRule rule) =>
        !IsCadenceEvidence(record)
        && rule.NowUtc - Parse(record.EndedUtc.Length > 0 ? record.EndedUtc : record.UpdatedUtc, rule.NowUtc) > rule.Retention
            ? new SweepStep(SweepAction.Delete, record)
            : SweepStep.Keep(record);

    /// <summary>A running record: orphaned when its process is dead, closed when it sat idle past its budget.</summary>
    private static SweepStep LiveStepOf(ConsultationRecord record, SweepRule rule) =>
        record.Status == ConsultationStatuses.Asking
            ? (rule.IsAlive(record.RunnerPid) ? SweepStep.Keep(record) : new SweepStep(SweepAction.Rewrite, Orphaned(record, rule.NowUtc)))
            : (IdleFor(record, rule.NowUtc) > rule.Idle ? new SweepStep(SweepAction.Rewrite, Idled(record, rule.NowUtc, rule.Idle)) : SweepStep.Keep(record));

    private bool Applied(SweepStep step) => step.Action switch
    {
        SweepAction.Delete => Deleted(step.Record),
        SweepAction.Rewrite => Rewritten(step.Record),
        _ => false,
    };

    private bool Rewritten(ConsultationRecord record)
    {
        Write(record);

        return true;
    }

    /// <summary>
    /// A turn whose server died: <c>interrupted</c> when it can be resumed, <c>failed</c> when it cannot.
    /// </summary>
    /// <remarks>
    /// The HANDLE decides, and that is the whole distinction: the turn may have been accepted and
    /// paid for, so a conversation the vendor can still be asked to continue is not a failure — it is
    /// one nobody read the answer to. Extracted with <see cref="Idled"/> so the sweep above reads as
    /// the three states it walks. (CodeRabbit, on the pull request.)
    /// </remarks>
    private static ConsultationRecord Orphaned(ConsultationRecord record, DateTime nowUtc) =>
        record.Handle.Length > 0
            ? record with { Status = ConsultationStatuses.Interrupted, Reason = Died, UpdatedUtc = Stamp(nowUtc) }
            : record with { Status = ConsultationStatuses.Failed, Reason = Died, EndedUtc = Stamp(nowUtc), UpdatedUtc = Stamp(nowUtc) };

    /// <summary>A conversation nobody came back to: closed, and its vendor handle dropped.</summary>
    private static ConsultationRecord Idled(ConsultationRecord record, DateTime nowUtc, TimeSpan idle) =>
        // Through `Lapse`, so this path and the spent-budget one in the service reach the same
        // decision rather than being two chances to leave the outcome empty. (issue #309.)
        ConsultationClosing.Lapse(record) with
        {
            Status = ConsultationStatuses.Closed,
            Reason = $"idle for {idle.TotalMinutes:0} minutes — the vendor's conversation handle was dropped",
            Handle = string.Empty,
            EndedUtc = Stamp(nowUtc),
            UpdatedUtc = Stamp(nowUtc),
        };

    private const string Died = "the server running this turn died before the answer was read";

    /// <summary>Whether a record is what the cadence gate counts: an ordered consultation closed with a verdict.</summary>
    internal static bool IsCadenceEvidence(ConsultationRecord record) =>
        record.Kind != Core.Consultation.ConsultKinds.Stuck
        && record.IsOver
        && ConsultationOutcomes.IsVerdict(record.Outcome);

    /// <remarks>
    /// <b>A cadence or risk consultation closed with a verdict is never reaped</b> (research/PLAN_consult_on_a_cadence.md,
    /// found while answering epic 2's code round): it is the only evidence the cadence gate reads that a group of
    /// epics or a risky piece was consulted on, and a plan's epics run for weeks — email-service's fourteen did —
    /// while retention is seven days. They are few (a group per three epics and at most three risk items per
    /// plan). A lapsed or failed one is no evidence and goes like any other.
    /// </remarks>
    /// <summary>A record past retention, removed — its decision made by <see cref="ExpiryOf"/>.</summary>
    private bool Deleted(ConsultationRecord record)
    {
        try
        {
            File.Delete(PathFor(record.Id));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Retried on the next sweep — but SAID, because a retention that silently never runs is
            // a directory that grows for ever with nothing anywhere reporting it. (codex, code round.)
            warn?.Invoke($"consultation {record.Id} is past retention and could not be removed: {e.Message}");

            return false;
        }

        return true;
    }

    /// <summary>Since the last turn was answered — or since it started, for a record with no stamp at all.</summary>
    public static TimeSpan IdleFor(ConsultationRecord record, DateTime nowUtc) =>
        nowUtc - Parse(record.UpdatedUtc.Length > 0 ? record.UpdatedUtc : record.StartedUtc, nowUtc);

    public static string Stamp(DateTime utc) => utc.ToString("O", CultureInfo.InvariantCulture);

    private static DateTime Parse(string stamp, DateTime fallback) =>
        DateTime.TryParse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed.ToUniversalTime()
            : fallback;

    private static ConsultationRecord? ReadFile(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var turn = SessionTurn.Take(path);
            return JsonSerializer.Deserialize(SharedRead.Text(path), ConsultationJsonContext.Default.ConsultationRecord);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null; // torn, busy, or half-written: not a consultation, and the next read may find it whole
        }
    }
}
