namespace CoaiMcp.Core.Rounds;

/// <summary>
/// The base a feature review was opened against, held to on every later call of the same plan.
/// </summary>
/// <remarks>
/// <para>A feature session is keyed by the plan's path (D13), and a path alone cannot tell two release
/// trains of one plan apart: the same plan reviewed on a release branch, a backport, a caller who
/// mistyped the ref. Continuing would silently inherit the other review's rejections and budget. So
/// the session records the resolved base of its first round (<c>PersistedSession.FeatureBase</c>) and
/// a call naming another one is refused with a sentence naming BOTH — and the door, because a
/// refusal with no door is a stall: <c>again: true</c> starts a fresh review against the new base —
/// its count and its standing rejections start over (<c>RoundMachine.FreshFeatureReview</c>, applied by
/// the engine with the round that runs) — and leaves the earlier rounds on the record. (The plan round,
/// 2026-09-25.)</para>
/// <para>Pure, so the rule is a unit test; the stage resolves the SHAs and hands them in.</para>
/// </remarks>
public static class FeatureBases
{
    /// <summary>Whether <paramref name="asked"/> names a base other than the recorded one — never before any round recorded one.</summary>
    public static bool IsAnother(string recorded, string asked) =>
        recorded.Length > 0 && !string.Equals(recorded, asked, StringComparison.OrdinalIgnoreCase);

    /// <param name="recorded">The base the session recorded, or empty before any round recorded one.</param>
    /// <param name="asked">The base this call resolved.</param>
    /// <param name="again">Whether the caller asked for a fresh review — the door.</param>
    /// <returns>Why this call may not continue the recorded review, or empty when it may.</returns>
    public static string WhyNot(string recorded, string asked, bool again) =>
        again || !IsAnother(recorded, asked)
            ? string.Empty
            : $"this plan's feature review was opened against base {recorded}, and this call names base {asked} — "
              + "a different base is a different review (a release branch, a backport, a mistyped ref), and "
              + "continuing would inherit the other review's rejections and budget. Pass again: true to start a "
              + $"fresh review against {asked}; the earlier rounds stay on the record.";
}
