using System.Text.Json;
using CoaiMcp.Core.Findings;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Runners.Reviewers;

/// <summary>
/// What a recorded run WAS, as the ledger spells it.
/// </summary>
/// <remarks>
/// <para>The names live HERE, in the library both halves compile against, rather than only in the
/// server's own <c>JobKind</c> — which the ledger cannot see, because the dependency runs
/// server → mcp and not back. Two vocabularies for one field is how a writer and a reader come to
/// disagree about what a spending row means, and the reader's fallback ("anything I do not know is a
/// review") would hide the disagreement rather than report it. A test in <c>src_server</c> asserts
/// the server's enum spells exactly these. (codex, code round.)</para>
/// <para><b>An unknown value is still WRITTEN.</b> A ledger line is history: refusing one because a
/// newer producer used a word this build has not heard would lose a run that already happened and
/// already cost money, which is the one thing a spending record must never do.</para>
/// </remarks>
public static class UsageKinds
{
    public const string Review = "review";
    public const string Chat = "chat";

    /// <summary>Every kind this build knows how to name. Order is the order a report reads best in.</summary>
    public static IReadOnlyList<string> Known { get; } = [Review, Chat];

    /// <summary>Whether a non-empty kind is one this build knows. Empty is "not said", which is fine.</summary>
    public static bool IsKnown(string kind) =>
        kind.Length == 0 || Known.Contains(kind, StringComparer.Ordinal);
}

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
    string Outcome,

    /// <summary>Who spent it, on a Team server.</summary>
    /// <remarks>
    /// Empty for a local run, where there is exactly one person and asking would be theatre.
    /// Trailing and defaulted, so every existing construction still compiles and every line
    /// already on disk stays valid — an old line simply has no email, which is the truth about it.
    /// </remarks>
    string Email = "",

    /// <summary>What this run WAS: a review, or a conversation.</summary>
    /// <remarks>
    /// The owner's ruling, 2026-09-08: chat turns go into the spending record like review turns and
    /// are DISTINGUISHED from them, because "what did the gate cost me" and "what did asking cost me"
    /// are two questions and one total answers neither.
    /// <para>Trailing and defaulted, like the email above it and for the same reason: every line
    /// already on disk stays valid. An old line has no kind, and everything written before this
    /// existed was a review — there was nothing else to be — so an absent kind is READ as one rather
    /// than shown as unknown. That is the truth about those lines, not a convenience.</para>
    /// </remarks>
    string Kind = "");

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

        Append(entry);
    }

    /// <summary>Records one job a Team server ran on somebody's behalf.</summary>
    /// <remarks>
    /// The same file and the same shape as a local run — ONE ledger, not two — with the email filled
    /// in. By the time the server records a job it no longer holds a <c>ReviewerInvocation</c>: the
    /// job outlived it and the outcome has already been mapped to the wire vocabulary. So this takes
    /// what the server actually has. Widening the ledger was the alternative to a second JSONL writer
    /// in <c>src_server</c>, and a second writer is how two spending records come to disagree.
    /// </remarks>
    public void RecordJob(
        string email,
        string provider,
        string model,
        string role,
        string outcome,
        TimeSpan elapsed,
        long tokensIn,
        long tokensOut,
        double? costUsd = null,
        string kind = "") =>
        Append(new UsageEntry(
            DateTime.UtcNow.ToString("O"),
            provider,
            model,
            role,
            "TeamServer",
            Math.Round(elapsed.TotalSeconds, 1),
            tokensIn,
            tokensOut,
            costUsd,
            outcome,
            email,
            kind));

    /// <summary>Never throws: a spending record that can fail a review is worse than one with a gap.</summary>
    private void Append(UsageEntry entry)
    {        try
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
