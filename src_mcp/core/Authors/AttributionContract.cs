using System.Collections.Immutable;

namespace CoaiMcp.Core.Authors;

/// <summary>
/// The ways an <see cref="AttributionInput"/> contradicts the contract it is read under (E4.S1). Pure: every rule is a function of
/// the input and returns the violations it finds, in a fixed order, so equal inputs are refused with equal words.
/// </summary>
/// <remarks>
/// An agent whose parent is empty — E3's <c>ParentKey = ""</c> for an agent file without a meta — or names an agent that is not
/// declared is NOT a violation: it has no parent and propagates nothing. <c>main</c> is always declared; no reader lists it.
/// </remarks>
internal static class AttributionContract
{
    private const string Recorded = "PlanReview or CodeReview";

    public static ImmutableArray<string> Violations(AttributionInput input) =>
    [
        .. AttributedRound(input),
        .. Journal(input.Gate),
        .. input.Peers.SelectMany(Journal),
        .. Agents(input.Caller),
        .. Messages(input.Caller),
        .. Writes(input.Caller),
    ];

    private static IEnumerable<string> AttributedRound(AttributionInput input)
    {
        var round = input.Gate.Rounds.FirstOrDefault(candidate => candidate.Stage == input.Stage && candidate.Number == input.Number);
        if (round is null)
        {
            return [$"round {input.Stage} {input.Number} is not in the gate's journal"];
        }

        return [.. NotSettled(round), .. WrongCaller(input.Caller, round)];
    }

    private static IEnumerable<string> NotSettled(GateRound round) =>
        Windows.IsSettled(round) ? [] : [$"round {round.Stage} {round.Number} is not recorded and settled (stage {Recorded}, not skipped, done)"];

    private static IEnumerable<string> WrongCaller(CallerObservation caller, GateRound round) =>
        caller.CallerSessionId == round.CallerSessionId
            ? []
            : [$"the observation's caller '{caller.CallerSessionId}' is not the caller '{round.CallerSessionId}' of round {round.Stage} {round.Number}"];

    private static IEnumerable<string> Journal(GateTimeline gate) => [.. Twice(gate), .. OutOfOrder(gate)];

    private static IEnumerable<string> Twice(GateTimeline gate) =>
        gate.Rounds.GroupBy(round => (round.Stage, round.Number)).Where(group => group.Count() > 1)
            .Select(group => $"gate '{gate.GateSessionId}' lists round {group.Key.Stage} {group.Key.Number} twice (listed twice)");

    private static IEnumerable<string> OutOfOrder(GateTimeline gate)
    {
        var starts = gate.Rounds.Where(Windows.IsRecorded).Select(round => round.StartedUtc).OfType<DateTimeOffset>().ToList();

        return starts.Zip(starts.Skip(1)).Any(pair => pair.Second < pair.First)
            ? [$"gate '{gate.GateSessionId}' has recorded rounds out of journal order (their starts decrease)"]
            : [];
    }

    private static IEnumerable<string> Agents(CallerObservation caller) =>
        [.. Declared(caller), .. NoFirstLine(caller), .. Loops(caller)];

    private static IEnumerable<string> Declared(CallerObservation caller) =>
        caller.Agents.GroupBy(agent => agent.Key.Id, StringComparer.Ordinal).Where(group => group.Count() > 1)
            .Select(group => $"agent '{group.Key}' is declared twice");

    private static IEnumerable<string> NoFirstLine(CallerObservation caller) =>
        caller.Agents.Where(agent => agent.TranscriptPresent && agent.FirstLineAt is null)
            .Select(agent => $"agent '{agent.Key.Id}' has a transcript but no first line");

    private static IEnumerable<string> Loops(CallerObservation caller)
    {
        var parents = caller.Agents.GroupBy(agent => agent.Key.Id, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First().Parent.Id, StringComparer.Ordinal);

        return caller.Agents.Select(agent => agent.Key.Id).Distinct(StringComparer.Ordinal).Where(id => !Climbs(id, parents))
            .Select(id => $"agent '{id}' has a parent chain that does not end at main or at an empty parent");
    }

    /// <summary>Whether the chain of parents from <paramref name="id"/> ends: at main, at an empty parent or at one not declared.</summary>
    private static bool Climbs(string id, Dictionary<string, string> parents)
    {
        var at = id;
        for (var steps = 0; steps <= parents.Count; steps++)
        {
            var parent = parents.GetValueOrDefault(at, string.Empty);
            if (parent.Length == 0 || parent == AgentKey.MainId)
            {
                return true;
            }

            at = parent;
        }

        return false;
    }

    private static IEnumerable<string> Messages(CallerObservation caller) => [.. UndeclaredMessages(caller), .. RepeatedMessages(caller)];

    private static IEnumerable<string> UndeclaredMessages(CallerObservation caller) =>
        caller.Messages.Where(message => !IsDeclared(caller, message.Agent))
            .Select(message => $"message '{message.MessageId}' names agent '{message.Agent.Id}', which is not declared");

    private static IEnumerable<string> RepeatedMessages(CallerObservation caller) =>
        caller.Messages.GroupBy(message => message.MessageId, StringComparer.Ordinal).Where(group => group.Count() > 1)
            .Select(group => $"message id '{group.Key}' appears twice");

    private static IEnumerable<string> Writes(CallerObservation caller) => [.. UndeclaredWrites(caller), .. Early(caller)];

    private static IEnumerable<string> UndeclaredWrites(CallerObservation caller) =>
        caller.Writes.Where(write => !IsDeclared(caller, write.Agent))
            .Select(write => $"a write names agent '{write.Agent.Id}', which is not declared");

    private static IEnumerable<string> Early(CallerObservation caller) =>
        caller.Writes.Where(write => write.AnsweredAt < write.At)
            .Select(write => $"a write by '{write.Agent.Id}' was answered before its message");

    private static bool IsDeclared(CallerObservation caller, AgentKey key) =>
        key.IsMain || caller.Agents.Any(agent => agent.Key == key);
}
