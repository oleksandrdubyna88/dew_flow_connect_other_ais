using System.Collections.Immutable;

namespace CoaiMcp.Core.Authors;

/// <summary>
/// Identity 1 of E4 §S2.5 — local completeness: for each non-blind window of an assignment, the messages, launches and writes the
/// input holds that fall in it are EXACTLY the ones placed in it, each once. A placement outside its window is named too. An
/// unobserved caller places nothing and is expected to place nothing.
/// </summary>
internal static class CompletenessCheck
{
    public static ImmutableArray<string> Violations(WindowAssignment assignment, AttributionInput input) =>
    [
        .. assignment.Windows.Where(window => !window.Blind).SelectMany(window => Messages(assignment, input, window).Concat(Launches(assignment, input, window)).Concat(Writes(assignment, input, window))),
        .. assignment.Messages.Where(placement => !placement.Window.Contains(placement.At)).Select(placement => $"message '{placement.MessageId}' is placed outside its window"),
    ];

    private static bool Expects(AttributionInput input) => input.Caller.State.Kind != ObservationKinds.Unobserved;

    private static IEnumerable<string> Messages(WindowAssignment assignment, AttributionInput input, Window window) =>
        Differences("message", window,
            Expects(input) ? input.Caller.Messages.Where(message => window.Contains(message.At)).Select(message => message.MessageId) : [],
            assignment.Messages.Where(placement => placement.Window == window).Select(placement => placement.MessageId));

    private static IEnumerable<string> Launches(WindowAssignment assignment, AttributionInput input, Window window) =>
        Differences("launch", window,
            Expects(input) ? input.Caller.Agents.Where(agent => agent.TranscriptPresent && agent.FirstLineAt is { } at && window.Contains(at)).Select(agent => agent.Key.Id) : [],
            assignment.Launches.Where(placement => placement.Window == window).Select(placement => placement.Agent.Id));

    private static IEnumerable<string> Writes(WindowAssignment assignment, AttributionInput input, Window window) =>
        Differences("write", window,
            Expects(input) ? input.Caller.Writes.Where(write => window.Contains(write.At) && window.CompletedUtc is { } horizon && write.AnsweredAt < horizon).Select(WriteKey) : [],
            assignment.Writes.Where(placement => placement.Window == window).Select(placement => placement.Agent.Id + "@" + placement.At.UtcTicks));

    private static string WriteKey(ObservedWrite write) => write.Agent.Id + "@" + write.At.UtcTicks;

    /// <summary>The ids that differ between what the input holds and what was placed, as a multiset: a missing one, an extra one, a twice-placed one.</summary>
    private static IEnumerable<string> Differences(string what, Window window, IEnumerable<string> expected, IEnumerable<string> placed)
    {
        var wanted = expected.GroupBy(id => id, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var found = placed.GroupBy(id => id, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        return wanted.Keys.Union(found.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Where(id => wanted.GetValueOrDefault(id) != found.GetValueOrDefault(id))
            .Select(id => $"{what} '{id}' is in window {window.Gate}/{window.Kind} {window.Stage} {window.Number} {wanted.GetValueOrDefault(id)} time(s) in the input and {found.GetValueOrDefault(id)} placed");
    }
}
