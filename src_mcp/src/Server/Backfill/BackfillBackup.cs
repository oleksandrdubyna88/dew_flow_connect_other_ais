using System.Globalization;
using System.Security.Cryptography;
using CoaiMcp.Server;
using Microsoft.Data.Sqlite;

namespace CoaiMcp.Server;

/// <summary>What a backup was checked against — each field is one check, and every one must hold before a single write.</summary>
/// <param name="Opened">The copy opens read-only.</param>
/// <param name="Integrity"><c>PRAGMA integrity_check</c> answers <c>ok</c> on the copy.</param>
/// <param name="UserVersion">The copy's <c>user_version</c> is the source's.</param>
/// <param name="Tables">How many tables were compared — the list is <c>sqlite_master</c>'s, never a hand-kept one.</param>
/// <param name="RowsMatch">Every table's row count in the copy equals the source's counted BEFORE and AFTER the backup.</param>
/// <param name="DataVersionUnchanged">The source's <c>PRAGMA data_version</c> is the same before and after: no other connection committed meanwhile.</param>
/// <param name="SessionFiles">How many session files were copied.</param>
/// <param name="FilesMatch">Every copied session file's SHA-256 equals its source's, and the file counts agree.</param>
public sealed record BackfillBackupCheck(
    bool Opened,
    bool Integrity,
    bool UserVersion,
    int Tables,
    bool RowsMatch,
    bool DataVersionUnchanged,
    int SessionFiles,
    bool FilesMatch)
{
    /// <summary>Every check held.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Verified => CopyIsSound && SourceWasQuiet;

    private bool CopyIsSound => Opened && Integrity && UserVersion;

    private bool SourceWasQuiet => RowsMatch && DataVersionUnchanged && FilesMatch;

    public static BackfillBackupCheck Failed { get; } = new(false, false, false, 0, false, false, 0, false);
}

/// <summary>A backup: the folder it went to — new, beside the store — and what it was checked against.</summary>
internal sealed record BackupOutcome(string Directory, BackfillBackupCheck Check);

/// <summary>
/// The verified backup that precedes every write (E7.S4, the owner's rule of 2026-10-15): SQLite's ONLINE backup of <c>coai.db</c> and a byte-for-byte
/// copy of every <c>sessions/session-*.json</c>, into a NEW folder <c>&lt;dataDir&gt;/backfill-backups/&lt;yyyy-MM-ddTHH-mm-ssZ&gt;/</c> beside the store.
/// </summary>
/// <remarks>
/// <para><b>Online backup, not a file copy.</b> A copy of <c>coai.db</c> + <c>-wal</c> + <c>-shm</c> made while another server writes can catch them
/// mid-checkpoint — a copy that opens and is wrong. The backup API reads one consistent snapshot, uncheckpointed WAL rows included.</para>
/// <para><b>What "verified" means</b> (<see cref="BackfillBackupCheck"/>): the copy opens read-only; <c>integrity_check</c> says <c>ok</c>; its
/// <c>user_version</c> is the source's; the row count of EVERY table equals the source's counted before AND after; the source's <c>data_version</c>, read
/// twice on the mode's OWN connection — each an autocommit statement, no reader left open between — is unchanged (SQLite changes it when ANY OTHER
/// connection commits, and never for the connection's own commits: counts alone cannot prove quiet, an <c>UPDATE</c> keeps them); every session file's
/// SHA-256 equals its source's. This verifies a recovery point. It does not stop a server writing afterwards — the write's in-transaction re-read does.</para>
/// <para><b>Nothing here deletes.</b> A refused backup leaves its folder; a second run makes its own; no backup is ever removed by the mode.</para>
/// </remarks>
internal static class BackfillBackup
{
    public const string FolderName = "backfill-backups";

    private sealed record Counts(SortedDictionary<string, long> Rows, long DataVersion);

    public static BackupOutcome Make(string dataDir, SqliteConnection source, TimeProvider clock, BackfillSeams seams)
    {
        var folder = NewFolder(dataDir, clock);
        var before = Take(source, versionFirst: true);
        var userVersion = Pragma(source, "user_version");
        CopyDatabase(source, Path.Combine(folder, RoundsDbFile));
        var copied = CopySessions(dataDir, folder);
        seams.DuringBackup?.Invoke();
        var after = Take(source, versionFirst: false);

        return new BackupOutcome(folder, Verify(folder, userVersion, before, after, copied, SessionFilesOf(dataDir)));
    }

    private static string RoundsDbFile => Store.RoundsDb.FileName;

