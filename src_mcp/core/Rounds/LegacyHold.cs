using System.Text.Json;

namespace CoaiMcp.Core.Rounds;

/// <summary>
/// A held session an older build left on disk — read, never written by the v2 round machine.
/// </summary>
/// <remarks>
/// <para>v2 has no held gate: <c>HumanGate</c>, <c>HoldQuestions</c> and the policy members are no longer fields of
/// <see cref="SessionState"/> or <see cref="PanelConfig"/>. A session file that still carries them loads (they land in
/// the <c>Unknown</c> member of each record, which a save writes back), and this type reads them from there so the
/// v2 migration can find the sessions it has to reset. It is deleted with the migration in 2.1.</para>
/// </remarks>
public static class LegacyHold
{
    /// <summary>The member an older build wrote on a held session.</summary>
    public const string HumanGateMember = "humanGate";

    /// <summary>The members an older build wrote for a hold, all of which a release drops.</summary>
    public static readonly IReadOnlyList<string> Members = [HumanGateMember, "holdQuestions", "requestQuestions", "escalationsUsed"];

    /// <summary>The single eligibility test: an older build held this session, and nobody answered the hold.</summary>
    public static bool Stands(SessionState s) =>
        s.Unknown is { } unknown
        && unknown.TryGetValue(HumanGateMember, out var held)
        && held.ValueKind == JsonValueKind.True;

    /// <summary>The session as v2 reads it: nothing held, the stage advances on the next resolve.</summary>
    /// <remarks>
    /// <c>AwaitingResolve</c> and <c>AdvanceOnResolve</c> are set together, whether or not the held round had been resolved: the next
    /// <c>resolve</c> consumes both, through the one path that records the decisions. The stage is not moved here.
    /// </remarks>
    public static SessionState Released(SessionState s)
    {
        var unknown = s.Unknown is null ? null : new Dictionary<string, JsonElement>(s.Unknown);
        foreach (var member in Members)
        {
            unknown?.Remove(member);
        }

        return s with { AwaitingResolve = true, AdvanceOnResolve = true, Unknown = unknown is { Count: > 0 } ? unknown : null };
    }
}
