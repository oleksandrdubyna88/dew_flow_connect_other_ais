using System.Globalization;
using System.Text.Json;

namespace CoaiMcp.Tests;

/// <summary>
/// E1's fixture folder as the C# tests read it: <c>manifest.json</c> (name, folder, counts, roots, checkouts) and a case's
/// <c>rounds.json</c>. Neither is a list typed in a test — the manifest IS the list of cases (E3.S4 and E3.S5 enumerate it).
/// </summary>
public static class FixtureManifest
{
    /// <summary><c>AppContext.BaseDirectory/fixtures/caller-transcripts</c> — the project copies it beside the test binary.</summary>
    public static string Folder { get; } = Path.Combine(AppContext.BaseDirectory, "fixtures", "caller-transcripts");

    public static IReadOnlyList<FixtureCase> Cases => Load();

    /// <summary>The string every text field of every fixture became — E1's value, read from <c>allowlist.json</c>, never re-typed.</summary>
    public static string Sentinel
    {
        get
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(Folder, "allowlist.json")));

            return document.RootElement.GetProperty("sentinel").GetString() ?? string.Empty;
        }
    }

    public static FixtureCase Find(string name)
    {
        var cases = Load();

        return cases.FirstOrDefault(c => c.Name == name)
            ?? throw new ArgumentException($"No fixture case named '{name}'. The known names are: {string.Join(", ", cases.Select(c => c.Name))}.");
    }

    public static FixtureRounds RoundsOf(FixtureCase fixture)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(Folder, fixture.Dir, "rounds.json")));
        var root = document.RootElement;

        return new FixtureRounds(Text(root, "callerSessionId"), [.. root.GetProperty("gateSessions").EnumerateArray().Select(Gate)]);
    }

    private static List<FixtureCase> Load()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(Folder, "manifest.json")));

        return [.. document.RootElement.GetProperty("cases").EnumerateArray().Select(Case)];
    }

    private static FixtureCase Case(JsonElement e) => new(
        Text(e, "name"),
        Text(e, "dir"),
        e.GetProperty("hasTranscript").GetBoolean(),
        Text(e, "pathStyle"),
        Strings(e, "fixtureRoots"),
        Strings(e, "checkouts"),
        Counts(e.GetProperty("counts")));

    private static FixtureCounts Counts(JsonElement e) => new(
        e.GetProperty("assistantLines").GetInt32(),
        e.GetProperty("distinctMessageIds").GetInt32(),
        e.GetProperty("agentFiles").GetInt32(),
        e.GetProperty("metas").GetInt32(),
        e.GetProperty("agentToolUses").GetInt32());

    private static FixtureGateSession Gate(JsonElement e) => new(
        Text(e, "id"),
        Text(e, "repoPath"),
        Instant(e, "openedUtc"),
        [.. e.GetProperty("rounds").EnumerateArray().Select(Round)]);

    private static FixtureRound Round(JsonElement e) => new(
        e.GetProperty("number").GetInt32(),
        Instant(e, "startedUtc"),
        Instant(e, "completedUtc"));

    private static string Text(JsonElement e, string name) => e.GetProperty(name).GetString() ?? string.Empty;

    private static List<string> Strings(JsonElement e, string name) =>
        [.. e.GetProperty(name).EnumerateArray().Select(item => item.GetString() ?? string.Empty)];

    private static DateTime Instant(JsonElement e, string name) =>
        DateTime.Parse(Text(e, name), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
}

/// <summary>One manifest entry. <see cref="Counts"/> are what the files hold: <c>AgentToolUses</c> counts BLOCKS, not calls (c07: 3 blocks, 2 calls).</summary>
public sealed record FixtureCase(
    string Name,
    string Dir,
    bool HasTranscript,
    string PathStyle,
    IReadOnlyList<string> FixtureRoots,
    IReadOnlyList<string> Checkouts,
    FixtureCounts Counts);

public sealed record FixtureCounts(int AssistantLines, int DistinctMessageIds, int AgentFiles, int Metas, int AgentToolUses);

/// <summary>A case's <c>rounds.json</c>: the caller's session id and its gate sessions. A test takes its bounds from these instants.</summary>
public sealed record FixtureRounds(string CallerSessionId, IReadOnlyList<FixtureGateSession> GateSessions);

public sealed record FixtureGateSession(string Id, string RepoPath, DateTime OpenedUtc, IReadOnlyList<FixtureRound> Rounds);

public sealed record FixtureRound(int Number, DateTime StartedUtc, DateTime CompletedUtc);
