using System.Text.Json;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using CoaiMcp.Store;

namespace CoaiMcp.Tests;

/// <summary>
/// The pieces every round-counts snapshot test builds a store out of (E6.S1): the OTHER side's snapshot document, written the way its
/// owner would write it, and this side's rounds recorded WITH their keys. One home, so the three test classes that read a
/// snapshot (the writer's, the merge's and the mode's) build the same fixture and cannot drift from each other.
/// </summary>
internal static class RoundCountsSnapshotFixtures
{
    /// <summary>The instant every merged-count test fixes the windows at.</summary>
    internal static readonly DateTime Now = new(2026, 10, 21, 9, 20, 0, DateTimeKind.Utc);

    /// <summary>The default WSL data directory — two distributions share it, which is the whole point of the id rule.</summary>
    internal const string WslDirectory = "/home/someone/.local/share/coai-mcp";

    internal static string Key(char digit) => new(digit, 32);

    /// <summary>A WSL store: its id is the one its OWNER computes, from the host as well as the directory (D1).</summary>
    internal static SnapshotStore Wsl(string distro, string directory = WslDirectory) =>
        new(RoundAuthorsMerge.SourceIdOf("linux", distro, directory), "linux", "wsl", distro, string.Empty, directory);

    internal static SnapshotReviewer Ran(string provider, string model = "gpt-6-luna") => new(provider, model);

    internal static SnapshotRound Round(long id, string key, DateTime started, string stage, params SnapshotReviewer[] reviewers) =>
        new(id, key, started.ToString("O"), stage, reviewers);

    internal static RoundCountsSnapshotDocument Document(SnapshotStore store, DateTime written, params SnapshotRound[] rounds) =>
        new(RoundCountsSnapshot.Schema, written.ToString("O"), "0.63.0", store, written.AddDays(-7).ToString("O"), false, rounds);

    /// <summary>Writes the document as its owner would, into <paramref name="folder"/>, and answers the path.</summary>
    internal static string Write(string folder, RoundCountsSnapshotDocument document, string name = RoundCountsSnapshot.FileName)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        File.WriteAllText(path, RoundCountsSnapshot.Serialize(document));

        return path;
    }

    /// <summary>Records one round into this side's database with the key and the stamps given; answers nothing — the id is the database's own.</summary>
    internal static void Record(RoundsDb db, string session, string stage, DateTime started, string key, params ReviewerState[] reviewers)
    {
        var state = new SessionState(session, "D:/repo", "feat/x", new PanelConfig()) { Stage = Stage.CodeReview };
        db.RecordRound(
            state,
            new RoundRecord(stage, 1, "proceed", 0, "answered", started) { StartedUtc = started, Key = key, ReviewerStates = [.. reviewers] },
            []);
    }

    internal static ReviewerState Reviewer(string provider, string model = "gpt-6-luna", string role = "Architecture") =>
        new(provider, role, ReviewerState.Done, 0, string.Empty, 1.0, model);

    internal static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;
}
