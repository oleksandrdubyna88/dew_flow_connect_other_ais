using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CoaiMcp.ServiceDefaults;

/// <summary>
/// One record onto the end of a file, atomically with respect to every other process appending to it.
/// </summary>
/// <remarks>
/// <para><b>Why this is not <c>FileMode.Append</c>.</b> Because <c>FileMode.Append</c> is not an
/// append. Measured on 2026-09-21, directly: open a <c>FileStream</c> in <c>FileMode.Append</c> on an
/// empty file, let another handle append 100 bytes behind its back, then write one byte through the
/// first stream. A kernel append puts that byte at the new end and the file is 101 bytes. It is
/// <b>100 bytes with the byte at offset 0</b> — .NET remembers the end it saw when it opened and
/// writes THERE, destroying what arrived in between. Windows 11 and Linux (ext4, .NET 10.0.112)
/// answer identically, so it is the runtime's behaviour and not one platform's.</para>
///
/// <para>At scale that is <c>npm run measure:append 8 1000 --dotnet=8</c>: eight processes, records
/// up to 60 KB, one file. Through <c>FileMode.Append</c>, <b>5512 of 8000 records survived and 1110
/// lines were torn</b>. Eight NODE writers over the same harness, whose <c>appendFile</c> opens with
/// <c>'a'</c>: 8000 of 8000, 0 torn. The difference is not the filesystem and not the size; it is
/// that one of them asks the kernel to append and the other does not.</para>
///
/// <para><b>What the kernels promise.</b> A handle opened for <c>FILE_APPEND_DATA</c> without
/// <c>FILE_WRITE_DATA</c> makes Windows ignore the file offset entirely and place every write at the
/// end; <c>O_APPEND</c> makes POSIX seek to the end and write as one operation. Both are documented,
/// and both are what a single <c>WriteFile</c> / <c>write</c> of a whole record needs to be
/// uninterruptible by another appender. This is the same system call node's writer makes, which is
/// what lets the measurement recorded for one of them mean anything about the other.</para>
///
/// <para><b>Why not a lock instead.</b> It was built and measured first: one handle opened
/// <c>FileShare.Read</c>, holding the tail inspection and the write together, with a retry when
/// another writer had it. It is correct and it changes the CONTRACT — under the same eight-process
/// run a writer exhausted its retries and lost a record, so a notice could be dropped because a
/// neighbour was busy. A best-effort recorder may lose a line to a broken disk; losing one to
/// contention is a different and worse thing, and an append that cannot be interrupted needs no lock
/// at all.</para>
///
/// <para><b>The flag is verified rather than trusted, once per process.</b> <c>O_APPEND</c> is
/// <c>0x400</c> on Linux and <c>0x8</c> on macOS, and a wrong constant would not fail loudly: it
/// would open a perfectly good handle that does not append, which is the corruption this file exists
/// to prevent, on the one platform that cannot be tested from here. So the first Unix open asks
/// <c>fcntl(F_GETFL)</c> whether the flag it asked for is actually set, and <see cref="TrueAppend"/>
/// answers false for ever if it is not — the caller then has a fact it can report rather than a file
/// it can corrupt.</para>
///
/// <para><b>And <c>O_CREAT</c> is never asked for at all</b>, because <c>open</c> is variadic and
/// <c>O_CREAT</c> is the only way to reach its variadic argument: a fixed-arity P/Invoke passes it in
/// a register, which is right on x86-64 and wrong on arm64 — every Apple Silicon Mac — where
/// variadic arguments go on the stack. See <see cref="EnsureExists"/>.</para>
/// </remarks>
internal static partial class AppendOnlyFile
{
    /// <summary>Whether this process has a true append on this platform. False only if the Unix check failed.</summary>
    internal static bool TrueAppend => AppendWorks.Value;

    /// <summary>
    /// Appends the whole record at the end of <paramref name="path"/>, creating the file if needed.
    /// </summary>
    /// <returns>Whether every byte was written.</returns>
    /// <exception cref="IOException">The platform refused the open or the write.</exception>
    internal static bool Write(string path, byte[] record) => Write(path, record, afterOpen: null);

    /// <summary>
    /// The same write, with a test's chance to grow the file BETWEEN the open and the write.
    /// </summary>
    /// <remarks>
    /// <para>That window is the whole defect, and without a seam it cannot be tested: it is
    /// microseconds wide, it needs a second writer, and the sequential tests beside this one pass
    /// just as happily against the broken <c>FileMode.Append</c> — which was checked, because a test
    /// that cannot fail is worse than none. With the seam the failure is deterministic: a handle
    /// that remembered its offset writes the record at the hundred bytes' place and the file is 101
    /// bytes short of what it should be.</para>
    /// <para>A PARAMETER rather than a static hook, so that nothing can be left set: xUnit runs test
    /// classes in parallel and a mutable seam would fire inside somebody else's append.</para>
    /// </remarks>
    internal static bool Write(string path, byte[] record, Action<string>? afterOpen)
    {
        if (!TrueAppend)
        {
            // Refused rather than written. A handle that does not append is how two processes
            // destroy each other's records, and a ledger with a gap in it is recoverable where a
            // ledger with a hole punched through the middle of a line is not. The caller answers
            // "not written", which is the truth, and the sentence names what to look at.
            throw new IOException(
                "this build's O_APPEND constant is not what this kernel uses, so an append here "
                + "would silently overwrite another process's records — see AppendOnlyFile.OAppend");
        }

        return OperatingSystem.IsWindows()
            ? WriteOnWindows(path, record, afterOpen)
            : WriteOnUnix(path, record, afterOpen);
    }

    // ---------- Windows ----------

