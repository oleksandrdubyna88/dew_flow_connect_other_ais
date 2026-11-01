using CoaiMcp.Server;

namespace CoaiMcp.Tests;

/// <summary>
/// What the test run does about the machine's old <c>coai-*</c> folders at its start — the ONE entry point the assembly fixture calls
/// (<c>todo/PLAN_test_leftovers_E5_S2_spec.md</c>, E5 S2-A).
/// </summary>
/// <remarks>
/// A seam, extracted first so the tests of the retirement call the same entry before and after it: the age-only sweep (the shared
/// <c>PanelService.PruneOldScratchDirs</c> with <c>coai-*</c>) is still behind it in this commit, behaviour unchanged.
/// </remarks>
internal static class StartOfRun
{
    internal static void Run(string tempRoot, SweepRule rule) =>
        PanelService.PruneOldScratchDirs(tempRoot, DateTime.UtcNow - rule.Keeps, [$"{rule.Prefix}*"], rule.NeverSwept);
}
