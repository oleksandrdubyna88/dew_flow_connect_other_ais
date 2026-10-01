using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Runners.Consultation;

/// <summary>
/// A vendor that can ANSWER a prompt in one shot: the base of <see cref="IConsultantRuntime"/>, and
/// what a question row is launched through (PLAN_question_consultant.md, §4).
/// </summary>
/// <remarks>
/// <para>Extracted from the consultant seam rather than widened out of it: a question row is one
/// launch with no conversation, so the members that are ABOUT a conversation — how the vendor
/// remembers it, how its handle is read, whether it dropped it, whether its usage accumulates —
/// stay on the derived interface, and no existing adapter changes shape. The <c>api</c> runtime
/// implements only this one: a hosted completion has nothing to resume.</para>
/// <para><c>Build</c> is pure on every CLI route and writes a prompt file on the two shim routes
/// (local, api), as the reviewer adapters it composes always have — see <c>LocalConsultant</c>.</para>
/// </remarks>
public interface IAnsweringRuntime
{
    string Vendor { get; }

    /// <summary>The process for ONE turn. Throws on a contract violation — a malformed handle, a plan this runtime cannot take.</summary>
    ReviewerInvocation Build(ConsultantLaunch launch);

    /// <summary>
    /// This route cannot run without the answer schema on disk, so a failure to provision it is a
    /// refusal for this route alone.
    /// </summary>
    /// <remarks>
    /// False for the three CLIs, which answer prose and are handed no schema at all — a read-only
    /// data directory must not stop a consultation that never needed the file. True for the two shim
    /// routes, whose request is refused without one.
    /// </remarks>
    bool NeedsAnswerSchema => false;

    /// <summary>
    /// The advice out of whatever shape this vendor answers in. Prose for every CLI; a schema-bound
    /// route unwraps its envelope.
    /// </summary>
    /// <remarks>
    /// On the ADAPTER rather than in the service, because it is vendor knowledge: unwrapping every
    /// answer unconditionally mangled a CLI's prose that happened to be JSON with an <c>answer</c>
    /// property — a consultant asked about a configuration file could return one — and a future route
    /// with its own envelope would have meant another branch in the service. (gemini, story 2's code
    /// round.) An empty string is returned AS an empty string, so the service's own
    /// "the consultant answered nothing" path fires instead of the envelope being shown as advice.
    /// </remarks>
    string ReadAdvice(string raw) => raw;
}
