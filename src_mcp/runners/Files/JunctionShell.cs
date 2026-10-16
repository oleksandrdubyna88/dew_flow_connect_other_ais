namespace CoaiMcp.Runners.Files;

/// <summary>
/// Everything <see cref="Junctions"/> needs from the operating system, and nothing else: bytes in, bytes out. The one real implementation is
/// <c>JunctionsNative</c> (P/Invoke and handles, run only on Windows, where the <c>windows-latest</c> job runs <c>JunctionsTests</c> and the
/// Kimi home suites); the Linux suite drives the same decisions — refusals, the length arithmetic, the read-back, the removal — through a
/// shell that makes a symbolic link, so every sentence is tested on any OS. It decides NOTHING.
/// </summary>
internal interface IJunctionShell
{
    /// <summary>What the kernel says a directory handle really names, or EMPTY when it cannot be opened or named (see <c>HandleRead.FinalDirectory</c>).</summary>
    string FinalDirectory(string path);

    /// <summary>The type of the drive whose root is <paramref name="root"/> (<c>C:\</c>); <see cref="DriveType.Unknown"/> when it cannot be asked.</summary>
    DriveType DriveOf(string root);

    /// <summary>
    /// Turns the EMPTY folder at <paramref name="link"/> into a link to <paramref name="target"/> (a full path, no trailing separator, whose
    /// reparse buffer is known to fit); empty on success, otherwise the system's message.
    /// </summary>
    string SetReparsePoint(string link, string target);
}
