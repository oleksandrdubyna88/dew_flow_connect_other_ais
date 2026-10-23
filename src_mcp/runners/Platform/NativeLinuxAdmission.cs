using System.Text.Json;

namespace CoaiMcp.Runners.Platform;

/// <summary>Why a runtime is admitted on a native Linux host: a measurement, or the owner's decision (which runs it WITH a note — "unmeasured" is never a refusal).</summary>
public enum AdmissionBasis
{
    /// <summary>The runtime was measured on a native Linux host; <c>source</c> names the RESULTS section.</summary>
    Measured,

    /// <summary>The owner decided it runs there unmeasured (2026-10-23); the entry's <c>note</c> is shown to the person.</summary>
    OwnerDecision,
}

/// <summary>One runtime's row in <c>shared/native-linux-admission.json</c>.</summary>
/// <param name="Runtime">The runtime's name as the file spells it.</param>
/// <param name="Since">The first <c>coai-mcp</c> release that runs it on a native Linux host.</param>
/// <param name="Source">The evidence: a RESULTS section for <see cref="AdmissionBasis.Measured"/>, the decision for <see cref="AdmissionBasis.OwnerDecision"/>.</param>
/// <param name="Basis">Why it is admitted.</param>
/// <param name="Note">The one sentence the person is shown when the basis is <see cref="AdmissionBasis.OwnerDecision"/>; empty for a measured runtime.</param>
public sealed record NativeAdmission(string Runtime, string Since, string Source, AdmissionBasis Basis, string Note);

/// <summary>
/// <c>shared/native-linux-admission.json</c>, embedded: the runtimes admitted on a native Linux host, and for each the first <c>coai-mcp</c> release that runs it there (E2 of
/// <c>research/PLAN_kimi_and_vibe_on_native_linux_E2.md</c>) and WHY. <see cref="HostAdmission.AdmittedOnNativeLinux"/> IS its runtime names; the extension's <c>ON_NATIVE_LINUX_SINCE</c> is held to the same file by
/// its own test, so no test reads the other side's source (<c>NothingReadsAnotherProgramsSourceTests</c>).
/// </summary>
/// <remarks>
/// Embedded for the reason every shared seed here is: a Native-AOT binary runs where no <c>shared/</c> folder exists. A file that cannot be read THROWS — the same posture as <see cref="Consultation.ConsultantLimitations"/> —
/// so a build that lost it refuses to start a launch rather than carrying an admission nobody can read; the ABSENCE of an entry is the refusal, never an empty default.
/// </remarks>
public static class NativeLinuxAdmission
{
    internal const string Resource = "CoaiMcp.Runners.native-linux-admission.json";

    /// <summary>Every admitted runtime's row, by runtime name, compared case-insensitively. Never empty.</summary>
    public static IReadOnlyDictionary<string, NativeAdmission> Entries { get; } = ParseEntries(Embedded());

    /// <summary>The first release per runtime, by runtime name, compared case-insensitively. Never empty.</summary>
    public static IReadOnlyDictionary<string, string> Since { get; } = SinceOf(Entries);

    /// <summary>The admission a document holds — or a refusal naming what is wrong with it.</summary>
    internal static IReadOnlyDictionary<string, string> Parse(string json) => SinceOf(ParseEntries(json));

    internal static IReadOnlyDictionary<string, NativeAdmission> ParseEntries(string json)
    {
        using var document = JsonDocument.Parse(json);
        var admitted = new Dictionary<string, NativeAdmission>(StringComparer.OrdinalIgnoreCase);

        foreach (var (entry, at) in Rows(document.RootElement).Select((entry, at) => (entry, at)))
        {
            var row = RowOf(entry, at);
            admitted.Add(row.Runtime, row);
        }

        return admitted.Count > 0 ? admitted : throw new InvalidOperationException($"{Resource} names no runtime — an empty admission would hide that the file was lost");
    }

    private static NativeAdmission RowOf(JsonElement entry, int at)
    {
        var (runtime, since, source) = (Text(entry, "runtime", at), Text(entry, "since", at), Text(entry, "source", at));
        var basis = BasisOf(Text(entry, "basis", at), at);

        return new NativeAdmission(runtime, since, source, basis, basis == AdmissionBasis.OwnerDecision ? Text(entry, "note", at) : string.Empty);
    }

    private static AdmissionBasis BasisOf(string word, int at) => word switch
    {
        "measured" => AdmissionBasis.Measured,
        "owner-decision" => AdmissionBasis.OwnerDecision,
        _ => throw new InvalidOperationException($"{Resource}: runtimes[{at}] has the basis `{word}` — legal: measured, owner-decision"),
    };

    private static IReadOnlyDictionary<string, string> SinceOf(IReadOnlyDictionary<string, NativeAdmission> admitted) =>
        new Dictionary<string, string>(admitted.ToDictionary(pair => pair.Key, pair => pair.Value.Since), StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<JsonElement> Rows(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty("runtimes", out var runtimes) && runtimes.ValueKind == JsonValueKind.Array
            ? runtimes.EnumerateArray()
            : throw new InvalidOperationException($"{Resource} has no `runtimes` array");

    private static string Text(JsonElement entry, string field, int at) =>
        entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!
            : throw new InvalidOperationException($"{Resource}: runtimes[{at}] has no `{field}`");

    private static string Embedded()
    {
        using var stream = typeof(NativeLinuxAdmission).Assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException($"the embedded resource {Resource} is missing — this build cannot say which runtimes run on a native Linux host");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
