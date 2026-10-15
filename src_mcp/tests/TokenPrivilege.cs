using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A privilege enabled on this process's token for the length of a test, and put back as it was when disposed — what a test needs to
/// play "somebody else made this folder" (<c>SeRestorePrivilege</c> sets a foreign owner). <see cref="Enable"/> answers null when the
/// token does not hold the privilege at all (a session that is not elevated), which a test turns into a skip with its reason.
/// </summary>
/// <remarks>
/// The native <c>TOKEN_PRIVILEGES</c> is a count, then a LUID (two 4-byte halves) and 4 bytes of attributes — 16 bytes, packed to 4. The
/// first version of this helper let the runtime lay the 8-byte LUID out on an 8-byte boundary (offset 8, attributes at 16), so
/// <c>AdjustTokenPrivileges</c> read a garbage LUID and E5 skipped on every machine, an elevated CI runner included.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class TokenPrivilege : IDisposable
{
    private const uint TokenAdjustPrivileges = 0x20;
    private const uint TokenQuery = 0x8;
    private const uint Enabled = 0x2;

    private readonly nint _token;
    private TokenPrivileges _previous;
    private readonly bool _changed;

    private TokenPrivilege(nint token, TokenPrivileges previous, bool changed)
    {
        _token = token;
        _previous = previous;
        _changed = changed;
    }

    /// <summary>The privilege enabled, or null when this token does not hold it.</summary>
    public static TokenPrivilege? Enable(string name)
    {
        if (!OpenProcessToken(GetCurrentProcess(), TokenAdjustPrivileges | TokenQuery, out var token))
        {
            return null;
        }

        if (!LookupPrivilegeValueW(null, name, out var luid))
        {
            CloseHandle(token);

            return null;
        }

        var wanted = new TokenPrivileges { Count = 1, Luid = luid, Attributes = Enabled };
        var previous = default(TokenPrivileges);
        var adjusted = AdjustTokenPrivileges(token, false, ref wanted, (uint)Marshal.SizeOf<TokenPrivileges>(), ref previous, out _);
        if (adjusted && Marshal.GetLastWin32Error() == 0)
        {
            return new TokenPrivilege(token, previous, previous.Count > 0);
        }

        // ERROR_NOT_ALL_ASSIGNED (1300): the call "succeeds" and assigns nothing, because the token never held the privilege.
        CloseHandle(token);

        return null;
    }

    /// <summary>The privilege put back as it was, and the token closed.</summary>
    public void Dispose()
    {
        if (_changed)
        {
            AdjustTokenPrivileges(_token, false, ref _previous, 0, 0, out _);
        }

        CloseHandle(_token);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TokenPrivileges
    {
        public uint Count;
        public long Luid;
        public uint Attributes;
    }

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(nint process, uint access, out nint token);

    [DllImport("advapi32.dll", EntryPoint = "LookupPrivilegeValueW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValueW(string? system, string name, out long luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustTokenPrivileges(nint token, [MarshalAs(UnmanagedType.Bool)] bool disableAll, ref TokenPrivileges state, uint length, ref TokenPrivileges previous, out uint returnLength);

    [DllImport("advapi32.dll", EntryPoint = "AdjustTokenPrivileges", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustTokenPrivileges(nint token, [MarshalAs(UnmanagedType.Bool)] bool disableAll, ref TokenPrivileges state, uint length, nint previous, out uint returnLength);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}

/// <summary>Tests that change the process token's privileges run one at a time: the token is the process's, not the test's.</summary>
[CollectionDefinition("process-privileges", DisableParallelization = true)]
public sealed class ProcessPrivilegesCollection;
