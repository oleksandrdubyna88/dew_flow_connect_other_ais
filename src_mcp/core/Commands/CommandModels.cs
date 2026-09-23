namespace CoaiMcp.Core.Commands;

/// <summary>The two models the split order names: the one for the hard half, and the one for the rest.</summary>
/// <param name="Strongest">The split itself and the stories where being wrong is expensive. Empty = not named.</param>
/// <param name="Implementation">The ordinary stories. Empty = not named.</param>
public sealed record ModelPair(string Strongest = "", string Implementation = "");

/// <summary>
/// Which models the split order names, per KIND of calling AI (issue #117).
/// </summary>
/// <remarks>
/// <para>The order is carried out by the CALLER, never by this gate — which is why a Codex session was
/// being told "Fable" and "Opus", two models it does not have. So the choice is per caller kind, the
/// shape the consultant already has: a Claude Code caller is named Claude models, a Codex caller Codex
/// ones, and a kind nobody configured is named no model at all rather than another vendor's.</para>
/// <para>The kinds are the ones <c>CallerIdentity.KindFrom</c> answers. They are spelled here because
/// this project is pure and cannot reference the server's; a test enumerates the server's list against
/// <see cref="Shipped"/> so the two cannot drift.</para>
/// </remarks>
public static class CommandModels
{
    private const string Other = "other";

    /// <summary>What every release before issue #117 named, kept for the caller it was right for.</summary>
    public static ModelPair ClaudeCode { get; } = new("Fable", "Opus");

    /// <summary>
    /// The shipped pair per caller kind. Only Claude Code's names anything: for the others this
    /// product has no evidence which of their models is strongest, and a guess named in an order is
    /// worse than the generic words the command falls back to.
    /// </summary>
    public static IReadOnlyDictionary<string, ModelPair> Shipped { get; } =
        new Dictionary<string, ModelPair>(StringComparer.Ordinal)
        {
            ["claude"] = ClaudeCode,
            ["codex"] = new(),
            ["gemini"] = new(),
            [Other] = new(),
        };

    /// <summary>
    /// The pair for one caller kind, resolved PER FIELD: a configured name, else the shipped one.
    /// </summary>
    /// <remarks>
    /// Per field, so a person who changed only the implementation model for Claude Code keeps Fable
    /// for the split. A kind with no row at all — blank, or one a newer client exports — resolves as
    /// <c>other</c>, so an unidentified caller is never named Claude models.
    /// </remarks>
    public static ModelPair For(IReadOnlyDictionary<string, ModelPair> configured, string kind)
    {
        var known = Shipped.ContainsKey(kind) || configured.ContainsKey(kind) ? kind : Other;
        var shipped = Shipped.GetValueOrDefault(known, Shipped[Other]);
        var chosen = configured.GetValueOrDefault(known, shipped);

        return new ModelPair(
            NamedOr(chosen.Strongest, shipped.Strongest),
            NamedOr(chosen.Implementation, shipped.Implementation));
    }

    /// <summary>A name, trimmed — or the fallback when there is none. The one spelling of "blank".</summary>
    public static string NamedOr(string name, string fallback) =>
        string.IsNullOrWhiteSpace(name) ? fallback : name.Trim();
}
