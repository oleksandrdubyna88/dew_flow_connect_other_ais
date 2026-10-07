namespace CoaiMcp.Server;

/// <summary>
/// How often a serving process does its background work — the two settings an idle server's cost depends on
/// (<c>todo/PLAN_an_idle_server_is_idle_and_starts_at_once.md</c>, 2026-10-06).
/// </summary>
/// <param name="SweepEvery">
/// <c>COAI_SWEEP_SECONDS</c>, default 60: the beat that closes idle consultations and retires old records. Clamped to
/// 10–3 600, and zero, negative or junk is the default — a zero interval would be a busy loop (plan round 1, codex).
/// </param>
/// <param name="ConsultantsReuse">
/// <c>COAI_CONSULTANTS_REUSE_SECONDS</c>, default 300: how long one start's consultants survey stands for every other
/// server on the same data directory, build and settings (<see cref="ConsultantsSurveyClaim"/>). Clamped to 30–86 400.
/// </param>
public sealed record ServerPace(TimeSpan SweepEvery, TimeSpan ConsultantsReuse)
{
    public const string SweepKey = "COAI_SWEEP_SECONDS";

    public const string ConsultantsReuseKey = "COAI_CONSULTANTS_REUSE_SECONDS";

    public static ServerPace Default { get; } = new(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(300));

    /// <summary>The longest consultants window any identity may use — what a claim is pruned by.</summary>
    public const int LongestConsultantsReuseSeconds = 86_400;

    /// <remarks>
    /// Read through <see cref="PanelSettings.IntVar"/> like every other count — zero, a negative or junk is the default —
    /// and then clamped. Read once per start: unlike the settings a tool call reads, a changed interval applies from the
    /// next start of the server.
    /// </remarks>
    public static ServerPace From(Func<string, string?> env) => new(
        Seconds(env, SweepKey, Default.SweepEvery, 10, 3_600),
        Seconds(env, ConsultantsReuseKey, Default.ConsultantsReuse, 30, LongestConsultantsReuseSeconds));

    private static TimeSpan Seconds(Func<string, string?> env, string key, TimeSpan fallback, int least, int most) =>
        TimeSpan.FromSeconds(Math.Clamp(PanelSettings.IntVar(env, key, (int)fallback.TotalSeconds), least, most));
}
