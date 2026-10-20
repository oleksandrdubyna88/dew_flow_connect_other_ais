using System.Text.Json;
using CoaiMcp.Core.Authors;

namespace CoaiMcp.Tests;

/// <summary>Short ways for a test to say what one assignment holds, in the minutes of <see cref="AttributionScenario"/>.</summary>
internal static class AssignmentViews
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public static double Minutes(DateTimeOffset at) => (at - AttributionScenario.Base).TotalMinutes;

    public static double? Minutes(DateTimeOffset? at) => at is { } instant ? Minutes(instant) : null;

    /// <summary>The placement of the message <paramref name="agent"/> sent at <paramref name="minute"/>.</summary>
    public static Placement Msg(this WindowAssignment assignment, string agent, double minute) =>
        assignment.Messages.Single(placement => placement.Agent.Id == agent && Minutes(placement.At) == minute);

    public static string Label(this WindowAssignment assignment, string agent, double minute) => assignment.Msg(agent, minute).Association;

    /// <summary>The launch placement of <paramref name="agent"/> (there is at most one per window an agent launches in).</summary>
    public static LaunchPlacement Launch(this WindowAssignment assignment, string agent) =>
        assignment.Launches.Single(placement => placement.Agent.Id == agent);

    /// <summary>The messages this assignment OWNS, as <c>agent@minute</c>, by minute and then agent.</summary>
    public static IReadOnlyList<string> OwnedMessages(this WindowAssignment assignment) =>
        [.. assignment.Messages.Where(placement => Association.IsOwning(placement.Association)).OrderBy(placement => placement.At).ThenBy(placement => placement.Agent.Id, StringComparer.Ordinal).Select(Key)];

    public static IReadOnlyList<string> Labelled(this WindowAssignment assignment, string association) =>
        [.. assignment.Messages.Where(placement => placement.Association == association).OrderBy(placement => placement.At).ThenBy(placement => placement.Agent.Id, StringComparer.Ordinal).Select(Key)];

    public static IReadOnlyList<string> OwnedLaunches(this WindowAssignment assignment) =>
        [.. assignment.Launches.Where(placement => Association.IsOwning(placement.Association)).Select(placement => placement.Agent.Id).Order(StringComparer.Ordinal)];

    public static string Key(Placement placement) => placement.Agent.Id + "@" + Minutes(placement.At);

    public static Window RoundWindow(this WindowAssignment assignment) => assignment.Windows.Single(window => window.Kind == WindowKind.Round);

    public static Window BeforeOpenWindow(this WindowAssignment assignment) => assignment.Windows.Single(window => window.Kind == WindowKind.BeforeOpen);

    public static WindowState StateOf(this WindowAssignment assignment, Window window) => assignment.States.Single(state => state.Window == window);

    public static WindowState RoundState(this WindowAssignment assignment) => assignment.StateOf(assignment.RoundWindow());

    public static AgentAssociation AssociationOf(this WindowAssignment assignment, string agent, Window window) =>
        assignment.Agents.Single(association => association.Agent.Id == agent && association.Window == window);

    /// <summary><c>[from, to)</c> in minutes; null is unbounded.</summary>
    public static (double? From, double? To) Bounds(this Window window) => (Minutes(window.From), Minutes(window.To));

    /// <summary>A pair of bounds, typed as <see cref="Bounds"/> returns them, so an expectation reads <c>Be(Span(0, 60))</c>.</summary>
    public static (double? From, double? To) Span(double? from, double? to) => (from, to);

    /// <summary>The authors of one round of a scenario: build the input, assign, aggregate.</summary>
    public static RoundAuthors AuthorsOf(this AttributionScenario scenario, string gate, string stage, int number)
    {
        var input = scenario.Build(gate, stage, number);

        return RoundAuthorsBuilder.Build(input, RoundAttribution.Assign(input));
    }

    /// <summary>One row in the words a test asserts: window, agent, model, effort, association, launches, turns, write calls.</summary>
    public static string Show(this AuthorRow row) =>
        $"{row.Window}|{row.Agent.Id}|{row.Model}|{row.Effort}|{row.Association}|L{row.Launches}|T{row.Turns}|W{row.WriteCalls}";

    /// <summary>The same assignment as text — a record holding an <c>ImmutableArray</c> prints the array's type name, so equality by value is judged on this.</summary>
    public static string AsText(this WindowAssignment assignment) => JsonSerializer.Serialize(assignment, Json);

    /// <summary>The assignment with the peers it saw left out: the one thing a new peer gate is allowed to change (§S2.2b G3 iii).</summary>
    public static string AsTextWithoutPeerGates(this WindowAssignment assignment) => (assignment with { PeerGates = [] }).AsText();
}