    private const uint FileAppendData = 0x0004;
    private const uint Synchronize = 0x00100000;
    private const uint ShareEverything = 0x00000001 | 0x00000002 | 0x00000004; // read | write | delete
    private const uint OpenAlways = 4;
    private const uint NormalAttributes = 0x80;

    private static bool WriteOnWindows(string path, byte[] record, Action<string>? afterOpen)
    {
        using var handle = CreateFileW(
            path, FileAppendData | Synchronize, ShareEverything, 0, OpenAlways, NormalAttributes, 0);
        if (handle.IsInvalid)
        {
            throw new IOException($"the ledger {path} could not be opened for appending", LastError());
        }

        afterOpen?.Invoke(path);

        // NO overlapped structure and NO offset: with FILE_APPEND_DATA and not FILE_WRITE_DATA the
        // offset is ignored by the kernel, and one WriteFile of a whole record is one append.
        if (!WriteFile(handle, record, (uint)record.Length, out var written, 0))
        {
            throw new IOException($"the ledger {path} refused a {record.Length} byte record", LastError());
        }

        return written == record.Length;
    }

    private static int LastError() => Marshal.GetLastPInvokeError();

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafeFileHandle CreateFileW(
        string path, uint access, uint share, nint security, uint disposition, uint flags, nint template);

    [LibraryImport("kernel32.dll", EntryPoint = "WriteFile", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WriteFile(
        SafeFileHandle handle, byte[] buffer, uint count, out uint written, nint overlapped);

    // ---------- Unix ----------

    private const int OWriteOnly = 0x0001;
    private const int FGetFl = 3;

    /// <summary>`O_APPEND`, which is not the same number on Linux and on macOS.</summary>
    private static int OAppend =>
        OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst() ? 0x0008 : 0x0400;

    private static readonly Lazy<bool> AppendWorks = new(CheckUnixAppend, isThreadSafe: true);

    /// <summary>
    /// The file, made to exist by the RUNTIME — because <c>open</c> is variadic and <c>O_CREAT</c>
    /// is the only way to reach its variadic argument.
    /// </summary>
    /// <remarks>
    /// On x86-64 a variadic argument arrives in the register a fixed one would, so a three-parameter
    /// P/Invoke to <c>open</c> works by accident. On arm64 — every Apple Silicon Mac this ships to —
    /// variadic arguments go on the STACK, and that same call hands the kernel a mode nobody passed:
    /// a ledger created with whatever was in those bytes. So this build never asks for
    /// <c>O_CREAT</c>. It calls <c>open</c> with exactly its two NAMED parameters, which is the same
    /// call on every ABI, and creates the file through <c>FileStream</c> when it is not there —
    /// which is also where the file gets the permissions every other file this product writes has.
    /// One extra syscall, once in a ledger's life.
    /// </remarks>
    private static void EnsureExists(string path)
    {
        // Opened and closed, nothing written: `OpenOrCreate` does not truncate, so a ledger that is
        // already there keeps every byte of it.
        using var created = new FileStream(
            path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
    }

    private static bool WriteOnUnix(string path, byte[] record, Action<string>? afterOpen)
    {
        var fd = Open(path, OWriteOnly | OAppend);
        if (fd < 0)
        {
            // Almost always "it is not there yet". Anything else — a permission, a directory in the
            // way — throws out of here with a sentence rather than an errno, and the ledger's catch
            // list turns it into a false.
            EnsureExists(path);
            fd = Open(path, OWriteOnly | OAppend);
        }

        if (fd < 0)
        {
            throw new IOException($"the ledger {path} could not be opened for appending", LastError());
        }

        afterOpen?.Invoke(path);

        try
        {
            var at = 0;
            while (at < record.Length)
            {
                // A short write is legal and is not an error: a signal can cut one anywhere. Only a
                // NEGATIVE answer is a failure, and continuing from `at` is what makes the record
                // whole — although a short write means this record is no longer one atomic append,
                // which is why the loop is here and not assumed away.
                var wrote = (int)WriteRaw(fd, ref record[at], (nuint)(record.Length - at));
                if (wrote < 0)
                {
                    throw new IOException($"the ledger {path} refused a {record.Length} byte record", LastError());
                }

                at += wrote;
            }

            return true;
        }
        finally
        {
            CloseRaw(fd);
        }
    }

    /// <summary>
    /// Opens a throwaway file with the flags this build believes in and asks the kernel what it got.
    /// </summary>
    private static bool CheckUnixAppend()
    {
        if (OperatingSystem.IsWindows())
        {
            return true;
        }

        var probe = Path.Combine(Path.GetTempPath(), $"coai-append-check-{Environment.ProcessId}");
        EnsureExists(probe);
        var fd = Open(probe, OWriteOnly | OAppend);
        if (fd < 0)
        {
            return false;
        }

        try
        {
            var flags = Fcntl(fd, FGetFl);

            return flags >= 0 && (flags & OAppend) != 0;
        }
        finally
        {
            CloseRaw(fd);
            File.Delete(probe);
        }
    }

    /// <summary>
    /// <c>open</c> with exactly its two NAMED parameters — never the variadic third.
    /// </summary>
    /// <remarks>
    /// See <see cref="EnsureExists"/>: a fixed-arity P/Invoke that passes a variadic argument is
    /// right on x86-64 by accident and wrong on arm64, where those arguments go on the stack.
    /// </remarks>
    [LibraryImport("libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int Open(string path, int flags);

    [LibraryImport("libc", EntryPoint = "write", SetLastError = true)]
    private static partial nint WriteRaw(int fd, ref byte buffer, nuint count);

    [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
    private static partial int CloseRaw(int fd);

    [LibraryImport("libc", EntryPoint = "fcntl", SetLastError = true)]
    private static partial int Fcntl(int fd, int command);
}
