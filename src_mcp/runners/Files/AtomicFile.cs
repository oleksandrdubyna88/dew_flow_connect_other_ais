using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Runners.Files;

/// <summary>
/// Writing a small file so that no reader ever sees half of it.
/// </summary>
/// <remarks>
/// <para>Write to a temp beside the destination, then move over it. The move is the only step a
/// reader can observe, and it is atomic on every filesystem this runs on, so a crash or a concurrent
/// read finds either the old file or the new one and never a truncated one.</para>
/// <para><b>The temp name carries a GUID</b>, which is what makes it safe under concurrency: two
/// processes writing the same destination at the same time would otherwise share one temp path and
/// interleave into it. <c>ConsultSchemaFile</c> found that first and this is its shape, extracted on
/// story 3.2a's code round when a reviewer counted the seventh open-coded copy.</para>
/// <para><b>A temp that could not be moved is removed</b>, best effort, before the failure flies on: a
/// directory of evidence or health files that collected orphaned temporaries on every failed write would
/// be a growth surface with no owner (epic 2's review of PLAN_the_consultant_works_on_every_vendor.md,
/// which moved <c>EvidenceFile</c> and <c>ConsultHealthStore</c> onto this instead of a third and fourth
/// hand-written copy).</para>
/// <para><b>The retry covers the refusal Windows really gives</b> — <see cref="UnauthorizedAccessException"/>, not only <see cref="IOException"/>
/// (measured 2026-10-20: a move over a file another handle holds open throws it for every share mode of the holder) — and its window runs on the
/// monotonic clock. A persistent denial is thrown after the window, the original bytes survive and the temp file is removed.</para>
/// <para><b>The temp name carries the writer's pid</b> (<c>&lt;record&gt;.&lt;pid&gt;.&lt;guid&gt;.tmp</c>), and a write first removes the temp files of
/// THIS record that are older than one hour AND whose writer is not alive (<see cref="OrphanAge"/>): a writer killed between its temp file and the move
/// leaves one orphan, and the record's next write is its owner. Age is scope, not proof — a writer suspended after its temp file keeps it.</para>
/// <para><b>Three older copies are still open-coded</b> — <c>ConsultSchemaFile</c>, <c>RemoteRuntime</c>, <c>ArtifactStore</c>.
/// (<c>Escalations</c>' <c>AtomicJson</c> and <c>SessionStore</c> write through this since E3 S4 and S5.) Each has its own error handling around the dance and moving them is a
/// change that deserves its own diff and its own round.</para>
/// </remarks>
public static class AtomicFile
{
    /// <summary>
    /// The ONE window a move over a file a reader holds is retried for: Windows refuses it, and the panel and the extension poll these
    /// files — a reader holds a small one for milliseconds. A tolerance, not a guarantee: a holder past it fails the write.
    /// </summary>
    public static readonly TimeSpan SharingRetry = TimeSpan.FromMilliseconds(500);

    /// <summary>Writes the text so that a reader sees the whole of it or none of it.</summary>
    /// <param name="path">The destination. Its directory must already exist.</param>
    /// <param name="text">What to write.</param>
    /// <param name="sharingRetry">
    /// How long to keep retrying the MOVE when the destination is held open by a reader — on Windows a move
    /// over a file another handle has open without delete sharing is refused. Zero, the default, tries once.
    /// </param>
    public static void Write(string path, string text, TimeSpan sharingRetry = default) => Write(path, text, sharingRetry, AtomicSeams.Real);

    internal static void Write(string path, string text, TimeSpan sharingRetry, AtomicSeams seams)
    {
        RemoveOrphanedTemps(path, seams);
        var temp = $"{path}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        try
        {
            seams.WriteText(temp, text);
            seams.AfterTemp(temp);
            MoveOver(temp, path, sharingRetry, seams);
        }
        catch
        {
            Discard(temp);
            throw;
        }
    }

    private static void MoveOver(string temp, string path, TimeSpan window, AtomicSeams seams)
    {
        // Windows throws UnauthorizedAccessException (HResult 0x80070005) for a move over a file another handle holds open, for every share mode of
        // the holder (measured 2026-10-20, Windows 11 26300, .NET 10.0.12); IOException is the sharing violation of a file opened by name. A
        // persistent ACL denial is both, retried only for the window, then thrown.
        // The window runs on the MONOTONIC clock: a wall clock stepped by a sync or a sleep would stretch or cut it.
        var started = seams.Clock.GetTimestamp();
        var tries = 0;
        while (true)
        {
            try
            {
                File.Move(temp, path, overwrite: true);
                return;
            }
            catch (IOException) when (seams.Clock.GetElapsedTime(started) < window)
            {
                seams.Pause(++tries);
            }
            catch (UnauthorizedAccessException) when (seams.Clock.GetElapsedTime(started) < window)
            {
                seams.Pause(++tries);
            }
        }
    }

    /// <summary>How old a temp file must be before its writer being dead makes it an orphan. A live writer's temp file lives for milliseconds.</summary>
    internal static readonly TimeSpan OrphanAge = TimeSpan.FromHours(1);

    /// <summary>
    /// Before this writer's own temp file: removes the temp files of THIS record (<c>&lt;record&gt;.&lt;pid&gt;.&lt;guid&gt;.tmp</c> beside it)
    /// older than <see cref="OrphanAge"/> whose writer is PROVEN gone (a pid that could not be asked about keeps its file: a deletion needs proof). Best effort — a failure is ignored and tried on the next write.
    /// </summary>
    private static void RemoveOrphanedTemps(string path, AtomicSeams seams)
    {
        var full = Path.GetFullPath(path);
        var record = Path.GetFileName(full);
        try
        {
            foreach (var temp in Directory.EnumerateFiles(Path.GetDirectoryName(full)!, $"{record}.*.tmp").Where(t => IsOrphan(t, record, seams)))
            {
                Discard(temp);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The scan is a courtesy to the folder; the write it precedes does not depend on it.
        }
    }

    private static bool IsOrphan(string temp, string record, AtomicSeams seams) =>
        WriterOf(Path.GetFileName(temp), record) is var pid and > 0
        && seams.Clock.GetUtcNow() - File.GetLastWriteTimeUtc(temp) > OrphanAge
        && seams.LivenessOf(pid) is Liveness.Gone;

    /// <summary>The pid in <c>&lt;record&gt;.&lt;pid&gt;.&lt;guid:N&gt;.tmp</c>, or 0 when the name is not exactly that shape — another record's, or a build that named no writer.</summary>
    private static int WriterOf(string name, string record) =>
        BetweenRecordAndExtension(name, record).Split('.') is [var pid, var id] && Guid.TryParseExact(id, "N", out _) ? Pid(pid) : 0;

    private static string BetweenRecordAndExtension(string name, string record) =>
        name.Length > record.Length + 5 && name.StartsWith(record + ".", StringComparison.Ordinal) && name.EndsWith(".tmp", StringComparison.Ordinal)
            ? name[(record.Length + 1)..^4]
            : string.Empty;

    private static int Pid(string digits) =>
        int.TryParse(digits, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var pid) ? pid : 0;

    private static void Discard(string temp)
    {
        try
        {
            File.Delete(temp);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The cleanup of a failure that is already flying; the caller's own handling reports it.
        }
    }
}
