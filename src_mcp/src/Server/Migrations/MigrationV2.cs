using System.Text.Json;
using System.Text.Json.Nodes;
using CoaiMcp.Core.Notices;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Store;

namespace CoaiMcp.Server.Migrations;

/// <summary>
/// The v2 data migration of ONE resolved data folder (E1.S4, <c>v2/epoch-1/e1-gate-simplify/stories/s4-migrate-v2/SPEC.md</c>): the objects a v1 build left
/// that v2 reads differently, each moved to the shape v2 reads, in an order a kill at any boundary can resume from.
/// </summary>
/// <remarks>
/// <para><b>The order, and why.</b> (1) Every held session leaves its hold: <see cref="LegacyHold.Released"/>, the held round's note, written to the SAME
/// file under the session's claim and turn, re-read under the claim first. (2) Every OPEN escalation card is marked <c>withdrawn_v2</c> in place and moved to
/// <c>escalations/archive/</c>; answered and expired cards are history and stay. (3) <c>qphase/</c> and <c>cadence/</c> are archived whole. (4) The
/// settings keys v2 does not read leave <c>settings.json</c> for <c>settings.v1-removed.json</c> under the extension's own <c>settings.lock</c>. (5) The
/// marker <c>migrations/v2.done</c> — only when nothing was deferred and nothing was unreadable. A held session whose card a person had answered is announced first, by
/// a notice in <c>server-notices.jsonl</c> (<see cref="HoldBoundary"/>), and converted only when that notice is on disk. Outside a run, a load of a
/// still-held session converts it too, across the same boundary (<see cref="HoldConversion"/>), and every v2 start sweeps the cards a v1 process raised since (<see cref="SweepCards"/>).</para>
/// <para><b>Idempotence comes from the object, never from the journal.</b> A session is converted while its file still says <c>humanGate: true</c>; a card is
/// withdrawn while it is open at the top level, and only moved when it is already marked; a store is archived while its source is there; a settings key is
/// moved while it is in <c>settings.json</c>. Whatever a kill left, the next run reads the objects and does exactly what is left — the journal is evidence
/// for one purpose (<see cref="MigrationV2Journal"/>).</para>
/// <para><b>The fault seam.</b> <see cref="AtBoundary"/> is called at every boundary between two steps of an apply, with the count so far, and a test makes
/// it throw to stand for a kill. NOTHING here catches an exception the seam could throw: the per-object catches name <see cref="IOException"/>,
/// <see cref="UnauthorizedAccessException"/>, <see cref="JsonException"/> and no more.</para>
/// <para><b>A dry run creates nothing</b> — no <c>migrations/</c>, no lease, no claim, no turn file — and its counts are the counts an apply over the same
/// folder reaches. The apply is in <c>MigrationV2Apply.cs</c>.</para>
/// </remarks>
internal sealed partial class MigrationV2
{
    /// <summary>What the held round's record says after the conversion. The verdict stays what it was.</summary>
    public const string ConversionNote = HoldConversion.Note;

    /// <summary>The marker's schema number.</summary>
    public const int MarkerSchema = 1;

    private readonly string _dataDir;
    private readonly Action<string> _said;
    private readonly Func<ServerNotice, bool> _notice;
    private readonly MigrationV2Tables _tables;
    private readonly TimeProvider _clock;
    private readonly string _version;
    private readonly SessionStore _store;
    private int _boundaries;

    public MigrationV2(string dataDir, Action<string> said, Func<ServerNotice, bool> notice, MigrationV2Tables? tables = null, TimeProvider? clock = null, string version = "")
    {
        _dataDir = dataDir;
        _said = said;
        _notice = notice;
        _tables = tables ?? MigrationV2Tables.Default;
        _clock = clock ?? TimeProvider.System;
        _version = version;
        _store = new SessionStore(dataDir);
    }

    /// <summary>
    /// Called at every boundary of an apply (before an object's <c>planned</c> line, after it, after the object's write, after its <c>done</c> line, and once
    /// before the marker, after each file of a store merge, and — for a session with answered holds — between its notice and its write) with the number of boundaries passed before this one. A test throws from it to stand for a kill at that boundary.
    /// </summary>
    internal Action<int>? AtBoundary { get; init; }

