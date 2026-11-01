namespace CoaiMcp.Runners.Platform;

/// <summary>
/// What <c>coai-mcp --host-facts</c> prints, as one record (E1.S2 of <c>todo/PLAN_kimi_and_vibe_on_native_linux_E1.md</c>, DN10 of the parent): the host kind, the kernel banner and how coai read it, each runtime's admission
/// answer, and the keyring-session facts — RECORDED, never read by the admission. The product's own reading, to be set beside the facts script's raw one (two readings, on purpose: only a disagreement can be seen).
/// </summary>
/// <param name="Server">The binary and its version, <c>coai-mcp &lt;version&gt;</c>.</param>
/// <param name="HostKind"><see cref="HostKinds.Word"/> of this host.</param>
/// <param name="Banner">The kernel banner and the generation coai takes it for.</param>
/// <param name="Admission">Each runtime's answer — the sentence it would be refused with, or empty when it may start here.</param>
/// <param name="KeyringSession">The three keyring-session facts.</param>
/// <param name="Decides">What the facts are used for — said in the line itself, so a reader cannot take a recorded fact for a verdict.</param>
public sealed record HostFactsReport(string Server, string HostKind, BannerFacts Banner, AdmissionFacts Admission, KeyringSessionFacts KeyringSession, string Decides)
{
    /// <summary>What <see cref="Decides"/> says.</summary>
    public const string DecidesText = "admission only; keyringSession is recorded, never read by the admission";

    /// <summary>The report of a host, from its parts. Pure: a test hands it any host.</summary>
    internal static HostFactsReport Of(string server, HostKind kind, BannerReading banner, WslGeneration generation, KeyringSessionFacts keyring) =>
        new(
            server,
            HostKinds.Word(kind),
            new BannerFacts(banner.Read, banner.Text.Trim(), generation.ToString()),
            new AdmissionFacts(Vibe.VibeLaunch.HostRefusal(kind, "vibe", generation), Kimi.KimiLaunch.HostRefusal(kind, "kimi", generation)),
            keyring,
            DecidesText);

    /// <summary>This process's host, read once: the operating system, the kernel's banner, the product's admission, the keyring-session facts.</summary>
    public static HostFactsReport ThisHost(string server)
    {
        var kind = HostKinds.Current;

        return Of(server, kind, OperatingSystem.IsLinux() ? ProcVersion.Reading : BannerReading.Unread, WslGenerations.ForHost(kind), KeyringSessionFacts.ThisHost());
    }
}

/// <summary>The kernel banner as the line states it: whether <c>/proc/version</c> was read, its text, and the <see cref="WslGeneration"/> name coai takes it for.</summary>
public sealed record BannerFacts(bool Read, string Text, string Generation);

/// <summary>Each runtime's admission answer on this host: the refusal sentence, or empty when it may start. The names are the runtimes' own, capitalised.</summary>
public sealed record AdmissionFacts(
    [property: System.Text.Json.Serialization.JsonPropertyName("Vibe")] string Vibe,
    [property: System.Text.Json.Serialization.JsonPropertyName("Kimi")] string Kimi);
