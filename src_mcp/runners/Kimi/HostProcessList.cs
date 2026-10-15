using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace CoaiMcp.Runners.Kimi;

/// <summary>
/// The host's process listing for <see cref="ForeignKimiWatch"/>: Windows' toolhelp snapshot (<c>CreateToolhelp32Snapshot</c>), which names every
/// process and its parent to any caller of the same user with no elevated rights. READ-ONLY — it lists, it never opens a process. Empty on any other
/// host (the shim refuses Kimi there, D18), on a 32-bit process, and when the snapshot cannot be taken.
/// </summary>
/// <remarks>
/// The record is read from a plain byte buffer at the documented offsets of <c>PROCESSENTRY32W</c> on a 64-bit process (568 bytes: size 0, process id 8,
/// parent id 32, the NUL-terminated wide name at 44) — a struct with an inline name would need runtime marshalling, which this project's
/// source-generated P/Invokes rule out.
/// </remarks>
public static partial class HostProcessList
{
    private const uint SnapProcess = 0x2;
    private const int NoMoreFiles = 18;
    private const int EntrySize = 568;
    private const int ProcessIdAt = 8;
    private const int ParentIdAt = 32;
    private const int NameAt = 44;
    private const int NameBytes = 520;

    /// <summary>The listing of this host.</summary>
    public static IProcessList Current { get; } = new Listing();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint CreateToolhelp32Snapshot(uint flags, uint processId);

    [LibraryImport("kernel32.dll", EntryPoint = "Process32FirstW", SetLastError = true)]
    private static partial int Process32First(nint snapshot, Span<byte> entry);

    [LibraryImport("kernel32.dll", EntryPoint = "Process32NextW", SetLastError = true)]
    private static partial int Process32Next(nint snapshot, Span<byte> entry);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int CloseHandle(nint handle);

    private sealed class Listing : IProcessList
    {
        public IReadOnlyList<ProcessEntry> Snapshot() => OperatingSystem.IsWindows() && Environment.Is64BitProcess ? Read() : [];

        private static List<ProcessEntry> Read()
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
            var entry = new byte[EntrySize];
            BinaryPrimitives.WriteUInt32LittleEndian(entry, EntrySize);
            for (var more = Process32First(snapshot, entry) != 0; more; more = Process32Next(snapshot, entry) != 0)
            {
                found.Add(Parsed(entry));
            }

            return Marshal.GetLastWin32Error() is NoMoreFiles or 0 ? found : [];
        }

        private static ProcessEntry Parsed(byte[] entry)
        {
            var name = Encoding.Unicode.GetString(entry, NameAt, NameBytes);

            return new ProcessEntry(
                BinaryPrimitives.ReadInt32LittleEndian(entry.AsSpan(ProcessIdAt)),
                BinaryPrimitives.ReadInt32LittleEndian(entry.AsSpan(ParentIdAt)),
                name[..Math.Max(0, name.IndexOf('\0'))]);
        }
    }
}
