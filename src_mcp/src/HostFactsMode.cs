using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using CoaiMcp.Runners.Platform;

namespace CoaiMcp;

/// <summary>
/// <c>coai-mcp --host-facts</c> — the host's facts in ONE JSON line on stdout, then exit 0 (E1.S2 of <c>todo/PLAN_kimi_and_vibe_on_native_linux_E1.md</c>; DN10 and S-N14 of the parent). It is the product's own reading of what a
/// native-Linux measurement sets beside the facts script's raw one: host kind, the kernel banner and whether it was read, each runtime's admission answer, and the keyring-session facts as RECORDED facts.
/// </summary>
/// <remarks>
/// <para><b>Read-only, and not a writer mode:</b> it opens no home, no lock, no network and no data folder (<c>Program.WriterModes</c> does not hold it; the missing-root test classifies it as a mode that never reaches the data
/// directory). It reads the environment, <c>/proc</c> and one socket's status, and prints.</para>
/// <para><b>Exit code:</b> 0, always — a fact that could not be read is stated in the line (<c>"read":false</c>, <c>"listing":"cannot tell"</c>), never an exit code. It never answers 64: 64 means "never heard of this mode".</para>
/// </remarks>
internal static class HostFactsMode
{
    /// <summary>This host's line.</summary>
    internal static int Run(TextWriter output) => Run(output, HostFactsReport.ThisHost($"{Runners.Reviewers.ShimNotes.AppName} {Program.VersionText}"));

    /// <summary>The line of <paramref name="report"/>.</summary>
    internal static int Run(TextWriter output, HostFactsReport report)
    {
        output.WriteLine(Json(report));

        return 0;
    }

    /// <summary>The report as one line.</summary>
    internal static string Json(HostFactsReport report) => JsonSerializer.Serialize(report, Context.HostFactsReport);

    private static readonly HostFactsJsonContext Context = new(new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });
}

/// <summary>The line's JSON, source-generated (no reflection: this binary is Native-AOT), camelCase; the sentences are read by people, so they are not escaped to <c>\uXXXX</c>.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(HostFactsReport))]
internal sealed partial class HostFactsJsonContext : JsonSerializerContext;
