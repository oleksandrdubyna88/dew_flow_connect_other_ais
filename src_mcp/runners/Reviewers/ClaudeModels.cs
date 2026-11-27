using System.Text.Json;
using CoaiMcp.Core.Authors;

namespace CoaiMcp.Runners.Reviewers;

/// <summary>
/// The model a claude launch is told, and the check that the CLI answered on the family it was asked for. ONE place for
/// every claude launch road — the reviewer, the stuck consultant and the question consultant all build their <c>--model</c>
/// here — because every one of them runs <c>--permission-mode plan</c>.
/// </summary>
/// <remarks>
/// <para><b>Measured 2026-11-27</b> (a v2 paid proof): claude CLI 2.1.332 was started with <c>--permission-mode plan --model haiku</c>
/// and the session's api-request <c>params.model</c> was <c>claude-sonnet-5-5</c>. In the CLI binary the function <c>npe</c> returns
/// <c>sonnet</c> for <c>haiku</c>, and <c>XMt</c> applies it only when the permission mode is <c>plan</c>; the CLI's own help says:
/// "A session whose model setting is 'opusplan', 'opusplan[1m]' or 'haiku' runs a different model in plan mode". A full id is not
/// mapped. So a person who chose <c>haiku</c> ran, and paid for, Sonnet. The alias is therefore sent as <see cref="HaikuId"/>.</para>
/// <para><b>Why that id.</b> It is the only Haiku the repository lists (<c>shared/model-display-vectors.json</c>, family
/// <c>haiku</c>; a test holds this constant to that file and fails when a newer Haiku is listed there). <c>opusplan</c> is left as
/// written: in plan mode it runs Opus, which is what its name says, so no row pays for a model it did not choose, and no
/// repository model list carries it.</para>
/// <para><b>The other half is the check.</b> The CLI's JSON result names the models that ANSWERED in <c>modelUsage</c>. The alias list
/// above is what is known today; <see cref="Mismatch"/> is what notices the next alias the CLI maps behind our back.</para>
/// </remarks>
public static class ClaudeModels
{
    /// <summary>The full id sent for the alias <c>haiku</c> — the CLI maps only the bare alias, and only in plan mode.</summary>
    public const string HaikuId = "claude-haiku-4-5-20251001";

    private const string HaikuAlias = "haiku";

    /// <summary>What the CLI is told for a row's model: the full Haiku id for the bare alias, every other value as the row wrote it.</summary>
    public static string Sent(string model) =>
        model.Trim().Equals(HaikuAlias, StringComparison.OrdinalIgnoreCase) ? HaikuId : model;

    /// <summary><c>--model &lt;value&gt;</c> for a row that names a model, nothing for one that does not (the CLI picks its own).</summary>
    public static string[] Arguments(ReviewerSettings settings) =>
        settings.Model.Length > 0 ? ["--model", Sent(settings.Model)] : [];

    /// <summary>
    /// One sentence when the CLI's own result says the run was answered by another family than the row asked for — or empty.
    /// </summary>
    /// <remarks>
    /// Empty whenever it cannot tell: a row that asks for no family this code can name (<c>opusplan</c>, an empty model, a non-claude id), a
    /// result without a readable <c>modelUsage</c>, ids that parse to no family (a gateway's own names), and any run in which the asked
    /// family is among the models that answered (a helper model beside the main one is not a mismatch). Never throws: it runs on the
    /// path of a run that has already been paid for.
    /// </remarks>
    public static string Mismatch(string asked, string stdOut)
    {
        var family = ModelFamily.OfRequested(asked);
        if (!ModelFamily.KnownWords.Contains(family, StringComparer.Ordinal))
        {
            return string.Empty;
        }

        var answered = Answered(stdOut);
        var families = answered.Select(ModelFamily.OfModel).Where(Named).Distinct(StringComparer.Ordinal).ToList();

        return families.Count == 0 || families.Contains(family, StringComparer.Ordinal)
            ? string.Empty
            : $"asked for {asked} ({family}), but the CLI's own result says it ran {string.Join(", ", answered)}; that is another family, billed as that model";
    }

    private static bool Named(string family) => family is not (ModelFamily.Other or ModelFamily.Unknown);

    /// <summary>The model ids the result's <c>modelUsage</c> names — none when it is absent or is not an object.</summary>
    private static List<string> Answered(string stdOut)
    {
        try
        {
            using var document = JsonDocument.Parse(stdOut.Trim());

            return document.RootElement is { ValueKind: JsonValueKind.Object } root
                && root.TryGetProperty("modelUsage", out var models)
                && models.ValueKind == JsonValueKind.Object
                    ? [.. models.EnumerateObject().Select(model => model.Name)]
                    : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
