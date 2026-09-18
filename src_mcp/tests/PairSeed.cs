using CoaiMcp.Core.Collecting;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using CoaiMcp.Store;
using Microsoft.Data.Sqlite;

namespace CoaiMcp.Tests;

/// <summary>
/// One collected pair, written into a real rounds database the way the product writes one — a
/// session, a round, a finding, a decision, a collect row — pointing at commits in a checkout.
/// </summary>
/// <remarks>
/// It was a private helper inside <c>TheRealMethodTests</c>; opening a file at its revision (story
/// 3.1 of the review-page plan) seeds the same row for the same reason, and the reuse rule's second
/// move — extract the shared half — is cheaper than a second copy that drifts from the schema.
/// </remarks>
internal static class PairSeed
{
    /// <summary>Seeds the pair and answers its finding id — the one thing a one-pair mode is asked with.</summary>
    public static long One(
        string dataDir, string repoPath, string headSha, string fixSha, string file, int line, CollectedPair skeletons)
    {
        using var db = RoundsDb.Open(dataDir, Serilog.Core.Logger.None)!;
        var session = new SessionState("s1", repoPath, "main", new PanelConfig()) { Stage = Stage.CodeReview };
        var found = new Finding(
            Severity.Major, Category.Reliability, file, line, "a race", "it races", "hold the lock", ["codex"]);
        db.RecordRound(
            session,
            new RoundRecord("CodeReview", 1, "revise", 1, "all answered", new DateTime(2026, 9, 18)),
            [found],
            new RoundContext("SCOPE", headSha, "claude-code"));
        db.RecordDecisions("s1", "CodeReview", 1, [Decisions.Accept([found], 0)]);

        using var read = new SqliteConnection($"Data Source={Path.Combine(dataDir, RoundsDb.FileName)};Pooling=False");
        read.Open();
        using var one = read.CreateCommand();
        one.CommandText = "SELECT id FROM findings";
        var id = (long)one.ExecuteScalar()!;

        db.RecordCollect(id, "", "collected", "", fixSha, "run-1", skeletons);

        return id;
    }
}
