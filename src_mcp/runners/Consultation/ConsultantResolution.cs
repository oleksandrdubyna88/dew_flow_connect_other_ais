using CoaiMcp.Core.Catalog;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Runners.Consultation;

/// <summary>
/// Which consultant adapter a configured vendor gets — dispatching on <see cref="RuntimeResolution.NameOf"/>
/// and never re-deciding what a vendor is.
/// </summary>
/// <remarks>
/// A vendor that cannot consult in this build is <c>null</c> here and a NAMED refusal at the tool:
/// never a substitute vendor, because the person chose this one and a call billed to an account
/// nobody picked is the failure <c>chatChoice</c> in the extension exists to prevent.
/// </remarks>
public static class ConsultantResolution
{
    /// <summary>
    /// The runtimes this build can hold a consultation with — the shared file's list itself, not a copy of it
    /// (todo/PLAN_one_model_catalog.md D4), so the panel offers exactly what this answers.
    /// </summary>
    public static IReadOnlyList<string> Consulting => FeatureAvailability.Builtin.Consultant;

    /// <remarks>
    /// It takes a vendor identity and nothing else. A <c>dataDir</c> parameter lived here for one
    /// round, so the local route could provision its schema — which made
    /// <c>For(identity)</c> answer null for a runtime <see cref="Consulting"/> advertises, and coupled
    /// the vendor factory to the server's storage layout. The schema is provisioned once when the
    /// service is built and travels on the launch.
    /// </remarks>
    public static IConsultantRuntime? For(VendorIdentity vendor) =>
        OnACli(RuntimeResolution.NameOf(vendor), vendor) ?? ByCompletion(RuntimeResolution.NameOf(vendor), vendor);

    /// <summary>A consultant that is a vendor's CLI holding its own conversation — or none for any other runtime.</summary>
    private static IConsultantRuntime? OnACli(string runtime, VendorIdentity vendor) => runtime switch
    {
        // A custom endpoint riding the codex CLI (OpenRouter, DeepSeek) consults with ITS provider: the consultant
        // carries the runtime's own -c overrides and key variable on every turn (PLAN_one_model_catalog.md E2.3).
        "codex" => CodexOn(vendor),
        "claude" =>
            new ClaudeConsultant(RuntimeResolution.For(vendor) ?? new ClaudeRuntime(vendor.Provider), vendor.Provider),
        "antigravity" =>
            new AntigravityConsultant(RuntimeResolution.For(vendor) ?? new AntigravityRuntime(vendor.Provider), vendor.Provider),
        _ => null,
    };

    /// <summary>A consultant that is one completion per turn, its transcript ours to carry — or none for any other runtime.</summary>
    private static IConsultantRuntime? ByCompletion(string runtime, VendorIdentity vendor) => runtime switch
    {
        // The local engine has no CLI and no conversation: it is a completion per turn, and the
        // transcript is ours to carry.
        // Whatever RuntimeResolution says the row IS, not a cast to what it usually is: a downcast
        // that fell back to `new LocalRuntime(...)` would discard any wrapper that resolution ever
        // returns, silently, along with its configuration. (gemini, code round.)
        "local" => new LocalConsultant(
            RuntimeResolution.For(vendor) ?? new LocalRuntime(vendor.Provider, vendor.BaseUrl),
            vendor.Provider),
        // A hosted completion keeps no conversation either: the same shape as the local engine's, through the api shim
        // (PLAN_one_model_catalog.md D9) — the adapter that already answered question rows, widened.
        "api" => new ApiConsultant(RuntimeResolution.For(vendor) ?? new ApiRuntime(vendor.Provider, vendor.BaseUrl), vendor.Provider, vendor.VaultName),
        _ => null,
    };

    /// <summary>
    /// A codex row's consultant, on the codex runtime its row resolves to — never another: a runtime that is not the Codex
    /// CLI cannot carry the row's provider, so it cannot consult (refused by name) rather than reach OpenAI's service.
    /// </summary>
    private static CodexConsultant? CodexOn(VendorIdentity vendor) => RuntimeResolution.For(vendor) switch
    {
        CodexRuntime codex => new CodexConsultant(codex, vendor.Provider),
        null => new CodexConsultant(new CodexRuntime(vendor.Provider), vendor.Provider),
        _ => null,
    };

    /// <summary>The sentence a refused vendor gets: what it is, and who could consult instead.</summary>
    public static string CannotConsult(VendorIdentity vendor) =>
        $"the vendor '{vendor.Provider}' runs on '{RuntimeResolution.NameOf(vendor)}'"
        // Only a codex row's base URL is a custom endpoint — a Team server row's is the server, an api row's its vendor.
        + (vendor.BaseUrl.Length > 0 && RuntimeResolution.NameOf(vendor) == "codex" ? " with a custom endpoint" : string.Empty)
        + $", which cannot hold a consultation in this build — consultants run on: {string.Join(", ", Consulting)}. "
        + "Pick one of those in ConnectOtherAIs > Consultant.";
}
