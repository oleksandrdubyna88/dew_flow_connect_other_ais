using System.Runtime.InteropServices;
using System.Text;

namespace CoaiMcp.Runners.Detach;

/// <summary>
/// The Windows road of <see cref="DetachedLauncher"/>: <c>CreateProcess</c> with <c>DETACHED_PROCESS | CREATE_NEW_PROCESS_GROUP | CREATE_BREAKAWAY_FROM_JOB</c>; when the client's job
/// object refuses the breakaway (access denied) the runner starts without it and the road is recorded as <c>windows-in-job</c> — a client that closes its job then ends the runner too.
/// </summary>
/// <remarks>
/// <para><b>NOT MEASURED on Windows.</b> It compiles and its command line is a pure function the tests run on any host (<see cref="CommandLine"/>), but no Windows machine was reachable
/// when it was written: whether a client's job object with <c>KILL_ON_JOB_CLOSE</c> ends a <c>DETACHED_PROCESS</c> child that did break away is exactly the case
/// <c>v2/spikes/RESULTS_detach.md</c> §5 records as "not measured here". It must be measured on a Windows machine before anyone relies on it. No memory limit is applied (E1).</para>
/// <para>No handle is inherited (<c>bInheritHandles</c> false) and no stream is redirected: the runner logs to its own file, and a handle of the client held open by the runner would
/// keep the client's files locked after the client is gone.</para>
/// </remarks>
internal static partial class WindowsDetach
{
    private const uint DetachedProcess = 0x00000008;
    private const uint CreateNewProcessGroup = 0x00000200;
    private const uint CreateBreakawayFromJob = 0x01000000;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const int AccessDenied = 5;

    internal static DetachResult Start(DetachSpec spec)
    {
        var commandLine = CommandLine(spec);
        var (started, error) = Create(spec, commandLine, DetachedProcess | CreateNewProcessGroup | CreateBreakawayFromJob);
        if (started)
        {
            return new DetachResult(true, "windows", string.Empty, string.Empty);
        }

        if (error != AccessDenied)
        {
            return new DetachResult(false, string.Empty, string.Empty, $"windows: CreateProcess failed with error {error}");
        }

        // The client's job object does not allow a breakaway. Without it the runner lives and dies with that job.
        (started, error) = Create(spec, commandLine, DetachedProcess | CreateNewProcessGroup);

        return started
            ? new DetachResult(true, "windows-in-job", string.Empty, "the client's job object refused CREATE_BREAKAWAY_FROM_JOB; the runner is in that job")
            : new DetachResult(false, string.Empty, string.Empty, $"windows-in-job: CreateProcess failed with error {error}");
    }

    /// <summary>The command line of a <c>CreateProcess</c> call: every argument quoted by the rules of <c>CommandLineToArgvW</c>, so the runner reads back exactly the list.</summary>
    internal static string CommandLine(DetachSpec spec) =>
        string.Join(' ', new[] { spec.Executable }.Concat(spec.Arguments).Select(Quoted));

    internal static string Quoted(string argument)
    {
        if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '\n', '\v', '"']) < 0)
        {
            return argument;
        }

        var text = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var c in argument)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            // Backslashes before a quote are doubled, and the quote is escaped; any other backslashes stay as they are.
            text.Append('\\', c == '"' ? (backslashes * 2) + 1 : backslashes);
            backslashes = 0;
            text.Append(c);
        }

        // A closing quote must not be escaped by the argument's own trailing backslashes.
        text.Append('\\', backslashes * 2).Append('"');

        return text.ToString();
    }

    private static (bool Started, int Error) Create(DetachSpec spec, string commandLine, uint flags)
    {
        // STARTUPINFOW is cb plus fields the call does not need set; PROCESS_INFORMATION is two handles and two ids. Sized for the wider layout.
        var startup = new byte[104];
        BitConverter.TryWriteBytes(startup.AsSpan(0, 4), nint.Size == 8 ? 104 : 68);
        var information = new byte[32];
        var buffer = (commandLine + '\0').ToCharArray();
        if (CreateProcessW(null, buffer, 0, 0, 0, flags | CreateUnicodeEnvironment, 0, spec.WorkingDirectory, startup, information) == 0)
        {
            return (false, Marshal.GetLastWin32Error());
        }

        // The handles are the launcher's own; the runner does not need them, and a handle held here keeps the process object alive.
        CloseHandle(HandleAt(information, 0));
        CloseHandle(HandleAt(information, nint.Size));

        return (true, 0);
    }

    private static nint HandleAt(byte[] bytes, int offset) =>
        nint.Size == 8 ? (nint)BitConverter.ToInt64(bytes, offset) : BitConverter.ToInt32(bytes, offset);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateProcessW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial int CreateProcessW(
        string? applicationName,
        Span<char> commandLine,
        nint processAttributes,
        nint threadAttributes,
        int inheritHandles,
        uint creationFlags,
        nint environment,
        string currentDirectory,
        Span<byte> startupInfo,
        Span<byte> processInformation);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int CloseHandle(nint handle);
}
