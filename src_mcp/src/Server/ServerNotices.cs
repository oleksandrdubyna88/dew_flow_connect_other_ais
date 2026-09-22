using System.Text;
using CoaiMcp.Core.Notices;
using CoaiMcp.ServiceDefaults;

namespace CoaiMcp.Server;

/// <summary>
/// Where this binary writes down what it refused, what failed, and that it died.
/// </summary>
/// <remarks>
/// <para><b>The reader shipped first, and had nothing to read for four days.</b>
/// <c>server-notices.jsonl</c> was named in <c>notificationsFile.ts</c> on 2026-09-17, its path
/// derived by <c>serverNoticesPath</c>, and three modules already merged it into the panel section,
/// the page and the derived count — while nothing wrote it, so every one of those surfaces reported
/// on half the product. This class is the other half: story 1.3 gave it a PATH, story 1.4 the append,
/// and story 2.2 the first caller that is not a test.</para>
/// <para><b>The directory is ASKED for, never composed.</b>
/// <see cref="SettingsFile.DataDirFrom"/> is the one rule — since 2026-09-18 it IS
/// <c>PanelSettings.DataDirectoryFor</c>, with the side applied and the trim applied — and the two
/// halves are held to each other by <c>shared/data-side-vectors.json</c>, which carries a
/// <c>serverNoticesPath</c> per case now beside <c>settingsPath</c> and <c>logsPath</c>. That
/// fixture exists because the failure of disagreeing here is the silent one: the server writes, the
/// extension reads an empty directory, and nothing says so.</para>
/// <para><c>Path.Combine</c> rather than string concatenation, which is what lets the same vector
/// hold on both platforms — the separator is not part of what the two halves have to agree on.</para>
/// </remarks>
public static class ServerNotices
{
    /// <summary>The file, named once — the extension spells the same name in `notificationsFile.ts`.</summary>
    public const string Name = "server-notices.jsonl";

    /// <summary>The previous generation, kept when the live file reaches its ceiling.</summary>
    public const string Archive = "server-notices.1.jsonl";

    /// <summary>The ceiling, so that the pair cannot exceed the 256 MB the parent plan named.</summary>
    public const long RollAt = 128L * 1024 * 1024;

    /// <summary>The notices file inside a data directory that has already been resolved.</summary>
    /// <remarks>
    /// <para>It takes the directory rather than the environment on purpose: every caller here
    /// already holds one, and a second function that resolved it would be a second resolver — which
    /// is exactly the defect <c>PLAN_the_settings_file_ignores_the_side.md</c> was written to fix,
    /// four days before this file existed.</para>
    /// <para><b>An empty directory is refused rather than combined.</b>
    /// <c>Path.Combine("", "server-notices.jsonl")</c> is the bare file name, which resolves against
    /// whatever directory happened to launch the server — so a data directory that failed to
    /// resolve would put a person's notices in a deployment folder, or in whatever the working
    /// directory of an editor's spawned process is, and the extension would read an empty directory
    /// and say nothing. A reviewer asked for this and it is the one input the type system cannot
    /// refuse on its own.</para>
    /// <para><b>The refusal is KEPT now that the parameter is a type</b> (story 1.4). The type's
    /// constructor refuses an empty path, and a record class cannot be defeated by <c>default</c>
    /// the way a struct can — but a null reference, or a field the compiler cannot see, still
    /// reaches here, and the refusal costs one line. A type that removes a guard is a type that
    /// has to be perfect.</para>
    /// </remarks>
    public static string PathFor(ResolvedDataDir dir) =>
        dir is null || string.IsNullOrWhiteSpace(dir.Path)
            ? throw new ArgumentException(
                "the data directory is empty, so the notices file would land beside whatever "
                + "launched this process instead of where the extension reads it", nameof(dir))
            : Path.Combine(dir.Path, Name);

    /// <summary>
    /// Writes one notice down, best-effort. The writer is the POLICY, and every judgement in it is
    /// already made elsewhere: the path by story 1.3, the bytes by story 1.2, the disk by
    /// <see cref="JsonlLedger"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>One line, and it is held to by bytes.</b> <c>ServerNoticesAppendTests</c> reads the
    /// file back and compares it with <see cref="ServerNoticeLine.Of"/> — not that the serialiser was
    /// called, but that its bytes are what landed. Story 1.2's round named the gap: until the
    /// serialiser was wired to a writer, a call site could serialise a notice by hand and the parity
    /// harness would stay green, because it exercises <c>NoticeTool</c> rather than this. And a
    /// census refuses a third road onto the disk: exactly two production call sites reach
    /// <c>JsonlLedger.AppendLine</c>, the spending ledger and this.</para>
    /// <para><b>Never throws for a reason the DISK gave</b> — a directory in the file's place, a
    /// share that stopped answering, a path the OS refuses — and answers <c>false</c> so a caller
    /// can one day count the loss. A refusal that fails to be written is still returned to the
    /// calling AI. What still throws is a programming error at the call site: a null-bearing
    /// directory (<see cref="PathFor"/>'s kept guard) or a record the line cannot write
    /// (<see cref="ServerNotice.More"/>'s refusals happen at construction, where the call site is).</para>
    /// <para><b>The directory is the TYPE.</b> <see cref="ResolvedDataDir"/> is minted by the one
    /// resolver, so <c>PanelSettings.DataRootFor</c> — the directory BEFORE the side, the one thing
    /// here that looks like a data directory and is not one — cannot be handed to this.</para>
    /// <para><b>Its caller since story 2.2 is <see cref="NoticeWriter"/></b>, on one thread behind a
    /// bounded queue, and every refusal this server returns reaches it — there turned out to be ONE
    /// road rather than the three this was written expecting, which is what story 2.1's census
    /// established. What is still owed is story 2.4's live seam leg: a real refusal over stdio,
    /// asserted on the bytes that land.</para>
    /// </remarks>
    public static bool Append(ResolvedDataDir dir, ServerNotice notice, long rollAt = RollAt)
    {
        var path = PathFor(dir);
        var line = ServerNoticeLine.Of(notice);

        return Room(path, rollAt, Encoding.UTF8.GetByteCount(line)) && JsonlLedger.AppendLine(path, line);
    }

