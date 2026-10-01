using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Runners.Consultation;

/// <summary>
/// Which answering runtime a question row's vendor gets (PLAN_question_consultant.md, §4) —
/// dispatching on <see cref="RuntimeResolution.NameOf"/> and never re-deciding what a vendor is.
/// </summary>
/// <remarks>
/// <para>The stuck consultant's list (<see cref="ConsultantResolution.Consulting"/>) is NOT widened:
/// an api row consults nobody when the AI is stuck, and this list is the one that grew. The four CLI
/// and local routes are the SAME adapters, reached through <see cref="ConsultantResolution.For"/>,
/// because they answer a question through their planned confinement rather than through a second
/// adapter per vendor.</para>
/// <para>A vendor that cannot answer in this build is <c>null</c> here and a NAMED refusal at the
/// fan-out: never a substitute vendor, because the person chose this one.</para>
/// </remarks>
public static class QuestionResolution
{
    /// <summary>The runtimes a question row may run on, in the order the table lists them.</summary>
    public static IReadOnlyList<string> Answering { get; } = [.. ConsultantResolution.Consulting, "api"];

    public static IAnsweringRuntime? For(VendorIdentity vendor) => RuntimeResolution.NameOf(vendor) switch
    {
        // Whatever RuntimeResolution says the row IS, not a `new ApiRuntime` regardless: a downcast that
        // fell back would discard any wrapper resolution ever returns, along with its configuration.
        "api" => new ApiConsultant(
            RuntimeResolution.For(vendor) ?? new ApiRuntime(vendor.Provider, vendor.BaseUrl), vendor.Provider, vendor.VaultName),
        _ => ConsultantResolution.For(vendor),
    };

    /// <summary>The sentence a refused vendor gets: what it is, and who could answer instead.</summary>
    public static string CannotAnswer(VendorIdentity vendor) =>
        $"the vendor '{vendor.Provider}' runs on '{RuntimeResolution.NameOf(vendor)}'"
        + (vendor.BaseUrl.Length > 0 && RuntimeResolution.NameOf(vendor) == "codex" ? " with a custom endpoint" : string.Empty)
        + $", which the question consultant cannot launch in this build — it launches on: {string.Join(", ", Answering)}. "
        + "Pick one of those in ConnectOtherAIs > Question consultant.";
}
