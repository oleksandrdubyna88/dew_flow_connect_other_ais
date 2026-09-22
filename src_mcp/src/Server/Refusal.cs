using System.Runtime.CompilerServices;
using System.Text.Json;
using CoaiMcp.Core.Notices;
using CoaiMcp.ServiceDefaults;

namespace CoaiMcp.Server;

/// <summary>
/// The one place a refusal becomes an answer — every "no" this server returns to a calling AI.
/// </summary>
/// <remarks>
/// <para><b>Why it is one place.</b> There were two, a private <c>Error</c> in <c>PanelService</c>
/// and another in <c>ConsultationService</c>, each building its own <c>ErrorAnswer</c>. Story 2.2 is
/// about to promise that every refusal returned to a calling AI is written down, and a promise about
/// a population is only as good as the thing that bounds it. Counting the CALL SITES was the first
/// proposal and story 2.1's plan round took it apart: a count cannot carry the guarantee, because
/// removing one call and adding an uninstrumented one leaves the number where it was; and the count
/// is not obtainable by a text scan anyway, since <c>_log.Error(</c> is everywhere in this
/// codebase.</para>
/// <para>So the boundary is the type. An <c>ErrorAnswer</c> is the only shape a refusal takes on the
/// wire, it is constructed HERE and nowhere else, and <c>TheRefusalRoadsAreCountedTests</c> holds
/// that to one file. "Every refusal passes through one instrumented point" is then a fact about the
/// code rather than a number somebody keeps up to date — which is what story 2.2 needs and what
/// 2.1 exists to give it.</para>
/// <para><b>It writes nothing yet.</b> Instrumenting this is 2.2's whole job; what 2.1 does is make
/// the place exist and prove it is the only one. A refusal is still returned exactly as it was: the
/// same JSON, from the same serializer context, with the same sentence.</para>
/// <para><b>A refusal a person may have to act on is worth a log line as well</b>, which is what
/// <c>PanelService.Refused</c> adds for document rounds before it calls this. That wrapper is not a
/// second road — it delegates — and the parent plan said otherwise until 2.1's research read the
/// code.</para>
/// </remarks>
internal static class Refusal
{
    /// <summary>The sentence, as the answer a calling AI receives instead of a review.</summary>
    /// <remarks>
    /// <para>The refusal is BUILT and then written, on two lines rather than one nested expression,
    /// because the point between them is where story 2.2 records the notice: a refusal exists there,
    /// and has not yet left. The first code round asked for that seam.</para>
    /// <para><b>The recording neither throws nor waits.</b> It hands the notice to one writer thread
    /// and returns — no resolve, no I/O, no lock on this path — which is what twelve findings of the
    /// second code round were about: a wait only stops WAITING, so a wedged share kept a thread-pool
    /// worker forever and every refusal cost two threads.</para>
    /// </remarks>
    internal static string Answer(string sentence, Serilog.ILogger log, [CallerMemberName] string from = "") =>
        Answer(sentence, log, from, NoticeWriter.Shared, Noticing.Where);

    /// <summary>
    /// The same, with the writer and the resolver supplied — the seam every test drives.
    /// </summary>
    /// <remarks>
    /// Parameters rather than a static hook, for the reason story 1.4 recorded: xUnit runs test classes
    /// in parallel and a hook left set fires inside somebody else's call. <c>COAI_DATA_DIR</c> is
    /// process-global, so a test that set it would be that hook wearing a different hat.
    /// </remarks>
    internal static string Answer(
        string sentence,
        Serilog.ILogger log,
        string from,
        NoticeWriter writer,
        Func<ResolvedDataDir> where)
    {
        var refusal = new ErrorAnswer(sentence);

        RefusalNotices.Record(sentence, from, log, writer, where);

        return JsonSerializer.Serialize(refusal, ServerJsonContext.Default.ErrorAnswer);
    }

}
