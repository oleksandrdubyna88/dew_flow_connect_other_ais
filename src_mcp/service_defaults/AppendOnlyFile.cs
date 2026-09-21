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
/// <para><b>The probe checks the BEHAVIOUR, not the flag — and that correction is the code round's.</b>
/// <c>O_APPEND</c> is <c>0x400</c> on Linux and <c>0x8</c> on macOS, and a wrong constant would not
/// fail loudly: it would open a perfectly good handle that does not append, which is the corruption
/// this file exists to prevent, on the one platform that cannot be tested from here. The first
/// version asked <c>fcntl(F_GETFL)</c> whether the flag it requested was set — and that check is
/// CIRCULAR: it asks for <c>X</c>, the kernel sets <c>X</c>, and <c>flags &amp; X</c> is true
/// whatever <c>X</c> means. It could only ever catch a kernel silently dropping the flag.</para>
/// <para>So the probe does what the unit test does: it writes one byte through the descriptor, lets
/// a SECOND handle grow the file behind its back, writes another byte, and reads the length. A true
/// append puts the second byte at the new end; a positional write puts it at offset 1 and the file
/// is shorter. That is the property, asked of the kernel in the kernel's own terms, and no constant
/// can be wrong in a way it does not see. <c>FD_CLOEXEC</c> is still asked of <c>F_GETFD</c>, where
/// the constant is 1 on every POSIX platform and the question is not circular.</para>
/// <para><b>A NO is not cached.</b> A transient temp-directory failure at startup — a full disk, a
/// permission that clears — would otherwise disable every append for the life of the process through
/// a <c>Lazy</c> that had already answered. A true answer is kept; a false one is re-asked.</para>
///
/// <para><b>And <c>O_CREAT</c> is never asked for at all</b>, because <c>open</c> is variadic and
/// <c>O_CREAT</c> is the only way to reach its variadic argument: a fixed-arity P/Invoke passes it in
/// a register, which is right on x86-64 and wrong on arm64 — every Apple Silicon Mac — where
/// variadic arguments go on the stack. See <see cref="EnsureExists"/>.</para>
/// </remarks>
internal static partial class AppendOnlyFile
{
    /// <summary>Whether this process has a true append on this platform. False only if the Unix check failed.</summary>
    internal static bool TrueAppend
    {
        get
        {
            if (_appendWorks)
            {
                return true;
            }

            lock (ProbeGate)
            {
                return _appendWorks = _appendWorks || CheckUnixAppend();
            }
        }
    }

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
    private const int FGetFd = 1;
    private const int FdCloExec = 1;

    /// <summary>`O_APPEND`, which is not the same number on Linux and on macOS.</summary>
    private static int OAppend =>
        OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst() ? 0x0008 : 0x0400;

    /// <summary>
    /// `O_CLOEXEC` — and it is not optional here, because THIS server spawns other people's CLIs.
    /// </summary>
    /// <remarks>
    /// A raw descriptor opened without it is inherited across every `fork`/`exec`, and `coai-mcp`
    /// starts reviewer processes (`claude`, `codex`, `agy`) routinely. A child holding a write
    /// handle to the ledger keeps it open after this process exits, blocks an unmount, and hands an
    /// external tool a descriptor to a file this product would rather it could not reach. The window
    /// is one write wide and that is not a reason to leave it open. (The code round, gemini.)
    /// </remarks>
    private static int OCloExec =>
        OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst() ? 0x1000000 : 0x80000;

    /// <summary>
    /// The probe's answer, remembered only when it is YES.
    /// </summary>
    /// <remarks>
    /// A <see cref="Lazy{T}"/> keeps whatever the first call produced, including a false that came
    /// from a full disk or a permission that has since cleared — and that answer disables every
    /// append for the life of the process. A true answer cannot change (the kernel's semantics are
    /// not going to move), so it is kept; a false one is asked again next time. (The code round,
    /// gemini.)
    /// </remarks>
    private static bool _appendWorks;

    private static readonly Lock ProbeGate = new();

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
        // No `O_CREAT`: it is the variadic argument, and `EnsureExists` is how the file comes to
        // exist instead — see its own remarks for why a fixed-arity P/Invoke must not pass one.
        var fd = Open(path, OWriteOnly | OAppend | OCloExec);
        if (fd < 0)
        {
            // Almost always "it is not there yet". Anything else — a permission, a directory in the
            // way — throws out of here with a sentence rather than an errno, and the ledger's catch
            // list turns it into a false.
            EnsureExists(path);
            fd = Open(path, OWriteOnly | OAppend | OCloExec);
        }