    /// <summary>The seam of the notice history's read: every line of <c>server-notices.jsonl</c> and its archive. A test counts how often a run reads it.</summary>
    internal Func<string, IEnumerable<string>> NoticeLines { get; init; } = ServerNotices.Lines;

    /// <summary>The seam of the confirm after a notice is written: the lines of the live notices file from a byte offset. A test checks where it starts and how much it reads.</summary>
    internal Func<string, long, IEnumerable<string>> NoticeTail { get; init; } = ServerNotices.LinesFrom;

    public static string MigrationsDir(string dataDir) => Path.Combine(dataDir, "migrations");

    public static string MarkerFile(string dataDir) => Path.Combine(MigrationsDir(dataDir), "v2.done");

    public static string JournalFile(string dataDir) => Path.Combine(MigrationsDir(dataDir), "v2.journal");

    private static string ArchiveDir(string dataDir) => Path.Combine(MigrationsDir(dataDir), "v1-archive");

    internal static string CardsDir(string dataDir) => Path.Combine(dataDir, "escalations");

    private static string SettingsFile(string dataDir) => Path.Combine(dataDir, "settings.json");

    private static string RemovedFile(string dataDir) => Path.Combine(dataDir, "settings.v1-removed.json");

    // ---------- the automatic run's gate ----------

    /// <summary>
    /// Whether a start of the server migrates by itself: the build is a v2 build (major version 2 or more — a development build is 0.0.0 and a v1
    /// release is 0.x) AND the marker is absent or was made by other tables. Pure.
    /// </summary>
    public static bool ShouldRunAutomatically(Version assembly, string? markerTablesHash, string tablesHash) =>
        assembly.Major >= 2 && markerTablesHash != tablesHash;

