namespace CoaiMcp.Core.QuestionConsult;

/// <summary>
/// What an ADMITTED pair carries beside its standing: nothing, or the caveat shown on the row and beside
/// every answer it gives (PLAN_question_consultant.md, D13, revised 2026-10-03: nothing to acknowledge).
/// </summary>
public enum AdmissionFlag
{
    /// <summary>Confined by a measured mechanism; nothing to acknowledge.</summary>
    None,

    /// <summary>The runtime can read this machine whatever it is told — allowed and flagged.</summary>
    Unconfined,

    /// <summary>Held only by the CLI's headless default, which nobody configured — allowed, flagged.</summary>
    DefaultDeny,

    /// <summary>
    /// The pair was never measured (web search on <c>vibe</c> and <c>api</c>) — the owner's decision of 2026-10-23: a refusal whose only reason is "not measured" is a note. The row runs; its plan gives it no tool it was not measured with,
    /// so the risk is a weaker answer (it may come without the capability), never a reach it should not have.
    /// </summary>
    Unmeasured,
}

/// <summary>The flag words as the shared vectors spell them.</summary>
public static class AdmissionFlags
{
    public static string Spelled(this AdmissionFlag flag) => flag switch
    {
        AdmissionFlag.None => string.Empty,
        AdmissionFlag.Unconfined => "unconfined",
        AdmissionFlag.DefaultDeny => "default-deny",
        AdmissionFlag.Unmeasured => "unmeasured",
        _ => throw new ArgumentOutOfRangeException(nameof(flag), flag, "a flag this build does not spell — map it here"),
    };

    /// <summary>The sentence a flagged row is shown beside — in the settings, the sidebar and the log (D13).</summary>
    public static string Caveat(this AdmissionFlag flag) => flag switch
    {
        AdmissionFlag.Unconfined => "unconfined: can read this machine — a shell is always there, and no flag removes it",
        AdmissionFlag.DefaultDeny => "held only by the CLI's headless permission default, which nobody configured",
        AdmissionFlag.Unmeasured => "not measured on this runtime for this capability: the row runs anyway, and the answer may come without it",
        _ => string.Empty,
    };
}

/// <summary>What the matrix decided for one pair: admitted with its standing and flag, or refused with a sentence.</summary>
public abstract record Admission
{
    public sealed record Admitted(CapabilityStanding Standing, AdmissionFlag Flag) : Admission;

    public sealed record Refused(string Reason) : Admission;

    private Admission() { }
}

/// <summary>
/// The one rule that turns a table row into a decision: confined admits; unconfined, default-deny and unmeasured
/// admit FLAGGED (unmeasured since the owner's decision of 2026-10-23: a note, never a refusal); unsupported refuses; a pair the table does not know refuses by name.
/// </summary>
/// <remarks>
/// A3's "blocked in the UI and refused by the server" is one rule because the extension's
/// <c>capabilityAdmission.ts</c> is this function over the same file, and both answer
/// <c>shared/capability-matrix-vectors.json</c>.
/// </remarks>
public static class CapabilityMatrix
{
    /// <summary>The decision for a pair given by its words — what a settings row or a vector carries.</summary>
    public static Admission Admit(string runtime, string capability) =>
        string.IsNullOrWhiteSpace(runtime) ? NoRuntime
        : Capabilities.TryParse(capability, out var parsed)
            ? Admit(runtime, parsed)
            : new Admission.Refused($"'{capability}' is not a capability a prompt can declare — a prompt needs one of: {Capabilities.AllSpelled} (asked for runtime '{runtime}')");

    /// <summary>The decision for a pair, from the embedded table.</summary>
    public static Admission Admit(string runtime, Capability capability) => Admit(RuntimeCapabilities.Builtin, runtime, capability);

    /// <summary>The decision from a given table — the embedded one in the product, a hand-built one in a test.</summary>
    public static Admission Admit(RuntimeCapabilities table, string runtime, Capability capability)
    {
        if (string.IsNullOrWhiteSpace(runtime))
        {
            return NoRuntime;
        }

        var row = table.Of(runtime, capability);
        if (row is null)
        {
            return new Admission.Refused(
                $"'{runtime}' is not a runtime the question consultant can launch for '{capability.Spelled()}' — it launches on: {string.Join(", ", RuntimeCapabilities.Runtimes)}");
        }

        return row.Standing switch
        {
            CapabilityStanding.Confined => new Admission.Admitted(row.Standing, AdmissionFlag.None),
            CapabilityStanding.Unconfined => new Admission.Admitted(row.Standing, AdmissionFlag.Unconfined),
            CapabilityStanding.DefaultDeny => new Admission.Admitted(row.Standing, AdmissionFlag.DefaultDeny),
            CapabilityStanding.Unmeasured => new Admission.Admitted(row.Standing, AdmissionFlag.Unmeasured),
            _ => new Admission.Refused(Unsupported(row)),
        };
    }

    /// <summary>
    /// A row with no runtime: its pick is gone, or it picks nothing — never "'' is not a runtime"
    /// (research/PLAN_names_and_a_missing_root.md story 2.2). Refused as before; only the words changed, so
    /// <c>shared/capability-matrix-vectors.json</c> still holds.
    /// </summary>
    private static Admission.Refused NoRuntime => new("this row has no model to run on — pick one");

    private static string Unsupported(RuntimeCapabilityRow row) =>
        $"'{row.Runtime}' cannot do '{row.Capability.Spelled()}': {row.MeasuredWith.Note} — give this row a prompt of another capability, or this prompt another runtime";

}
