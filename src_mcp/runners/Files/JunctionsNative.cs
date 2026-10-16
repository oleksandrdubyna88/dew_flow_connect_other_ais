using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CoaiMcp.Runners.Files;

/// <summary>
/// The Windows-only NATIVE SHELL of <see cref="Junctions"/>: the kernel calls that make a mount point and name a folder's real path —
/// <c>CreateFileW</c>, <c>DeviceIoControl(FSCTL_SET_REPARSE_POINT)</c> — and nothing that decides. It is excluded from Sonar's coverage
/// (<c>.github/workflows/sonarcloud.yml</c>, which says why) because the analysis host is Linux; <c>JunctionsTests</c> and the Kimi home suites run it
/// for real in the <c>windows-latest</c> job. Every verdict and every sentence lives in <see cref="Junctions"/>, which takes this through
/// <see cref="IJunctionShell"/>.
/// </summary>
internal sealed partial class JunctionsNative : IJunctionShell
{
    /// <summary>The product's shell.</summary>
    public static JunctionsNative Instance { get; } = new();

    private const uint FsctlSetReparsePoint = 0x000900A4;
    private const uint GenericWrite = 0x40000000;
    private const uint OpenExisting = 3;

    /// <summary>FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_BACKUP_SEMANTICS: open the folder itself, not what it may already point at.</summary>
    private const uint OpenTheLinkItself = 0x00200000 | 0x02000000;

    public string FinalDirectory(string path) => HandleRead.FinalDirectory(path);

    public DriveType DriveOf(string root)
    {
        try
        {
            return new DriveInfo(root).DriveType;
        }
        catch (ArgumentException)
        {
            return DriveType.Unknown;
        }
    }

    public string SetReparsePoint(string link, string target)
    {
        using var handle = HandleRead.CreateFileW(link, GenericWrite, 0, 0, OpenExisting, OpenTheLinkItself, 0);
        if (handle.IsInvalid)
        {
            return Marshal.GetPInvokeErrorMessage(Marshal.GetLastPInvokeError());
        }

        var buffer = Junctions.ReparseBuffer(target);

        return DeviceIoControl(handle, FsctlSetReparsePoint, buffer, (uint)buffer.Length, 0, 0, out _, 0)
            ? string.Empty
            : Marshal.GetPInvokeErrorMessage(Marshal.GetLastPInvokeError());
    }

    [LibraryImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControl(
        SafeFileHandle handle, uint code, ReadOnlySpan<byte> input, uint inputSize, nint output, uint outputSize, out uint returned, nint overlapped);
}