    /// <summary>A folder that did not exist a moment ago: two runs in one second get <c>-2</c>, never a shared folder.</summary>
    private static string NewFolder(string dataDir, TimeProvider clock)
    {
        var stem = Path.Combine(dataDir, FolderName, clock.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd'T'HH-mm-ss'Z'", CultureInfo.InvariantCulture));
        var path = stem;
        for (var again = 2; Directory.Exists(path); again++)
        {
            path = stem + "-" + again.ToString(CultureInfo.InvariantCulture);
        }

        Directory.CreateDirectory(path);

        return path;
    }

    // ---------- the source, read on the mode's own connection ----------

    /// <summary>
    /// The rows of every table and the data version. The version is read FIRST before the copy and LAST after it, so the two reads enclose the whole
    /// backup; each statement is its own autocommit and its reader is disposed before the next begins.
    /// </summary>
    private static Counts Take(SqliteConnection db, bool versionFirst)
    {
        var first = versionFirst ? Pragma(db, "data_version") : 0;
        var rows = TableRows(db);
        var last = versionFirst ? first : Pragma(db, "data_version");

        return new Counts(rows, last);
    }

    private static long Pragma(SqliteConnection db, string name)
    {
        using var read = db.CreateCommand();
        read.CommandText = "PRAGMA " + name;

        return Convert.ToInt64(read.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static SortedDictionary<string, long> TableRows(SqliteConnection db)
    {
        var counts = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var table in Tables(db))
        {
            using var count = db.CreateCommand();
            count.CommandText = $"SELECT COUNT(*) FROM \"{table.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
            counts[table] = Convert.ToInt64(count.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        return counts;
    }

    private static List<string> Tables(SqliteConnection db)
    {
        using var read = db.CreateCommand();
        read.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
        using var rows = read.ExecuteReader();
        var names = new List<string>();
        while (rows.Read())
        {
            names.Add(rows.GetString(0));
        }

        return names;
    }

    // ---------- the copy ----------

    private static void CopyDatabase(SqliteConnection source, string destination)
    {
        using var copy = new SqliteConnection($"Data Source={destination};Pooling=False");
        copy.Open();
        source.BackupDatabase(copy);

        // The source is in WAL mode and the copy would be too: a read-only open of it would then make a -wal and a -shm beside it. One file is what a
        // backup should be.
        using var mode = copy.CreateCommand();
        mode.CommandText = "PRAGMA journal_mode = DELETE";
        mode.ExecuteNonQuery();
    }

    private static string SessionsOf(string dataDir) => Path.Combine(dataDir, "sessions");

    private static List<string> SessionFilesOf(string dataDir) =>
        Directory.Exists(SessionsOf(dataDir)) ? [.. Directory.GetFiles(SessionsOf(dataDir), "session-*.json").Order(StringComparer.Ordinal)] : [];

    /// <summary>Every session file, byte for byte, read with the sharing the session store itself allows — a live writer must not make the copy fail.</summary>
    private static List<string> CopySessions(string dataDir, string folder)
    {
        var destination = Path.Combine(folder, "sessions");
        Directory.CreateDirectory(destination);
        var copied = new List<string>();
        foreach (var file in SessionFilesOf(dataDir))
        {
            File.WriteAllBytes(Path.Combine(destination, Path.GetFileName(file)), ReadShared(file));
            copied.Add(Path.GetFileName(file));
        }

        return copied;
    }

    private static byte[] ReadShared(string file)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);

        return memory.ToArray();
    }

    // ---------- the check ----------

    private static BackfillBackupCheck Verify(string folder, long userVersion, Counts before, Counts after, List<string> copied, List<string> sources)
    {
        var (opened, integrity, version, copyRows) = Inspect(Path.Combine(folder, RoundsDbFile), userVersion);
        var files = FilesMatch(folder, copied, sources);

        return new BackfillBackupCheck(
            opened, integrity, version, before.Rows.Count,
            opened && copyRows.SequenceEqual(before.Rows) && before.Rows.SequenceEqual(after.Rows),
            before.DataVersion == after.DataVersion, copied.Count, files);
    }

    private static (bool Opened, bool Integrity, bool UserVersion, SortedDictionary<string, long> Rows) Inspect(string copy, long userVersion)
    {
        try
        {
            using var db = new SqliteConnection($"Data Source={copy};Mode=ReadOnly;Pooling=False");
            db.Open();

            return (true, Text(db, "PRAGMA integrity_check") == "ok", Pragma(db, "user_version") == userVersion, TableRows(db));
        }
        catch (SqliteException)
        {
            return (false, false, false, []);
        }
    }

    private static string Text(SqliteConnection db, string sql)
    {
        using var read = db.CreateCommand();
        read.CommandText = sql;

        return Convert.ToString(read.ExecuteScalar(), CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static bool FilesMatch(string folder, List<string> copied, List<string> sources) =>
        copied.Count == sources.Count && sources.All(source => Hash(source) == Hash(Path.Combine(folder, "sessions", Path.GetFileName(source))));

    private static string Hash(string file) => Convert.ToHexStringLower(SHA256.HashData(ReadShared(file)));
}
