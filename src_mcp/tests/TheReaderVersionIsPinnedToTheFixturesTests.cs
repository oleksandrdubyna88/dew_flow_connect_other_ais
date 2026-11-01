using CoaiMcp.Store;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// E3.S4 test 43: <see cref="ObservedSession.ReaderVersion"/> is tied to what the reader counts over every fixture E1 cut.
/// </summary>
/// <remarks>
/// <para>A counting rule that changes any figure below changes what E5 stores, so the figures and the version move together: this test is
/// the reminder. The expected figures were taken from the first green run and checked by hand against the manifest's own counts
/// (<c>distinctMessageIds</c> excludes synthetic lines, so messages equal it case by case; <c>agentToolUses</c> counts BLOCKS, so c07's 3 is 2
/// calls). The case list is the manifest's: a case without a row, or a row without a case, fails.</para>
/// </remarks>
public sealed class TheReaderVersionIsPinnedToTheFixturesTests
{
    private const string Prefix = "testsToDelete-coai-pinned-";

    /// <summary>Messages, launches (agents other than main), writes and the four token terms summed over the messages. The writes of c12, c17 and c19 are the ones E1's re-cut (PR #851) added: 4 more than the same fixtures before it.</summary>
    private sealed record Totals(ObservationKind Kind, int Messages, int Launches, int Writes, long Input, long CacheCreation, long CacheRead, long Output);

    private static readonly IReadOnlyDictionary<string, Totals> Expected = new Dictionary<string, Totals>
    {
        ["background-agent-across-round-start"] = new(ObservationKind.Observed, 5, 1, 0, 5, 15, 10, 35),
        ["message-as-several-lines"] = new(ObservationKind.Observed, 4, 1, 0, 4, 10, 10, 11),
        ["message-last-line-below-max"] = new(ObservationKind.Observed, 4, 1, 0, 4, 10, 10, 11),
        ["meta-without-model"] = new(ObservationKind.Observed, 4, 1, 0, 4, 10, 6, 17),
        ["missing-child-transcripts"] = new(ObservationKind.Partial, 6, 2, 0, 6, 21, 15, 45),
        ["model-switch-in-window"] = new(ObservationKind.Observed, 2, 0, 1, 2, 3, 3, 3),
        ["nested-agents"] = new(ObservationKind.Observed, 7, 2, 0, 10, 28, 21, 33),
        ["no-effort-lines"] = new(ObservationKind.Observed, 1, 1, 0, 1, 1, 1, 1),
        ["no-evidence-two-repositories"] = new(ObservationKind.Observed, 1, 0, 0, 1, 1, 1, 1),
        ["persistence-off"] = new(ObservationKind.Unobserved, 0, 0, 0, 0, 0, 0, 0),
        ["refused-agent-call"] = new(ObservationKind.Observed, 1, 0, 0, 1, 1, 1, 1),
        ["registry-and-variable-disagree"] = new(ObservationKind.Observed, 2, 0, 2, 2, 3, 3, 3),
        ["replay-same-round-twice"] = new(ObservationKind.Observed, 1, 0, 1, 1, 1, 1, 1),
        ["resumed-agent"] = new(ObservationKind.Observed, 21, 1, 1, 23, 231, 192, 161),
        ["second-gate-session-same-caller"] = new(ObservationKind.Observed, 1, 0, 1, 1, 1, 1, 1),
        ["skipped-round-between-recorded"] = new(ObservationKind.Observed, 1, 0, 1, 1, 1, 1, 1),
        ["two-repositories-one-session"] = new(ObservationKind.Observed, 3, 0, 2, 3, 6, 6, 6),
        ["work-before-open"] = new(ObservationKind.Observed, 1, 0, 1, 1, 1, 1, 1),
        ["write-into-submodule-and-scratchpad"] = new(ObservationKind.Observed, 1, 0, 2, 1, 1, 1, 1),
        ["writes-only-elsewhere"] = new(ObservationKind.Observed, 1, 0, 1, 1, 1, 1, 1),
    };

    private static Totals Measured(ObservedSession session) => new(
        session.State.Kind,
        session.Messages.Count,
        Math.Max(session.Agents.Count - 1, 0),
        session.Writes.Count,
        session.Messages.Sum(m => m.Tokens.Input),
        session.Messages.Sum(m => m.Tokens.CacheCreation),
        session.Messages.Sum(m => m.Tokens.CacheRead),
        session.Messages.Sum(m => m.Tokens.Output));

    [Fact]
    public void The_reader_version_is_pinned_to_the_fixtures_totals()
    {
        const string Because = "a counting rule changed — bump ObservedSession.ReaderVersion and update these totals in the same change";
        FixtureManifest.Cases.Should().NotBeEmpty();
        Expected.Keys.Should().BeEquivalentTo(FixtureManifest.Cases.Select(c => c.Name), "every manifest case has a row and every row a case");

        foreach (var fixtureCase in FixtureManifest.Cases)
        {
            using var dir = TempDir.For(Prefix);
            var session = CallerTranscriptFixture.Materialise(fixtureCase.Name, dir).Read();

            Measured(session).Should().Be(Expected[fixtureCase.Name], fixtureCase.Name + ": " + Because);
        }

        ObservedSession.ReaderVersion.Should().Be(1, Because);
    }

    [Fact]
    public void Every_message_the_manifest_counts_is_a_message_the_reader_returns()
    {
        foreach (var fixtureCase in FixtureManifest.Cases.Where(c => c.HasTranscript))
        {
            using var dir = TempDir.For(Prefix);
            var session = CallerTranscriptFixture.Materialise(fixtureCase.Name, dir).Read();

            session.Messages.Should().HaveCount(fixtureCase.Counts.DistinctMessageIds, fixtureCase.Name);
        }
    }
}
