using System.Security;
using System.Text;

namespace CoaiMcp.ServiceDefaults;

/// <summary>
/// The ONE append in this repository: one JSON line onto a ledger, best-effort, safe beside another
/// PROCESS writing the same file.
/// </summary>
/// <remarks>
/// <para><b>Extracted, not copied.</b> <c>UsageLedger.Append</c> was the only production append in
/// the C# half, and the notices writer of story 1.4 needed every line of it. A second implementation
/// of that bargain is a defect from the moment it compiles — the two drift — so the spending ledger
/// became this method's first caller and the notices writer its second. A test enumerates the
/// production call sites and refuses a third.</para>
///
/// <para><b>Why here.</b> This library is "how this process touches the data directory":
/// <see cref="CoaiLogPath"/> lives here, the Serilog file sink writes from here, it is AOT-clean and
/// every other project can reference it. <c>CoaiMcp.Core</c> is declared pure — "no Process, no
/// HttpClient, no filesystem" — and a <c>FileStream</c> there to make a test convenient would break a
/// stated boundary. The name is <c>jsonlLedger.appendLine</c>'s, the extension's half of the same
/// primitive: one vocabulary across the one file both halves write in the same shape.</para>
///
/// <para><b>The append itself is <see cref="AppendOnlyFile"/>, and that is the finding of story
/// 1.4.</b> <c>FileMode.Append</c> is not an append — it is a positional write at the end .NET saw
/// when it opened — so the first version of this method lost 2488 of 8000 records to eight
/// processes writing one file, where node's <c>appendFile</c> lost none. The measurement, the probe
/// that names the mechanism, and why a lock was built and then rejected are all recorded on
/// <see cref="AppendOnlyFile"/>. What is left here is the POLICY: what a line is, when a tail is
/// repaired, and what is swallowed.</para>
///
/// <para><b>A torn tail is quarantined on EVERY append.</b> A process killed mid-line leaves a file
/// whose last byte is not a newline, and an append that simply followed it would fuse its record
/// onto the wreck, losing that record as surely as the fragment. The first draft checked once per
/// process and wrote the repair as an append of its own; the plan round took both halves apart.
/// <i>codex:</i> two writes are not atomic against another process — A reads a torn tail, B appends
/// a whole record, A appends its bare newline, and B's record is fused for ever. <i>gemini:</i> a
/// peer that crashes AFTER this process's first write leaves a tail it would never look at again.
/// So the last byte is read before EVERY append — one extra open and a one-byte read, on a path that
/// already costs an open and a write, for events that are rare by construction — and when it is not
/// a newline the newline is PREPENDED to the record and goes out in the single append that was going
/// to happen anyway.</para>
///
/// <para><b>What remains, stated rather than hidden.</b> After a crash, whichever process appends
/// first may still fuse its record onto the fragment if it read the tail before the crash's last
/// byte landed: ONE record, the same one that would be lost with no repair at all. Closing it needs
/// the tail inspection and the write to be one operation, which is a lock — measured, and rejected
/// on <see cref="AppendOnlyFile"/>, because it loses records to contention instead. Two processes
/// may both repair, which leaves one blank line; the extension's reader skips it while still
/// counting its byte, so one phantom unread, once, after a crash, against a lost record. Both are
/// asserted rather than assumed.</para>
///
/// <para><b>A tail that cannot be READ is never a reason to drop the record.</b> The inspection
/// swallows the same failures the write does and answers "not torn": the worst a wrong answer there
/// can do is leave a fragment unrepaired, and refusing to write because the file could not be peeked
/// at would turn a transient sharing hiccup into a lost notice.</para>
///
/// <para><b>The line carries its own terminator, and the writer will not let it not.</b>
/// <c>ServerNoticeLine.Of</c> ends its line in a newline exactly as <c>notificationLine</c> does on
/// the other side, and <c>appendLine</c> there takes it as-is; both readings corrupt the file — a
/// doubled newline is a blank line between every record, which <c>countSince</c> counts, and a
/// missing one fuses every record into one line. So a missing terminator is ADDED rather than thrown
/// over: a best-effort writer may not throw, and an unterminated JSONL line is the corruption this
/// exists to prevent.</para>
///
/// <para><b>It never throws, and "never" is a list.</b> <c>IOException</c> and
/// <c>UnauthorizedAccessException</c> are inherited from <c>UsageLedger</c>
/// (<c>PathTooLongException</c> and <c>DirectoryNotFoundException</c> are <c>IOException</c>s). A
/// rooted, non-empty directory the OS refuses satisfies every validation above it and throws BEFORE
/// any I/O — two reviewers found the hole independently — so the path-shaped failures are caught
/// too: <c>ArgumentException</c>, <c>NotSupportedException</c>, <c>SecurityException</c>. Measured on
/// .NET 10 / Windows 11: the plan's own example, <c>C:\data*</c>, throws <c>IOException</c> ("the
/// filename, directory name, or volume label syntax is incorrect"); the shape that really reaches
/// the new clauses is a NUL inside a segment ("Null character in path", an <c>ArgumentException</c>
/// on every platform). A null argument is excluded from that clause on purpose, and
/// <c>Exception</c> is not caught at all: an <c>OutOfMemoryException</c>, a
/// <c>NullReferenceException</c>, a null path are programming errors, and swallowing one would make
/// this the quietest bug in the product.</para>
///
/// <para><b>It answers <c>bool</c></b> — written or not — so a caller can one day count a loss.
/// Nothing in story 1.4 reads the answer.</para>
/// </remarks>
public static class JsonlLedger
{
    /// <summary>
    /// One lock per LEDGER, not one for the process.
    /// </summary>
    /// <remarks>
    /// <para>Not what makes the append safe — the kernel does that, across processes, which is the
    /// only place it can be done. This keeps two of OUR threads from interleaving a tail inspection
    /// with the other's repair, which would leave a blank line inside one process for no reason at
    /// all.</para>
    /// <para><b>Per path, because the ledgers are now two.</b> A single process-wide lock made a
    /// stalled write to one file — a data directory on a NAS that has stopped answering is the case
    /// this product actually meets — block the other, so a hung notice could hold up a spending
    /// record it has nothing to do with. The dictionary is bounded by the number of ledger paths a
    /// process writes, which is two. (The code round, codex.)</para>
    /// </remarks>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lock> Gates =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The line terminator, spelled by NUMBER: an escape here has reached disk as the raw byte three times.</summary>
    private const byte Newline = 10;

