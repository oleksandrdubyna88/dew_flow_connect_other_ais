using System.Text.Json;

namespace CoaiMcp.Runners.Platform;

/// <summary>
/// <c>shared/native-linux-admission.json</c>, embedded: the runtimes admitted on a native Linux host, and for each the first <c>coai-mcp</c> release that runs it there (E2 of
/// <c>research/PLAN_kimi_and_vibe_on_native_linux_E2.md</c>). <see cref="HostAdmission.MeasuredOnNativeLinux"/> IS its runtime names; the extension's <c>ON_NATIVE_LINUX_SINCE</c> is held to the same file by
/// its own test, so no test reads the other side's source (<c>NothingReadsAnotherProgramsSourceTests</c>).
/// </summary>
/// <remarks>
/// Embedded for the reason every shared seed here is: a Native-AOT binary runs where no <c>shared/</c> folder exists. A file that cannot be read THROWS — the same posture as <see cref="Consultation.ConsultantLimitations"/> —
/// so a build that lost it refuses to start a launch rather than carrying an admission nobody can read; the ABSENCE of an entry is the refusal, never an empty default.
/// </remarks>
public static class NativeLinuxAdmission
{
    internal const string Resource = "CoaiMcp.Runners.native-linux-admission.json";

    /// <summary>The first release per runtime, by runtime name, compared case-insensitively. Never empty.</summary>
    public static IReadOnlyDictionary<string, string> Since { get; } = Parse(Embedded());

    /// <summary>The admission a document holds — or a refusal naming what is wrong with it.</summary>
    internal static IReadOnlyDictionary<string, string> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var entries = Entries(document.RootElement);
        var since = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (entry, at) in entries.Select((entry, at) => (entry, at)))
        {
            since.Add(Text(entry, "runtime", at), Text(entry, "since", at));
            Text(entry, "source", at);
        }

        return since.Count > 0 ? since : throw new InvalidOperationException($"{Resource} names no runtime — an empty admission would hide that the file was lost");
    }

    private static IEnumerable<JsonElement> Entries(JsonElement root) =>
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
