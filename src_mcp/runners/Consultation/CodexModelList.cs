using System.Text.Json;
using CoaiMcp.Core.Roles;

namespace CoaiMcp.Runners.Consultation;

/// <summary>One model of the list <c>codex debug models</c> prints: its <c>slug</c> (the name <c>-m</c> takes) and its <c>visibility</c> (<c>list</c> or <c>hide</c>).</summary>
internal sealed record CodexListedModel(string Slug, string Visibility)
{
    /// <summary>The visibility of a model the CLI offers in its picker; a hidden model (<c>hide</c>) is never taken for a family.</summary>
    public const string Listed = "list";
}

/// <summary>
/// The live list of models of the installed codex CLI (epoch 3 E3 s2 t5; <c>SPEC_CHANGES.md</c> section Q1; <c>RESULTS_model_lists.md</c>): the output of <c>codex debug models</c> WITHOUT <c>--bundled</c> — a network call and no model call, what this account is offered today —
/// and the choice of the newest model of a family from it.
/// </summary>
/// <remarks>
/// <para><b>The reading is strict.</b> The output is read only when it is ONE JSON object with a <c>models</c> array of objects that each have a string <c>slug</c>, and only up to <see cref="MostChars"/> characters. Anything else is a list that cannot be read, and the entry that asked for it ends
/// <c>model-unavailable</c>. Nothing here ever reads a sentence the CLI printed: only the slugs and the visibility words of a list that has the shape.</para>
/// <para><b>Which name is the newest.</b> Only a name of the exact shape <c>gpt-&lt;version&gt;-&lt;family&gt;</c> (<see cref="CodexModelShape"/>) with visibility <c>list</c> is a candidate; the versions are compared number by number with a missing number as 0; of two equal versions the one listed first wins. A hidden name and a name with other
/// words (<c>gpt-terra-latest</c>, <c>gpt-6.1-terra-preview</c>) are never taken, so the text that reaches the argv after <c>-m</c> always has the shape, whatever the list holds.</para>
/// </remarks>
internal static class CodexModelList
{
    /// <summary>The arguments of the command: the live list, never <c>--bundled</c> (the bundled list holds <c>gpt-5.5</c>, which the account is not offered).</summary>
    public static IReadOnlyList<string> Arguments { get; } = ["debug", "models"];

    /// <summary>How long the command may run: it is a network call and no model call, and it answered in seconds when it was measured.</summary>
    public static TimeSpan Deadline { get; internal set; } = TimeSpan.FromSeconds(60);

    /// <summary>The most characters of output that are read (the list measured about 920 000); a longer output is not a list.</summary>
    public const int MostChars = 4 * 1024 * 1024;

    /// <summary>The models of an output, in the order the CLI lists them; null when it is not the list (not JSON, not the expected shape, over the limit, no model with a name).</summary>
    public static IReadOnlyList<CodexListedModel>? Read(string output)
    {
        if (output.Length == 0 || output.Length > MostChars)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(output);
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var list = new List<CodexListedModel>();
            foreach (var model in models.EnumerateArray())
            {
                if (model.ValueKind != JsonValueKind.Object || !model.TryGetProperty("slug", out var slug) || slug.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(slug.GetString()))
                {
                    return null;
                }

                list.Add(new CodexListedModel(slug.GetString()!, model.TryGetProperty("visibility", out var visibility) && visibility.ValueKind == JsonValueKind.String ? visibility.GetString() ?? string.Empty : string.Empty));
            }

            return list;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The slug of the newest listed model of <paramref name="family"/> (a family of <paramref name="families"/>), or null when the list holds none.</summary>
    public static string? Newest(IReadOnlyList<CodexListedModel> list, string family, IReadOnlyCollection<string> families)
    {
        string? best = null;
        IReadOnlyList<int> bestVersion = [];
        foreach (var model in list.Where(model => model.Visibility == CodexListedModel.Listed))
        {
            if (!CodexModelShape.TryParse(model.Slug, families, out var found, out var version) || found != family)
            {
                continue;
            }

            // Strictly newer only: of two equal versions the one listed first stays.
            if (best is null || CodexModelShape.Compare(version, bestVersion) > 0)
            {
                best = model.Slug;
                bestVersion = version;
            }
        }

        return best;
    }

    /// <summary>Whether the list holds <paramref name="name"/> (any visibility: a person may name a model the picker hides, and the CLI takes it).</summary>
    public static bool Holds(IReadOnlyList<CodexListedModel> list, string name) => list.Any(model => string.Equals(model.Slug, name, StringComparison.Ordinal));
}
