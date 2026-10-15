using System.Text.Json;
using CoaiMcp.Core.Findings;

namespace CoaiMcp.Runners.Kimi;

/// <summary>
/// The shim's one JSON object (<c>AskAcpMode.Report</c>) as the parent reads it — Kimi's OWN reader (E4.4 of <c>todo/PLAN_vibe_and_kimi_runtimes_E4.md</c>):
/// the ACP report nests the usage under <c>usage</c>, where <c>ApiRuntime.ReadUsage</c> looks for it at the root.
/// </summary>
/// <remarks>
/// Classification reads these FIELDS and the exit, never the text of the trace: the report carries every path the agent read and every tool it titled,
/// and a path named <c>quota.cs</c> is not a rate limit (risk consultation <c>ba5f661a</c>).
/// </remarks>
/// <param name="End">The shim's end word (<see cref="AnsweredWord"/>, <see cref="NotStartedWord"/>, <see cref="ModelNotOfferedWord"/>, <c>agent_failed</c>, <c>deadline</c>, <c>protocol</c>, <c>cancelled</c>); empty for an unreadable report.</param>
/// <param name="Failure">A known failure's NAME (<c>not_signed_in</c>, <c>subscription_model</c>, <c>balance_spent</c>), or empty.</param>
/// <param name="Reason">The shim's own sentence for how the turn ended.</param>
/// <param name="Answer">The agent's answer — empty unless the turn was answered.</param>
/// <param name="Usage">The session's tokens when <c>/usage</c> gave them; unknown otherwise, never a guess and never a cost (D9).</param>
public sealed record KimiReport(string End, string Failure, string Reason, string Answer, Usage Usage)
{
    /// <summary>The shim's exit for a known SPENT sentence — declared here, where it is READ, and taken by <c>AskAcpMode</c>, where it is written.</summary>
    public const int SpentExit = 75;

    /// <summary>The end word of an answered turn.</summary>
    public const string AnsweredWord = "answered";

    /// <summary>The end word of a turn that never started an agent.</summary>
    public const string NotStartedWord = "not_started";

    /// <summary>The end word of a turn whose session did not offer the asked model — found out before any prompt.</summary>
    public const string ModelNotOfferedWord = "model_not_offered";

    /// <summary>A stdout that is not the shim's report (a crash before it printed, a launcher's own text).</summary>
    public static KimiReport Unreadable { get; } = new(string.Empty, string.Empty, string.Empty, string.Empty, Usage.Unknown);

    /// <summary>Whether the stdout was a report at all.</summary>
    public bool Readable => End.Length > 0;

    /// <summary>Whether the turn was answered.</summary>
    public bool Answered => End == AnsweredWord;

    /// <summary>
    /// The name a sentence of this report is filed under: the shim's known failure, else — for the one end that is itself a verdict — its end word,
    /// else nothing. What <c>VendorDiagnosis</c> reads as its marker.
    /// </summary>
    public string Label => Failure.Length > 0 ? Failure : End == ModelNotOfferedWord ? End : string.Empty;

    /// <summary>The report of <paramref name="stdout"/>; <see cref="Unreadable"/> when it is not one.</summary>
    public static KimiReport Parse(string stdout)
    {
        try
        {
            using var document = JsonDocument.Parse(stdout);

            return document.RootElement is { ValueKind: JsonValueKind.Object } root ? From(root) : Unreadable;
        }
        catch (JsonException)
        {
            return Unreadable;
        }
    }

    private static KimiReport From(JsonElement root)
    {
        var end = Text(root, "end");

        return end.Length == 0
            ? Unreadable
            : new KimiReport(end, Text(root, "failure"), Text(root, "reason"), Text(root, "answer"), UsageOf(root, end));
    }

    /// <summary>
    /// The totals when the shim captured them; otherwise unknown — except for a launch that never reached the model (no agent started, or the
    /// session did not offer the model: found out before any prompt), which consumed nothing, a fact and not an unknown.
    /// </summary>
    private static Usage UsageOf(JsonElement root, string end) =>
        TryTokens(root, out var tokens) ? tokens : NeverReachedTheModel(end) ? Usage.None : Usage.Unknown;

    private static bool NeverReachedTheModel(string end) => end is NotStartedWord or ModelNotOfferedWord;

    private static bool TryTokens(JsonElement root, out Usage tokens)
    {
        tokens = Usage.Unknown;
        if (!TryObject(root, "usage", out var usage) || Flag(usage, "notCaptured"))
        {
            return false;
        }

        tokens = new Usage(Count(usage, "tokensIn"), Count(usage, "tokensOut"), null);

        return true;
    }

    private static bool TryObject(JsonElement root, string name, out JsonElement value) =>
        root.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.Object;

    private static string Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    private static long Count(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.TryGetInt64(out var count) ? count : 0;

    private static bool Flag(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
