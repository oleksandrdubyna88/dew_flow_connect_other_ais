using CoaiMcp.Core.Collecting;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Microsoft.Data.Sqlite;

namespace CoaiBugs.Tests;

/// <summary>
/// A client-side `coai.db` with pairs in it, which is what both cross-boundary suites need.
/// </summary>
/// <remarks>
/// One helper rather than one per suite: `BothHalvesTests` drives the client IN-PROCESS and
/// `TheBuiltBinariesTests` drives the built EXECUTABLE, and the only thing that differs between them
/// is how the client is started. A second copy of this would be a second thing to keep true about
/// what a real round writes.
/// </remarks>
internal static class Seed
{
    /// <summary>
    /// Records one accepted finding per pair, collects each, and keeps all of them.
    /// </summary>
    /// <remarks>
    /// One round for the whole set, because that is what a round is: a review answers about every
    /// finding it was given, and seeding them one round each would be a database no run produces.
    /// </remarks>
    /// <returns>The finding row ids, in the order the pairs were given.</returns>
    public static IReadOnlyList<long> KeptPairs(string dir, params CollectedPair[] pairs)
    {
        using var db = RoundsDb.Open(dir, Serilog.Core.Logger.None)!;
        var found = pairs
            .Select(pair => new Finding(
                Severity.Major, Category.Reliability, $"src/{pair.SymbolName}.cs", 5, "a race",
                "it races", "hold the lock", ["codex"]))
            .ToList();
        var session = new SessionState("s1", "D:/repo", "feat/x", new PanelConfig())
        {
            Stage = Stage.CodeReview,
        };

        db.RecordRound(
            session,
            new RoundRecord("CodeReview", 1, "revise", found.Count, "all answered", new DateTime(2026, 9, 16)),
            found,
            new RoundContext("SCOPE", "aaaa111", "claude-code"));
        db.RecordDecisions(
            "s1",
            "CodeReview",
            1,
            [.. Enumerable.Range(0, found.Count).Select(at => DecisionAt.Accept(found, at)!)]);

        var ids = Findings(dir);
        ids.Should().HaveCount(pairs.Length, "every finding must have landed");
        for (var at = 0; at < pairs.Length; at++)
        {
            db.RecordCollect(ids[at], "", "collected", "", "bbbb222", "run-1", pairs[at])
                .Should().BeTrue();
        }

        db.RecordKeep([.. ids.Select(id => new KeepDecision(id, Keep.Kept))])
            .Should().Be(pairs.Length);

        return ids;
    }

    /// <summary>The finding row ids, in the order they were written.</summary>
    private static IReadOnlyList<long> Findings(string dir)
    {
        using var read = new SqliteConnection(
            $"Data Source={Path.Combine(dir, RoundsDb.FileName)};Pooling=False");
        read.Open();
        using var all = read.CreateCommand();
        all.CommandText = "SELECT id FROM findings ORDER BY id";
        using var rows = all.ExecuteReader();
        var ids = new List<long>();
        while (rows.Read())
        {
            ids.Add(rows.GetInt64(0));
        }

        return ids;
    }
}
