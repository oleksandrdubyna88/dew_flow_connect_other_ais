using System.Text.Json.Serialization;

namespace CoaiMcp.Core.Security;

/// <summary>The operator's required causal evidence, retained as untrusted text and never executed.</summary>
public sealed record AttackEvidence(string Trigger, string Mechanism, string Consequence)
{
    [JsonIgnore]
    public int Characters => Trigger.Length + Mechanism.Length + Consequence.Length;

    [JsonIgnore]
    public bool Complete => Characters <= Reproduction.MaxCharacters
        && new[] { Trigger, Mechanism, Consequence }.All(s => !string.IsNullOrWhiteSpace(s));

    public static AttackEvidence? Read(string? trigger, string? mechanism, string? consequence) =>
        trigger is null && mechanism is null && consequence is null ? null
            : new(trigger?.Trim() ?? string.Empty, mechanism?.Trim() ?? string.Empty, consequence?.Trim() ?? string.Empty);
}
