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

    /// <summary>Writes one refusal down. Never throws, never blocks.</summary>
    internal static void Record(
        string sentence,
        string from,
        Serilog.ILogger log,
        NoticeWriter writer,
        Func<ResolvedDataDir> where)
    {
        try
        {
            writer.Offer(where, Of(sentence, from), log);
        }
        catch (Exception failure)
        {
            // Building the record is the only thing left that can throw here — a code the list does
            // not carry, a required field that redacts away. Both are programming errors, and neither
            // is worth a refusal that never arrives.
            Quietly(() => log.Warning(failure, "the refusal from {From} could not be written down", from));
        }
    }

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
    /// <para>The sentence is not cut here. <c>Redaction.SafeText</c> cuts every string field to
    /// <c>TitleLimit</c>, which is what makes §7's per-record ceiling a fact rather than a hope, and a
    /// second cut in front of it would be a second rule to keep in step.</para>
    /// </remarks>
    internal static ServerNotice Of(string sentence, string from) => new()
    {
        Utc = ServerNotice.Iso(DateTimeOffset.UtcNow),
        Class = Class,
        Source = Source,
        Code = ServerNoticeCodes.Refused,
        Subject = from,
        Title = sentence,
    };

    /// <summary>A log that throws must not become the silence it was there to prevent.</summary>
    private static void Quietly(Action saying)
    {
        try
        {
            saying();
        }
        catch (Exception)
        {
            // A disposed logger or a full sink, inside the call that reports a lost notice. There is
            // nowhere left to say it: this process's stdout may be carrying a protocol.
        }
    }
}
