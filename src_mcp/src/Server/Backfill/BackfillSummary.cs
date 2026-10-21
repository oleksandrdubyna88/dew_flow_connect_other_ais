using CoaiMcp.Core.Authors;
using CoaiMcp.Store;

namespace CoaiMcp.Server;

/// <summary>What was written, by how the round came to be written. Exclusive buckets: a round counts once.</summary>
public sealed record BackfillWritten(int Computed, int RecomputedProvisional, int RecomputedSnapshotLost, int Reprojected, int DatabaseOnly, int Corrected);

/// <summary>What was left as it was, and why.</summary>
public sealed record BackfillKept(int EqualOrNewer, int StrongerExisting, int TranscriptGone, int PeerStillRunning, int NoFile, int Unchanged);

/// <summary>What could not be done now: counted, never recorded on the round, retried on the next run.</summary>
public sealed record BackfillDeferred(int Busy, int Unreadable, int ChangedUnderUs, int PeersUnknown);

/// <summary>The size half: sizes written, sizes kept, the rounds whose size cannot be had (per closed reason) and the delta state of every size written.</summary>
public sealed record BackfillSizeSummary(int Written, int Kept, SortedDictionary<string, int> Unrecoverable, SortedDictionary<string, int> Delta);

/// <summary>
/// What <c>coai-mcp --backfill-authors</c> prints on stdout: counts and closed words, never a prompt, a description, a message, a branch or a path — except
/// <see cref="BackupDir"/>, the operator's own folder, printed once and never committed (E7.S4).
/// </summary>
/// <param name="Since"><c>--since</c> as an ISO-8601 UTC instant, or empty.</param>
/// <param name="BackupDir">The folder the verified backup went to; empty for a dry run, which makes none.</param>
/// <param name="BackupCheck">What the backup was checked against; absent for a dry run.</param>
/// <param name="Unobserved">The states written, per reason: how many rounds came out <c>unobserved: &lt;reason&gt;</c>.</param>
/// <param name="Next">A sentence when something was deferred (run again), else empty.</param>
public sealed record BackfillSummary(
    int ReaderVersion,
    int AttributionVersion,
    string Since,
    bool DryRun,
    string BackupDir,
    BackfillBackupCheck? BackupCheck,
    int Selected,
    BackfillWritten Written,
    int Healed,
    BackfillKept Kept,
    BackfillDeferred Deferred,
    SortedDictionary<string, int> Unobserved,
    BackfillSizeSummary Size,
    long ElapsedMs,
    string Next)
{
    internal const string RunAgain = "some rounds were deferred (a live round holds the session, or a live server wrote it meanwhile): run the same command again";

    /// <summary>The summary of a run's counts. The versions are read from the types — never literals.</summary>
    internal static BackfillSummary Of(
        BackfillCounts counts, DateTime? since, bool dryRun, BackupOutcome? backup, long elapsedMs) =>
        new(
            ObservedSession.ReaderVersion,
            RoundAttribution.Version,
            SinceText(since),
            dryRun,
            backup is null ? string.Empty : backup.Directory,
            backup?.Check,
            counts.Selected,
            new BackfillWritten(
                counts.Written[WrittenAs.Computed], counts.Written[WrittenAs.RecomputedProvisional], counts.Written[WrittenAs.RecomputedSnapshotLost],
                counts.Written[WrittenAs.Reprojected], counts.Written[WrittenAs.DatabaseOnly], counts.Written[WrittenAs.Corrected]),
            counts.Healed,
            new BackfillKept(
                counts.Kept[BackfillWhy.EqualOrNewer], counts.Kept[BackfillWhy.StrongerExisting], counts.Kept[BackfillWhy.TranscriptGone],
                counts.Kept[BackfillWhy.PeerStillRunning], counts.Kept[BackfillWhy.NoFile], counts.Kept[BackfillWhy.Unchanged]),
            DeferredOf(counts),
            counts.Unobserved,
            new BackfillSizeSummary(counts.SizeWritten, counts.SizeKept, counts.SizeUnrecoverable, counts.SizeDelta),
            elapsedMs,
            counts.Deferred.Total > 0 ? RunAgain : string.Empty);

    private static string SinceText(DateTime? since) => since is { } from ? from.ToString("O", System.Globalization.CultureInfo.InvariantCulture) : string.Empty;

    private static readonly BackfillWhy[] Deferrals = [BackfillWhy.Busy, BackfillWhy.FileUnreadable, BackfillWhy.ChangedUnderUs, BackfillWhy.PeersUnknown];

    /// <summary>
    /// The four deferral buckets. A reason counted as a deferral that none of them names is refused LOUDLY (code gate finding 19): a summary that
    /// printed zeros for it would tell the operator "run again" while the four fields read done.
    /// </summary>
    internal static BackfillDeferred DeferredOf(BackfillCounts counts)
    {
        var stray = counts.Deferred.All.Keys.Where(why => !Deferrals.Contains(why)).ToList();
        if (stray.Count > 0)
        {
            throw new InvalidOperationException("a deferral the summary cannot name: " + string.Join(", ", stray));
        }

        return new(counts.Deferred[BackfillWhy.Busy], counts.Deferred[BackfillWhy.FileUnreadable], counts.Deferred[BackfillWhy.ChangedUnderUs], counts.Deferred[BackfillWhy.PeersUnknown]);
    }
}
