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
    /// <summary>The runtimes this build can hold a consultation with, in the order the stories added them.</summary>
    public static IReadOnlyList<string> Consulting { get; } = ["codex", "claude", "antigravity", "local"];

    /// <remarks>
    /// It takes a vendor identity and nothing else. A <c>dataDir</c> parameter lived here for one
    /// round, so the local route could provision its schema — which made
    /// <c>For(identity)</c> answer null for a runtime <see cref="Consulting"/> advertises, and coupled
    /// the vendor factory to the server's storage layout. The schema is provisioned once when the
    /// service is built and travels on the launch.
    /// </remarks>
    public static IConsultantRuntime? For(VendorIdentity vendor) => RuntimeResolution.NameOf(vendor) switch
    {
        // A custom endpoint riding the codex CLI (DeepSeek) authenticates and configures differently;
        // it consults when its provider overrides are measured, not before.
        "codex" when vendor.BaseUrl.Length == 0 =>
            new CodexConsultant(RuntimeResolution.For(vendor) ?? new CodexRuntime(vendor.Provider), vendor.Provider),
        "claude" =>
            new ClaudeConsultant(RuntimeResolution.For(vendor) ?? new ClaudeRuntime(vendor.Provider), vendor.Provider),
        "antigravity" =>
            new AntigravityConsultant(RuntimeResolution.For(vendor) ?? new AntigravityRuntime(vendor.Provider), vendor.Provider),
        // The local engine has no CLI and no conversation: it is a completion per turn, and the
        // transcript is ours to carry.
        "local" => new LocalConsultant(
            RuntimeResolution.For(vendor) as LocalRuntime ?? new LocalRuntime(vendor.Provider, vendor.BaseUrl),
            vendor.Provider),
        _ => null,
    };

    /// <summary>The sentence a refused vendor gets: what it is, and who could consult instead.</summary>
    public static string CannotConsult(VendorIdentity vendor) =>
        $"the vendor '{vendor.Provider}' runs on '{RuntimeResolution.NameOf(vendor)}'"
        + (vendor.BaseUrl.Length > 0 ? " with a custom endpoint" : string.Empty)
        + $", which cannot hold a consultation in this build — consultants run on: {string.Join(", ", Consulting)}. "
        + "Pick one of those in the Consultant section of the ConnectOtherAIs panel.";
}