    /// <summary>
    /// The ceiling. At <see cref="RollAt"/> the live file becomes <see cref="Archive"/> and a new one
    /// starts, so the pair cannot pass the 256 MB the parent plan named.
    /// </summary>
    /// <remarks>
    /// <para><b>Why it ships with the first repeating writer.</b> §7 of the parent bounds this file at
    /// ~400 B typical and ~10 KB worst case per record, 300/day — 44 MB/year, or 1.1 GB/year at the
    /// worst the serialiser permits — and named 256 MB as the trigger for a roll-up somebody would
    /// build later. Story 2.2's plan round refused that: `planning-docs.md` requires a plan that
    /// creates something that GROWS to name its budget, its owner and its retirement rule BEFORE the
    /// first write, and story 2.2 is the first story with a repeating writer — 48 refusal sites, every
    /// one reachable on every round. So 256 MB is a MAXIMUM here rather than a trigger, and the
    /// retirement rule is the oldest generation.</para>
    /// <para><b>Two generations, not many.</b> What the page shows is recent, what a person debugging
    /// wants is the recent past, and a numbered series is a retention policy with no end — which is
    /// what this was asked to avoid rather than reinvent.</para>
    /// <para><b>A roll that cannot happen is not an error.</b> Another server on the same NAS rolling
    /// the same file in the same moment is the normal case for this product, and the disk arbitrates
    /// it: one move wins and the other is refused. The record then lands in whichever file is live,
    /// which is the right answer either way. What must never happen is a refusal failing because a
    /// rename did not.</para>
    /// </remarks>
    /// <summary>
    /// Whether this record fits under the ceiling, counting the record.
    /// </summary>
    /// <remarks>
    /// The first version compared the file's size BEFORE the append, so a file one byte under the
    /// ceiling still took a whole record and the ceiling was really "the ceiling plus one record".
    /// CodeRabbit found it on the pull request. The line is serialised ONCE and its bytes — the
    /// newline included, because that is what lands — are what the comparison is about.
    /// </remarks>
    private static bool Room(string path, long rollAt, int adding) =>
        Size(path) + adding <= rollAt || Rolled(path);

    /// <summary>
    /// How big the live file is, and never an exception.
    /// </summary>
    /// <remarks>
    /// <c>FileInfo.Exists</c> is a snapshot and <c>Length</c> reads it, so a neighbour deleting or
    /// renaming the file between the two throws — which the code round named, and which on a shared
    /// NAS is a Tuesday. An unreadable length answers zero: the ceiling bounds a file this process can
    /// SEE, and refusing to write because a stat failed would lose a notice for a reason that has
    /// nothing to do with size.
    /// </remarks>
    private static long Size(string path)
    {
        try
        {
            var live = new FileInfo(path);

            return live.Exists ? live.Length : 0;
        }
        catch (Exception failure) when (IsTheDisksFault(failure))
        {
            return 0;
        }
    }

    /// <summary>
    /// Whether the live file was moved aside, making room. FALSE stops the append.
    /// </summary>
    /// <remarks>
    /// The code round, codex: the first version rolled best-effort and appended regardless, so a file
    /// at the ceiling whose rename kept failing — a locked archive, a denied replacement — grew without
    /// limit while the ceiling said otherwise. A ceiling that yields under exactly the conditions it
    /// exists for is not one. The notice is lost instead, and <see cref="NoticeWriter"/> says so.
    /// </remarks>
    private static bool Rolled(string live)
    {
        try
        {
            File.Move(live, Path.Combine(Path.GetDirectoryName(live) ?? "", Archive), overwrite: true);

            return true;
        }
        catch (Exception failure) when (IsTheDisksFault(failure))
        {
            return false;
        }
    }

    /// <summary>
    /// What the DISK gave, as opposed to a programming error.
    /// </summary>
    /// <remarks>
    /// One filter rather than two catch blocks, which is the shape <c>JsonlLedger</c> already uses:
    /// a second block for the second type is two places to keep in step and two branches a test has
    /// to reach separately to be honest about its coverage.
    /// </remarks>
    private static bool IsTheDisksFault(Exception failure) =>
        failure is IOException or UnauthorizedAccessException;
}
