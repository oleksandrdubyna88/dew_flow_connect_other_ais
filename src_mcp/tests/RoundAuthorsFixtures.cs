using System.Collections.Immutable;
using CoaiMcp.Core.Authors;

namespace CoaiMcp.Tests;

/// <summary>Stored-shape <see cref="RoundAuthors"/> built by hand, for the stories that read what S3 stored rather than what S2 assigned (E4.S4).</summary>
internal static class RoundAuthorsFixtures
{
    public static AuthorRow Row(string agent, string window = WindowKind.Round, string stage = "CodeReview", int number = 1, string model = "claude-opus-5-5", string association = Association.WroteHere, int writeCalls = 1, string requested = "opus", string effort = "high") =>
        new(window, stage, number, agent == AgentKey.MainId ? "main" : "subagent", new AgentKey(agent), agent == AgentKey.MainId ? string.Empty : "general-purpose", requested, model, effort, association, 0, 1, new TokenTerms(1, 1, 1, 1), writeCalls);

    /// <summary>A round with the given rows; <paramref name="state"/> and its reason are the round-level roll-up.</summary>
    public static RoundAuthors Round(string stage, int number, string caller, string state = ObservationKinds.Observed, string reason = "", params AuthorRow[] rows) => new(
        stage,
        number,
        caller,
        state,
        reason.Length == 0 ? [] : [reason],
        [.. rows],
        ActivityTotals.Zero,
        ActivityTotals.Zero,
        ActivityTotals.Zero,
        0,
        [],
        [],
        []);

    public static RoundAuthors Observed(string stage, int number, string caller, params AuthorRow[] rows) => Round(stage, number, caller, ObservationKinds.Observed, string.Empty, rows);

    public static ImmutableArray<RoundAuthors> Journal(params RoundAuthors[] rounds) => [.. rounds];
}
