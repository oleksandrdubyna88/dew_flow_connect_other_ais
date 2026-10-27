using System.Collections.Immutable;
using CoaiMcp.Core.Authors;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using CoaiMcp.Store;

namespace CoaiMcp.Tests;

/// <summary>
/// Hand-built <see cref="AuthorsSnapshot"/>s for the stories that store, log and show what E5 keeps (E5.S2–S5): the same shapes in every
/// test class, so the writer, the reader and the page are proved against one snapshot rather than against four near-copies of it.
/// </summary>
internal static class AuthorsSnapshotFixtures
{
    public const string Caller = "11111111-1111-1111-1111-111111111111";

    public static ActivityTotals Totals(int agents) =>
        new(agents, agents * 2, agents * 3, new TokenTerms(agents * 10, agents * 20, agents * 30, agents * 40));

    public static AuthorRow Row(string agent, string model, string association, string window = WindowKind.Round, string stage = "CodeReview", int number = 1) =>
        new(window, stage, number, agent == AgentKey.MainId ? "main" : "subagent", new AgentKey(agent), agent == AgentKey.MainId ? string.Empty : "general-purpose",
            "opus", model, "high", association, 1, 2, new TokenTerms(5, 6, 7, 8), 3);

    /// <summary>Three rows — main, a coder that wrote here, a child before the session opened — and totals that are all distinct, so a swapped column shows.</summary>
    public static AuthorsSnapshot Observed(string stage = "CodeReview", int number = 1, string session = Caller) =>
        new(stage, number, session, ObservationKinds.Observed, [],
            [Row(AgentKey.MainId, "claude-opus-5-5", Association.SameSession, stage: stage, number: number),
             Row("a1", "claude-sonnet-5-5", Association.WroteHere, stage: stage, number: number),
             Row("a2", "claude-sonnet-5-5", Association.ChildOf, WindowKind.BeforeOpen, stage, number)],
            Totals(1), Totals(2), Totals(3), 4,
            [new WindowAuthors(WindowKind.BeforeOpen, ObservationKinds.Partial, [AttributionReasons.PeerRunning], Totals(1), Totals(2), Totals(3), 4)],
            ["peer-gate-1"],
            CallerSessionId.Registry, ObservedSession.ReaderVersion, RoundAttribution.Version, 1, 2);

    /// <summary>The same round without a transcript: nothing measured, so every count is -1 and the two versions say which component ran.</summary>
    public static AuthorsSnapshot Unobserved(string stage = "CodeReview", int number = 1, string reason = ObservationReasons.TranscriptNotFound) =>
        AuthorsSnapshot.Unknown(stage, number, string.Empty, ObservationKinds.Unobserved, [reason], CallerSessionId.Registry, 0, 0);

    public static RoundRecord Round(string stage, int number, AuthorsSnapshot? authors = null, DateTime? started = null) =>
        new(stage, number, "proceed", 0, "answered", (started ?? new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc)).AddMinutes(number))
        {
            StartedUtc = (started ?? new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc)).AddMinutes(number - 1),
            Authors = authors,
        };

    public static SessionState State(string id = "s1", string repo = "D:/repo", Stage stage = Stage.CodeReview) =>
        new(id, repo, "feat/x", new PanelConfig()) { Stage = stage };

    public static ImmutableArray<string> Reasons(params string[] reasons) => [.. reasons];
}
