namespace CoaiMcp.Tests;

/// <summary>
/// Whether THIS process can give a folder the setgid bit — found by trying, never by a variable that says where the test runs. The shared-instance tests of the key store need a data folder with mode 2770; a chmod that sets it is refused with "Operation not permitted"
/// where the process is not a member of the folder's group as the kernel sees it (a unit with its own user namespace, a check boundary: the folder's group is not mapped there), and a few file systems accept the call and keep no such bit. Either is a machine on which
/// the tests cannot run, not a failure of the key store: they skip, with the sentence this class gives, and run on every machine that can.
/// </summary>
/// <param name="Set">The chmod; a refusal throws as <see cref="File.SetUnixFileMode(string, UnixFileMode)"/> does.</param>
/// <param name="Get">The mode read back.</param>
internal sealed record SetgidSupport(Action<string, UnixFileMode> Set, Func<string, UnixFileMode> Get)
{
    /// <summary>The real file system.</summary>
    public static SetgidSupport Real { get; } = new(RealSet, RealGet);

    private static void RealSet(string folder, UnixFileMode mode)
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("modes are a Unix thing");
        }

        File.SetUnixFileMode(folder, mode);
    }

    private static UnixFileMode RealGet(string folder) => OperatingSystem.IsWindows() ? throw new PlatformNotSupportedException("modes are a Unix thing") : File.GetUnixFileMode(folder);

    /// <summary>
    /// Sets <paramref name="mode"/> on <paramref name="folder"/> and reads it back. True only when the setgid bit is there afterwards; otherwise false and <paramref name="why"/> says which of the two things failed. Never throws for a refusal.
    /// </summary>
    public bool TryApply(string folder, UnixFileMode mode, out string why)
    {
        try
        {
            Set(folder, mode);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            why = $"this process cannot give a folder the setgid bit here ({e.Message}); a unit with its own user namespace is such a place";

            return false;
        }

        if ((Get(folder) & UnixFileMode.SetGroup) == 0)
        {
            why = "this file system does not keep the setgid bit";

            return false;
        }

        why = string.Empty;

        return true;
    }
}
