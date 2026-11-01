using System.Text.Json;
using CoaiMcp.Core.Authors;
using CoaiMcp.Server;
using CoaiMcp.Store;

namespace CoaiMcp.Tests;

/// <summary>
/// One of E1's fixture cases, materialised and read through E3's REAL reader, mapped through the PRODUCTION <see cref="AttributionMapping"/>
/// (E4.S5), and ready to be attributed. The clock is a rank clock: instants are an order, so no test asserts a duration, a date or a
/// token magnitude read from here.
/// </summary>
internal sealed class RecordedSession : IDisposable
{
    private const string Prefix = "testsToDelete-coai-recorded-";

    private RecordedSession(TempDir dir, CallerTranscriptFixture fixture, ObservedSession session, IReadOnlyList<GateTimeline> timelines)
    {
        Dir = dir;
        Fixture = fixture;
        Session = session;
        Timelines = timelines;
        Observation = AttributionMapping.Observation(session);
    }

    public TempDir Dir { get; }

    public CallerTranscriptFixture Fixture { get; }

    public ObservedSession Session { get; }

    public CallerObservation Observation { get; }

    public IReadOnlyList<GateTimeline> Timelines { get; }

    /// <summary>Materialises the case in a <c>testsToDelete-</c> folder the caller disposes; <paramref name="shape"/> may reshape every gate's journal (the one test-side extension).</summary>
    public static RecordedSession Open(string dirName, Func<IReadOnlyList<RoundRecord>, IReadOnlyList<RoundRecord>>? shape = null, Func<CallerTranscriptFixture, DateTime?>? truncateAfter = null)
    {
        var dir = TempDir.For(Prefix);
        var name = FixtureManifest.Cases.Single(candidate => candidate.Dir == dirName).Name;
        var fixture = CallerTranscriptFixture.Materialise(name, dir);
        if (truncateAfter?.Invoke(fixture) is { } instant)
        {
            FixtureTruncation.After(fixture, instant);
        }

        var session = fixture.Read();

        return new RecordedSession(dir, fixture, session, AttributionFixtureLoader.Timelines(fixture, shape));
    }

    public AttributionInput Input(string gate, int number) => AttributionFixtureLoader.Input(Observation, Timelines, gate, number);

    /// <summary>Every (gate, round) of the file, in journal order.</summary>
    public IEnumerable<(string Gate, int Number)> Rounds() => Timelines.SelectMany(timeline => timeline.Rounds.Select(round => (timeline.GateSessionId, round.Number)));

    public WindowAssignment Assign(string gate, int number) => RoundAttribution.Assign(Input(gate, number));

    public RoundAuthors Authors(string gate, int number)
    {
        var input = Input(gate, number);

        return RoundAuthorsBuilder.Build(input, RoundAttribution.Assign(input));
    }

    /// <summary>The instant of the case's largest stamp, which the test-side extension round starts one second after.</summary>
    public DateTime LastInstant => Fixture.LargestStamp();

    public void Dispose() => Dir.Dispose();

    /// <summary>A serialised value with this materialisation's temporary root named <c>&lt;root&gt;</c>, so two materialisations compare by what they say.</summary>
    public string Text(object value) =>
        JsonSerializer.Serialize(value).Replace(JsonEscaped(Fixture.Root), "<root>", StringComparison.OrdinalIgnoreCase).Replace(JsonEscaped(Fixture.Root.Replace('\\', '/')), "<root>", StringComparison.OrdinalIgnoreCase);

    private static string JsonEscaped(string text) => JsonSerializer.Serialize(text)[1..^1];
}
