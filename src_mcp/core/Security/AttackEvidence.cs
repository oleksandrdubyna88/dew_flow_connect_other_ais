using System.Text.Json.Serialization;

namespace CoaiMcp.Core.Security;

/// <summary>The operator's required causal evidence, retained as untrusted text and never executed.</summary>
public sealed record AttackEvidence(string Trigger, string Mechanism, string Consequence)
{
    /// <summary>
    /// What the declared schema allows each of the three fields: a third of the total, so no answer the
    /// schema admits can exceed <see cref="Reproduction.MaxCharacters"/> together and be refused here.
    /// </summary>
    /// <remarks>A schema cannot express a sum across properties; a per-field share of it is the bound it can.</remarks>
    public const int MaxFieldCharacters = Reproduction.MaxCharacters / 3;

    [JsonIgnore]
    public int Characters => Trigger.Length + Mechanism.Length + Consequence.Length;

    [JsonIgnore]
    public bool Complete => Characters <= Reproduction.MaxCharacters
        && new[] { Trigger, Mechanism, Consequence }.All(s => !string.IsNullOrWhiteSpace(s));

    /// <summary>No evidence when the reviewer sent none of the three fields; otherwise each one trimmed, a missing one empty.</summary>
    public static AttackEvidence? Read(string? trigger, string? mechanism, string? consequence) =>
        IsAbsent(trigger, mechanism, consequence) ? null : new(Clean(trigger), Clean(mechanism), Clean(consequence));

    private static bool IsAbsent(string? trigger, string? mechanism, string? consequence) =>
        trigger is null && mechanism is null && consequence is null;

    private static string Clean(string? field) => field?.Trim() ?? string.Empty;
}
