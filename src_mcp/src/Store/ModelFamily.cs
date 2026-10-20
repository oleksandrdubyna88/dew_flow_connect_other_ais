using System.Text.RegularExpressions;

namespace CoaiMcp.Store;

/// <summary>
/// Which family a model counts under in the report (D4 of the E6 stories) — ONE function for the observed id (<c>claude-sonnet-5-5</c>) and for
/// the requested alias (<c>sonnet</c>), so the requested-to-answered comparison cannot disagree with the grouping.
/// </summary>
/// <remarks>
/// <para>The rule is the card's (<c>authorsLine.ts</c>, <c>modelShown</c>): <c>claude-&lt;family&gt;-&lt;major&gt;-&lt;minor&gt;[-&lt;date&gt;]</c> is
/// <c>&lt;family&gt;</c>; an empty model and the word <c>unknown</c> are <see cref="Unknown"/>; anything else is <see cref="Other"/> — a new naming
/// scheme is never guessed into a family it may not belong to. <b>The binary reads no vectors file</b>:
/// <c>shared/model-display-vectors.json</c> is TEST data that holds this function and the card's TypeScript to one answer (the
/// <c>path-family-vectors.json</c> precedent), and a test holds <see cref="KnownWords"/> to the families that file names.</para>
/// <para><b><see cref="Unknown"/> is not a family</b> in a coder mix (E4 G10): the launch-only and two-repositories rows carry it, which is no
/// model claim at all.</para>
/// </remarks>
public static partial class ModelFamily
{
    /// <summary>No model was claimed: an empty id, <c>unknown</c>.</summary>
    public const string Unknown = "unknown";

    /// <summary>A model id the rule does not parse.</summary>
    public const string Other = "other";

    /// <summary>The family words a requested ALIAS may equal; every other alias goes through the id rule.</summary>
    public static readonly IReadOnlyList<string> KnownWords = ["fable", "haiku", "opus", "sonnet"];

    /// <summary>The family of an observed (answering) model id.</summary>
    public static string OfModel(string model) =>
        model.Length == 0 || model == Unknown ? Unknown : FamilyOfId(model);

    /// <summary>The family of a REQUESTED value: an alias that equals a known family word is that family, anything else is read as an id.</summary>
    public static string OfRequested(string requested) =>
        KnownWords.Contains(requested, StringComparer.Ordinal) ? requested : OfModel(requested);

    private static string FamilyOfId(string model)
    {
        var found = IdRule().Match(model);

        return found.Success ? found.Groups[1].Value : Other;
    }

    /// <summary>The end anchor is <c>\z</c> and digits are ASCII, so a trailing newline or a non-Latin digit is another id, as the card reads it.</summary>
    [GeneratedRegex(@"^claude-([a-z]+)-([0-9]+)-([0-9]{1,2})(?:-[0-9]{8})?\z", RegexOptions.CultureInvariant)]
    private static partial Regex IdRule();
}
