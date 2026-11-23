using System.Globalization;
using CoaiMcp.Runners.Detach;
using CoaiMcp.Runners.Platform;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>
/// The three settings of the role jobs, read from the layered settings (the environment over the data directory's settings file) — each refused naming what was legal, never silently replaced:
/// <c>COAI_ROLE_JOBS_MAX</c> (live jobs at once; 3, and 2 on WSL), <c>COAI_ROLE_MEMORY_MB</c> (the unit's <c>MemoryMax</c>; 4096) and <c>COAI_ROLE_LAUNCHER</c> (<c>auto</c>, <c>systemd-run</c>, <c>setsid</c>).
/// </summary>
internal sealed record RoleJobSettings(int MaxLiveJobs, int MemoryMegabytes, LauncherChoice Launcher)
{
    internal const string JobsMaxKey = "COAI_ROLE_JOBS_MAX";
    internal const string MemoryKey = "COAI_ROLE_MEMORY_MB";
    internal const string LauncherKey = "COAI_ROLE_LAUNCHER";

    internal const int MostLiveJobs = 20;
    internal const int LeastMemoryMegabytes = 256;
    internal const int MostMemoryMegabytes = 262_144;

    /// <summary>The settings, or the sentence that says which one is wrong and what is legal. <paramref name="onWsl"/> picks the default cap.</summary>
    public static (RoleJobSettings? Settings, string Refusal) Read(Func<string, string?> settings, bool onWsl)
    {
        var (max, maxWhy) = Whole(settings(JobsMaxKey), onWsl ? RoleJobLimits.DefaultLiveJobsOnWsl : RoleJobLimits.DefaultLiveJobs, 1, MostLiveJobs, JobsMaxKey);
        var (memory, memoryWhy) = Whole(settings(MemoryKey), RoleJobLimits.DefaultMemoryMegabytes, LeastMemoryMegabytes, MostMemoryMegabytes, MemoryKey);
        var (launcher, launcherWhy) = LauncherOf(settings(LauncherKey));
        var refusal = string.Join("; ", new[] { maxWhy, memoryWhy, launcherWhy }.Where(why => why.Length > 0));

        return refusal.Length > 0 ? (null, refusal) : (new RoleJobSettings(max, memory, launcher), string.Empty);
    }

    private static (int Value, string Refusal) Whole(string? text, int fallback, int least, int most, string key)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return (fallback, string.Empty);
        }

        return int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value >= least && value <= most
            ? (value, string.Empty)
            : (fallback, $"{key} is '{text}'; it must be a whole number from {least} to {most}");
    }

    private static (LauncherChoice Value, string Refusal) LauncherOf(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        null or "" or "auto" => (LauncherChoice.Auto, string.Empty),
        "systemd-run" => (LauncherChoice.SystemdRun, string.Empty),
        "setsid" => (LauncherChoice.Setsid, string.Empty),
        _ => (LauncherChoice.Auto, $"{LauncherKey} is '{text}'; it must be auto, systemd-run or setsid"),
    };

    /// <summary>True on a WSL host, where the default cap is lower: one distro's memory is shared with Windows.</summary>
    public static bool OnWsl => HostKinds.Current == HostKind.Wsl;
}
