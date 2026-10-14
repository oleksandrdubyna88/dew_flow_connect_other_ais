namespace CoaiMcp.Runners.Consultation;

/// <summary>
/// Where a consultant CLI's coai-owned conversation homes live: one folder per runtime under the person's local
/// application data, one sub-folder per conversation, named by its consultation id (D17, D19 and D20 of
/// <c>todo/PLAN_vibe_and_kimi_runtimes.md</c>).
/// </summary>
/// <remarks>
/// <para>One list, read by both sides of the folder's life: the adapter names its folder
/// (<see cref="IConsultantRuntime.HomesFolder"/>), and the home sweep walks every folder here — Kimi's included before
/// any Kimi adapter exists, so a home left by a later build is never outside the sweep.</para>
/// <para>Short names on purpose: a deep Vibe home broke on a path over 260 characters
/// (research/RESULTS_vibe_and_kimi_first_probe.md §4).</para>
/// </remarks>
public static class ConsultantHomes
{
    /// <summary>Vibe's (D17): <c>&lt;local data&gt;/coai-vibe/&lt;consultation id&gt;</c>.</summary>
    public const string Vibe = "coai-vibe";

    /// <summary>Kimi's (D19), whose homes carry a <c>credentials</c> junction to the person's own folder.</summary>
    public const string Kimi = "coai-kimi";

    /// <summary>Every folder the home sweep walks (D20).</summary>
    public static IReadOnlyList<string> All { get; } = [Vibe, Kimi];

    /// <summary>A conversation's folder: <paramref name="root"/> / <paramref name="homes"/> / <paramref name="consultationId"/>.</summary>
    public static string ConversationFolder(string root, string homes, string consultationId) =>
        Path.Combine(root, homes, consultationId);
}
