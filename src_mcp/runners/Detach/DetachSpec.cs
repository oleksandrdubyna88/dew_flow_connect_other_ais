namespace CoaiMcp.Runners.Detach;

/// <summary>
/// What a detached runner is started with: the program, its arguments, where it runs, the name its unit gets and its memory ceiling.
/// </summary>
/// <param name="UnitName">The systemd unit name, without the <c>.service</c> suffix — only the <c>systemd-run</c> road uses it.</param>
/// <param name="MemoryMegabytes">The unit's <c>MemoryMax</c>, in MiB — only the <c>systemd-run</c> road can enforce it (<c>v2/spikes/RESULTS_detach.md</c> §5).</param>
public sealed record DetachSpec(
    string Executable,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    string UnitName,
    int MemoryMegabytes);

/// <summary>Which launcher the caller wants. <see cref="Auto"/> tries them in the recorded order; the other two take that one road and refuse rather than fall back.</summary>
public enum LauncherChoice
{
    Auto = 0,
    SystemdRun,
    Setsid,
}

/// <summary>
/// How a detach ended. <see cref="Launcher"/> is the road that worked — recorded in the job so a reader knows what survives what (<c>systemd-run</c>,
/// <c>setsid</c>, <c>sh-nohup</c>, <c>windows</c>, <c>windows-in-job</c>) — and empty when none did; <see cref="Detail"/> then says what each road answered.
/// </summary>
public sealed record DetachResult(bool Started, string Launcher, string Unit, string Detail);
