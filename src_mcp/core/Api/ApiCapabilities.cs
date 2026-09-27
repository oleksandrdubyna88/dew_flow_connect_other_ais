namespace CoaiMcp.Core.Api;

/// <summary>
/// What a vendor module can be told, as data the panel renders: whether thinking has a switch, which
/// effort levels the vendor accepts (its own names, in its own order), and whether an effort and a
/// thinking budget exclude each other.
/// </summary>
/// <remarks>
/// <para>The panel's dropdown lists exactly <see cref="EffortLevels"/> — never a list typed on the
/// extension's side (the operator's requirement, 2026-09-27). Every value here is from the vendor's own
/// reference or a measurement, recorded on the module that declares it.</para>
/// </remarks>
/// <param name="ThinkingSwitchable">Whether the vendor documents a way to switch thinking OFF for this family.</param>
/// <param name="EffortLevels">The effort values the vendor accepts, spelled as the vendor spells them.</param>
/// <param name="EffortExcludesThinkingBudget">Whether the vendor DOCUMENTS that an effort and a thinking budget cannot share one request (qwen3.8-max does); false where nothing is documented — unverified, not contradicted.</param>
/// <param name="ThinkingOffLevel">The effort value that switches thinking off, when one does (<c>none</c> on qwen3.8-max); empty otherwise.</param>
public sealed record ApiCapabilities(
    bool ThinkingSwitchable,
    IReadOnlyList<string> EffortLevels,
    bool EffortExcludesThinkingBudget,
    string ThinkingOffLevel = "")
{
    /// <summary>A vendor that documents nothing about effort or thinking: every configured effort is sent verbatim.</summary>
    public static readonly ApiCapabilities Undeclared = new(false, [], false);

    /// <summary>
    /// Whether this vendor takes the effort: one of its levels, the panel's <c>engine</c> (send nothing),
    /// or nothing at all (the module's default applies). A vendor that declares no levels takes any word.
    /// </summary>
    public bool Accepts(string effort)
    {
        var word = effort.Trim();

        return word.Length == 0
            || string.Equals(word, ApiDialect.EngineDecides, StringComparison.OrdinalIgnoreCase)
            || EffortLevels.Count == 0
            || EffortLevels.Contains(word, StringComparer.OrdinalIgnoreCase);
    }
}
