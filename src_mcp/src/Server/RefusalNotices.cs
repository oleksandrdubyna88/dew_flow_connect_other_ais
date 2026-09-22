using CoaiMcp.Core.Notices;
using CoaiMcp.ServiceDefaults;

namespace CoaiMcp.Server;

/// <summary>
/// What a refusal looks like written down, and the promise that writing it cannot cost the refusal.
/// </summary>
/// <remarks>
/// <para><b>Nothing here may throw, and that took five plan-round findings to state properly.</b> All
/// three providers put a finding on the same sentence: the first design resolved the data directory
/// outside the <c>try</c>, so a <c>COAI_DATA_DIR</c> that cannot resolve — a configuration fault, not
/// a disk one — would have thrown past the refusal's own <c>return</c> and the calling AI would have
/// received nothing at all. A review that stops working because a setting is wrong is a worse product
/// than one that loses a notice.</para>
///
/// <para><b>And nothing here may WAIT.</b> The version after that ran the append on the thread pool
/// and waited two seconds for it; twelve findings on the code round said the same thing, and they were
/// right — a wait only stops waiting, so a wedged share kept a pool worker forever and every refusal
/// cost two threads. <see cref="NoticeWriter"/> is the answer: one writer thread, a bounded queue, and
/// a refusal that hands its notice over and returns.</para>
///
/// <para><b>The policy is in the writer; what a REFUSAL is stays here.</b> codex: a boundary
/// specialised to refusals is one story 2.3 must bypass or copy.</para>
/// </remarks>
internal static class RefusalNotices
{
    /// <summary>What a refusal is, as a row on the page.</summary>
    private const string Class = "refusal";

    /// <summary>Which half of the product said it — the extension writes its own name in its own.</summary>
    private const string Source = "coai-mcp";

    /// <summary>
    /// Writes one refusal down. Never throws, never blocks.
    /// </summary>
    /// <remarks>
    /// <b>There is no <c>try</c> here, and that is the claim rather than an omission.</b> An earlier
    /// version wrapped this line, and Sonar was right that the catch was never reached by any test —
    /// because nothing inside it can throw. <see cref="NoticeWriter.Offer"/> is a queue write with its
    /// own boundary, and <see cref="Of"/> builds a record whose every required field is a constant and
    /// whose every variable field is optional, so a sentence the redactor empties is DROPPED rather
    /// than refused. A catch no test can reach is a guarantee nobody has checked; the theory below
    /// checks this one instead, over sentences chosen to break it.
    /// </remarks>
    internal static void Record(
        string sentence,
        string from,
        Serilog.ILogger log,
        NoticeWriter writer,
        Func<ResolvedDataDir> where) =>
        writer.Offer(where, Of(sentence, from), log);

    /// <summary>
    /// The notice a refusal makes.
    /// </summary>
    /// <remarks>
    /// <para><c>Title</c> carries the sentence because the helper knows nothing else — the parent plan
    /// accepted that cost in writing rather than take <c>Error(code, sentence)</c> to 48 call sites.
    /// <c>Subject</c> is what keeps it usable: the extension keys repeats on <c>(code, subject)</c>, so
    /// one <c>refused</c> code for every site would collapse "no reviewers are configured" and "the
    /// plan text is empty" into one row a person cannot read. The subject is the CALLING MEMBER, filled
    /// by the compiler through <c>[CallerMemberName]</c> on each service's own <c>Error</c> helper — a
    /// stable key with no round number in it, and not one call site had to change to get it. (codex, on
    /// the plan round.)</para>
    /// <para><b>The sentence is cut to the same limit the serialiser will cut it to</b>, and that is
    /// about MEMORY rather than bytes on disk. <c>Redaction.SafeText</c> cuts every string field to
    /// <c>TitleLimit</c> when the line is written — but the notice sits in the writer's queue until
    /// then, and 256 queued records each holding a megabyte of refusal sentence is 256 MB of process
    /// held because a share stopped answering. CodeRabbit found it on the pull request. The cut here
    /// is the plain prefix and nothing else; the redaction, the control characters and the suffix all
    /// still belong to the serialiser, so there is still one rule about what a written field is.</para>
    /// </remarks>
    internal static ServerNotice Of(string sentence, string from) => new()
    {
        Utc = ServerNotice.Iso(DateTimeOffset.UtcNow),
        Class = Class,
        Source = Source,
        Code = ServerNoticeCodes.Refused,
        Subject = from,
        Title = Shorter(sentence),
    };

    /// <summary>As much of the sentence as can ever be written, and no more held in memory.</summary>
    private static string Shorter(string sentence) =>
        sentence.Length <= Redaction.TitleLimit ? sentence : sentence[..Redaction.TitleLimit];

}
