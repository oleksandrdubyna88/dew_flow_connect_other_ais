using System.Buffers.Binary;
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
/// source-generated P/Invokes rule out. The layout and the parse are here, on any OS; the calls that fill the buffer are
/// <c>HostProcessListNative</c>'s (the excluded shell).
/// </remarks>
public static class HostProcessList
{
    private const int EntrySize = 568;
    private const int ProcessIdAt = 8;
    private const int ParentIdAt = 32;
    private const int NameAt = 44;
    private const int NameBytes = 520;

    /// <summary>The listing of this host.</summary>
    public static IProcessList Current { get; } = new Listing();

    /// <summary>Whether the snapshot can be read here: the buffer's offsets are the 64-bit ones, and only Windows has the call.</summary>
    internal static bool Applies(bool windows, bool is64BitProcess) => windows && is64BitProcess;

    /// <summary>A buffer for the first call: zeroed, with its own size in the first four bytes, as <c>dwSize</c> asks.</summary>
    internal static byte[] FreshEntry()
    {
        var entry = new byte[EntrySize];
        BinaryPrimitives.WriteUInt32LittleEndian(entry, EntrySize);

        return entry;
    }

    /// <summary>The process id, the parent's and the NUL-terminated name of a filled buffer.</summary>
    internal static ProcessEntry Parsed(byte[] entry)
    {
        var name = Encoding.Unicode.GetString(entry, NameAt, NameBytes);

        return new ProcessEntry(
            BinaryPrimitives.ReadInt32LittleEndian(entry.AsSpan(ProcessIdAt)),
            BinaryPrimitives.ReadInt32LittleEndian(entry.AsSpan(ParentIdAt)),
            name[..Math.Max(0, name.IndexOf('\0'))]);
    }

    private sealed class Listing : IProcessList
    {
        public IReadOnlyList<ProcessEntry> Snapshot() => Applies(OperatingSystem.IsWindows(), Environment.Is64BitProcess) ? HostProcessListNative.Read() : [];
    }
}