    /// <summary>The tables hash the marker of this folder carries, or null for no marker (or one that cannot be read).</summary>
    public static string? MarkerHash(string dataDir)
    {
        try
        {
            using var marker = JsonDocument.Parse(File.ReadAllText(MarkerFile(dataDir)));

            return marker.RootElement.ValueKind == JsonValueKind.Object
                && marker.RootElement.TryGetProperty("tablesHash", out var hash)
                && hash.ValueKind == JsonValueKind.String
                    ? hash.GetString()
                    : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    // ---------- the dry run ----------

    /// <summary>What an apply would do, read from the objects. Creates nothing and changes nothing.</summary>
    public MigrateV2Report Plan() =>
        Directory.Exists(_dataDir) ? ReportOf("plan", Examine()) : Failed(apply: false, "the data folder does not exist: " + _dataDir);

    // ---------- what there is to do ----------

    private abstract record Work(string Kind, string Id);

    /// <summary>A held session: leave the hold.</summary>
    private sealed record SessionWork(string File, PersistedSession Session, IReadOnlyList<MigrateV2AnsweredHold> Answered) : Work("session", Path.GetFileName(File));

    /// <summary>A converted session whose conversion has no <c>done</c> line: its projection into <c>coai.db</c> is written again.</summary>
    private sealed record ReprojectWork(string File, PersistedSession Session) : Work("session", Path.GetFileName(File));

    private sealed record CardWork(string File, bool Withdraw) : Work("card", Path.GetFileNameWithoutExtension(File));

    private sealed record StoreWork(string Name) : Work("store", Name);

    private sealed record SettingsWork(IReadOnlyList<string> Removed, IReadOnlyList<string> Renamed) : Work("settings", "settings.json");

    /// <summary>An object that could not be read just now (busy, denied): left for the next run, which keeps the marker away.</summary>
    private sealed record DeferredWork(string ObjectKind, string ObjectId, string Why) : Work(ObjectKind, ObjectId);

    private sealed class Scan
    {
        public List<Work> Work { get; } = [];

        public List<MigrateV2Step> Unreadable { get; } = [];
    }

    private Scan Examine()
    {
        Holds.Forget(); // a run reads the notice history afresh, once (HoldBoundary)
        var scan = new Scan();
        ExamineSessions(scan);
        ExamineCards(scan);
        ExamineStores(scan);
        ExamineSettings(scan);

        return scan;
    }

    private void ExamineSessions(Scan scan)
    {
        var sessions = Path.Combine(_dataDir, "sessions");
        if (!Directory.Exists(sessions))
        {
            return;
        }

        var done = MigrationV2Journal.Read(JournalFile(_dataDir)).Where(l => l.Kind == "session" && l.State == "done").Select(l => l.Id).ToHashSet();
        var database = File.Exists(Path.Combine(_dataDir, RoundsDb.FileName));
        foreach (var file in Directory.EnumerateFiles(sessions, "session-*.json").Order(StringComparer.Ordinal))
        {
            var id = Path.GetFileName(file);
            if (!TryLoadSession(file, id, scan, out var session))
            {
                continue;
            }

            if (LegacyHold.Stands(session.State))
            {
                scan.Work.Add(HeldWork(file, id, session));
            }
            else if (database && !done.Contains(id) && session.Rounds.Count > 0 && session.Rounds[^1].Note == ConversionNote)
            {
                scan.Work.Add(new ReprojectWork(file, session));
            }
        }
    }

    /// <summary>A held session to convert, or — when an answer file of it exists and could not be read — a deferred one: whether a person answered is unknown, so the marker waits.</summary>
    private Work HeldWork(string file, string id, PersistedSession session)
    {
        var answers = Holds.AnsweredOf(id, session.State);

        return answers.Known ? new SessionWork(file, session, answers.Answered) : new DeferredWork("session", id, answers.Unknown);
    }

    /// <summary>Reads one session file; a file that is not a session is <c>unreadable</c>, one that could not be opened just now is <c>deferred</c>.</summary>
    private bool TryLoadSession(string file, string id, Scan scan, out PersistedSession session)
    {
        session = null!;
        try
        {
            session = _store.LoadFile(file)!;
        }
        catch (JsonException e)
        {
            scan.Unreadable.Add(new MigrateV2Step("session", id, "leave", "unreadable: " + e.Message));

            return false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            scan.Work.Add(new DeferredWork("session", id, e.Message));

            return false;
        }

        if (session is null)
        {
            scan.Unreadable.Add(new MigrateV2Step("session", id, "leave", "unreadable: the file holds no session"));

            return false;
        }

        return true;
    }

    private void ExamineCards(Scan scan)
    {
        var dir = CardsDir(_dataDir);
        if (!Directory.Exists(dir))
        {
            return;
        }

        foreach (var file in HoldBoundary.TopLevelCards(dir))
        {
            var id = Path.GetFileNameWithoutExtension(file);
            JsonObject card;
            try
            {
                card = JsonNode.Parse(File.ReadAllText(file)) as JsonObject ?? throw new JsonException("the card is not a JSON object");
            }
            catch (JsonException e)
            {
                scan.Unreadable.Add(new MigrateV2Step("card", id, "leave", "unreadable: " + e.Message));
                continue;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                scan.Work.Add(new DeferredWork("card", id, e.Message));
                continue;
            }

            if (CardWorkOf(file, id, card) is { } work)
            {
                scan.Work.Add(work);
            }
        }
    }

    /// <summary>What one readable card needs, or null for a card that is history. A marked one only moves; an open one is withdrawn.</summary>
    private Work? CardWorkOf(string file, string id, JsonObject card)
    {
        var status = StatusOf(card);
        if (status == WithdrawnStatus)
        {
            return new CardWork(file, Withdraw: false);
        }

        return status.Length == 0 ? OpenCardWork(file, id) : null;
    }

    /// <summary>
    /// Open = no status, or the empty one, and no answer a person gave. Expired and answered cards are history. A card whose answer file exists and cannot be read is NOT open:
    /// withdrawing it would move the card out from under the held session that names it, and the answer's notice could never be issued — it is deferred.
    /// </summary>
    private Work? OpenCardWork(string file, string id) => HoldBoundary.ReadAnswer(_dataDir, id) switch
    {
        AnswerRead.None => new CardWork(file, Withdraw: true),
        AnswerRead.Unreadable unreadable => new DeferredWork("card", id, $"its answer file exists and could not be read: {unreadable.Why}"),
        _ => null,
    };

    /// <summary>What a card says once the migration withdrew it.</summary>
    public const string WithdrawnStatus = "withdrawn_v2";

    private static string StatusOf(JsonObject card) => card["status"] is JsonValue status && status.TryGetValue<string>(out var text) ? text : string.Empty;

    private void ExamineStores(Scan scan)
    {
        foreach (var name in _tables.Stores.Where(name => Directory.Exists(Path.Combine(_dataDir, name))))
        {
            scan.Work.Add(new StoreWork(name));
        }
    }

    private void ExamineSettings(Scan scan)
    {
        if (!File.Exists(SettingsFile(_dataDir)))
        {
            return;
        }

        try
        {
            var settings = ReadObject(SettingsFile(_dataDir)) ?? throw new JsonException("settings.json is not a JSON object");
            var removed = File.Exists(RemovedFile(_dataDir))
                ? ReadObject(RemovedFile(_dataDir)) ?? throw new JsonException("settings.v1-removed.json is not a JSON object")
                : new JsonObject();
            var edit = SettingsEdit.Of(settings, removed, _tables);
            if (edit.Any)
            {
                scan.Work.Add(new SettingsWork(edit.RemovedKeys, edit.RenamedKeys));
            }
        }
        catch (JsonException e)
        {
            scan.Unreadable.Add(new MigrateV2Step("settings", "settings.json", "leave", "unreadable: " + e.Message));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            scan.Work.Add(new DeferredWork("settings", "settings.json", e.Message));
        }
    }

    /// <summary>The JSON object in a file, or null when it holds JSON that is not an object. Throws <see cref="JsonException"/> for text that is not JSON.</summary>
    private static JsonObject? ReadObject(string file) => JsonNode.Parse(File.ReadAllText(file)) as JsonObject;

    // ---------- the report ----------

    private static MigrateV2Step StepOf(Work work) => work switch
    {
        SessionWork s => new(work.Kind, work.Id, "convert", "leave the held gate" + (s.Answered.Count > 0 ? "; a person had answered it" : string.Empty)),
        ReprojectWork => new(work.Kind, work.Id, "project", "write the conversion note into coai.db again"),
        CardWork { Withdraw: true } => new(work.Kind, work.Id, "withdraw", "mark it withdrawn_v2 and move it to escalations/archive"),
        CardWork => new(work.Kind, work.Id, "archive", "move the withdrawn card to escalations/archive"),
        StoreWork => new(work.Kind, work.Id, "archive", $"move {work.Id}/ to migrations/v1-archive/{work.Id}/"),
        SettingsWork s => new(work.Kind, work.Id, "move", $"removed: [{string.Join(", ", s.Removed)}]; renamed: [{string.Join(", ", s.Renamed)}]"),
        DeferredWork d => new(work.Kind, work.Id, "defer", d.Why),
        _ => new(work.Kind, work.Id, "none", string.Empty),
    };

    private MigrateV2Report ReportOf(string status, Scan scan)
    {
        var counts = new MigrateV2Counts(
            scan.Work.OfType<SessionWork>().Count(),
            scan.Work.OfType<CardWork>().Count(),
            scan.Work.OfType<StoreWork>().Count(),
            scan.Work.OfType<SettingsWork>().Sum(w => w.Removed.Count),
            scan.Work.OfType<SettingsWork>().Sum(w => w.Renamed.Count),
            scan.Unreadable.Count,
            scan.Work.OfType<DeferredWork>().Count());

        return new MigrateV2Report(
            "migrate-v2", Apply: false, _dataDir, status, counts,
            [.. scan.Work.OfType<SessionWork>().SelectMany(w => w.Answered)],
            [.. scan.Unreadable, .. scan.Work.Select(StepOf)])
        {
            Notices = PendingNotices(scan),
        };
    }

    private static readonly MigrateV2Counts NoCounts = new(0, 0, 0, 0, 0, 0, 0);

    private MigrateV2Report Failed(bool apply, string error, string status = "failed") =>
        new("migrate-v2", apply, _dataDir, status, NoCounts, [], []) { Error = error };
}
