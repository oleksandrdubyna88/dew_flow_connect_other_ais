using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Files;
using CoaiMcp.Store;
using Microsoft.Data.Sqlite;

namespace CoaiMcp.Server.Migrations;

/// <summary>The apply half of <see cref="MigrationV2"/>: lease, journal, one object at a time, marker.</summary>
internal sealed partial class MigrationV2
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>What one object's step came to. <c>After</c> runs once the boundary after the object's write has passed.</summary>
    private sealed record Outcome(string State, string Detail)
    {
        public Action? After { get; init; }

        public int Removed { get; init; }

        public int Renamed { get; init; }
    }

    /// <summary>The counters an apply keeps, which become the report's.</summary>
    private sealed class Tally(int unreadable, int deferred)
    {
        public int Sessions { get; set; }

        public int Cards { get; set; }

        public int Stores { get; set; }

        public int Removed { get; set; }

        public int Renamed { get; set; }

        public int Unreadable { get; set; } = unreadable;

        public int Deferred { get; set; } = deferred;

        public List<MigrateV2AnsweredHold> Answered { get; } = [];

        public List<MigrateV2Step> Steps { get; } = [];
    }

    /// <summary>
    /// Migrates the folder. Takes <c>claims/migrate-v2.lease</c> first (<c>busy</c> when another migration holds it, <c>failed</c> when the folder cannot take it),
    /// then goes object by object, and writes the marker only when nothing was deferred AND nothing was unreadable (SPEC §5.2, §6.4): an object the run could not
    /// read is an object it did not migrate, and a marker would tell the next start not to look again.
    /// </summary>
    public MigrateV2Report Apply() => Leased(Migrate);

    /// <summary>
    /// The sweep every v2 start makes, whatever the marker says (SPEC §6.3): every OPEN card at the top level of <c>escalations/</c> is withdrawn and archived,
    /// journaled like any other card step. A v1 process still running on the folder can raise a card after <c>v2.done</c>; nothing else would ever look at it.
    /// </summary>
    /// <remarks>
    /// Looks first and takes the lease only when there is a card to take: a start with nothing to sweep creates no file. Never writes the marker — it is not the
    /// migration — and never touches a session, a store or the settings.
    /// </remarks>
    public MigrateV2Report SweepCards()
    {
        if (!Directory.Exists(_dataDir))
        {
            return Failed(apply: true, "the data folder does not exist: " + _dataDir);
        }

        var scan = new Scan();
        ExamineCards(scan);

        return scan.Work.OfType<CardWork>().Any() ? Leased(Sweep) : new("migrate-v2", Apply: true, _dataDir, "done", NoCounts, [], []);
    }

    private MigrateV2Report Leased(Func<MigrateV2Report> body)
    {
        if (!Directory.Exists(_dataDir))
        {
            return Failed(apply: true, "the data folder does not exist: " + _dataDir);
        }

        return MigrationV2Lease.Take(_dataDir) switch
        {
            LeaseTake.Taken taken => RunLeased(taken.Lease, body),
            LeaseTake.Busy busy => Failed(apply: true, busy.Why, "busy"),
            LeaseTake.Unusable unusable => Failed(apply: true, "the data folder cannot take the migration's lease: " + unusable.Why),
            _ => Failed(apply: true, "the data folder cannot take the migration's lease"),
        };
    }

    private MigrateV2Report RunLeased(MigrationV2Lease lease, Func<MigrateV2Report> body)
    {
        using (lease)
        {
            try
            {
                Directory.CreateDirectory(MigrationsDir(_dataDir));

                return body();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return Failed(apply: true, "the data folder could not be written: " + e.Message);
            }
        }
    }

    private MigrateV2Report Sweep()
    {
        var journal = new MigrationV2Journal(JournalFile(_dataDir), _clock);
        var scan = new Scan();
        ExamineCards(scan);
        var tally = new Tally(0, 0);
        foreach (var work in scan.Work.OfType<CardWork>())
        {
            Execute(work, journal, tally);
        }

        var counts = new MigrateV2Counts(0, tally.Cards, 0, 0, 0, tally.Unreadable, tally.Deferred);

        return new MigrateV2Report(
            "migrate-v2", Apply: true, _dataDir, tally.Deferred == 0 && tally.Unreadable == 0 ? "done" : "partial", counts, [], [.. tally.Steps]);
    }

    private MigrateV2Report Migrate()
    {
        var journal = new MigrationV2Journal(JournalFile(_dataDir), _clock);
        var scan = Examine();
        var tally = new Tally(scan.Unreadable.Count, scan.Work.OfType<DeferredWork>().Count());
        foreach (var unreadable in scan.Unreadable)
        {
            journal.Append(unreadable.Kind, unreadable.Id, "unreadable", unreadable.Detail);
        }

        foreach (var work in scan.Work)
        {
            Execute(work, journal, tally);
        }

        Boundary();
        var complete = tally.Deferred == 0 && tally.Unreadable == 0;
        if (complete)
        {
            WriteMarker();
        }

        var counts = new MigrateV2Counts(tally.Sessions, tally.Cards, tally.Stores, tally.Removed, tally.Renamed, tally.Unreadable, tally.Deferred);

        return new MigrateV2Report(
            "migrate-v2", Apply: true, _dataDir, complete ? "done" : "partial", counts, tally.Answered, [.. scan.Unreadable, .. tally.Steps])
        {
            Notices = [.. Holds.Written],
        };
    }

    private void Boundary() => AtBoundary?.Invoke(_boundaries++);

    private void Execute(Work work, MigrationV2Journal journal, Tally tally)
    {
        var step = StepOf(work);
        if (work is DeferredWork deferred)
        {
            journal.Append(work.Kind, work.Id, "deferred", deferred.Why);
            tally.Steps.Add(step);

            return;
        }

        Boundary();
        journal.Append(work.Kind, work.Id, "planned", step.Detail);
        Boundary();
        var outcome = Do(work);
        Boundary();
        outcome.After?.Invoke();
        journal.Append(work.Kind, work.Id, outcome.State, outcome.Detail);
        Boundary();
        Count(work, outcome, tally, step);
    }

    private static void Count(Work work, Outcome outcome, Tally tally, MigrateV2Step step)
    {
        switch (outcome.State)
        {
            case "unreadable":
                tally.Unreadable++;
                tally.Steps.Add(step with { Action = "leave", Detail = outcome.Detail });
                break;
            case "deferred":
                tally.Deferred++;
                tally.Steps.Add(step with { Action = "defer", Detail = outcome.Detail });
                break;
            case "skipped":
                tally.Steps.Add(step with { Action = "skip", Detail = outcome.Detail });
                break;
            default:
                tally.Steps.Add(step);
                Done(work, outcome, tally);
                break;
        }
    }

    private static void Done(Work work, Outcome outcome, Tally tally)
    {
        switch (work)
        {
            case SessionWork session:
                tally.Sessions++;
                tally.Answered.AddRange(session.Answered);
                break;
            case CardWork:
                tally.Cards++;
                break;
            case StoreWork:
                tally.Stores++;
                break;
            case SettingsWork:
                tally.Removed += outcome.Removed;
                tally.Renamed += outcome.Renamed;
                break;
        }
    }

    /// <summary>One object's step. A failure the folder causes (<see cref="IOException"/>, a denial) defers the object; it is retried by the next run.</summary>
    private Outcome Do(Work work)
    {
        try
        {
            return work switch
            {
                SessionWork session => ConvertSession(session),
                ReprojectWork reproject => Project(reproject),
                CardWork card => WithdrawCard(card),
                StoreWork store => ArchiveStore(store),
                SettingsWork => MoveSettings(),
                _ => new Outcome("skipped", "nothing to do"),
            };
        }
        catch (JsonException e)
        {
            return new Outcome("unreadable", e.Message);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new Outcome("deferred", e.Message);
        }
    }

    // ---------- sessions ----------

    private Outcome ConvertSession(SessionWork work)
    {
        var state = work.Session.State;
        using var claim = SessionClaim.TryTake(_dataDir, state.RepoPath, state.Branch, state.Document, state.Feature);
        if (claim is null)
        {
            return new Outcome("deferred", SessionClaim.Busy(state.Branch));
        }

        // Re-read UNDER the claim: the plan was made before it, and a call that held the session between the two may have released the hold.
        var fresh = _store.LoadFile(work.File);
        if (fresh is null || !LegacyHold.Stands(fresh.State))
        {
            return new Outcome("skipped", "the hold is gone");
        }

        // The person is told BEFORE the answer is overridden (HoldBoundary, the boundary a load crosses too): a session that cannot be announced stays held, and the next run tries again.
        // A kill between the notice and the conversion leaves a held session and its notice: the next run finds the notice and converts.
        var converted = Holds.Released(fresh, work.File, afterNotice: Boundary);
        if (converted is null)
        {
            return new Outcome("deferred", "the notice of an answered hold could not be written, so the session was not converted");
        }

        _store.SaveTo(work.File, converted);

        return new Outcome("done", "released")
        {
            After = converted.Rounds.Count > 0 ? () => WriteNote(converted.State.SessionId, converted.Rounds[^1]) : null,
        };
    }

    private Outcome Project(ReprojectWork work)
    {
        if (work.Session.Rounds.Count > 0)
        {
            WriteNote(work.Session.State.SessionId, work.Session.Rounds[^1]);
        }

        return new Outcome("done", "projected");
    }

    /// <summary>The note into <c>coai.db</c>, best effort: the database is a projection rebuilt from the session files, and one that is absent or will not open is left alone.</summary>
    private void WriteNote(string sessionId, RoundRecord round) => HoldConversion.WriteNote(_dataDir, sessionId, round, _said);

    // ---------- cards ----------

    private Outcome WithdrawCard(CardWork work)
    {
        var id = Path.GetFileNameWithoutExtension(work.File);
        var card = JsonNode.Parse(File.ReadAllText(work.File)) as JsonObject ?? throw new JsonException("the card is not a JSON object");
        var status = StatusOf(card);
        if (status.Length == 0 && HoldBoundary.AnswerOf(_dataDir, id) is null)
        {
            // In place first, atomically and with every other member kept: a kill between this and the move leaves a card the next run only moves.
            card["status"] = WithdrawnStatus;
            AtomicFile.Write(work.File, card.ToJsonString(Indented) + "\n", AtomicFile.SharingRetry);
            Boundary();
        }
        else if (status != WithdrawnStatus)
        {
            return new Outcome("skipped", "the card is no longer open");
        }

        var archive = Path.Combine(CardsDir(_dataDir), "archive");
        Directory.CreateDirectory(archive);
        File.Move(work.File, Unoccupied(Path.Combine(archive, Path.GetFileName(work.File))));

        return new Outcome("done", "withdrawn_v2");
    }

    /// <summary>The path, or the first <c>name.N.ext</c> beside it that nothing occupies — an archive never overwrites.</summary>
    private static string Unoccupied(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return path;
        }

        var stem = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path));
        var extension = Path.GetExtension(path);
        for (var n = 1; ; n++)
        {
            var candidate = $"{stem}.{n}{extension}";
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    // ---------- stores ----------

    private Outcome ArchiveStore(StoreWork work)
    {
        var source = Path.Combine(_dataDir, work.Name);
        if (!Directory.Exists(source))
        {
            return new Outcome("skipped", "the store is gone");
        }

        var archive = ArchiveDir(_dataDir);
        var target = Path.Combine(archive, work.Name);
        Directory.CreateDirectory(archive);
        if (!Directory.Exists(target))
        {
            Directory.Move(source, target);

            return new Outcome("done", "moved");
        }

        // An archive of this name is already there (a store re-created after the first run): merge file by file, never overwriting.
        MergeInto(source, target);

        return new Outcome("done", "merged");
    }

    /// <summary>
    /// Merges a re-created store into the archive of the same name, file by file (SPEC §5.3, §6.1). Every file is decided by ITS PATH and its BYTES alone, so a run that is
    /// killed between two files and run again decides each remaining file exactly as one clean run would — and a file is never decided by what happens to be in the folder
    /// next to it: see <see cref="MoveOne"/>.
    /// </summary>
    /// <remarks>
    /// A boundary follows every file, so a test can kill the merge between any two of them. The source folder goes last: a kill before it leaves a folder the next run
    /// merges again (it holds the files not yet moved, and empty folders).
    /// </remarks>
    private void MergeInto(string source, string target)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            MoveOne(file, destination);
            Boundary();
        }

        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
        {
            Directory.Delete(directory);
        }

        Directory.Delete(source);
    }

    /// <summary>
    /// One file into the archive, by its path identity. The same relative path with the same bytes IS the same file: the source is removed. A DIFFERENT file at that path
    /// goes beside it under <c>name.v1-&lt;first eight hex digits of the SHA-256 of its content&gt;.ext</c> — a name that depends on the content and on nothing the run did, so the
    /// same file always lands at the same place. Two names with identical bytes are two files, and both stay. Nothing is ever overwritten.
    /// </summary>
    private static void MoveOne(string file, string destination)
    {
        if (!File.Exists(destination))
        {
            File.Move(file, destination);

            return;
        }

        if (SameBytes(file, destination))
        {
            File.Delete(file);

            return;
        }

        var suffixed = Path.Combine(
            Path.GetDirectoryName(destination)!,
            $"{Path.GetFileNameWithoutExtension(destination)}.v1-{ContentHash(file)}{Path.GetExtension(destination)}");
        if (!File.Exists(suffixed))
        {
            File.Move(file, suffixed);
        }
        else if (SameBytes(file, suffixed))
        {
            File.Delete(file); // this very file was merged by an earlier run
        }
        else
        {
            File.Move(file, Unoccupied(suffixed)); // eight hex digits of two different contents agree: numbered, never overwritten
        }
    }

    private static bool SameBytes(string a, string b) =>
        new FileInfo(a).Length == new FileInfo(b).Length && File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));

    /// <summary>The first eight lowercase hex digits of the SHA-256 of the file's bytes.</summary>
    private static string ContentHash(string file) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file)))[..8];

    // ---------- settings ----------

    private Outcome MoveSettings()
    {
        // The extension's own lock, taken the way it takes it (SettingsLockFile): never waited for, a held one defers the keys.
        using var held = SettingsLockFile.TryTake(_dataDir, _clock);
        if (held is null)
        {
            return new Outcome("deferred", "settings.lock is held by a window that is writing settings.json");
        }

        // Read again UNDER the lock: the scan was made before it, and a window writes this file at every activation.
        var settings = ReadObject(SettingsFile(_dataDir));
        var removedFile = File.Exists(RemovedFile(_dataDir)) ? ReadObject(RemovedFile(_dataDir)) : new JsonObject();
        if (settings is null || removedFile is null)
        {
            return new Outcome("unreadable", "settings.json or settings.v1-removed.json is not a JSON object");
        }

        var edit = SettingsEdit.Of(settings, removedFile, _tables);
        if (!edit.Any)
        {
            return new Outcome("skipped", "no key is left to move");
        }

        // The removed keys are SAFE before they leave: a kill between the two writes leaves them in both files, and the next run drops them from the settings.
        if (edit.Removed.Count != removedFile.Count)
        {
            AtomicFile.Write(RemovedFile(_dataDir), edit.Removed.ToJsonString(Indented) + "\n", AtomicFile.SharingRetry);
        }

        Boundary();
        if (!held.Holds())
        {
            return new Outcome("deferred", "the settings lock was taken over while the keys were being moved");
        }

        AtomicFile.Write(SettingsFile(_dataDir), edit.Settings.ToJsonString(Indented) + "\n", AtomicFile.SharingRetry);

        return new Outcome("done", $"removed: [{string.Join(", ", edit.RemovedKeys)}]; renamed: [{string.Join(", ", edit.RenamedKeys)}]")
        {
            Removed = edit.RemovedKeys.Count,
            Renamed = edit.RenamedKeys.Count,
        };
    }

    // ---------- the marker ----------

    private void WriteMarker()
    {
        if (MarkerHash(_dataDir) == _tables.Hash)
        {
            return; // already says it: a run that found nothing to do changes no byte
        }

        var marker = new MigrateV2Marker(MarkerSchema, _tables.Hash, _clock.GetUtcNow().UtcDateTime.ToString("O"), _version);
        AtomicFile.Write(
            MarkerFile(_dataDir),
            JsonSerializer.Serialize(marker, ServerJsonContext.Default.MigrateV2Marker) + "\n",
            AtomicFile.SharingRetry);
    }
}
