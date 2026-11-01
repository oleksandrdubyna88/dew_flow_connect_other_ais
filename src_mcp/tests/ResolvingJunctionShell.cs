using CoaiMcp.Runners.Files;

namespace CoaiMcp.Tests;

/// <summary>
/// The operating system as a test needs it for decision j of E5.3 (<c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>): every answer the
/// real shell gives, except where the FINAL PATH of one folder leads — a network path is always this seam's answer, never a real target.
/// Install it with <c>Junctions.StandIn</c> around the call under test and put the previous shell back after.
/// </summary>
internal sealed class ResolvingJunctionShell(IJunctionShell inner, string folder, string leadsTo) : IJunctionShell
{
    public string FinalDirectory(string path) =>
        Junctions.SameFolder(path, folder) ? leadsTo : inner.FinalDirectory(path);

    public DriveType DriveOf(string root) => inner.DriveOf(root);

    public string MakeLink(string link, string target) => inner.MakeLink(link, target);
}
