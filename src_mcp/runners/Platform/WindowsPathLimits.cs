namespace CoaiMcp.Runners.Platform;

/// <summary>
/// The longest absolute paths Windows accepts without long-path support, in UTF-16 units — the unit Windows counts,
/// never bytes. The one place these numbers live (<c>.agents/conventions/common/platform-limits.md</c> §1 item 4;
/// <c>research/PLAN_one_windows_path_limit.md</c>): every guard and every sentence that names one reads it here.
/// </summary>
/// <remarks>
/// Windows' numbers only. Which platform a caller is on stays the caller's decision (<c>VibePathBudget.ForThisProcess</c>,
/// <c>WorktreeFailure</c>'s <see cref="OperatingSystem.IsWindows"/>), and no other platform inherits these.
/// </remarks>
internal static class WindowsPathLimits
{
    /// <summary>
    /// The longest FILE path: 259 UTF-16 units were created, 260 were refused (probes P1, P2 of
    /// <c>research/PLAN_review_trees_survive_long_paths.md</c>; the same 259 with Vibe's own interpreter,
    /// <c>research/RESULTS_vibe_confinement.md</c> §27).
    /// </summary>
    internal const int FileUnits = 259;

    /// <summary>
    /// The longest FOLDER path — 12 units short of a file's: git for Windows made a 247-unit folder and refused 248
    /// (probed 2026-10-17, git 2.55), the same 247 <c>research/PLAN_vibe_live_round_fixes.md</c> measured with Python.
    /// </summary>
    internal const int FolderUnits = 247;
}