        if (fd < 0)
        {
            throw new IOException($"the ledger {path} could not be opened for appending", LastError());
        }

        afterOpen?.Invoke(path);

        try
        {
            var wrote = (int)WriteRaw(fd, ref record[0], (nuint)record.Length);
            if (wrote < 0)
            {
                throw new IOException($"the ledger {path} refused a {record.Length} byte record", LastError());
            }

            // A SHORT write fails the record rather than being continued, and that is the code
            // round's finding. `write` may legally return early — a signal can cut one — and the
            // obvious loop makes the record whole at the cost of the only property this file exists
            // for: the remainder would be a SECOND append, and another process may land between the
            // two. So the caller is told the line was not written, the partial tail stays as a torn
            // tail, and the next append quarantines it. One lost line, never a fused pair.
            return wrote == record.Length;
        }
        finally
        {
            CloseRaw(fd);
        }
    }

    /// <summary>
    /// Opens a throwaway file and makes the kernel DEMONSTRATE that it appends.
    /// </summary>
    /// <remarks>
    /// Asking <c>fcntl</c> whether the flag one asked for is set answers itself. This writes through
    /// the descriptor, grows the file behind its back through another handle, writes again, and
    /// reads the length: a true append puts the second byte at the new end and the file is 6 bytes,
    /// a positional write puts it at offset 1 and the file stays 5. <c>FD_CLOEXEC</c> is still a
    /// flag question, because its constant is 1 everywhere and the question is not circular.
    /// </remarks>
    private static bool CheckUnixAppend()
    {
        if (OperatingSystem.IsWindows())
        {
            return true;
        }

        var probe = Path.Combine(Path.GetTempPath(), $"coai-append-check-{Environment.ProcessId}");
        try
        {
            EnsureExists(probe);
            var fd = Open(probe, OWriteOnly | OAppend | OCloExec);

            return fd >= 0 && Demonstrates(fd, probe);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // A temp directory that will not take a file today. Answering false is right; REMEMBERING
            // it is not, which is why the caller asks again.
            return false;
        }
        finally
        {
            Forget(probe);
        }
    }

    /// <summary>One byte, somebody else's four, one more byte — and the length says which it was.</summary>
    private static bool Demonstrates(int fd, string probe)
    {
        try
        {
            var first = "a"u8.ToArray();
            var second = "b"u8.ToArray();
            if (WriteRaw(fd, ref first[0], 1) != 1)
            {
                return false;
            }

            // Behind its back, through a handle of its own — what another process would do.
            using (var other = new FileStream(probe, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            {
                other.Write("xxxx"u8);
            }

            if (WriteRaw(fd, ref second[0], 1) != 1)
            {
                return false;
            }

            var descriptor = Fcntl(fd, FGetFd);

            return new FileInfo(probe).Length == 6
                && descriptor >= 0 && (descriptor & FdCloExec) != 0;
        }
        finally
        {
            CloseRaw(fd);
        }
    }

    /// <summary>
    /// <c>open</c> with exactly its two NAMED parameters — never the variadic third.
    /// </summary>
    /// <remarks>
    /// See <see cref="EnsureExists"/>: a fixed-arity P/Invoke that passes a variadic argument is
    /// right on x86-64 by accident and wrong on arm64, where those arguments go on the stack.
    /// </remarks>
    /// <summary>
    /// Deletes the probe, and cannot be the reason a process fails to start.
    /// </summary>
    /// <remarks>
    /// This runs inside a <see cref="Lazy{T}"/> during the first append, so an exception here would
    /// surface as a type-initialisation failure rather than as "no append available". A read-only
    /// or restricted temp directory is the person's machine being unusual, not this file's business.
    /// (The code round, gemini.)
    /// </remarks>
    private static void Forget(string probe)
    {
        try
        {
            File.Delete(probe);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
        {
        }
    }

    [LibraryImport("libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int Open(string path, int flags);

    [LibraryImport("libc", EntryPoint = "write", SetLastError = true)]
    private static partial nint WriteRaw(int fd, ref byte buffer, nuint count);

    [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
    private static partial int CloseRaw(int fd);

    [LibraryImport("libc", EntryPoint = "fcntl", SetLastError = true)]
    private static partial int Fcntl(int fd, int command);
}
