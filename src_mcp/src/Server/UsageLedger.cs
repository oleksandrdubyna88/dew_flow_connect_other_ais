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
/// in a millisecond for a year of rounds. Concurrency is a shared lock plus O_APPEND semantics —
/// two servers on one data directory is the normal case here, not an edge one.</para>
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
        var usage = outcome is ReviewerOutcome.Ok ok ? ok.Usage : Usage.None;
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
            lock (Gate)
            {
                File.AppendAllText(Path, JsonSerializer.Serialize(entry, ServerJsonContext.Default.UsageEntry) + "\n");
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
