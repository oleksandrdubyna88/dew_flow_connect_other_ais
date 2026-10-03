namespace CoaiMcp.Server;

/// <summary>
/// The retention of what a consultation leaves on disk beside its record: orphaned health temporaries,
/// and kept transcripts.
/// </summary>
/// <remarks>
/// <para>Epic 2's plan round: a growth surface needs a named owner (<c>reliability.md</c> — everything that
/// grows has an owner). Two surfaces arrived with the failure classification, and this is theirs:</para>
/// <list type="bullet">
/// <item><c>consultations/health/*.tmp</c> older than <see cref="TemporaryAge"/>: a health write that died
/// between writing its temporary file and moving it. Younger ones are left — they may be another server's
/// write in flight.</item>
/// <item><c>unparseable/consultations/*</c> older than <see cref="EvidenceAge"/>: a consultation's kept
/// transcript, in a directory of its own (<see cref="EvidenceDirectory"/>). It was a <c>consult-*</c> prefix
/// beside the REVIEWERS' evidence, which has no retention and is not this sweep's to retire — and a reviewer
/// provider whose id begins "consult" would have had its evidence pruned with it (epic 2's review).</item>
/// </list>
/// <para><b>Never the reason a sweep fails.</b> Run by the consultation sweep AFTER the records are swept, at
/// startup and on every <see cref="ConsultationSweeper"/> beat; a directory that cannot be listed, or a file
/// whose time cannot be read, is logged and skipped — the next beat tries again (epic 2's review: an
/// unreadable directory had thrown out of the sweep before the records were reached).</para>
/// </remarks>
public static class ConsultationRetention
{
    public static readonly TimeSpan TemporaryAge = TimeSpan.FromHours(1);

    public static readonly TimeSpan EvidenceAge = TimeSpan.FromDays(30);

    /// <summary>Where a consultation's transcripts are kept — its own subdirectory of the evidence directory.</summary>
    public static string EvidenceDirectory(string dataDir) => Path.Combine(dataDir, "unparseable", "consultations");

    /// <summary>Removes what is past its age, and answers how many files went.</summary>
    public static int Sweep(string dataDir, DateTime nowUtc, Action<string> warn) =>
        Expire(ConsultHealthPaths.HealthDirectory(dataDir), "*.tmp", nowUtc - TemporaryAge, warn)
        + Expire(EvidenceDirectory(dataDir), "*", nowUtc - EvidenceAge, warn);

    private static int Expire(string directory, string pattern, DateTime before, Action<string> warn)
    {
        try
        {
            return Directory.Exists(directory)
                ? Directory.EnumerateFiles(directory, pattern).Where(file => OlderThan(file, before)).Count(file => Removed(file, warn))
                : 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            warn($"consultations: the retention pass could not read {directory}, and the next beat will try again: {e.Message}");
            return 0;
        }
    }

    /// <summary>Whether the file is past its age — a time that cannot be read is no reason to delete it.</summary>
    private static bool OlderThan(string file, DateTime before)
    {
        try
        {
            return File.GetLastWriteTimeUtc(file) < before;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool Removed(string file, Action<string> warn)
    {
        try
        {
            File.Delete(file);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            warn($"consultations: {file} is past its retention and could not be removed: {e.Message}");
            return false;
        }
    }
}
