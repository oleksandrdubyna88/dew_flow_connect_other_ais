using CoaiMcp.Core.Notices;
using CoaiMcp.ServiceDefaults;

namespace CoaiMcp.Server;

/// <summary>
/// Where this binary writes down what it refused, what failed, and that it died.
/// </summary>
/// <remarks>
/// <para><b>The reader shipped first, and has had nothing to read.</b>
/// <c>server-notices.jsonl</c> has been named in <c>notificationsFile.ts</c> since 2026-09-17, its
/// path derived by <c>serverNoticesPath</c>, and three modules already merge it into the panel
/// section, the page and the derived count. Nothing writes it, so every one of those surfaces
/// reports on half the product. This class is the other half; story 1.3 is its PATH, and the writer
/// arrives with story 1.4.</para>
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
    /// <para><b>No call sites yet, by design.</b> Story 2.2 instruments the three refusal roads once
    /// story 2.1's census has bounded them; until then the only caller is a test, and no published
    /// binary can be made to write a real notice. That is story 2.4's live seam leg.</para>
    /// </remarks>
    public static bool Append(ResolvedDataDir dir, ServerNotice notice) =>
        JsonlLedger.AppendLine(PathFor(dir), ServerNoticeLine.Of(notice));
}
