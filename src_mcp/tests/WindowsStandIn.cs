using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Worktrees;

namespace CoaiMcp.Tests;

/// <summary>
/// What lets a Kimi decision — a launch refusal, the owned home's allowlist, a disposal's order, the sign-in lock's key — be tested on a
/// LINUX host, where the sign-in is not a junction and the kernel cannot name a folder by its handle (E4 of
/// <c>todo/PLAN_vibe_and_kimi_runtimes_E4.md</c>; the same method as <c>IFolderShell</c> and <c>PrivateFolderWindowsDecisionTests</c> for a private folder).
/// On Windows it replaces NOTHING: the test runs on the real junction maker and the real final-path call, which is what the
/// <c>windows-latest</c> job checks.
/// </summary>
/// <remarks>
/// Off Windows it stands in for exactly the three answers the OS gives and the decisions only ASK: a "junction" is a directory symbolic link (the
/// same shape for everything that reads it — <c>OwnedPaths.IsLinkIfThere</c>, <c>Junctions.Target</c>, the deleter), a folder's final path is its
/// real spelling, and every drive is a fixed one. Per async flow, restored on dispose. The decisions run unchanged; nothing in the product reads
/// this class.
/// </remarks>
internal static class WindowsStandIn
{
    /// <summary>The stand-in for this test (nothing on Windows); dispose it when the test is over.</summary>
    public static IDisposable Begin() => OperatingSystem.IsWindows() ? new NothingStandsIn() : new Standing();

    /// <summary>The real spelling of a folder that exists, or EMPTY — what the kernel's final path is on Windows.</summary>
    internal static string RealFolder(string path) => Directory.Exists(path) ? WorktreePaths.Real(path) : string.Empty;

    private sealed class NothingStandsIn : IDisposable
    {
        public void Dispose()
        {
        }
    }

    private sealed class Standing : IDisposable
    {
        private readonly Func<string, string>? _finalBefore = KimiCredentialsLock.Final;

        public Standing()
        {
            Junctions.StandIn(new SymbolicLinks());
            KimiCredentialsLock.Final = RealFolder;
        }

        public void Dispose()
        {
            Junctions.StandIn(null);
            KimiCredentialsLock.Final = _finalBefore;
        }
    }

    private sealed class SymbolicLinks : IJunctionShell
    {
        public string FinalDirectory(string path) => RealFolder(path);

        public DriveType DriveOf(string root) => DriveType.Fixed;

        public string SetReparsePoint(string link, string target)
        {
            Directory.Delete(link);
            Directory.CreateSymbolicLink(link, target);

            return string.Empty;
        }
    }
}
