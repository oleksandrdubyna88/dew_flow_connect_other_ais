using System.Text.Json;
using CoaiMcp.Core.Notices;
using CoaiMcp.Core.Rounds;

namespace CoaiMcp.Server.Migrations;

/// <summary>
/// The notice an answered hold gets (SPEC §5.4, §6.2): one entry in <c>server-notices.jsonl</c> per card, keyed by the card id, written BEFORE the session it is about
/// is converted and read back before the conversion goes on.
/// </summary>
/// <remarks>
/// <para><b>Why before.</b> After the conversion the session is no longer held, so no later run lists the answered hold again: a kill between the conversion and the
/// notice would lose the notice for good. Written first, a kill leaves a held session and a notice; the next run finds the notice (the card id is the key) and converts.</para>
/// <para><b>Why read back.</b> The sink answers "accepted" — the writer's queue took it — and the append itself can still be refused by the disk; the writer only logs that.
/// The migration trusts the file: the session is converted only when the line is there, and is deferred (no marker, the next run tries again) when it is not. An answer that
/// v2 overrides is never overridden unannounced.</para>
/// <para><b>A dry run lists and never writes.</b> <see cref="PendingNotices"/> is the same list an apply works from; the dry run reads the notices file and nothing else.</para>
/// </remarks>
internal sealed partial class MigrationV2
{
    private readonly List<MigrateV2Notice> _written = [];

    private static MigrateV2Notice RecordOf(MigrateV2AnsweredHold hold, SessionState state) =>
        new(ServerNoticeCodes.V2ConvertedAnsweredHold, hold.Card, state.SessionId, hold.Decision);

    /// <summary>The notices a dry run proposes: one per answered hold of a session that would be converted, whose card has no notice yet.</summary>
    private List<MigrateV2Notice> PendingNotices(Scan scan) =>
        [.. scan.Work.OfType<SessionWork>()
            .SelectMany(work => work.Answered.Select(hold => RecordOf(hold, work.Session.State)))
            .Where(notice => !NoticeWritten(notice.Subject))];

    /// <summary>
    /// Makes sure every answered hold of this session has its notice on disk: true when each is there (already, or written now and read back), false when one could not be.
    /// </summary>
    private bool Announced(IReadOnlyList<MigrateV2AnsweredHold> holds, SessionState state)
    {
        foreach (var hold in holds)
        {
            if (NoticeWritten(hold.Card))
            {
                continue;
            }

            if (!_notice(NoticeOf(hold, state)) || !NoticeWritten(hold.Card))
            {
                return false;
            }

            _written.Add(RecordOf(hold, state));
        }

        return true;
    }

    private ServerNotice NoticeOf(MigrateV2AnsweredHold hold, SessionState state) => new()
    {
        Utc = ServerNotice.Iso(_clock.GetUtcNow()),
        Class = "outcome",
        Source = "migrate-v2",
        Code = ServerNoticeCodes.V2ConvertedAnsweredHold,
        Subject = hold.Card,
        Title = ServerNotice.Shortened(
            $"v2 released a held gate that a person had already answered{(hold.Decision.Length > 0 ? $" ('{hold.Decision}')" : string.Empty)} — v1 never applied the answer and v2 never waits, so the gate was released as good_enough"),
        Detail = $"session {state.SessionId} on branch {state.Branch}, card {hold.Card}",
        Cure = "if the decision still matters, review the session again and decide with resolve",
        Answer = hold.Decision.Length > 0 ? hold.Decision : null,
        Repo = state.RepoPath,
        Branch = state.Branch,
        Session = state.SessionId,
    };

    /// <summary>Whether a notice of the answered-hold code with this card id as its subject is already in <c>server-notices.jsonl</c> (or its archive).</summary>
    private bool NoticeWritten(string card)
    {
        foreach (var line in ServerNotices.Lines(_dataDir))
        {
            if (line.Contains(ServerNoticeCodes.V2ConvertedAnsweredHold, StringComparison.Ordinal) && IsNoticeOf(line, card))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsNoticeOf(string line, string card)
    {
        try
        {
            using var notice = JsonDocument.Parse(line);
            var root = notice.RootElement;

            return root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("code", out var code) && code.ValueEquals(ServerNoticeCodes.V2ConvertedAnsweredHold)
                && root.TryGetProperty("subject", out var subject) && subject.ValueEquals(card);
        }
        catch (JsonException)
        {
            return false; // a torn line of somebody else's append is not this card's notice
        }
    }
}
