using System.Runtime.InteropServices;

namespace CoaiMcp.Runners.Kimi;

/// <summary>
/// The Windows-only NATIVE SHELL of <see cref="HostProcessList"/>: the toolhelp calls, and nothing that decides. Excluded from Sonar's coverage
/// (<c>.github/workflows/sonarcloud.yml</c> says why); <c>ForeignKimiTests</c> run it against this machine's real process table in the
/// <c>windows-latest</c> job. The buffer's layout and its parse are <see cref="HostProcessList"/>'s and are tested on any OS.
/// </summary>
internal static partial class HostProcessListNative
{
    private const uint SnapProcess = 0x2;
    private const int NoMoreFiles = 18;

    internal static List<ProcessEntry> Read()
    {
        var snapshot = CreateToolhelp32Snapshot(SnapProcess, 0);
        if (snapshot is -1 or 0)
        {
            return [];
        }

        try
        {
            return Walk(snapshot);
        }
        finally
        {
            CloseHandle(snapshot);
        }
    }

    private static List<ProcessEntry> Walk(nint snapshot)
    {
        var found = new List<ProcessEntry>();
        var entry = HostProcessList.FreshEntry();
        for (var more = Process32First(snapshot, entry) != 0; more; more = Process32Next(snapshot, entry) != 0)
        {
            found.Add(HostProcessList.Parsed(entry));
        }

        return Marshal.GetLastWin32Error() is NoMoreFiles or 0 ? found : [];
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint CreateToolhelp32Snapshot(uint flags, uint processId);

    [LibraryImport("kernel32.dll", EntryPoint = "Process32FirstW", SetLastError = true)]
    private static partial int Process32First(nint snapshot, Span<byte> entry);

    [LibraryImport("kernel32.dll", EntryPoint = "Process32NextW", SetLastError = true)]
    private static partial int Process32Next(nint snapshot, Span<byte> entry);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int CloseHandle(nint handle);
}