    /// <summary>
    /// Writes one line, terminated, onto the ledger at <paramref name="path"/>. Never throws for a
    /// reason the disk gave.
    /// </summary>
    /// <param name="path">The ledger file. Its directory is created when missing.</param>
    /// <param name="line">One JSON record, ending in a newline — a missing one is added.</param>
    /// <returns>Whether the line reached the file.</returns>
    public static bool AppendLine(string path, string line) => AppendLine(path, line, AppendOnlyFile.Write);

    /// <summary>
    /// The seam a test counts appends through: the writer is the real one wrapped, never a lookalike.
    /// </summary>
    internal static bool AppendLine(string path, string line, Func<string, byte[], bool> append)
    {
        try
        {
            CreateDirectoryOf(path);
            lock (Gates.GetOrAdd(path, _ => new Lock()))
            {
                return append(path, Bytes(Terminated(line), repairFirst: TailIsTorn(path)));
            }
        }
        catch (Exception e) when (IsTheDisksFault(e))
        {
            // A missed line is a gap in a chart or one refusal fewer in a file. A thrown exception
            // here would be a failed review, or a refusal that never reached the caller.
            return false;
        }
    }

    /// <summary>The directory may genuinely not exist: this can be the first thing written under a data directory.</summary>
    private static void CreateDirectoryOf(string path)
    {
        // Resolved against the working directory first, exactly as node's `dirname` does, so a
        // bare file name has a directory to create rather than an empty string to refuse.
        if (Path.GetDirectoryName(Path.GetFullPath(path)) is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
        }
    }

    /// <summary>Whether the file's last byte is something other than a newline. A missing or empty file is not torn.</summary>
    internal static bool TailIsTorn(string path)
    {
        try
        {
            using var file = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (file.Length == 0)
            {
                return false;
            }

            file.Seek(-1, SeekOrigin.End);

            return file.ReadByte() != Newline;
        }
        catch (Exception e) when (IsTheDisksFault(e))
        {
            // Not torn as far as this process can tell, and the record is written either way.
            return false;
        }
    }

    private static string Terminated(string line) =>
        line.Length > 0 && line[^1] == (char)Newline ? line : line + (char)Newline;

    /// <summary>The whole write, as ONE array: the repair newline, when needed, in front of the record.</summary>
    private static byte[] Bytes(string terminated, bool repairFirst) =>
        repairFirst
            ? [Newline, .. Encoding.UTF8.GetBytes(terminated)]
            : Encoding.UTF8.GetBytes(terminated);

    /// <summary>The list. Everything else is a programming error and flies.</summary>
    private static bool IsTheDisksFault(Exception e) =>
        e is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or SecurityException
            or (ArgumentException and not ArgumentNullException);
}
