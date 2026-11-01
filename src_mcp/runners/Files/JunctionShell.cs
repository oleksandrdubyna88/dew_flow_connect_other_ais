namespace CoaiMcp.Runners.Files;

/// <summary>
/// Everything <see cref="Junctions"/> needs from the operating system, and nothing else: bytes in, bytes out. The one real implementation is
/// <c>JunctionsNative</c> on Windows (P/Invoke and handles; the <c>windows-latest</c> job runs <c>JunctionsTests</c> and the Kimi home suites) and
/// <c>JunctionsUnix</c> on Linux and WSL (a directory symbolic link); a test's, per async flow, names paths by their real spelling, so every
/// decision — refusals, the length arithmetic, the read-back, the removal — runs on any OS. It decides NOTHING.
/// </summary>
internal interface IJunctionShell
{
    /// <summary>What the kernel says a directory handle really names, or EMPTY when it cannot be opened or named (see <c>HandleRead.FinalDirectory</c>).</summary>
    string FinalDirectory(string path);

    /// <summary>The type of the drive whose root is <paramref name="root"/> (<c>C:\</c>); <see cref="DriveType.Unknown"/> when it cannot be asked.</summary>
    DriveType DriveOf(string root);

    /// <summary>
    /// Makes the link at <paramref name="link"/> — a path that does NOT exist yet — to <paramref name="target"/> (a full path, no trailing separator, whose
    /// reparse buffer is known to fit); empty on success, otherwise the system's message. Atomic where the system allows it: never a delete-then-create window.
    /// <b>The link's PARENT folder is created when it is missing</b> (both shells; <c>Junctions.Make</c> relies on it for a fresh owned home) — a caller never makes it, and a shell that did not would fail on the first launch of a new home.
    /// </summary>
    string MakeLink(string link, string target);
}
