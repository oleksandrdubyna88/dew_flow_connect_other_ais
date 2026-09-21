using CoaiMcp.Core.Notices;
using CoaiMcp.ServiceDefaults;

namespace CoaiMcp.Server;

/// <summary>
/// The failure boundary around writing a refusal down: nothing in here may cost the refusal.
/// </summary>
/// <remarks>
/// <para><b>It is a boundary and not a call because the plan round said so five times.</b> All three
/// providers put a finding on the same sentence of story 2.2's plan, and each of them was right about
/// the same gap: the pseudocode resolved the data directory OUTSIDE the try, so a
/// <c>COAI_DATA_DIR</c> that cannot resolve — a configuration fault, not a disk one — would have
/// thrown past the <c>return</c>, and the calling AI would have received nothing at all. A review
/// that stops working because a setting is wrong is a worse product than one that loses a notice.
/// So resolution, construction and the append are inside ONE catch, and four tests drive it: a
/// resolver that throws, a writer that throws, a writer that answers false, and a writer that never
/// returns.</para>
///
/// <para><b>The write is time-bounded, and that is the fourth of those.</b> codex: this product's
/// data directory has been a NAS share, and a synchronous append to a stalled one blocks — so the
/// refusal would never be serialised and the calling AI would time out, which is the failure this
/// story exists to prevent arriving by a new road. The append runs on the thread pool and is waited
/// on for <see cref="Budget"/>. Past that the refusal goes without it and the task is abandoned: it
/// may still land, or never, and either is better than a refusal that does not arrive. That is what
/// "explicitly lossy" means here, and it is the residual this design leaves.</para>
///
/// <para><b>A lost notice is never silent.</b> <c>ServerNotices.Append</c> answers <c>false</c> for
/// anything the disk gave, and a run where that happens looks exactly like one where it did not —
/// so the log says which refusal was lost. The logger is optional because the seam tests do not
/// need one, and because a refusal must not depend on there being one.</para>
/// </remarks>
internal static class RefusalNotices
{
    /// <summary>How long a notice may take before the refusal goes without it.</summary>
    internal static readonly TimeSpan Budget = TimeSpan.FromSeconds(2);

    /// <summary>What a refusal is, as a row on the page.</summary>
    private const string Class = "refusal";

    /// <summary>Which half of the product said it — the extension writes its own name in its own.</summary>
    private const string Source = "coai-mcp";

    /// <summary>Writes one refusal down. Never throws, never blocks past <paramref name="budget"/>.</summary>
    internal static void Record(
        string sentence,
        string from,
        Serilog.ILogger? log,
        Func<ResolvedDataDir> where,
        Func<ResolvedDataDir, ServerNotice, bool> append,
        TimeSpan budget)
    {
        try
        {
            Said(Wrote(sentence, from, where, append, budget), from, log);
        }
        catch (Exception failure)
        {
            // Everything: a directory that cannot resolve, a path the OS refuses, a record the line
            // cannot write, an append that threw on a share. The refusal is already built and the
            // caller returns it the moment this comes back.
            log?.Warning(failure, "the refusal from {From} could not be written down", from);
        }
    }

    /// <summary>
    /// The notice a refusal makes.
    /// </summary>
    /// <remarks>
    /// <para><c>Title</c> carries the sentence because the helper knows nothing else — the parent
    /// plan accepted that cost in writing rather than take <c>Error(code, sentence)</c> to 48 call
    /// sites. <c>Subject</c> is what keeps it usable: the extension keys repeats on
    /// <c>(code, subject)</c>, so one <c>refused</c> code for every site would collapse "no reviewers
    /// configured" and "the plan text is empty" into one row a person cannot read. The subject is the
    /// CALLING MEMBER, filled by the compiler through <c>[CallerMemberName]</c> — a stable key with no
    /// round number in it, and not one call site had to change to get it. (codex, on the plan
    /// round.)</para>
    /// <para>The sentence is not cut here. <c>Redaction.SafeText</c> cuts every string field to
    /// <c>TitleLimit</c>, which is what makes §7's per-record ceiling a fact rather than a hope, and
    /// a second cut in front of it would be a second rule to keep in step.</para>
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

    /// <summary>Whether it landed, waited on for no longer than the budget.</summary>
    private static bool Wrote(
        string sentence,
        string from,
        Func<ResolvedDataDir> where,
        Func<ResolvedDataDir, ServerNotice, bool> append,
        TimeSpan budget)
    {
        var writing = Task.Run(() => append(where(), Of(sentence, from)));

        return writing.Wait(budget) && writing.Result;
    }

    private static void Said(bool wrote, string from, Serilog.ILogger? log)
    {
        if (wrote)
        {
            return;
        }

        log?.Warning("the refusal from {From} was not written to {File}", from, ServerNotices.Name);
    }
}
