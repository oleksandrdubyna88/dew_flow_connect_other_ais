namespace CoaiMcp.Server.Migrations;

/// <summary>
/// The notices of a migration run (SPEC §5.4, §6.2): the part of <see cref="MigrationV2"/> that proposes them. Writing one, confirming it and the conversion that follows are
/// <see cref="HoldBoundary"/> — the same boundary a load of a still-held session crosses.
/// </summary>
/// <remarks>
/// <para><b>A dry run lists and never writes.</b> <see cref="PendingNotices"/> is the same list an apply works from; the dry run reads the notices file and nothing else.</para>
/// <para><b>One read of the history per run.</b> The boundary reads the card ids that already have a notice once; the confirm after each write reads only the bytes that write
/// added. A run therefore costs one read of the history, however many notices it writes.</para>
/// </remarks>
internal sealed partial class MigrationV2
{
    private HoldBoundary? _holds;

    /// <summary>The run's boundary, made on first use so the test seams (<see cref="NoticeLines"/>, <see cref="NoticeTail"/>) set at construction are the ones it reads.</summary>
    private HoldBoundary Holds => _holds ??= new(_dataDir, _notice, _clock) { NoticeLines = NoticeLines, NoticeTail = NoticeTail };

    /// <summary>The notices a dry run proposes: one per answered hold of a session that would be converted, whose card has no notice yet.</summary>
    private List<MigrateV2Notice> PendingNotices(Scan scan) =>
        [.. scan.Work.OfType<SessionWork>()
            .SelectMany(work => work.Answered.Select(hold => HoldBoundary.RecordOf(hold, work.Session.State)))
            .Where(notice => !Holds.NoticeWritten(notice.Subject))];
}
