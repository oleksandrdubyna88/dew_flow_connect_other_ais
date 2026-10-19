using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using CoaiMcp.Core.Authors;
using CoaiMcp.Server;
using CoaiMcp.Store;

namespace CoaiMcp.Tests;

/// <summary>
/// E1's <c>rounds.json</c> as attribution inputs (E4.S5). The file is a fixture format, not E5's session file, so a loader turns it into
/// <see cref="RoundRecord"/>s and hands them to the PRODUCTION mapping — <see cref="AttributionMapping.Timeline"/> — so the rule that
/// a completion counts only on a <c>done</c> row runs on the data too. It decides nothing else.
/// </summary>
/// <remarks>
/// <para><b>What the loader assumes, and says.</b> <c>rounds.json</c> has no stage, no verdict and no caller per round: every round is a
/// <c>CodeReview</c> round with an empty verdict, served by the file's <c>callerSessionId</c>; <c>status</c> and <c>completedUtc</c> are
/// copied as given. Every gate session of the file is a peer of every other, and <c>PeersKnown</c> is true — the file is taken as the
/// caller's COMPLETE set of gate sessions. The repository paths are the fixture roots re-pointed into the materialised folder.</para>
/// </remarks>
internal static class AttributionFixtureLoader
{
    private const string FixturePrefix = "D:/fixture";

    public static IReadOnlyList<GateTimeline> Timelines(CallerTranscriptFixture fixture, Func<IReadOnlyList<RoundRecord>, IReadOnlyList<RoundRecord>>? shape = null)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(FixtureManifest.Folder, fixture.Case.Dir, "rounds.json")));
        var caller = fixture.Rounds.CallerSessionId;

        return [.. document.RootElement.GetProperty("gateSessions").EnumerateArray().Select(gate => Timeline(fixture, gate, caller, shape))];
    }

    /// <summary>The input for one round of one gate session: the others are its peers, the universe is E5's rule over the roots.</summary>
    public static AttributionInput Input(CallerObservation observation, IReadOnlyList<GateTimeline> timelines, string gate, int number)
    {
        var mine = timelines.Single(timeline => timeline.GateSessionId == gate);
        var peers = timelines.Where(timeline => timeline.GateSessionId != gate).ToImmutableArray();

        return new AttributionInput(observation, mine, peers, AttributionMapping.Universe(mine, peers, observation), "CodeReview", number, PeersKnown: true);
    }

    /// <summary>One synthetic round after the case's last instant — the one labelled test-side extension (c01, c14): start one second later, completion one second after that.</summary>
    public static RoundRecord Extension(IReadOnlyList<RoundRecord> rounds, DateTime lastInstant) =>
        new("CodeReview", rounds.Max(round => round.Number) + 1, string.Empty, 0, string.Empty, lastInstant.AddSeconds(2))
        {
            Status = RoundRecord.Done,
            StartedUtc = lastInstant.AddSeconds(1),
        };

    private static GateTimeline Timeline(CallerTranscriptFixture fixture, JsonElement gate, string caller, Func<IReadOnlyList<RoundRecord>, IReadOnlyList<RoundRecord>>? shape)
    {
        var rounds = gate.GetProperty("rounds").EnumerateArray().Select(Record).ToList();
        var shaped = shape is null ? rounds : shape(rounds);

        return AttributionMapping.Timeline(
            gate.GetProperty("id").GetString() ?? string.Empty,
            fixture.Root + gate.GetProperty("repoPath").GetString()![FixturePrefix.Length..],
            Instant(gate, "openedUtc"),
            shaped,
            _ => caller);
    }

    private static RoundRecord Record(JsonElement round) =>
        new("CodeReview", round.GetProperty("number").GetInt32(), string.Empty, 0, string.Empty, Instant(round, "completedUtc"))
        {
            Status = round.GetProperty("status").GetString() ?? string.Empty,
            StartedUtc = Instant(round, "startedUtc"),
        };

    private static DateTime Instant(JsonElement element, string name) =>
        DateTime.Parse(element.GetProperty(name).GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
}
