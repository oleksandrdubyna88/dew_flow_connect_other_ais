using System.Text.Json;
using CoaiMcp.Core.Findings;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Server;

/// <summary>One reviewer run, as it will be counted forever.</summary>
/// <param name="Outcome">`ok` or the failure's own name — a round that cost money without
/// answering is exactly what a spending record must not hide.</param>
public sealed record UsageEntry(
    string Utc,
    string Provider,
    string Model,
    string Role,
    string Stage,
    double Seconds,
    long TokensIn,
    long TokensOut,
    double? CostUsd,
    string Outcome);

/// <summary>
/// The append-only record of what every reviewer has consumed.
/// </summary>
/// <remarks>
/// <para>Session files hold the CURRENT story of one repo+branch and are rewritten as rounds
/// advance; a spending history must not live there, because the question it answers — "what has
/// this cost me this month" — spans every session and must survive all of them being deleted.</para>
/// <para>JSON Lines, appended, never rewritten: an append cannot corrupt what is already there,
/// a torn last line costs one entry rather than the file, and reading it is a scan a panel can do
/// in a millisecond for a year of rounds.</para>
/// <para><b>Two servers share one data directory routinely</b>, so the append opens the file with
/// <c>FileShare.ReadWrite</c> and seeks to the end. An in-process lock alone was what the first
/// version had, and the round that reviewed this file caught it from both vendors:
/// <c>File.AppendAllText</c> takes a write lock the other process cannot pass, so the loser's line
/// would be swallowed by the catch below — a silent gap in a spending record, which is the one
/// place a gap is worse than an error. Each line is written in ONE call and is far below the
/// atomic-write size, so interleaving cannot split a line.</para>
/// </remarks>
public sealed class UsageLedger(string dataDir)
{
    private static readonly Lock Gate = new();

    public string Path => System.IO.Path.Combine(dataDir, "usage.jsonl");

    /// <summary>
    /// Records one reviewer. Never throws: a spending record that can fail a review is worse than
    /// one with a gap in it.
    /// </summary>
    public void Record(ReviewerInvocation invocation, ReviewerOutcome outcome, string model, string stage, TimeSpan elapsed)
    {
        // An unparseable run COMPLETED and reported what it consumed; only a process that never
        // finished has nothing to declare. Reading usage from `Ok` alone made every failed
        // reviewer look free, which is the opposite of what a spending record is for.
        var usage = outcome switch
        {
            ReviewerOutcome.Ok ok => ok.Usage,
            ReviewerOutcome.Unparseable bad => bad.Usage,
            _ => Usage.None,
        };
        var entry = new UsageEntry(
            DateTime.UtcNow.ToString("O"),
            invocation.Provider,
            model,
            invocation.Role.ToString(),
            stage,
            Math.Round(elapsed.TotalSeconds, 1),
            usage.TokensIn,
            usage.TokensOut,
            usage.CostUsd,
            outcome is ReviewerOutcome.Ok ? "ok" : ReviewerSummaryFactory.Describe(outcome));

        try
        {
            Directory.CreateDirectory(dataDir);
            var line = System.Text.Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(entry, LedgerJsonContext.Default.UsageEntry) + "\n");
            lock (Gate)
            {
                using var file = new FileStream(Path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                file.Write(line);
            }
        }
        catch (IOException)
        {
            // A missed line is a gap in a chart. A thrown exception here would be a failed review.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
